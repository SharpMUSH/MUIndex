# Running MUIndex

There is one deployable. The public site, the owner dashboard, the read API and the crawler are a
single ASP.NET Core process (spec §4.11), and the only thing it needs beside it is a PostgreSQL.

**This document does not choose a host or a domain.** Spec §15.1 (the domain) and §15.3 (hosting and
the cost envelope) are open decisions, and the packaging here is deliberately neutral about both — a
container image, a compose file and a workflow that publishes the image. [What is still
open](#what-is-still-open) lists the levers each decision turns.

## The shortest path

```bash
docker compose up --build          # or: podman compose up --build
```

The site is on <http://localhost:8080>, the schema is applied, and the crawler is running against an
empty registry with nothing to do. Give it something:

```bash
MUI_CRAWL_SEEDS='mush.pennmush.org:4201 aardmud.org:4000' docker compose up --build
```

Within a cycle the listing has measurements in it. **Seeds are addresses of real servers belonging to
other people**, and the crawler dials them: use a game you run, or one you are content to send a
handful of packets to. Politeness is spec §11's business and the crawler honours it, but the choice
of who to dial is yours.

Without `MUI_POSTGRES` the site still starts — on the demo fixture, with a banner on every page
saying that nothing on it was measured. That is a property of the packaging and not a fallback; see
[The demo path](#the-demo-path).

## The image

```bash
docker build -t muindex .
docker run --rm -p 8080:8080 -e MUI_POSTGRES='Host=…;Database=muindex;Username=…;Password=…' muindex
```

Multi-stage: the .NET 10 SDK builds and publishes, and the runtime layer is
`mcr.microsoft.com/dotnet/aspnet:10.0` with no SDK, no package feed and nothing that can compile. It
runs as the base image's `app` user (UID 1654) and listens on **8080**.

**The process writes nothing to disk** — every write goes to Postgres — so it runs happily with a
read-only root filesystem, which the compose file asks for. The one exception is discussed under
[Data protection keys](#data-protection-keys).

`migrations/` and `content/reference/` are compiled *into* the assemblies as embedded resources, so
the image resolves no content root and no SQL directory, and a page or a migration cannot be present
in one deployment and missing in another. A build context that omits `migrations/` now fails the
build rather than producing an image that starts and applies no schema — it did exactly that once,
which is why the check exists.

## Environment

| Variable | Default | What it does |
| --- | --- | --- |
| `MUI_POSTGRES` | *(unset)* | The connection string. **Unset means demo data.** Read before `ConnectionStrings:MUIndex`, which is the same setting through configuration. |
| `MUI_CRAWL_SEEDS` | *(empty)* | `host:port` addresses the crawler knows on day one, separated by commas or whitespace. Bracketed IPv6 (`[2001:db8::1]:4201`) is understood. Also `Crawler:Seeds`. |
| `MUI_CRAWL_ENABLED` | `true` | `false` makes this replica a pure web tier. Anything that is not `true` or `false` is refused at startup. Also `Crawler:Enabled`. |
| `Passkeys__ServerDomain` | *(unset)* | The WebAuthn relying-party ID. **Tied to §15.1** — see below. Only affects sign-in and claiming. |
| `Dataset__LicenceId`, `Dataset__LicenceName`, `Dataset__LicenceUrl`, `Dataset__Attribution`, `Dataset__Notice` | `CC-BY-4.0`, … | The terms the published data goes out under. Configuration rather than a literal because §15.2 is open and the code's licence is not the dataset's. |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Set in the image. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | *(unset)* | ASP.NET Core's own switch, for when a reverse proxy terminates TLS. |

Everything with a `Key:Path` name can equally be given as `Key__Path` in the environment; the two
`MUI_*` variables above are read from the environment first so a container is pointed at a database
and given a seed list the same way, with no config file shipped beside it.

**No environment variable can exempt a seed from the resolved-address gate.** §7.2's exemption — the
one that lets the crawler dial a private address — is a claim a person makes about one address they
chose on purpose, and a variable copied between deployments is not that person. It stays
`mui-crawl --seed-exempt`, typed by somebody who means it.

## Migrations

They are applied **at startup, by the process, before it serves a request**. There is no migration
step to run and no job to schedule.

`MigrationRunner` applies the numbered `.sql` files in lexical order, each in its own transaction,
recording each in the `mui_migration` ledger, and DDL is transactional in PostgreSQL so a migration
that fails halfway leaves nothing behind. It is idempotent: it runs in every replica on every start,
for ever, and a second run applies nothing.

```
info: MUIndex[0] Applied migration 0001_game.sql
…
info: MUIndex[0] Applied 8 migration(s): 0001_game.sql, …
info: MUIndex[0] Reading the catalogue from PostgreSQL.
```

A build that carries no embedded migrations refuses to start rather than reporting success against a
database with no schema.

The files are plain SQL at the repository root on purpose — legible to anyone with `psql`, which is
the property that matters when something has gone wrong at four in the morning. Applying them by
hand is a supported thing to do; the ledger is a two-column table.

## More than one replica

Run as many as you like. **They will not multiply the crawler.** The crawl loop is gated on a
PostgreSQL session-level advisory lock (spec §12): every replica asks, one gets it, and the rest sit
in a retry loop serving the site and nothing else.

```
replica 1: info: MUI.Crawler.CrawlerService[0] Holding the crawl lease. 1 of 1 configured seeds were new
replica 2: info: MUI.Crawler.CrawlerService[0] Another replica holds the crawl lease; this one will keep asking
```

What the lock guarantees, precisely:

- **One crawler per database**, not per host or per image. Two deployments pointed at the same
  database are one crawl; two deployments with separate databases are two crawls dialling everybody
  twice, which is a politeness failure and not a scaling strategy.
- **Failover with nothing to clean up.** The lock lives as long as the session that took it, so a
  replica that is killed, deploys away or loses the network releases it when the backend notices the
  socket has gone. There is no lease table and no expiry to tune.
- **Not a distributed transaction.** The holder re-asks the database every cycle whether it still
  holds the lock, because a connection that dropped unnoticed is a replica that believes it is the
  crawler and holds nothing.

What it does not do: it does not make the *web* tier stateful, and it does not decide which replica
crawls. If the crawl should run somewhere specific — a machine with a better route, or one you are
willing to have appear in other people's logs — set `MUI_CRAWL_ENABLED=false` everywhere else. A
replica told not to crawl says so once in its log rather than being quietly silent.

## Data protection keys

ASP.NET Core's key ring holds sign-in cookies and antiforgery tokens. With a read-only filesystem it
is ephemeral, which is fine for a single replica that nobody signs into and wrong the moment there
are two: an operator signed in against one replica is signed out by the next request.

The container's `HOME` is `/home/app`, so a writable volume there persists the ring:

```yaml
    read_only: true
    tmpfs: [/tmp]
    volumes:
      - keys:/home/app/.aspnet          # DataProtection-Keys lands here
```

Shared storage across replicas, or a key ring in the database, is the real answer once there is more
than one; there is nothing in the code that assumes either.

## The demo path

With no `MUI_POSTGRES` and no `ConnectionStrings:MUIndex`, the site starts on a fixture and **every
page carries a demo banner**. This is a correctness property of the packaging, not a convenience: a
directory whose whole claim is that its data is measured must never present invented data as though
it were real.

```
warn: MUIndex[0] No MUI_POSTGRES and no ConnectionStrings:MUIndex: serving DEMO data.
                 Nothing on this site was measured.
```

The publish workflow checks it on the image it just pushed. Sign-in and claiming are simply absent on
the demo path rather than present and broken — half a claim flow over invented games is a worse
answer than none.

## Publishing

`.github/workflows/publish.yml` builds the image on every push to `main` and pushes it to GHCR as
`ghcr.io/sharpmush/muindex:latest` and `:sha-<commit>`, with a build-provenance attestation. It is
separate from `ci.yml` on purpose: that workflow's job is to say whether the code is correct, and a
broken registry credential must not read as a failing test.

The SHA tag is the one a rollback needs — `latest` cannot say which build it is.

Nothing deploys the image. There is nowhere to deploy it to yet.

## What is still open

Two of the spec's open questions bear directly on running this, and neither is answered here.

### §15.1 — the domain

Undecided. It is not cosmetic, because **a passkey is bound to `Passkeys:ServerDomain` and every
credential registered under one value has to be registered again if it moves.** The levers, once it
is decided:

- `Passkeys__ServerDomain` — the registrable domain, set explicitly rather than inferred from the
  `Host` header, which the ASP.NET Core documentation calls a credential-scoping risk.
- `Dataset__Attribution` and the info URL the crawler self-identifies with (spec §11), so an admin
  reading their logs can find out who we are.
- Whatever terminates TLS, plus `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` if it is a reverse proxy.

Until it is decided, deploy without `Passkeys__ServerDomain` and accept that claiming is not usable —
that is honest, and re-registering every credential later is not.

### §15.3 — hosting and the cost envelope

Undecided, and it is the envelope that **bounds probe frequency and retention**. The levers it turns,
all of which are code today because a number chosen before the envelope is a number chosen at
random:

- **`DiscoveryOptions`** (`src/MUI.Discovery`) — `MaxConcurrency`, `BatchSize`, `PollInterval`,
  `GlobalInterval` and `PerHostInterval`. These set how much crawl a replica does per unit time and
  are the direct translation of a CPU and bandwidth budget.
- **`ProbeOptions`** (`src/MUI.Crawl`) — `Timeout`, `QuietPeriod`, `SilenceGrace`. Per-probe cost.
- **The revisit trigger, spec §4.13.** How often a *game* is re-probed, as distinct from how fast the
  loop turns. It is reasoned in the spec and is not to be reopened without a reason.
- **Retention** for `PresenceSample` before rollup (§15.4, also open) — the table that grows without
  bound, and therefore the one that decides what a disk costs. Nothing here deletes anything today;
  §7.5's archiving removes a game from a listing and from nothing else.
- The database itself: a managed Postgres, a container on the same box, or one you run. Everything
  above talks to it through `MUI_POSTGRES` and nothing here cares which.

`CRAWL DELAY` is honoured as a floor regardless of any of this, and no envelope decision may raise a
frequency past it.

## Checking a deployment

```bash
curl -fsS http://localhost:8080/api/games | head -c 200      # the read API, same reads as the pages
curl -fsS http://localhost:8080/ | grep -c demo-banner       # 0 with a database, 1 without
psql "$MUI_POSTGRES" -c 'SELECT name FROM mui_migration ORDER BY name'
```

There is no `/health` endpoint. `GET /` is the health check, because it exercises the reads the site
actually serves.

## Crawling by hand

`mui-crawl` runs cycles against a database and prints what landed, which is the thing to reach for
when a deployment's crawl is not doing what you expected. It is not in the image — it is a tool a
person runs on purpose, and an image that shipped it would invite it into an entrypoint.

```bash
MUI_CRAWL_POSTGRES=… dotnet run -c Release --project src/MUI.Crawler.Cli -- \
  --seed mush.pennmush.org:4201 --dry-run
```

`--seed-exempt` is the only way to point the crawler at an address that is not globally routable, and
it exists so somebody can dial their own `127.0.0.1` and mean it.
