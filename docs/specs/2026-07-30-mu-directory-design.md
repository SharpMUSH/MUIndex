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

`source ∈ { mssp, handshake, who, banner, owner, staff }` — every one of them something this crawler
or a person here produced. There is no imported source, because the backfill contributes addresses
and no values (§7.6).

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
is observed; `owner` for enrichment-only fields; `mssp`; `banner`. `staff` overrides anything, and is
logged. The ladder had two rungs below `banner` for imported values and no longer does — a source
nothing can write is an invitation to write one. Player count is not a `GameField` and does not use this ladder — it lives
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

**One probe writes at most one presence row.** The table is keyed `(game_id, at)`, which settles
something §5.2 left open: the `who` beats `mssp` precedence must be applied **before** the writer is
called, by whatever assembles the probe's reading. It cannot be applied afterwards by keeping both
and choosing later, because there is nowhere to keep both. This is a constraint on the ingestor, not
a limitation of the store.

**Rollups are not built and have no owner.** §5.2 states the retention shape — raw 90 days, hourly
two years, daily forever — and nothing implements it. Until it does, the heatmap reads raw samples
over an eight-week window. Monthly range-partitioning is in place, which is what makes a rollup a
cheap addition rather than a rewrite.

**Activity band**, the facet §9 exposes, is derived here and defined once: `players now` (a non-null
count above zero in the most recent hourly rollup), `active this week` (any such count in 7 days),
`quiet` (reachable within 30 days but no non-zero count), `dark` (not reachable), `archived` (§7.5).
A game whose counts are all unmeasurable is `quiet`, never `dark` — being uncountable is not being
absent.

### 5.3 Availability — historical, as intervals

```
AvailabilityInterval(game_id, state, from_at, to_at NULL, cause, origin)
```

`state ∈ { reachable, degraded, unreachable }`; `cause ∈ { dns, refused, tls, timeout, handshake_stalled,
… }`; `origin ∈ { first_party }`.

**`origin` has one value and is still a column.** It existed because imported reachable history was
credited toward archive grace at half weight, which is unimplementable unless an interval records
whose probe produced it. §7.6 no longer imports history, so every interval is ours — but the column
stays, because if another party's measurements are ever ingested an undifferentiated total would
already be in the table and could not be split back apart. A column is cheap; the distinction is not
recoverable after the fact.

**`degraded` means we got in and could not finish**: the TCP connection succeeded and the banner was
captured, but the session did not complete negotiation within the probe timeout (`handshake_stalled`
is the cause that produces it), or a stated TLS port failed while the plaintext port answered.

It follows — and the first cut of this spec never said so — that **a degraded game is not dark**. The
socket answered, so §7.5's grace clock does not run. Archiving measures absence, and a server that
picks up the phone and then falls silent is present in the only sense availability tracks. It is neither reachable nor unreachable and the design renders
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
| Probe failed, or no probe at all | `AvailabilityInterval` transition, **no** presence row | Empty cell — *no measurement for that hour* |

The middle row is the one the first cut of this spec missed: it said only that a *failed* probe
writes no sample, which left a successful probe with an unparseable WHO writing nothing either —
identical on screen to downtime. A game whose `DOING` header is customised past our parser would
have rendered as permanently dark while running fine.

A measured zero is a filled cell, not an absence. It means we got in and nobody was there, which is
a real and useful fact about a game.

**The empty cell is a statement about us, and must be worded as one.** Because a failed probe writes
no presence row, the third case covers both an hour we could not reach and an hour we never probed —
and the store cannot tell them apart from presence alone. Every surface therefore says *no
measurement*, never *not reachable*: it shipped the other way, and a game the crawler had measured
once, and found perfectly reachable, had 167 hours of its week described as downtime. Whether the
game was reachable in an hour is §5.8's question, answered from intervals, which carry that fact
directly.

The same reading applies to any figure derived from a partly-observed window. A reachability
percentage divides by *observed* time, so a surface may print it as a fraction of the last 90 days
only when all ninety were measured; otherwise it names the days it measured. One successful probe an
hour ago is not "reachable 100% of the last 90 days", and the arithmetic being right does not make
the sentence true.

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

**MSSP must be asked for, not waited for.** The specification says a server "should send
`IAC WILL MSSP`" on connect, and a great many servers that fully support it never volunteer
anything — they answer `IAC DO MSSP` and are otherwise silent. A crawler that only listens reports
those servers as having no MSSP, which is why the protocol's own reference client asks. Sending
`IAC DO 70` is negotiation, not traffic.

#### An empty report has three meanings

TelnetNegotiationCore 2.7.0 bounds the payload and, at the ceiling, **drops the report rather than
truncating it** — correctly, since half a report parses cleanly and lies. Layer 4 therefore has
three outcomes and the store must carry which one:

| Outcome | Meaning |
|---|---|
| `NotOffered` | The server never offered MSSP and did not answer the plaintext request |
| `Received` | A report arrived and was parsed. It may still be empty — that is the server's answer |
| `RejectedTooLarge` | A report arrived and exceeded our ceiling, so we dropped it whole |

**`RejectedTooLarge` must never render as "no MSSP".** We asked, the server answered, and we chose
not to hold it; publishing that as an absence would state our own size limit as a fact about their
game — the same error as recording an unparseable `WHO` as zero players (§5.4) or a scope refusal as
downtime (§7.2). The rejected byte count is retained so the ceiling can be tuned against real
servers rather than guessed at.

The ceiling is set well below the library's 1 MiB default: a real report is a few kilobytes and the
official vocabulary is 45 variables, so the bound is generous for anything legitimate and finite for
anything hostile. A crawler connects to servers it does not trust by definition.

**Which route produced a value is part of its provenance.** Option 70 and the plaintext reply are
read by different code paths and need not agree byte for byte, so the transport is recorded
alongside the value rather than discarded once parsed.

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

**Measured, and rarer than the protocol implies.** Of 141 live servers probed on 30 July 2026 — the
codebase survey's set plus every entry in TinTin's MSSP crawler list — exactly **two** publish
`REFERRAL`, and both name the same game: `mud.virtustan.net:8888` and `mud.kharkov.org:3000` (one
operator, two servers) point at `tbamud.com`, one of them naming two ports. Both referred endpoints
became depth-1 targets, were probed on their own schedule, and are **not listed** — tbaMUD's only
self-description is `NAME "tbaMUD"`, its codebase's name, which §7.3's placeholder rule reads as
unset. That is the gate above working exactly as written, and it is worth knowing that the first
real graph this crawler walked ended that way.

Two consequences. Referral is a *bonus* discovery path and cannot be the primary one at this
density, which is what §7.6's backfill is for. And the rule has a cost worth restating: a real,
reachable game stays unlisted because its operator never edited one line — recoverable the moment it
publishes a name, and the target is kept and re-probed for ever in the meantime.

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

**A codebase default is the absence of a signal, not a weak one.** Observed on the second server
this crawler ever probed: it publishes `NAME "PennMUSH"`, because whoever installed it never edited
that line — and so does every other unedited PennMUSH on the internet. Scored naively, all of them
match each other on the strongest textual signal in the table, and auto-merge fuses unrelated games
into one listing. The failure is silent and the damage is hard to unpick, because a merge that
should never have happened looks exactly like a merge that should.

So every signal is filtered through a placeholder check before it is weighed, and a placeholder
contributes **nothing** rather than a little. That includes the codebase's own name as the game's
name, the same with a version appended, blank values, and template text (`Unknown`, `Change Me`,
`Your MUD Name`). Two absences must never score as an agreement.

The same caution applies to `CONTACT` and `WEBSITE`, which are shared across every game on a hosting
provider more often than they are unique, and to `CREATED`, which is a year and therefore collides
freely on its own.

**The banner signal has the same failure mode one layer down, and the probe is where it is fixed.**
A connect screen is a good fingerprint because operators edit it; a *placeholder* connect screen is
the codebase's, shared by every install. tbaMUD sends `Attempting to Detect Client, Please Wait...`,
pauses for about a second and a half, and then paints the real screen — so a probe that settles on a
gap between lines stored that one placeholder line as the banner, and two unrelated tbaMUDs
fingerprinted identically. That is `NAME "PennMUSH"` again in a different field, and the answer is
not to discount the signal but to stop truncating the evidence: the connect-screen phase waits
longer when what it has is slight and has not reached a prompt (`ProbeOptions.BannerPatience`).
Found by a referral crawl, which is the only thing that had put two servers of one codebase side by
side.

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
flapping does not accrue grace for the gaps.

**Only our own measurements count, because only our own measurements exist.** This clause used to
credit imported history at half weight — a third party that ran its own probe produced a measurement,
halved because we could not audit their prober. §7.6 no longer imports history at all, so there is no
such time to weigh: however a game reached the catalogue, its grace is earned here.

**A claimed game always receives the ceiling.** Someone with server access has demonstrably staked
a claim, which is worth a year regardless of how long we have been watching. This is also one more
concrete reason to claim (§8).

**Known limitation, stated plainly on the about page:** grace is computed from reachable time *we*
probed, so a game running since 1995 starts at the floor on the day we find it and accrues from
there. This is a larger limitation than it was — a backfill that imported history used to soften it
for the games some other directory had been watching — and it is the accepted cost of §7.6's decision
that every fact here is measured here. We do not credit MSSP `CREATED`, because it is hand-typed and
unverifiable, and crediting it would make the archive threshold trivially gameable by editing one
line of `mush.cnf`.

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

### 7.6 Backfill: a list of addresses, and nothing else

The existing directories are the best day-one seed available. **What we take from them is the list of
games — host, port, and nothing more.** Every fact about a game on this site is then measured by this
crawler.

**This is deliberately less than the sites can give.** Several of them hold years of dated player
counts and reachability history, and an earlier version of this section imported it, split into two
tiers — `imported_measured` for a site that runs its own probe, `imported_asserted` for a
hand-maintained list — with the first credited toward archive grace at half weight and both sitting
at the bottom of §5.1's precedence ladder. That machinery is gone: there are no imported field
sources, no imported presence rows, no imported availability intervals, and no provenance sidecar
recording which site a value came from.

Three reasons, and the first is the strongest.

**A game's origin is not one fact.** The catalogue will be cross-checked against several directories
over time, and any game worth listing appears in more than one of them. "Imported from MudStats" is
then a statement about which fetch happened to run first, not about the game — and a provenance chip
saying it would be presenting an accident as a fact, which is the failure this whole design exists to
avoid. There is no honest single-origin field to store.

**That a game exists is public information.** The address of a public MU\* is published by its
operator to be dialled. Recording where we happened to read it adds nothing a reader can use, and it
is the part of a third party's work with the least claim to be ours to republish. The addresses seed
a crawl; the crawl produces the data.

**The point is to start with a lot of games, then gather our own data.** A backfill that also
imported history would fill the heatmaps and the reachable strips of exactly the games some other
directory had been watching, in a way no reader could distinguish from our own measurement without
reading the fine print — and would leave the site's central claim resting on somebody else's prober.
Starting every game's history empty is slower and is the whole point.

**What it costs, stated plainly.** Every game starts at the archive floor on the day we find it
(§7.5), every heatmap starts empty, and the day-one site is a large list of games about which we know
their address and one probe's worth of everything else. That is the intended shape.

**Etiquette is unchanged, and still binds** — asking for a bulk export or a documented API in
preference to scraping, honouring `robots.txt`, rate-limiting hard where scraping is the only option,
and crediting every source we read on the about page. Taking less data does not make a crawl of
somebody's site less of a crawl of somebody's site. **The contacted-maintainer gate is satisfied by a
caller who can make the claim, never by a default in a source file**: it once defaulted to true for
MudStats with a comment asserting the maintainer had been approached, and a 143-page crawl went out
before anyone had emailed them.

**The import is a one-time operation against one deployment, and its output is not part of the
source tree.** MUIndex is deployed to a single place; the backfill exists to give *that* database a
day-one population, and once it has run the crawler keeps the catalogue true by measuring. So it is
one command, run once, by an operator, pointed at the production connection string — not a startup
step, not a scheduled job, and not something a fresh clone reproduces.

The consequence is a repository rule with two halves. **Neither the harvested data nor the importer
lives on `main`.**

*The data*, first and most obviously. No harvested catalogue is checked in — no mirrored listing
file, no seed list of real hosts scraped from a directory, no snapshot of anyone's database. Test
fixtures are exempt because they are inputs to a test rather than the dataset: a handful of
hand-written rows exercising a parser is a fixture, and a copy of a third party's catalogue is not,
whatever it is named. A run that produces an artifact writes it to a path the operator chose, and
those paths are ignored by git. Republishing another site's catalogue as a file in our repository is
a redistribution nobody agreed to, whatever the etiquette above secured for *ingesting* it — and a
committed dataset is asserted data with a git history, the exact thing the front page says this
project does not do, rotting in place while the live catalogue moves on.

*The importer*, which is the less obvious half. Fetchers, HTML parsers for four third-party sites,
the etiquette gate and the one-off runner exist only while the run is happening, and carrying them on
`main` means maintaining parsers against sites we intend never to fetch again, in CI, for ever. A
parser that never runs but still compiles is worse than no parser: it rots silently and reads as a
supported feature. So the importer lives on a branch outside `main`, and running the import means
checking that branch out.

What stays behind on `main` is **`docs/import-sources.md`** — which sources were read, which were
refused and why, and the record of the MudStats crawl that went out before anyone had emailed them.
It outlives the code that acted on it.

What does *not* stay is anything that existed to carry imported values: the `import_provenance`
table, `FieldSource.ImportedMeasured`/`ImportedAsserted`, and `IntervalOrigin.ImportedMeasured` are
all deleted, because a seed-only import writes no row any of them could label. `IntervalOrigin`
survives as a one-member enum and `availability_interval.origin` as a column: if another party's
measurements are ever ingested, an undifferentiated total would already be in the table and could not
be split back apart.

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

### 8.1 The order is: sign in, then claim

An account exists **before** any token does, and the claim binds to the account rather than to the
token. That ordering is not a convenience; it is what makes the scheme sound.

1. The visitor signs in. A session cookie now identifies a durable account.
2. They press *claim this game*. The site mints a token and stores a **pending claim** keyed
   `(account, game)`.
3. They publish the token where a probe can read it.
4. The next probe compares. On a match the claim completes, bound to **the account that minted the
   token** — never to whoever holds it.

**The token is a nonce, not a credential, and it cannot be anything else.** We ask an operator to
publish it on a connect screen or in an MSSP field, which every anonymous connection reads —
including every other crawler. A bearer-secret model, where holding the token confers the claim, is
therefore broken the instant it succeeds. What the token proves is that *somebody with write access
to that server published it*; the account binding answers the separate question of *who asked*.

Mallory reading Alice's token off the connect screen can do nothing with it: it verifies only against
Alice's pending claim. To take the game she must publish her own token on that server, which is
precisely the control being tested.

**Nobody has to write anything down.** The pending claim is durable server-side state, shown on the
claimant's dashboard for as long as it is pending, with each channel's exact line ready to copy. Close
the tab, come back next week, it is still there. A token that had to be captured in one sitting would
put a transcription error between an owner and their listing.

Bounds: one pending token per `(account, game)`, expiring after 30 days — otherwise abandoned tokens
accumulate on connect screens and linger as identity beacons for claims nobody completed. Verification
is idempotent and re-runnable; a non-match is *not yet* rather than a failure, and the page says when
we last looked and when we will look again.

**Verification is asked for, not polled at.** A claimant may request one on-demand probe per pending
claim per few minutes — enough that an operator who has just edited `mush.cnf` is not waiting on the
scheduler, and bounded so that the button is not a free way to make us dial a stranger. `CRAWL DELAY`
still binds, and the target must already be one we crawl.

### 8.2 Sign-in is passkeys, and v1 has nothing else

**Passkeys only** (WebAuthn/FIDO2, native to ASP.NET Core Identity in .NET 10). No passwords, no
email, no federated provider, no third party of any kind. We hold a public key; the private key never
leaves the operator's authenticator.

Three properties earned rather than assumed. There is **no password database to breach** — what we
store is public by construction. Sign-in is **phishing-resistant structurally**, because the browser
binds the credential to our domain and will not release a signature to a look-alike. And replay is
caught by the credential's own signature counter.

**The hard part of passwordless does not apply here.** Account recovery is what usually forces a
password, an email flow or recovery codes onto a passkey deployment. Our recovery path is: make a new
account, publish a fresh token on your game, verify. **The root of trust is the server the operator
controls, not the credential** — so losing every device is recoverable without us knowing an email
address, and an account is worth almost nothing to steal.

Three consequences to hold onto:

- **Sign-in requires JavaScript, and it is the only thing on this site that does.** `navigator
  .credentials` has no scripting-off path. The public catalogue — listing, game pages, archive,
  plain mode, the API — stays fully functional without scripting, and that boundary is a design
  constraint rather than an accident: the part that requires JS is the part used by people who
  administer a game server.
- **A passkey is bound to a domain**, and §15.1's open domain question therefore has a deadline.
  `IdentityPasskeyOptions.ServerDomain` is set explicitly rather than inferred from the host header
  (the inference is a credential-scoping risk), and no untrusted content is ever served on a
  subdomain of it. Passkeys registered before the domain settles must be re-registered after a move;
  either settle it before claiming opens or accept a one-time re-enrolment and say so on the page.
- **Enrolment is still a minority behaviour** across the web — a reason to expect federated options
  to be added later, and not a reason to add them now. Every person who can complete a claim already
  has shell access to a MU\* server; this is the audience most able to use a passkey.

Federated sign-in (Discord, a forge, the fediverse) is a **later** addition, and one that also
restores a scripting-free login path, since OAuth is redirects. It is deliberately out of v1.

### 8.3 The channels a token may be published in

**MSSP** (`MUINDEX CLAIM`, with `MUINDEX_CLAIM` and `CONTACT_TOKEN` also accepted — an MSSP variable
name does not reliably survive a config file, and an operator who did exactly what they were told must
not be told their claim failed) and **the connect screen** (`MUINDEX-CLAIM: muidx-…`). Both are read
by the probe that already exists.

**DNS TXT is deferred, and not merely for lack of a resolver.** A TXT record proves control of a
*hostname*, and a hostname is not a game: MU\* hosting routinely puts many unrelated games on one
domain, separated only by port. The host's operator could claim all of them, and a game running on
somebody else's domain could never use the channel at all. If it returns it needs a port qualifier.
The two channels above prove control of *that listener*, which is the thing being claimed.

### 8.4 Presence establishes; absence never revokes

A verified claim survives the token being removed. The alternative — absence revokes — hands
revocation to any transient failure: a server restart, an MSSP hiccup, a compression bug eating a
subnegotiation. This project has already watched MCCP swallow a connection's payload whole, and a
silent unclaiming on that basis would be indistinguishable from an owner walking away.

So two timestamps, because they are two facts: `claimed_at`, written once when verification succeeds,
and `beacon_last_seen_at`, updated whenever a probe still sees the token. Revocation is explicit, or
the consequence of a **counter-claim** — a different account proving control *now* — which is also
the correct handling of a game changing hands. The published token keeps earning its keep meanwhile
as §7.3's decisive identity signal, which is the concrete technical reason to leave it in place.

### 8.5 What a claim grants, and the line it may not cross

Enrichment fields (fandom/IP, RP enforcement, application process, consent tools), connect-screen
suppression, `WHO`-format override, opt-out, and the MSSP linter scorecard — continuous rather than
one-shot, flagging missing fields, wrong types and non-standard values. Multi-owner, transfer, and an
audit log; one account may hold many games, and a game may have several owners, each having verified
a token of their own.

**An owner may never edit a measurement.** They can add `FANDOM`; they cannot touch a player count, a
capability matrix, or a reachability history. The writable set *is* the field registry's
`OwnerEnrichable` flag, and a write to any other field is refused out loud rather than dropped — a
silent no-op teaches an owner that the site is broken, and a successful one would make the whole site
a self-report with extra steps.

Owner-published outputs: a live player-count SVG badge and a JSON endpoint for the game's own site.

**Claiming lights up two paths that are currently unreachable**, and that is worth knowing when
testing it: nothing sets `game.is_claimed` today, so the `claimed` badge in the listing and
`ArchivePolicy`'s ceiling-grace-for-claimed-games (§7.5) have never once been exercised against real
data.

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

### 10.1 Known gap — the listing endpoint is less honest than the listing page

`GameSummary` carries no provenance, so `/api/games` publishes `playersNow` and `codebase` as bare
values while `/api/games/{slug}` labels every field with its source, age and staleness. **That is the
one place the API contradicts the rule the whole project exists to serve**, and it is a view-model
gap rather than a mapping choice — the summary type has nowhere to put the label.

Fixing it means putting `ProvenanceChip` on `GameSummary` for at least the count and the codebase.
Until then, a consumer reading only the listing cannot tell a count measured four minutes ago from
one asserted six years ago, which is exactly the confusion the incumbents' directories thrive on.

Three smaller gaps found the same way, all currently worked around inside `src/MUI.Web/Api/`:
`IGameQueries` has no `FindAsync(Guid)`, so a GUID lookup scans the whole listing; `FeedEntry` has no
`Id`, so every feed request reads the listing to join identifiers onto slugs; and §5.7's
forever-redirect has no former-slug table, so aliases live in configuration rather than beside the
games.

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
