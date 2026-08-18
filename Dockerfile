# The one deployable (spec §4.11): the public site, the owner dashboard, the read API and the
# crawler, in a single ASP.NET Core process. There is deliberately no second image for the crawler —
# it is a BackgroundService in this one, and the Postgres advisory lock is what keeps N replicas of
# this image to exactly one crawler (spec §12).
#
# mui-crawl is not in here. It is the one-shot tool a person runs against a database on purpose, and
# an image that shipped it would invite it into a container's entrypoint.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# The manifests first, so the restore layer survives every change that is not a dependency change.
# Only the site's own graph is restored — the CLI and the test projects are not in this image and
# pulling their packages would slow every build to no end.
#
# This list is hand-written and was therefore wrong the first time somebody added a project: MUI.I3
# arrived referenced by MUI.Crawler, CI restored the whole solution and went green, and the publish
# below died on a missing assets file — after merge, so production sat on a stale image until
# somebody read the workflow log. `tools/check-image-restore.py` runs in CI and resolves MUI.Web's
# transitive project closure against this block, so the next one fails on the pull request with the
# COPY line it needs in the message.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/MUI.Catalog/MUI.Catalog.csproj      src/MUI.Catalog/
COPY src/MUI.Crawl/MUI.Crawl.csproj          src/MUI.Crawl/
COPY src/MUI.Crawler/MUI.Crawler.csproj      src/MUI.Crawler/
COPY src/MUI.Discovery/MUI.Discovery.csproj  src/MUI.Discovery/
COPY src/MUI.I3/MUI.I3.csproj                src/MUI.I3/
COPY src/MUI.Web/MUI.Web.csproj              src/MUI.Web/
RUN dotnet restore src/MUI.Web/MUI.Web.csproj

# migrations/ and content/ are not incidental: MUI.Catalog embeds the .sql files and MUI.Web embeds
# the reference pages, so the published assemblies carry both and the runtime image resolves no
# content root and no SQL directory. A page or a migration cannot be present in one deployment and
# missing in another — and an image built without migrations/ would start, apply no schema and
# report nothing wrong, which is why MUI.Catalog.csproj now fails the build instead.
COPY migrations/ migrations/
COPY src/ src/
COPY content/ content/

RUN dotnet publish src/MUI.Web/MUI.Web.csproj -c Release --no-restore -o /app

# No SDK past this line. The runtime image has the ASP.NET shared framework and nothing that can
# compile, restore or reach a package feed.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

COPY --from=build /app ./

# InvariantGlobalization is on solution-wide, so no ICU is needed here.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_TieredPGO=1

EXPOSE 8080

# Somewhere for `createdump` to write, owned by the user that will be doing the writing. Docker
# initialises an empty named volume from the image directory it is mounted over, ownership included,
# so this line is what makes the volume land as 1654 rather than as root. Without it the mount is
# root-owned, the app user cannot write to it, and DOTNET_DbgEnableMiniDump fails silently at the
# one moment it exists for — which is how it was found: by trying, in production, after the fact.
RUN mkdir -p /dumps && chown $APP_UID:$APP_UID /dumps

# curl, for the HEALTHCHECK below. The aspnet runtime image ships neither curl nor wget — it is
# Ubuntu underneath, not distroless — so this is the smallest addition that lets something inside the
# container ask the process itself whether it is ready, in the same vocabulary Docker/Podman's own
# health state and Compose's `condition: service_healthy` already speak. Cleaned up in the same layer
# so the apt cache does not ride along in the image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# The `app` user the base image already provides (UID 1654). The process writes nothing to disk in
# the ordinary course — every write goes to Postgres, and /dumps above is only touched on the way
# out — so a read-only root filesystem is a reasonable thing to ask of it.
USER $APP_UID

# GET /health is the readiness contract: 200 once the process can actually serve a request (it checks
# Postgres reachability when a connection string is configured, and is always ready on the demo
# fixture). This is what makes a version cutover not look like downtime — Traefik's own active
# healthcheck (deploy/compose.production.yaml) polls the same path from outside the container to
# decide whether to route to it at all, which is the check that actually gates traffic; this
# HEALTHCHECK is what feeds Docker/Podman's own health state for everything else that reads it —
# `docker compose ps`, and any orchestration that keys off it. start-period gives startup — including
# a migration run, when one is pending — room to finish before a slow first check counts as a failure.
HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "MUI.Web.dll"]
