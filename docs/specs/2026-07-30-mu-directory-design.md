# MUIndex — design

**Status:** approved design, pre-implementation.
**Date:** 2026-07-30.
**Name:** **MUIndex**, short form **MUI** — also the assembly prefix (`MUI.Catalog`, `MUI.Crawl`,
`MUI.Discovery`, `MUI.Web`). Deliberately not `Sharp`-prefixed: a directory that indexes PennMUSH,
Evennia and DIKU games on equal terms should not wear one server's brand.

---

## 1. What this is

An information site for the MU\* hobby — MUSHes, MUDs, MUCKs, MOOs — whose distinguishing
property is that **its data is measured rather than asserted**. Every fact on a game's page
carries how it was obtained and how old it is. The catalogue is a by-product of continuous
measurement, not a form somebody filled in once.

Four catalogues, weighted heavily toward the first:

| Catalogue | v1 | How it stays true |
|---|---|---|
| **Games** | Full automation | Continuous telnet probing (§6) |
| **Clients** | Hand-written pages | Repo/release tracking, later |
| **Codebases** | Hand-written pages | Repo/release tracking + crawl-derived usage counts |
| **Protocols** | Hand-written pages | Implementation matrix derived from measured handshakes |

## 2. Non-goals

Explicitly out of scope, permanently:

- Forums, reviews, wikis, comments, chat.
- User-submitted ratings and **vote-driven rankings of any kind**. Rankings are computed from
  measured data only. Vote-gaming is what reduced Top Mud Sites to a link graveyard.
- Player profiles or a social graph.
- Hosting games, or being a web client.
- Storing player identities. Aggregates only (§11).

## 3. Why it exists

The incumbent directories fail in three recurring ways, and each failure maps to a design
decision here.

| Site | State (as researched 2026-07) | Failure |
|---|---|---|
| The MUD Connector | Died; revived Jul 2023; no new listings past ~Sep | Unbounded moderation queue |
| Top Mud Sites | "Zombie" — links to dead games, never updates | Vote-driven, no liveness measurement |
| MudStats | Defunct late 2022, back Sep 2024 | Single maintainer, no automation floor |
| MudVerse | Active; hourly MSSP crawl; purged dead games | Closest prior art; MUD-centric |
| Grapevine | Active; listings, events, one-shot MSSP checker | MUD-centric; checker is not continuous |
| MUNexus | Small; RP-focused; **manual** verification when MSSP absent | Human verification does not scale |
| MUSHCode.com, mush.wikidot | Hand-maintained MU\* lists | Stale since ~2009 |

Note the pattern in the revival dates: MudStats came back in Sept 2024 and TMC in Jul 2023, and
*no directory noticed automatically* — including their own. §7's permanent backoff floor exists
because of this.

### 3.1 MSSP is not the gap

Contrary to a common assumption, MSSP coverage across MUSH codebases is broadly fine:

| Codebase | MSSP | Evidence |
|---|---|---|
| PennMUSH | Yes, 1.8.4+ | `mssp <field>/<value>` in `game/mushcnf.dst`; `src/bsd.c`, `hdrs/conf.h`, `CHANGES.184` |
| TinyMUX | Yes, 2.14+ | Ships alongside GMCP and WebSocket support |
| Evennia | Yes | `evennia/server/portal/mssp.py`, per-game `server/conf/mssp.py` |
| RhostMUSH | **No** | Only the `TELNET_TELOPT_MSSP` constant in vendored `libtelnet`; no server implementation |
| TinyMUSH 3.x | **No** | No occurrences in repo |

The real gaps are different, and both are addressed by §6:

1. **MSSP on a MUSH is largely hand-typed static text that rots.** PennMUSH auto-fills nine
   fields (NAME, PLAYERS, UPTIME, PORT, CODEBASE, FAMILY, PUEBLO, SSL, WEBSITE); everything else
   is a string set once at install. A crawler that presents all MSSP fields with equal confidence
   is publishing a 2017 answer as a live one.
2. **A game can claim capabilities it does not have.** `GMCP 1` in MSSP is an assertion. The
   telnet handshake is an observation.

### 3.2 The MSSP spec already provides two things this project needs

From <https://tintin.mudhalla.net/protocols/mssp/>:

- **`REFERRAL`** — "other MSSP-enabled MUDs to crawl". The crawl-tree mechanism is standardised;
  seeds need not be hand-curated.
- **`CRAWL DELAY`** — minimum hours between crawler visits. The politeness contract is standardised.

The taxonomy is also more MU\*-usable than its DIKU-shaped reputation suggests: `GAMEPLAY`
includes Roleplaying and Social, `GAMESYSTEM` includes World of Darkness and d20, `SUBGENRE`
includes Urban Fantasy, Dark Fantasy, Steampunk and Cyberpunk. `CHARSET`, `DISCORD`, `IPV6` and
`SSL` became official in Dec 2022.

**Decision:** MSSP is the baseline vocabulary. Owner enrichment sits on top of it. No new protocol
namespace is invented.

Genuinely absent from MSSP and therefore owner-supplied: fandom/IP (`SUBGENRE` cannot say "Marvel"
or "Exalted"), character application process, RP enforcement level, and consent/content tooling.
Peak activity hours are *derived from the crawl*, not asked for.

## 4. Locked decisions

1. **Centre of gravity: the truth engine.** Live data, provenance, history. The catalogue follows.
2. **Breadth:** games, clients, codebases, protocols — games first by a wide margin.
3. **Taxonomy:** MSSP defaults, plus owner enrichment.
4. **Auto-listing, opt-out.** Anything reachable that answers gets a page, marked *discovered,
   unclaimed*. Rationale: a game emitting MSSP is broadcasting for crawlers by design, and this is
   the only ingestion policy that avoids the incumbents' empty-queue failure mode.
5. **Referrals: verify, don't trust.** A referred host is a candidate hostname only.
6. **WHO: count plus anonymised aggregates.** Never raw names at rest.
7. **WHO parsing: structural, not per-codebase.**
8. **Connect screen: stored and displayed**, ANSI intact, suppressible on owner request.
9. **Claiming: prove it through the game itself** — site-issued token via MSSP, connect screen, or
   DNS TXT.
10. **Identity: scored fingerprint with auto-merge above threshold.**
11. **Stack:** one ASP.NET Core deployable; crawler as an in-process `BackgroundService`; one
    **PostgreSQL** database (Npgsql + Dapper, SQL migrations, no EF Core). Probe engine built
    directly on **TelnetNegotiationCore**, with MUIndex owning its own MSSP domain types — see
    §4.13.
12. **No shared crawler library.** MUIndex implements its own crawler; SharpMUTerm keeps its own.
    Extracting a common package was tried and abandoned: TelnetNegotiationCore 2.6.5 had already
    absorbed most of what was worth sharing, and the remainder — an in-memory bounded-run frontier —
    is a different shape from a permanent database-backed registry that never retires a host (§7.4).
    What MUIndex owns instead is small: host/referral parsing with private-address scope gating
    (§7.2), and the domain readings TNC does not provide (`CRAWL DELAY -1` as "no preference", ports
    validated as ports, `REFERRAL` as crawlable hosts).
13. **Postgres, despite the referral graph.** The graph is real but shallow and small — order 10k
    edges, almost all queries one hop, with recursive traversal needed only for §7.2's subtree prune
    and a discovery path. A recursive CTE covers both. Everything that actually dominates the
    workload — partitioned presence time series, availability interval arithmetic, faceted counts —
    is where relational is strongest and graph stores are weakest, so a graph database would
    optimise a twentieth of the queries at the expense of the rest. **Revisit if, and only if, the
    referral graph becomes something the product queries rather than records** — shortest paths,
    community detection, centrality. Centrality in particular would collide with rankings being
    computed from measured data only (§2).
14. **v1:** crawler + game listings + hand-written reference pages for the other three catalogues.

## 5. Domain model

Storage splits three ways because the data has three different shapes and lifetimes.

### 5.1 Descriptive fields — current, with age

No append-only ledger. One row per `(game, field, source)`:

```
GameField(game_id, field, source, value, first_seen_at, last_confirmed_at)
```

`source ∈ { mssp, handshake, who, banner, owner, staff, imported_measured, imported_asserted }`.
The two import tiers are defined in §7.6.

**Keyed by source, not just by field** — the first cut of this spec said one row per `(game, field)`
and *also* asked the page to show the losing sources, which cannot both be true. The capability
matrix's two columns are a design requirement, not an edge case: `GMCP ✕ measured / ◇ declared` needs
both values, each with its own age, at the same time. The winner is derived at read time by the
precedence ladder below and is never stored, so it cannot go stale against the rows it summarises.

There is no `confidence` column. Provenance and age carry the meaning between them, and an
unspecified numeric confidence would be a field nothing sets consistently and nothing renders.

Every probe does exactly one of two things to each field:

- **Confirm** — bump `last_confirmed_at`, write nothing else.
- **Change** — update the current row *and* append one row to `FieldChange`.

```
FieldChange(game_id, field, old_value, new_value, source, at)
```

A game whose `GENRE` never moves costs one row per source forever, not one per hour. This yields
per-field provenance, per-field age (so stale hand-typed MSSP can be greyed out), and a per-game
change feed that is a table of *events that actually happened* — which is also what one wants to
render.

**Precedence when sources disagree** (highest first): `handshake` for capability fields, since it
is observed; `owner` for enrichment-only fields; `mssp`; `banner`; `imported_measured`;
`imported_asserted`. `staff` overrides anything, and is logged. Player count is not a `GameField` and does not use this ladder — it lives
in §5.2, where `who` outranks `mssp`. A page shows the winning value and offers
the losing ones with their sources — "declared GMCP, not offered in handshake" is a fact worth
surfacing, not a conflict to hide.

### 5.2 Presence — historical, high volume

```
PresenceSample(game_id, at, count NULL, source, unmeasurable_reason NULL, aggregates)
```

Partitioned by time; rolled up hourly and daily. `source` distinguishes a WHO parse from MSSP
`PLAYERS`. Feeds the day-of-week × hour heatmap and trend lines. The only table growing linearly
with games × time.

**`count` is nullable, and that is load-bearing (§5.4).** A probe that *succeeded* but could not
yield a number — WHO unparseable and no MSSP `PLAYERS` — writes a row with `count = NULL` and an
`unmeasurable_reason`. Writing nothing at all would be indistinguishable from not having probed,
which renders identically to downtime.

`aggregates` is a JSON column holding what §11 permits: idle-time histogram buckets, session-length
estimates, and a unique-player estimate derived from salted rotating hashes. It is populated only
when the WHO parser reaches per-player confidence (§6.3); otherwise null.

**Rollups.** Raw samples are retained for 90 days, then dropped. An hourly rollup — count min/max/mean
and the three-state tally from §5.4 — is retained for two years; a daily rollup is retained forever. The
day × hour heatmap reads the hourly rollup over an 8-week window, never the raw table. This is the only
data in the system that is ever deleted, and only after it has been aggregated into something that
outlives it.

**Activity band**, the facet §9 exposes, is derived here and defined once: `players now` (a non-null
count above zero in the most recent hourly rollup), `active this week` (any such count in 7 days),
`quiet` (reachable within 30 days but no non-zero count), `dark` (not reachable), `archived` (§7.5).
A game whose counts are all unmeasurable is `quiet`, never `dark` — being uncountable is not being
absent.

### 5.3 Availability — historical, as intervals

```
AvailabilityInterval(game_id, state, from_at, to_at NULL, cause)
```

`state ∈ { reachable, degraded, unreachable }`; `cause ∈ { dns, refused, tls, timeout, handshake_stalled,
… }`.

**`degraded` means we got in and could not finish**: the TCP connection succeeded and the banner was
captured, but the session did not complete negotiation within the probe timeout, or a stated TLS port
failed while the plaintext port answered. It is neither reachable nor unreachable and the design renders
it as its own short bar. Without this definition the state was named by the schema and produced by
nothing. A game reachable for three years is one open row, not twenty-six thousand samples.
"Reachable over 90 days" and "longest outage" become arithmetic over a handful of rows.

Each probe either extends the open interval or closes it and opens a new one. **Only a cause change
writes a transition** — a hundred consecutive timeouts are one interval.

### 5.4 The three states an hour can be in

**Zero players is not the same fact as unreachable — and neither is "we got in but could not
count".** The heatmap has three renderings, so the store must distinguish three cases:

| What happened | Written | Renders as |
|---|---|---|
| Probe succeeded, count obtained | `PresenceSample(count = n)` | Filled cell, including a measured zero |
| Probe succeeded, no count obtainable | `PresenceSample(count = NULL, unmeasurable_reason)` | Hatched cell — *probed, unmeasurable* |
| Probe failed | `AvailabilityInterval` transition, **no** presence row | Empty cell — *not reachable* |

The middle row is the one the first cut of this spec missed: it said only that a *failed* probe
writes no sample, which left a successful probe with an unparseable WHO writing nothing either —
identical on screen to downtime. A game whose `DOING` header is customised past our parser would
have rendered as permanently dark while running fine.

A measured zero is a filled cell, not an absence. It means we got in and nobody was there, which is
a real and useful fact about a game.

### 5.5 Endpoints

```
GameEndpoint(game_id, host, port, kind, first_seen_at, last_seen_at, state)
```

`kind ∈ { telnet, tls, websocket, http }`. Plural and historical: a game that moves does not become
unfindable, because old endpoints are still probed at the backoff floor, and a referral or DNS
record pointing at an old address re-links to the existing game rather than minting a duplicate.

### 5.6 The field registry, and why staleness is a stored property

Every descriptive field is declared once in a registry: its name, its type, whether it is
owner-enrichable, and — the part that matters — its **expected refresh window**.

"Old" is not one duration. A player count is stale in hours; a hand-typed MSSP `GENRE` is
unremarkable at six months and notable at six years. The window belongs beside the field
definition, not in a front-end conditional, because the API, the plain-text surface and the
rendered page must all agree on when a value has aged out, and only one of them is a front end.

`GameField` therefore exposes a derived `IsStale(now)` from `last_confirmed_at` plus the
registry's window for that field. Nothing downstream re-derives it.

### 5.7 Identity: a GUID for the API, a slug for the URL

Two identifiers, deliberately:

- **`id`** — an immutable GUID minted once and never reused. This is what the API returns, what bulk
  dumps key on, and what every foreign key points at. It never changes, including across a merge
  (§7.3), where the surviving game keeps its own and the absorbed one's id becomes a permanent alias.
- **`slug`** — a URL segment minted from the game's name (`/g/tidewater-nights/`), and mutable, because
  games rename themselves. Every slug a game has ever had redirects to it, forever. Nothing is ever
  deleted here either: a URL that once worked keeps working, which is the same promise the archive
  makes about pages.

Slugs are minted from the winning `NAME` field, lowercased, non-alphanumerics collapsed to hyphens,
with a numeric suffix on collision. A rename does **not** re-mint automatically — a game that flips its
name daily would otherwise churn its URL — it is re-minted only when the name has been stable for one
grace period, and the old slug redirects from that moment.

### 5.8 Reachable, not uptime

The vocabulary is **reachable** throughout — schema, API, and copy. We measure a socket from one
vantage point at intervals; we did not measure whether the game was up, and "uptime" claims we did.
A single game with a routing problem to our host is unreachable and perfectly alive.

This is a naming rule with teeth because the word leaks: `AvailabilityInterval.state` uses
`Reachable`/`Unreachable`, the API field is `reachablePercent`, and the archive grace input
(§7.5) is *cumulative reachable time*.

## 6. The probe

One telnet connection per target, yielding four independent layers. This is **not** a fallback
chain — you always get layers 1 and 2, usually 3, and zero-or-all of 4.

### 6.1 Layer 1 — the handshake is a measured capability probe

What the server offers via `IAC WILL/DO` is *observed*, not claimed: GMCP, MSDP, MCCP2/3, MXP, MSP,
EOR/GA, NAWS, CHARSET, TTYPE/MTTS, MNES (NEW-ENVIRON, opt 39), MSLP-via-MTTS. TLS is likewise
observed by completing a handshake on the advertised port or failing to.

`TelnetNegotiationCore` already implements all of this. **The crawler is our own library pointed at
a stranger** — which is itself a benefit: consuming it from the client side will surface bugs that
also benefit SharpMUTerm and SharpMUSH, and the fix path is ours.

### 6.2 Layer 2 — the connect screen

Display asset and fingerprint both. Version banners identify the codebase when `CODEBASE` is unset
or wrong, and a banner hash is a strong identity signal (§7.3).

### 6.3 Layer 3 — `WHO` / `DOING` at the connect screen

The MU\*-family advantage: Penn, MUX, Rhost and the TinyMUD family answer `WHO` and `DOING`
*before* login. DIKU-family generally does not. For a MUSH-focused site this is frequently a better
count than MSSP `PLAYERS`, because it is live rather than whatever the codebase last cached.

**Parsing is structural, not dialectal.** Locate the trailing `N Players logged in`-style summary
line; failing that, count rows between the header rule and the footer. The parser reports one of
three confidence levels:

- **count** — the number is trustworthy. Writes a `PresenceSample`.
- **per-player** — the name column is positionally identifiable, so §11's aggregates can be
  computed. Writes `PresenceSample.aggregates` too.
- **unknown** — writes nothing; the site falls back to MSSP `PLAYERS`, labelled as such.

A claimed owner may override the format from the dashboard, or simply assert "use MSSP `PLAYERS`".

**Parsers never fabricate. An unreadable WHO yields unknown, never zero.**

### 6.4 Layer 4 — MSSP

Telnet option 70, with the plaintext `MSSP-REQUEST` fallback (tab-separated, delimited by
`MSSP-REPLY-START` / `MSSP-REPLY-END`).

### 6.5 The seam

```
ProbeSession  ──▶  ProbeResult  ──┬──▶  FieldReconciler   (§5.1)
                                  ├──▶  PresenceWriter    (§5.2)
                                  └──▶  AvailabilityWriter (§5.3)
```

`ProbeResult` is one immutable object carrying handshake capabilities, banner, WHO, MSSP, and
timings. None of the three writers knows a socket exists. This is also the primary test surface:
`ProbeResult` fixtures captured from real games exercise every downstream behaviour without a
network.

## 7. Discovery, scheduling, and identity

### 7.1 Discovery is how a game is found; never how it is scheduled

The moment a host answers, it is promoted to a `CrawlTarget` with its own independent
`next_probe_at` and is probed forever after on its own account.

```
ReferralEdge(from_game_id, to_host, to_port, first_seen_at, last_seen_at, present)
```

Referral edges are recorded purely as provenance: they permit tracing and wholesale pruning of a
poisoned source under §7.2, and they let the site render who points at whom. **An edge disappearing
updates `present` and nothing else.** The effective seed set is therefore every game ever found,
growing monotonically; configured start locations matter only on day one.

### 7.2 Referrals are candidate hostnames, not facts

A referred host must independently answer MSSP with its own `NAME`/`HOSTNAME` before it is listed.
Depth and fan-out per source are capped. The referring game is recorded on the discovered entry so
that a hostile or careless `REFERRAL` list can be traced and its whole subtree pruned.

#### The gate is on the resolved address, not the name

**Checking the hostname is not enough, and treating it as enough is a server-side request forgery
hole.** `10.0.0.5` and `localhost` are refused by inspection, but nothing stops a hostile `REFERRAL`
naming `games.example.com` and pointing its DNS at `127.0.0.1`, `169.254.169.254`, or anything on
the network the crawler happens to run inside. The name passes; the socket goes somewhere it must
never go.

So every dial resolves first and is refused unless **every** returned address is globally routable:

- **Refuse, don't filter.** If a name answers with one public address and one private one, the whole
  target is refused. Connecting to "the good one" is a coin flip we would lose the moment DNS
  reordered, and a mixed answer is itself evidence of intent.
- **"Could not resolve" and "resolved somewhere we won't go" are different facts**, and only the
  second is a refusal. The first is an ordinary DNS failure and gets ordinary backoff.
- **A refusal writes no availability sample.** We declined to dial; we did not measure. Recording it
  as downtime would put our own security policy into a game's public reachability history, which is
  the same class of lie as recording an unparseable WHO as zero players (§5.4).
- **Operator-supplied seeds may be exempted, and nothing else may.** The exemption is a stored
  property defaulting to *not exempt*, never inferred, and never granted by a referral or an import —
  so the dangerous paths are guarded by not having to remember to guard them.

**Known limitation, stated rather than implied:** this is a time-of-check-to-time-of-use gap. The
name is resolved, then connected by name, so a DNS answer that changes in between is not caught. The
fix is to connect to the pinned `IPAddress` that was checked rather than re-resolving, and it is
worth doing. Caching resolutions would *widen* this window, so the crawler resolves per dial. Do not
restate the guard as airtight; it raises the cost of the attack, it does not close it.

### 7.3 Identity: scored fingerprint, auto-merge above threshold

Weighted match over stable signals:

| Signal | Weight rationale |
|---|---|
| Previously-seen endpoint (host, port, or resolved IP) | Strongest; direct continuity |
| MSSP `NAME` + `CREATED` | `CREATED` is a year and rarely changes |
| Connect-screen banner hash | Survives host moves; changes on redesign |
| `WEBSITE`, `CONTACT` | Stable, and rarely coincidental |
| `CODEBASE` + version | Weak alone; useful as corroboration |
| Site-issued claim token (§8) | Decisive when present — a claimed game is never duplicated |

Above threshold: auto-merge into the existing game, recording the endpoint change as a
`FieldChange`. Middling: open a suspected-duplicate pair for review — **both pages stay live and
link to each other reciprocally**, because a wrongly hidden game is worse than a visible duplicate.
Below: create a new game.
**Merges are reversible and logged.**

Duplicate listings are the specific failure that clutters every incumbent catalogue. This is the
component that prevents it.

### 7.4 Unreachable never means removed

Failures lengthen the probe interval exponentially **against a floor** — a game dark for two years
is still probed weekly, forever, *including after it has been archived*. A returning game therefore
re-lists itself with no human involved, which is precisely what no incumbent managed (§3).

Lifecycle states are presentational, derived from availability history: `active` → `quiet` →
`dark` → `archived`. **Nothing is ever deleted.** An archived game keeps its page, its history and
its URL — the historical record is part of the product.

### 7.5 Archiving: tiered by measured history

A game that has been dark long enough leaves the default listing for the archive. The threshold is
not a constant, because a fortnight-old game and a decade-old institution do not deserve the same
benefit of the doubt:

```
grace = clamp(cumulative_reachable_time / 4, 60 days, 365 days)
```

| Measured time reachable | Grace before archiving |
|---|---|
| ≤ 8 months | 60 days (floor) |
| 1 year | 91 days |
| 2 years | 182 days |
| ≥ 4 years | 365 days (ceiling) |

**Cumulative, not span.** The input is the sum of `up` interval durations from §5.3, so a game
that was reachable for two years out of five is credited with two. A game with a long history of
flapping does not accrue grace for the gaps. Imported history counts at half weight, per §7.6.

**A claimed game always receives the ceiling.** Someone with server access has demonstrably staked
a claim, which is worth a year regardless of how long we have been watching. This is also one more
concrete reason to claim (§8).

**Known limitation, stated plainly on the about page:** grace is computed from reachable time that
*somebody probed* — ours at full weight, an importable third party's at half (§7.6). A game running
since 1995 that no directory ever recorded starts at the floor and accrues from there. We do not
credit MSSP `CREATED`, because it is hand-typed and unverifiable, and crediting it would make the
archive threshold trivially gameable by editing one line of `mush.cnf`.

#### What archiving does and does not do

| | Archived game |
|---|---|
| Default listing and search | **Excluded.** Reachable via an explicit *include archived* filter and a dedicated archive section |
| Rankings | Excluded |
| "Games active today" headline | Excluded |
| Historical series on the ecosystem dashboard | **Included**, for the periods it was actually up — archiving changes presentation, never the past |
| Its own page, URL, history, change feed | Unchanged, plus a clear statement of when it went dark and how long it was known live |
| Probing | Continues forever, at the weekly floor or the game's own `CRAWL DELAY`, whichever is longer (§7.7) |
| API | Present, with `state` as a field; `?include=archived` on collection endpoints |
| Search-engine indexing | Retained — the historical record is part of the product |

**Un-archiving is automatic and immediate.** One successful probe restores the game to the default
listing and fires the *came back* feed (§9). Archiving is never a manual action, never permanent,
and never requires a human on either side of the transition.

### 7.6 Backfill and imported history

The existing directories are the best day-one seed available, and several of them hold years of
history we cannot otherwise obtain. Importing them is planned. Two rules govern it.

**Imported data splits by whether the source measured or merely recorded.** This is the same
measured-versus-declared spine that runs through the rest of the design, applied one level up:

| Tier | Sources | What it may do |
|---|---|---|
| `imported_measured` | MudStats, MudVerse, Grapevine — sites that actively ping | Seeds discovery; populates historical `AvailabilityInterval` and `PresenceSample` rows; **counts toward grace at half weight** (§7.5) |
| `imported_asserted` | The MUD Connector, MUSHCode lists, hand-maintained pages | Seeds discovery and endpoints **only**. No history, no presence, no grace |

A third party that ran its own probe produced a measurement, and a measurement is worth more than a
self-report — that is the whole argument of this project, and it does not stop applying because
someone else did the probing. A hand-maintained list is an assertion and is treated as one.

Half weight for `imported_measured` reflects that we cannot audit their probe, their parser or
their failure handling. The existing clamp still applies, so an eight-year record credits four
years and reaches the ceiling — which is the correct outcome for a genuine decade-old institution.

**Imported facts never outrank measured ones and are never laundered into looking first-party.**
Both import tiers already sit at the bottom of the §5.1 precedence ladder. Every imported value carries
the originating site and the import date in its provenance chip, and the about page names every
source we ingested.

**Etiquette, before any of it runs:** ask for a bulk export or use a documented API in preference to
scraping; honour `robots.txt` and rate-limit hard where scraping is the only option; attribute
every source on the about page and in the API. These sites are run by people in the same small
hobby, and several of them are the reason any of this data exists at all. A short email first is
both the decent move and the one most likely to get better data than scraping would.

### 7.7 Scheduling

A single scheduler picks due targets by `next_probe_at`, feeding a bounded worker pool. Interval is
`max(CRAWL DELAY, base_interval)`, tightened for games with recent activity, lengthened on failure.
Archiving does not change the schedule. Per-host serialisation prevents a multi-port game from being
hit concurrently.

**When `CRAWL DELAY` and the permanent floor disagree, politeness wins.** §7.4's "still probed weekly,
forever" is a bound on how far *our own backoff* may lengthen — it is not a promise to knock weekly at a
server that asked for less. A game stating `CRAWL DELAY 720` is probed monthly, and the two rules
compose as `max(CRAWL DELAY, backoff)` with backoff capped at a week. Read the other way round, the
floor would let us override a server's own stated wishes, which is the one thing the politeness contract
exists to prevent, and it is stated in a *standard* rather than invented by us.

The liveness guarantee survives this: a game asking for a month is still re-listed automatically when it
returns, just up to a month later. A game that wants to be crawled less than that has effectively said
so, and §11's opt-out is the honest way to say it entirely.

## 8. Claiming and ownership

The site issues a token. The owner proves control by emitting it in one of three places — an MSSP
field, a line on the connect screen, or a DNS TXT record on the hostname. All three require server
or DNS access; all three are verified by the crawler that already exists; none requires the site to
send mail or trust a third-party registry.

The claim token doubles as a permanent identity beacon (§7.3), which gives owners a concrete
technical reason to claim beyond editing their listing.

Owner dashboard: enrichment fields (fandom/IP, RP enforcement, application process, consent tools),
connect-screen suppression, WHO-format override, opt-out, and the MSSP linter scorecard —
continuous rather than one-shot, flagging missing fields, wrong types and non-standard values.
Multi-owner, transfer, and an audit log.

Owner-published outputs: a live player-count SVG badge and a JSON endpoint for the game's own site.

## 9. Site surface, v1

**Game listing.** Faceted search over the MSSP taxonomy plus derived facets: activity band,
*measured* protocol support, TLS, charset, language, last-seen. Archived games are excluded by
default and reachable via an *include archived* toggle. Random game. A find-a-game facet wizard.

**The archive.** A first-class section, not a hidden flag — browsable and searchable over games
that have gone dark, with the date each was last reachable and how long it was known live. This is
the historical record the incumbents threw away, and it is worth presenting as an asset rather than
as a bin.

**Game page.** The *game* is the hero — mark, name, its own one-line description, the paragraph it
wrote about itself, and the address you connect to, in that order and above the fold. The
ANSI-rendered connect screen follows immediately, labelled *what you see when you connect*, which
is what it actually is: the first piece of evidence, not the masthead.

This reverses the first cut, on the design handoff's argument, and the argument is right on two
counts. A reader arriving from a search engine needs to know what the game *is* in one glance, and
forty lines of box-drawing does not answer that. And the connect screen is the one element we do
not control — leading with it hands the top of every page to whatever the server happens to send,
including the blank, enormous and hostile cases.

Below: live count, day × hour activity heatmap, the 90-day reachable strip, a capability matrix
showing **measured beside declared with an age on each**, endpoint history, change feed, referral
neighbours, outbound links.

**Plain mode, `?plain=1`**, served automatically to text browsers. Not a courtesy — it is the test
of the whole system: *if a fact cannot survive in plain text, its graphic on the main site is
decoration.* Bounded in cost because it renders from the same view models; the graphical game page
is the plain page with graphics added, not a second document.

**Rankings.** Computed from measured data only.

**Ecosystem dashboard.** Codebase market share and protocol adoption curves (TLS, UTF-8, GMCP,
MXP) — *shares, not totals*. The absolute "how many people play MU\*" figure is deliberately
withheld (§15.7): a ratio over the measured set survives the unclaimed and unreachable biases, and
an absolute count does not. No existing site publishes even the ratios; the crawler produces them
as a by-product.

**The three liveness feeds** — *newly discovered*, *went dark*, *came back*. These are the product
differentiator and no incumbent can publish them.

**Reference pages.** Clients, codebases, protocols: hand-written in v1, already cross-linked to
crawl-derived counts ("games running this codebase: 47"). Client pages carry a capability matrix
including screen-reader accessibility. Orientation content — MUSH vs MUD vs MUCK vs MOO, "you want
collaborative RP → these codebases → these clients → these games" — is curated, single-author, and
versioned in git. **Not a wiki**; this is how wiki value is obtained without wiki governance.

## 10. API and open data

Read-only JSON with stable IDs and ETags. Bulk dumps under an open licence. Time-series endpoints
for presence and availability. RSS on status change in v1; webhooks are deferred (§14).

Consume Grapevine and the TinTin mudlist as seed sources; republish rather than silo.

## 11. Politeness, consent, privacy

- `CRAWL DELAY` honoured as a floor.
- Crawler self-identifies in TTYPE/MTTS and MNES `CLIENT_NAME`, with an info URL, so an admin
  reading their logs can discover who we are and how to opt out.
- Documented opt-out — MSSP field, DNS TXT, or request — honoured within one cycle and recorded.
- **Player names are never persisted.** WHO responses are parsed in memory. Aggregates use salted
  hashes with a rotating salt, so a unique-player estimate is possible while re-identification
  across salt epochs is not.
- Raw probe payloads are retained on a short TTL, redacted of names before touching disk, keyed to
  the `GameField` and `FieldChange` writes they produced so that parser improvements can be
  replayed over a recent window.
- Connect screens are displayed on the grounds that the server sends them unauthenticated to every
  anonymous connection. Suppressed on owner request, no questions asked.

## 12. Failure handling

Every probe is hard-bounded by timeout and `CancellationToken`. Global concurrency cap; per-host
serialisation. Because the crawler runs in-process with the web tier, a wedged probe must not be
able to starve request threads — bounding is a correctness requirement, not hygiene.

Multi-replica deployments gate the crawl loop behind a Postgres advisory lock, so the worker is
conditionally active and N web replicas still run exactly one crawler.

Failures classify into causes; only a cause change writes an availability transition. Parser
failures degrade to `unknown` and are logged with the response redacted.

## 13. Testing

- `ProbeResult` fixtures captured from real games — PennMUSH, TinyMUX, RhostMUSH, Evennia, and a
  DIKU-family game for contrast — exercise every downstream behaviour without a socket.
- A scripted fake MU\* server (canned negotiation, banner, WHO, MSSP, plus deliberate
  misbehaviour: half-open connections, truncated subnegotiation, enormous banners) tests the probe
  engine end to end.
- Property tests for the structural WHO parser against a corpus of real, softcode-customised
  `DOING` headers.
- Identity matcher tested against known move events and against deliberate near-collisions.
- Availability arithmetic tested against synthetic interval sequences.
- **SharpMUSH is the first-party fixture** — a real server we control and can make misbehave on
  purpose.

## 14. v1 boundary

**In:** probe engine, discovery graph, identity matcher, the three storage shapes, auto-listing,
claiming, game listing and game pages, tiered archiving and the archive section, the three liveness
feeds, ecosystem dashboard, read API, hand-written reference pages for clients/codebases/protocols,
and a one-off backfill import from the existing directories (§7.6).

**Out, designed for but not built:** automated tracking of client and codebase releases; protocol
conformance matrices derived from measured handshakes; hosting-provider and tools catalogues;
webhooks beyond RSS; non-English UI (listings carry `LANGUAGE` from day one).

## 15. Open questions

1. **Domain.** The name is settled; the domain is not.
2. **Dataset licence.** The codebase is MIT. The licence for the *published data* is a separate
   decision and need not match.
3. **Hosting and cost envelope**, which bounds probe frequency and retention.
4. **Retention policy** for `PresenceSample` before rollup, and the salt rotation period for §11
   aggregates.
5. **Auto-merge threshold** in §7.3 — needs calibration against real data, so ship conservative and
   tune.
6. **The archive grace factor** in §7.5 — the quarter, the 60-day floor and the 365-day ceiling are
   reasoned but unvalidated. Once a year of availability history exists, check them against what
   actually came back: if returning games were routinely archived first, the factor is too tight.
7. ~~Whether the ecosystem dashboard's population figures are defensible enough to publish headline
   numbers.~~ **Resolved by the design handoff: they are not, and the absolute figure does not
   ship.** Per-codebase and per-protocol *shares* do — they are ratios over the same measured set,
   so the unclaimed and unreachable biases cancel. "How many people play MU\*?" is withheld until
   there is a method that survives being quoted.
