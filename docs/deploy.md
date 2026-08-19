# Running MUIndex

There is one deployable. The public site, the owner dashboard, the read API and the crawler are a
single ASP.NET Core process (spec §4.11), and the only thing it needs beside it is a PostgreSQL.

**The domain is `mu-index.com` and the host is one small VM** — §15.1 and §15.3, closed, and written
down in [The domain](#the-domain) and [The host](#the-host) with the arithmetic behind the second.
The packaging itself stays neutral: a container image, a compose file and a workflow that publishes
the image, none of which name either. What a deployment decides, it decides in the environment.

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
| `MUI_CRAWL_SEEDS` | *(empty)* | `host:port` addresses the crawler knows on day one, separated by commas or whitespace. An IPv6 literal must be bracketed — `[2001:db8::1]:4201` — because `2001:db8::1:4201` does not say which colon is the port, and an address that could mean two things is refused at startup rather than guessed at. Also `Crawler:Seeds`. |
| `MUI_CRAWL_ENABLED` | `true` | `false` makes this replica a pure web tier. Anything that is not `true` or `false` is refused at startup. Also `Crawler:Enabled`. |
| `MUI_CRAWL_INFO_URL` | *(a placeholder)* | Where an admin who has just been dialled reads what we do and asks us to stop (§11), announced over TTYPE and MNES to every server the crawler reaches. Must be an absolute **https** URL or startup refuses it. Also `Crawler:Probe:InfoUrl`. |
| `MUI_DNS_CLAIMS_ENABLED` | `true` | `false` stops this deployment reading TXT records for games somebody is mid-claim on (§8.3). On by default — it opens no socket to any game, and costs one lookup per host with a live claim — but it is the only pass that makes outbound DNS queries of its own, so a deployment with no egress can turn it off rather than read a warning on a loop. Also `Crawler:DnsClaims:Enabled`. |
| `Passkeys__ServerDomain` | *(unset)* | The WebAuthn relying-party ID — the **registrable** domain, `mu-index.com`. Only affects sign-in and claiming. |
| `MUI_MCP_TOKEN` | *(unset)* | The bearer secret that gates `/mcp` (see [Administering the site over MCP](#administering-the-site-over-mcp)). **Unset means every request fails authentication** — fail closed, not fail open. Also `Mcp:Token`. |
| `Dataset__LicenceId`, `Dataset__LicenceName`, `Dataset__LicenceUrl`, `Dataset__Attribution`, `Dataset__Notice` | `CC-BY-4.0`, … | The terms the published data goes out under. Configuration rather than a literal because §15.2 is open and the code's licence is not the dataset's. |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Set in the image. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | *(unset)* | ASP.NET Core's own switch, for when a reverse proxy terminates TLS. |

Everything with a `Key:Path` name can equally be given as `Key__Path` in the environment; the two
`MUI_*` variables above are read from the environment first so a container is pointed at a database
and given a seed list the same way, with no config file shipped beside it.

**The contact address is a setting and not a default, on purpose.** The compiled-in value is
`https://muindex.example/crawler`, which answers nobody, and it stays that way now that the domain is
settled: a default naming this deployment's contact page would have every fork and every laptop run
announce *our* address to the servers *they* dial, which is a claim about somebody else's crawl.
`/about` compares what it holds against that default and marks the page when they match, so a
deployment that forgot is visible to a reader rather than only to whoever it dialled.

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

```text
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

```text
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

```text
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

## The domain

`mu-index.com`, registered through Cloudflare, which is therefore also the DNS.

| Record | Points at | Proxy |
| --- | --- | --- |
| `mu-index.com`, `www` | the VM | either; see below |
| `crawler.mu-index.com` | the crawl egress address | **DNS-only, and this one is not a preference** |

`crawler` has to be grey-clouded because the reverse record depends on it. An admin who finds an
unfamiliar connection runs `dig -x` on the address, and the PTR — set at the VM provider, which is
the only place that can set it, not at Cloudflare — answers `crawler.mu-index.com`. That is worth
something only if resolving the name *forward* lands back on the same address. Proxied, it resolves
to Cloudflare's anycast addresses, the two do not agree, and the identification we owe under §11
fails at the one step somebody actually takes.

**Proxying the site buys no origin secrecy here, and it is not supposed to.** This deployment opens
connections to thousands of strangers' machines and its address is in all their logs; being
identifiable is the obligation, not a leak. Proxy the site for TLS, for absorbing a flood, or for
serving `wwwroot` — not to hide, which this design cannot do and does not want.

Three things must be off, or the site stops being what it says it is:

- **"Cache Everything", and anything that caches HTML.** Every page states how old the measurement on
  it is. A CDN answering from a copy makes that sentence a lie, with no way for the reader to tell.
  Cache rules for `wwwroot` and nothing else.

  The corollary bit once and is worth writing down: **`wwwroot` is cached for four hours and a deploy
  does not purge it**, so an asset linked by a flat name is served from before the deploy for as long
  as the edge copy lives. The site shipped new markup against a stylesheet written before the graphic
  in it existed, which reads as a broken page rather than as a stale one. The site now links its
  stylesheet at a fingerprinted address (`app.<hash>.css`, from `MapStaticAssets`), so the URL changes
  when the bytes do and no purge is needed — but **anything added to `wwwroot` and referenced by a
  flat name inherits the original trap.**
- **Bot Fight Mode and the interstitial challenges.** `?plain=1`, `curl` and `/api/games` are surfaces
  meant to be read by programs (§9, §10). A JavaScript challenge in front of them defeats the point
  of having them.
- **Rocket Loader and anything that injects script.** The site ships no required JavaScript, which is
  a property somebody checks by turning scripting off. A CDN adding some undoes it.

The rest of the zone, once:

| Setting | Value | Why |
| --- | --- | --- |
| SSL/TLS mode | **Full (strict)** | With the Origin CA certificate in place it verifies something. `Flexible` would send plaintext to the origin over the public internet. |
| Always Use HTTPS | on | |
| Minimum TLS | 1.2 | |
| DNSSEC | on | One click at the registrar half, which is the same account. |
| HSTS | **later** | It is a promise browsers cache for as long as it says. Turn it on once the certificate path has been stable for a while, not on the first day. |
| Email Routing | `crawler@`, `abuse@` | "Ask us to stop" has to reach a person. It is the address `/about` publishes. |
| Cache rules | static assets only | See above. Never "Cache Everything". |

Behind the proxy, `Submissions__TrustedProxyHops` is **2** — Cloudflare, then Traefik — because both
append to `X-Forwarded-For` and the middleware walks exactly that many hops. A forwarded header
nobody counted is a header anybody may write, and the thing reading it is a rate limit.

### The two names that outlive the host

`Passkeys__ServerDomain` is `mu-index.com` — the registrable domain, not `www.mu-index.com`, which
would not cover the apex. Every credential registered under one value has to be registered again if
it changes, so it is set explicitly rather than inferred from the `Host` header, and it must never be
a hostname the hosting provider gave us.

`MUI_CRAWL_INFO_URL` is `https://mu-index.com/crawler`, which redirects to the part of `/about` that
explains what a probe does and the three ways to stop it. It is short because it is retyped by hand
off a log line by somebody who is already mildly annoyed.

Both of these are reasons the domain had to be decided before the host and not after: they are what a
stranger holds, and moving them costs other people something.

## The host

One VM: the app, Postgres and a reverse proxy on it, dual-stack, with a dedicated IPv4 whose reverse
record we control. What picks it is not CPU.

- **Arbitrary outbound TCP** to thousands of hosts on ports between 23 and 9999, held open for
  seconds. That alone rules out every serverless and edge runtime.
- **An always-on process.** The crawl loop polls on a timer and holds a session-level advisory lock;
  scale-to-zero has nothing to hold it.
- **A stable egress address.** This is a *data* requirement rather than an operational one. §7.4's
  reachability series measures a socket from one vantage point, and moving the vantage point changes
  what the series means without changing anything the schema records. See
  [what is still open](#what-is-still-open).
- **Somewhere that tolerates the traffic and answers mail about it.**

The arithmetic, so that nobody has to guess later: at ~3,000 registry entries and §7.7's intervals —
two hours for a game with players on it, six for a quiet one, a week for one long dark — the crawl
makes roughly 6–7k probes a day. That is 0.08 connections a second against the four a second
`DiscoveryOptions.GlobalInterval` already permits: about two per cent of our own politeness ceiling,
eight sockets at a time, and something like 5–10 GB of traffic a month. `presence_sample` grows by a
couple of million rows a year, monthly-partitioned. **The envelope is not what bounds probe frequency
here; politeness is** — which is the useful half of closing §15.3, because it means no number in
`DiscoveryOptions` or `ProbeSchedule` should be re-tuned to fit a bill.

### Docker publishes past the host firewall

The compose file publishes `${MUI_PORT:-8080}:8080`, and Docker implements that by writing iptables
rules **ahead of** `ufw`. A host firewall that denies 8080 does not close it: the site answers the
internet directly on 8080, beside the proxy holding its certificate. Both halves of the fix:

```bash
MUI_PORT=127.0.0.1:8080          # in .env — an interface may be written into this value
```

and a firewall the container runtime cannot edit, outside the VM — on Hetzner, a Cloud Firewall:
inbound 80 and 443, SSH from somewhere known, everything else denied; **outbound unrestricted**,
which the crawl needs. Postgres publishes no port at all and wants none; it is reachable on the
compose network and nowhere else.

While there: give Docker a log rotation (`max-size`, `max-file` in `/etc/docker/daemon.json`).
Unrotated JSON logs are the ordinary way a 40 GB volume fills.

### Bringing it up

**Start with `MUI_CRAWL_ENABLED=false`, and turn the crawl on as a separate, deliberate step.** It is
the first line of `.env` to get right rather than a caveat further down, because the moment the site
starts it opens sockets to other people's machines — and everything that is easy to get wrong on a
first deployment is wrong *at* that moment: an unset `MUI_CRAWL_INFO_URL`, an image that predates the
setting, a restored catalogue whose targets are all overdue and therefore all dialled at once. The
site serves its whole catalogue with the crawl off, so every check below is available before a single
connection leaves the box. Turning it on afterwards costs one restart; turning it off afterwards
costs somebody else's logs.

Three files in `deploy/` and one `.env`. The repository is cloned to `/opt/muindex` for its compose
files rather than as a build tree — the image is pulled, never built here.

```bash
git clone https://github.com/SharpMUSH/MUIndex.git /opt/muindex && cd /opt/muindex
cp .env.example .env && $EDITOR .env        # server block, password, MUI_CRAWL_ENABLED=false
mkdir -p deploy/tls                          # origin.pem and origin.key go here, from Cloudflare
install -m 644 deploy/muindex.service /etc/systemd/system/muindex.service
systemctl daemon-reload && systemctl enable --now muindex
```

Then, once `/about` shows a contact address rather than the placeholder and the checks below pass:

```bash
sed -i 's/^MUI_CRAWL_ENABLED=false/MUI_CRAWL_ENABLED=true/' .env
docker compose up -d web                     # recreates it with the new environment
```

`COMPOSE_FILE` in `.env` is what makes a bare `docker compose` command read
`deploy/compose.production.yaml` as well, so neither the unit nor a person at three in the morning
has to remember a `-f` flag. What the overlay changes:

- **Nothing is published.** The base file's `8080` mapping is dropped rather than narrowed. Traefik
  reaches the site over the compose network, and the safest number of host ports to argue about with
  Docker's iptables rules is none.
- **Traefik** terminates TLS for `mu-index.com` and `www`, redirects `www` to the apex, and answers
  `crawler.mu-index.com` with a redirect to `/crawler` — the same three jobs Caddy used to do, now as
  labels on `web` rather than a second config file, because Traefik's Docker provider can tell which
  of `web`'s containers is actually answering `GET /health` and route only to those.
- **`web` runs as two containers** (`deploy.replicas: 2`), so a version cutover has one still serving
  while the other is mid-swap, and **Watchtower replaces them one at a time**
  (`WATCHTOWER_ROLLING_RESTART=true`) rather than stopping both before starting either replacement.
  Watchtower still touches nothing but `web`.
- **`Submissions__TrustedProxyHops=2`** — Cloudflare, then Traefik. Both append to `X-Forwarded-For`,
  so the middleware walks two hops and lands on the address Cloudflare actually saw; a client that
  forges the header has its value pushed out of reach rather than believed.

The containers carry `restart: unless-stopped`, so Docker alone brings them back after a reboot. The
unit is what makes that deterministic: it applies the compose files *as they are on disk*, so an edit
takes effect on the next boot instead of a container returning as whatever it was when it was
created, and `systemctl stop muindex` means the stack rather than a container the next boot
resurrects.

### Certificates, and why they come from a file

The apex is proxied, so Cloudflare terminates TLS at the edge and the origin certificate is only ever
presented to Cloudflare. That is what a **Cloudflare Origin CA** certificate is for: issued in the
dashboard, valid for years, not publicly trusted and not needing to be. Paste the pair into
`deploy/tls/origin.pem` and `deploy/tls/origin.key` (gitignored), and set SSL/TLS to **Full
(strict)** — which then checks something real instead of accepting whatever the origin presents.

`crawler.mu-index.com` is the exception and gets an ordinary Let's Encrypt certificate, because it is
DNS-only: port 80 reaches this box directly, so HTTP-01 works and no challenge has to be routed
through a proxy. That is the same property the PTR depends on, doing a second job.

### Updating, and what it costs

Watchtower polls GHCR for a changed digest every five minutes and swaps the site container when
`.github/workflows/publish.yml` publishes a new `latest` from `main`.

**`WATCHTOWER_LABEL_ENABLE` is the load-bearing setting.** Only containers carrying
`com.centurylinklabs.watchtower.enable=true` are updated, and only `web` carries it. Postgres is
labelled `false` on purpose: an automatic major-version bump refuses to start against an existing
data directory, and the catalogue is the asset. It gets upgraded by hand, after a dump.

The cost of this arrangement, stated rather than discovered: **a merge to `main` is on the public site
within five minutes, with no human between the two.** That is a deliberate trade for a project of
this size, and the SHA tags are what make it survivable — `docker compose down web` and a `docker run`
against `ghcr.io/sharpmush/muindex:sha-<commit>` is the rollback, and `latest` cannot name a build.
If that trade stops being worth it, point the overlay at a `:release` tag that a person moves.

If the GHCR package is private, Watchtower needs credentials: `docker login ghcr.io` on the host with
a token that has `read:packages`, which lands in `/root/.docker/config.json` — already mounted. Making
the package public is one setting and removes the credential entirely.

### Before committing to an address

Boot the box and probe about twenty games from `docs/codebase-survey-2026-07-30.md` from it, then run
the same list from somewhere else and compare. Cheap hosting ranges are filtered by a fair number of
operators, and a filtered range does not look like a filtered range from here — it looks like other
people's games being unreachable, which is our decision appearing in their public record as a fact
about them (rule 5). If the two lists disagree, the range is the reason; an address is a few euro and
a PTR update, and a catalogue of quietly wrong reachability is not recoverable.

```bash
MUI_CRAWL_INFO_URL=https://mu-index.com/crawler \
  dotnet run -c Release --project src/MUI.Probe -- mush.pennmush.org 4201
```

### Backups leave the provider

Nothing is ever deleted (rule 3), so the database is the whole asset — every measurement is a moment
that cannot be re-observed. Snapshots at the provider are worth having and are not a backup: they are
crash-consistent images inside the same account that an abuse ticket suspends. A nightly `pg_dump`
belongs somewhere with a different login, and it is a backup only once it has been restored once.

## Starting from an existing catalogue

## Turning on Intermud-3

I3 reaches games the telnet probe cannot count: the LP family predates MSSP and never adopted it,
and its login prompts take a character name rather than commands, so `WHO` at a connect screen is
read as a name and there is nothing to parse. `who-req` answers with an array of users instead, and
the count is its length.

It is off, and it takes **two** switches in `.env`, because they do different things:

```bash
COMPOSE_PROFILES=i3        # starts the sidecar — this is the irreversible half
MUI_I3_ENABLED=true        # makes the site talk to it
MUI_I3_NAME=MUIndex
MUI_I3_ADMIN_EMAIL=you@example.com
MUI_I3_SECRET=$(openssl rand -hex 32)
MUI_I3_API_KEY=$(openssl rand -hex 32)
```

`COMPOSE_PROFILES` is read out of `.env` by Compose itself, so no command has to remember a `--profile`
flag — the same arrangement `COMPOSE_FILE` already uses, and for the same reason.

**Starting the sidecar registers a mud name on a public network, permanently.** I3 mudlist entries
are never removed, only marked down; the live list still carries `probe-test-12345` and
`Daniel's Test Server` years after whoever made them stopped. Whatever `MUI_I3_NAME` says on the
first connect is what the network remembers. The documented test router — `*wir` at
`136.144.155.250:3004` — does not answer, while `*wpr` responds on the same address at 8080, so
there is no rehearsal to be had and the first connection is a real one.

What the network is told about us is `deploy/i3/config.yaml`: player port 0, `open_status: n/a`, and
one service. Not none — `*i4` silently discards a startup packet that advertises no services at all,
with no reply and no error packet, so `who` is on. The gateway answers it from local presence, which
is an empty list, which is true.

**The first pass seeds roughly 130 addresses**, all due immediately, which the ordinary crawler then
dials at its usual batch size. That is a visible step up in outbound traffic for a cycle or two. It
is the point of the feature, and it is worth watching the first time:

```bash
docker compose logs -f web | grep -i intermud
```

Two operational notes:

- **The `i3state` volume is not disposable.** The router assigns a password on first connect and the
  sidecar persists it there. Lose it and the next startup packet goes out with password 0, which is
  a re-registration rather than a reconnect. It belongs in the same backup as the database.
- **Watchtower does not update the sidecar**, and that is deliberate. It is unlabelled and
  `WATCHTOWER_LABEL_ENABLE` is on, so nothing touches it but a person. Its build is pinned to a
  commit rather than to `main`, because it is the one container holding our router credential and a
  branch is whatever upstream merged that morning.

## Starting from an existing catalogue

A deployment can be brought up on a database somebody already crawled. Restore **before** the site
has ever run, not after: `MigrationRunner` applies what is missing from the ledger, so an older
catalogue is carried forward by starting the site on it, and a restore into an already-migrated
database collides with tables it just made.

```bash
docker compose up -d postgres                                  # postgres alone; no site yet
docker compose exec -T postgres \
  pg_restore -U muindex -d muindex --no-owner --no-acl < muindex-YYYY-MM-DD.dump
docker compose exec postgres \
  psql -U muindex -d muindex -c "DELETE FROM mui_migration WHERE name LIKE '0100_%'"
docker compose up -d web                                       # applies 0009+ on the way up
```

Two things about an old catalogue, both of which have already been true once:

- **`import_provenance` and its `0100_` migration must not come across.** The table and everything
  around it are deleted from this project (§7.6) and a database predating that still has both. Dump
  with `--exclude-table=import_provenance`, and delete the ledger row, which names a file the tree no
  longer contains.
- **Its measurements were taken from wherever it was crawled.** Presence and availability rows carry
  no vantage point (see [what is still open](#what-is-still-open)), so a catalogue crawled from a
  laptop and one crawled from this host are indistinguishable once merged. That is a decision to make
  deliberately: the registry — games, endpoints, crawl targets, fields — is the part that is
  vantage-independent, and `--exclude-table=presence_sample --exclude-table=availability_interval`
  takes it without the part that is not.

**Every target restored is due immediately**, its last probe being however old the dump is. The first
cycle after the site comes up is therefore a burst — bounded by `GlobalInterval` and `MaxConcurrency`,
and `CRAWL DELAY` still wins per host, but several hundred connections leave in the first few minutes
rather than spread over a day. That is the reason the crawl is off for the first start: a restored
catalogue turns every mistake in `.env` into a mistake made at once, to everybody in it.

## What is still open

§15.1 and §15.3 are answered above. Three things this document now touches are not.

**The vantage point is not recorded anywhere, and this document has just named it.** §7.4's
reachability series is a socket measured from one address; nothing in the schema says which. Move the
crawl to another host and the intervals either side of the move describe two different measurements
under one heading, with nothing to tell them apart. The same gap has a sharper edge: a game whose
firewall drops our range is recorded as unreachable, which is our decision appearing in their public
record as a fact about them — the thing rule 5 exists to prevent. Naming the host here and on the
methodology page is the floor. A column is a schema decision and wants its own review, and it is the
prerequisite for a second vantage point ever being sensible.

**Retention is implemented and unset (§15.4).** `PresenceRetentionOptions` drops whole partitions and
never rows, and every window on it — `RawSamples`, `HourlyRollups`, `DailyRollups` — defaults to
`null`, which means for ever. That is the right default for a catalogue whose whole claim is that
nothing is deleted, and it is also the number that decides what a disk costs, so it should be set
deliberately rather than discovered. The floor under raw is `HeatmapWindow`: the site reads raw
samples for the day × hour graphic, so a shorter retention blanks the left-hand end of every heatmap.

**§15.2 — the licence for the published data** is still open. `Dataset__LicenceId` and the four
settings beside it ship as CC-BY-4.0 because a deployment has to send *something* with the API, not
because the question is answered.

## Checking a deployment

```bash
curl -fsS http://localhost:8080/health                        # 200 once the process can serve a request
curl -fsS http://localhost:8080/api/games | head -c 200      # the read API, same reads as the pages
curl -fsS http://localhost:8080/ | grep -c demo-banner       # 0 with a database, 1 without
curl -fsS http://localhost:8080/about | grep -c placeholder  # 0 once a contact address is set
curl -isS http://localhost:8080/crawler | head -2            # 302 to /about#about-crawler
psql "$MUI_POSTGRES" -c 'SELECT name FROM mui_migration ORDER BY name'
```

The third and fourth curl lines are the two halves of §11: an address to reach us at, and an address
that answers. Both are checkable from outside, which is the point — they are the only settings here
whose failure is visible to a stranger before it is visible to us.

`GET /health` is what the Dockerfile's own `HEALTHCHECK` and the production overlay's Traefik
`loadbalancer.healthcheck` both poll — 200 once the process can actually serve a request, checking
Postgres reachability when a connection string is configured. It is the readiness check, not a
substitute for the one above: `GET /` still exercises the reads the site actually serves, and is worth
running by hand the same way.

## Crawling by hand

`mui-crawl` runs cycles against a database and prints what landed, which is the thing to reach for
when a deployment's crawl is not doing what you expected. It is not in the image — it is a tool a
person runs on purpose, and an image that shipped it would invite it into an entrypoint.

```bash
MUI_CRAWL_POSTGRES=… MUI_CRAWL_INFO_URL=https://mu-index.com/crawler \
  dotnet run -c Release --project src/MUI.Crawler.Cli -- \
  --seed mush.pennmush.org:4201 --dry-run
```

It reads `MUI_CRAWL_INFO_URL` for the same reason the deployable does: an admin cannot tell a hand-run
cycle from the site's own, and both are connections to their machine. A real cycle started without one
says so on its way past — a dry run dials nobody and owes nobody an address.

`--seed-exempt` is the only way to point the crawler at an address that is not globally routable, and
it exists so somebody can dial their own `127.0.0.1` and mean it.

`--rename <slug> <newName> --because "…"` renames a game and mints it a new, unique slug at once —
the immediate, no-grace path a verified owner's own rename already takes (spec §5.7), for a game with
no claimed owner or where staff has decided what it is called. The old slug redirects to the new page
for ever; nothing else about the game moves.

## Administering the site over MCP

For everything short of `--seed-exempt`, `/mcp` is the alternative to ssh'ing in and running
`mui-crawl` through `docker compose run --rm --no-deps ... --entrypoint /cli/mui-crawl web ...`. It is
an authenticated [Model Context Protocol](https://modelcontextprotocol.io) endpoint mounted inside
this same deployable (Streamable HTTP transport, `ModelContextProtocol.AspNetCore`), so an MCP client
— Claude Code, calling over HTTPS — can seed the crawler, record an opt-out, list what's due, force a
crawl pass, read the registry/crawl summary, hand-set one field of one game, rename a game, or merge
a duplicate pair, without a shell on the box.

Set `MUI_MCP_TOKEN` (`openssl rand -hex 32`, never committed) and point a client at
`https://<site>/mcp` with `Authorization: Bearer <token>`. Unset, every request gets a 401 and MUI.Web
says so once at startup — this endpoint fails closed, never open.

The nine tools (`src/MUI.Web/Mcp/MuiMcpTools.cs`) mirror `mui-crawl`'s CLI surface — `crawl_seed_add`,
`crawl_opt_out_record`, `crawl_opt_out_check`, `crawl_due_targets`, `crawl_run_cycle`,
`crawl_summary` — plus three new capabilities. `game_field_set` is a staff override of a single
`GameField` row (`FieldSource.Staff`, spec §5.1) for fixing a mis-parsed value by hand without raw
SQL, and explicitly declines to re-mint a game's slug when the field is `NAME`. `game_rename` (also
`mui-crawl --rename`) is that missing half: it writes `NAME` through the same staff override and then
runs `SlugMinter`'s immediate mint-and-rename path, retiring the old slug into `game_slug_history` so
`FormerSlugRedirects` 301s it for ever — a collision with another game's slug is not an error, it just
mints a numbered suffix the same way any other mint does. `game_merge` (also `mui-crawl --merge
--because`) drains one `duplicate_review` pair by hand (spec §7.3) through `ReviewMergeService`: it
folds a loser slug into a winner slug, resolves an open review naming that pair if one exists
(carrying its score and signals onto `merge_log` unchanged), and is still usable on a pair the
identity matcher never flagged, recorded as a judgement with no signals. The loser's page 301s to the
winner's for ever; nothing else about the loser is touched. It refuses — surfacing the schema's own
message — on a loser already absorbed elsewhere (`merge_log_absorbed_once_idx`) or a redirect chain
(`merge_log_no_chains`, e.g. a game renamed and then asked to absorb another). `crawl_run_cycle`'s
real (non-dry) run reuses the exact same `CrawlCycle` and advisory-lock machinery the hosted crawler
uses, so it correctly no-ops rather than double-crawls while the hosted crawler holds the lease — see
the tool's own description for why that is not a bug.

`crawl_summary` returns the registry totals in full but only a page of the per-game listing (`games`,
default 25; `offset` to walk the rest; `games=0` for totals alone). The whole listing outgrew what an
MCP client will accept in one answer — at ~900 games it is 140 KB, and a client that refuses the
result gives the caller nothing at all, so a page beats a complete answer nobody can read. `mui-crawl`'s
own printout is unpaged, since a terminal can take it.

**A deploy ends every open MCP session.** Watchtower recreates `muindex-web-1`/`-2` within five
minutes of an image push, and the new container knows nothing of a client's connection: an MCP client
mid-task starts reporting `MCP server "muindex" is not connected` and will not necessarily reconnect
on its own. This is expected, not a fault in the endpoint — reconnect the client (in Claude Code,
`/mcp`) once the new containers report healthy. Do not run a long batch of MCP administration while
PRs are landing.
