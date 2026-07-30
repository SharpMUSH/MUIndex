# MUIndex

An information site for the MU\* hobby — MUSHes, MUDs, MUCKs, MOOs — whose distinguishing property
is that **its data is measured rather than asserted**.

Every fact on a game's page carries how it was obtained and how old it is. The catalogue is a
by-product of continuous measurement, not a form somebody filled in once.

> **Status:** design complete, implementation not started. What exists is the design, a brief for a
> site-design session, and a solution skeleton holding the few types the spec pinned down concretely.

Short form **MUI**, which is also the assembly prefix.

---

## Why

Every incumbent MU\* directory fails the same three ways.

| | Failure |
|---|---|
| **Unbounded moderation queues** | Listings sit "awaiting approval" for a year, so the catalogue never fills. |
| **Vote-driven rankings** | Gameable, then gamed, then worthless. Top Mud Sites became a link graveyard this way. |
| **Silent staleness** | No distinction between *listed*, *verified*, and *last seen alive*. Dead games sit beside live ones indefinitely. |

The sharpest illustration: MudStats went dark in late 2022 and returned in Sept 2024; The MUD
Connector died and returned in Jul 2023. **No directory noticed automatically — including their
own.** A returning game should re-list itself with no human involved, and here it does.

## How

Three design decisions, one per failure:

- **Auto-listing with opt-out.** Anything reachable that answers gets a page immediately, marked
  *discovered, unclaimed*. No queue to rot.
- **Rankings computed only from measured data.** There is no voting affordance anywhere on the
  site, and there never will be.
- **A permanent probe floor.** Failures lengthen the probe interval exponentially but never past a
  floor — a game dark for two years is still checked weekly, forever, *including after it has been
  archived*. Nothing is ever deleted.

### The archive

A game that stays dark eventually leaves the default listing, but only ever *into* the archive —
a browsable section in its own right, never a deletion. Its page, URL, history and change feed are
untouched, and a single successful probe restores it to the listing and fires the *came back* feed.
No human is involved in either direction.

How long it gets is tiered by what we actually measured, because a fortnight-old game and a
decade-old institution don't deserve the same benefit of the doubt:

```
grace = clamp(cumulative_measured_uptime / 4, 60 days, 365 days)
```

| Measured lifetime up | Grace before archiving |
|---|---|
| ≤ 8 months | 60 days |
| 1 year | 91 days |
| 2 years | 182 days |
| ≥ 4 years | 365 days |

Cumulative rather than span, so a game up two years out of five is credited with two. **A claimed
game always gets the ceiling** — someone with server access has demonstrably staked a claim.

## What gets measured

One telnet connection per probe, yielding four independent layers — not a fallback chain:

1. **The handshake**, which is a *measured* capability probe. What a server offers via `IAC WILL/DO`
   — GMCP, MSDP, MCCP, MXP, MSP, EOR, NAWS, CHARSET, MTTS, MNES — is observed, not claimed. MSSP
   can say `GMCP 1` and be wrong; the handshake cannot.
2. **The connect screen** — display asset and codebase fingerprint both.
3. **`WHO` / `DOING` at the login screen.** The MU\*-family advantage: Penn, MUX, Rhost and the
   TinyMUD family answer before login, so this is often a better count than MSSP `PLAYERS`. Parsed
   structurally rather than per-dialect, and it reports *unknown* rather than fabricating a zero.
4. **MSSP**, telnet option 70, with the plaintext `MSSP-REQUEST` fallback.

Discovery walks the MSSP `REFERRAL` graph, honours `CRAWL DELAY`, and verifies rather than trusts —
a referred host is a candidate hostname until it answers for itself.

## Shape

One ASP.NET Core deployable — public site, owner dashboard, and read API — with the crawler running
in-process as a `BackgroundService` against a shared database, gated on an advisory lock so replicas
don't multiply it.

The probe engine is built on
[TelnetNegotiationCore](https://github.com/HarryCordewener/TelnetNegotiationCore). **The crawler is
that library pointed outward**, which is also its own reward: consuming it from the client side
surfaces bugs that benefit SharpMUTerm and SharpMUSH.

Storage splits three ways, because the data has three shapes:

| Store | Shape | Why |
|---|---|---|
| Descriptive fields | Current value + source + age; append a row only *on change* | A field that never moves costs one row forever, not one per hour |
| Presence | High-volume time series, rolled up | Feeds the day × hour activity heatmap |
| Availability | Intervals, not samples | A game up for three years is one row; "longest outage" is arithmetic |

Keeping presence and availability apart is what makes the heatmap honest: **zero players is not the
same fact as unreachable.** A failed probe writes an availability transition and no presence sample,
so downtime leaves a gap rather than a run of zeroes that would render as a dead-but-running game.

## Catalogues

| Catalogue | v1 | Kept true by |
|---|---|---|
| **Games** | Full automation | Continuous telnet probing |
| **Clients** | Hand-written | Repo/release tracking, later |
| **Codebases** | Hand-written | Release tracking + crawl-derived usage counts |
| **Protocols** | Hand-written | Implementation matrix derived from measured handshakes |

Games dominate by a wide margin, deliberately.

## Not this

No forums, reviews, wikis, comments, or chat. No user ratings and no vote-driven rankings of any
kind. No player profiles or social graph. We do not host games and we are not a web client.

Player names are never persisted — `WHO` is parsed in memory, and activity aggregates use salted
hashes with a rotating salt, so unique-player estimates are possible while re-identification across
salt epochs is not.

## Documents

- **[`docs/specs/2026-07-30-mu-directory-design.md`](docs/specs/2026-07-30-mu-directory-design.md)**
  — the system design. Authoritative; read this first.
- **[`docs/design-brief.md`](docs/design-brief.md)** — input to a site-design session: the
  constraints design may not violate, and the areas it must decide.

## Building

.NET 10, with `TreatWarningsAsErrors` on solution-wide so a clean build means something.

```bash
dotnet build MUIndex.slnx -c Release
```

Tests are [TUnit](https://tunit.dev/) on Microsoft.Testing.Platform. `dotnet test` does **not** work
— .NET 10 dropped VSTest — so each suite is run directly, with `</dev/null` to stop the test host
waiting on stdin:

```bash
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests   </dev/null
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests     </dev/null
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Web.Tests       </dev/null
```

## Licence

Code is [MIT](LICENSE). The licence for the **published dataset** is a separate decision and is
still open — see the spec's open questions.
