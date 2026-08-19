# MUIndex

A directory for the MU\* hobby — MUSHes, MUDs, MUCKs, MOOs — where every fact is measured, not
submitted. `mui-crawl` probes live servers over telnet; the site renders what it found, with the
source and age of every field. No signup form, no moderation queue.

Live at [mu-index.com](https://mu-index.com).

## Why

Every existing MU\* directory fails the same way: moderation queues that never clear, vote-driven
rankings that get gamed into worthlessness, and no way to tell *listed* from *verified* from *last
seen alive*. MudStats and The MUD Connector both went dark for over a year and came back — neither
directory noticed on its own.

MUIndex fixes each of those directly:

- **Auto-listed, opt-out.** Anything that answers a probe gets a page immediately, marked
  *discovered, unclaimed*. No queue to rot in.
- **No voting.** Rankings are computed from measured data only.
- **Nothing is deleted.** A dark game keeps being probed on a permanent floor interval and moves to
  the archive rather than vanishing. One successful probe brings it back, automatically.

### Opting out

| Route | Publish | Covers |
|---|---|---|
| MSSP | `MUINDEX OPT-OUT 1` | The listener that published it |
| DNS | `_muindex.your.host. IN TXT "opt-out"` (or `"opt-out=4201"` for one port) | The whole host unless a port is named |
| Claim | [Claim](https://mu-index.com) the game, then flip it off from the owner dashboard | Whatever you claimed |
| Ask | [Discord](https://discord.gg/KNRGZnQGpa) | Whatever was asked for |

Honoured within one crawl cycle. The page, address, and everything measured before the opt-out stay
up — stopping isn't deletion and isn't downtime. There's no email support — Discord or a claim are
the only ways to reach a human.

## What gets measured

One telnet connection, four independent signals:

- **Handshake** — which options the server offers (GMCP, MSDP, MCCP, MXP, MSP, EOR, NAWS, CHARSET,
  MTTS, MNES), observed rather than declared.
- **Connect screen** — banner and codebase fingerprint.
- **WHO / DOING** at the login screen — often a better player count than MSSP, and reports *unknown*
  rather than a fabricated zero.
- **MSSP** (telnet option 70) — asked for explicitly rather than waited on.

Discovery follows the MSSP `REFERRAL` graph and verifies every referred host before trusting it.

## Shape

One ASP.NET Core deployable: public site, owner dashboard, read API, and a crawler running
in-process as a `BackgroundService`, gated on a Postgres advisory lock so replicas don't multiply it.
Built on [TelnetNegotiationCore](https://github.com/HarryCordewener/TelnetNegotiationCore).

Storage splits by shape: descriptive fields keep current value + source + age and append a row only
on change; presence is a rolled-up time series feeding the activity heatmap; availability is
intervals, not samples, so "longest outage" is arithmetic. An hour is filled (measured, including a
real zero), hatched (measured but uncountable), or empty (not measured) — never assumed down.

## Not this

No forums, reviews, comments, ratings, or player profiles. No hosting, no web client. Player names
are never persisted — `WHO` is parsed in memory and aggregates use salted, rotating hashes.

## Docs

[`docs/specs/2026-07-30-mu-directory-design.md`](docs/specs/2026-07-30-mu-directory-design.md) is
the system design and the authoritative source for anything below.

## Build & test

.NET 10, `TreatWarningsAsErrors` on.

```bash
dotnet build MUIndex.slnx -c Release
```

`dotnet test` doesn't work — .NET 10 dropped VSTest — so run each suite directly:

```bash
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests    </dev/null
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests      </dev/null
dotnet run -c Release --no-build --project tests/MUI.Crawler.Tests    </dev/null
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests  </dev/null
dotnet run -c Release --no-build --project tests/MUI.I3.Tests         </dev/null
dotnet run -c Release --no-build --project tests/MUI.Web.Tests        </dev/null
```

## Run

```bash
docker compose up --build      # http://localhost:8080
```

No database configured? It still starts, on a fixture, and says so on every page. See
[`docs/deploy.md`](docs/deploy.md) for the real thing.

## Licence

Code is [MIT](LICENSE). The dataset's licence is still undecided.
