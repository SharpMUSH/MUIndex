# MuIndex — design

**Status:** approved design, pre-implementation.
**Date:** 2026-07-30.
**Working name:** *MuIndex*. Provisional; naming is an open question (§15) and a design-session topic.

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
    database. Probe engine built on **TelnetNegotiationCore**.
12. **v1:** crawler + game listings + hand-written reference pages for the other three catalogues.

## 5. Domain model

Storage splits three ways because the data has three different shapes and lifetimes.

### 5.1 Descriptive fields — current, with age

No append-only ledger. One row per `(game, field)`:

```
GameField(game_id, field, value, source, confidence, first_seen_at, last_confirmed_at)
```

`source ∈ { mssp, handshake, who, banner, owner, staff, imported }`.

Every probe does exactly one of two things to each field:

- **Confirm** — bump `last_confirmed_at`, write nothing else.
- **Change** — update the current row *and* append one row to `FieldChange`.

```
FieldChange(game_id, field, old_value, new_value, source, at)
```

A game whose `GENRE` never moves costs one row forever, not one per hour. This yields per-field
provenance, per-field age (so stale hand-typed MSSP can be greyed out), and a per-game change feed
that is a table of *events that actually happened* — which is also what one wants to render.

**Precedence when sources disagree** (highest first): `handshake` for capability fields, since it
is observed; `who` for player count; `owner` for enrichment-only fields; `mssp`; `banner`;
`imported`. `staff` overrides anything, and is logged. A page shows the winning value and offers
the losing ones with their sources — "declared GMCP, not offered in handshake" is a fact worth
surfacing, not a conflict to hide.

### 5.2 Presence — historical, high volume

```
PresenceSample(game_id, at, count, source, aggregates)
```

Partitioned by time; rolled up hourly and daily. `source` distinguishes a WHO parse from MSSP
`PLAYERS`. Feeds the day-of-week × hour heatmap and trend lines. The only table growing linearly
with games × time.

`aggregates` is a JSON column holding what §11 permits: idle-time histogram buckets, session-length
estimates, and a unique-player estimate derived from salted rotating hashes. It is populated only
when the WHO parser reaches per-player confidence (§6.3); otherwise null.

### 5.3 Availability — historical, as intervals

```
AvailabilityInterval(game_id, state, from_at, to_at NULL, cause)
```

`state ∈ { up, degraded, unreachable }`; `cause ∈ { dns, refused, tls, timeout, handshake_stalled,
… }`. A game up for three years is one open row, not twenty-six thousand samples. "Uptime over 90
days" and "longest outage" become arithmetic over a handful of rows.

Each probe either extends the open interval or closes it and opens a new one. **Only a cause change
writes a transition** — a hundred consecutive timeouts are one interval.

### 5.4 The distinction that makes the heatmap honest

**Zero players is not the same fact as unreachable.** A failed probe writes an availability
transition and *no* presence sample. A fortnight of downtime therefore leaves a gap in the heatmap,
not a fortnight of zeroes that would render as a running-but-dead game.

### 5.5 Endpoints

```
GameEndpoint(game_id, host, port, kind, first_seen_at, last_seen_at, state)
```

`kind ∈ { telnet, tls, websocket, http }`. Plural and historical: a game that moves does not become
unfindable, because old endpoints are still probed at the backoff floor, and a referral or DNS
record pointing at an old address re-links to the existing game rather than minting a duplicate.

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
`FieldChange`. Middling: open a suspected-duplicate pair for review. Below: create a new game.
**Merges are reversible and logged.**

Duplicate listings are the specific failure that clutters every incumbent catalogue. This is the
component that prevents it.

### 7.4 Unreachable never means removed

Failures lengthen the probe interval exponentially **against a floor** — a game dark for two years
is still probed weekly, forever. A returning game therefore re-lists itself with no human involved,
which is precisely what no incumbent managed (§3).

Lifecycle states are presentational, derived from availability history: `active` → `quiet` →
`dark` → `archived`. **Nothing is ever deleted.** An archived game keeps its page, its history and
its URL — the historical record is part of the product.

### 7.5 Scheduling

A single scheduler picks due targets by `next_probe_at`, feeding a bounded worker pool. Interval is
`max(CRAWL DELAY, base_interval)`, tightened for games with recent activity, lengthened on failure,
floored per §7.4. Per-host serialisation prevents a multi-port game from being hit concurrently.

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
*measured* protocol support, TLS, charset, language, last-seen. Random game. A find-a-game facet
wizard.

**Game page.** Hero is the ANSI-rendered connect screen. Below it: live count, day × hour activity
heatmap, uptime, a capability matrix showing **measured beside declared with an age on each**,
endpoint history, change feed, referral neighbours, outbound links.

**Rankings.** Computed from measured data only.

**Ecosystem dashboard.** Hobby population over time, codebase market share, protocol adoption
curves (TLS, UTF-8, GMCP, MXP). No existing site publishes these; the crawler produces them as a
by-product.

**The three liveness feeds** — *newly discovered*, *went dark*, *came back*. These are the product
differentiator and no incumbent can publish them.

**Reference pages.** Clients, codebases, protocols: hand-written in v1, already cross-linked to
crawl-derived counts ("games running this codebase: 47"). Client pages carry a capability matrix
including screen-reader accessibility. Orientation content — MUSH vs MUD vs MUCK vs MOO, "you want
collaborative RP → these codebases → these clients → these games" — is curated, single-author, and
versioned in git. **Not a wiki**; this is how wiki value is obtained without wiki governance.

## 10. API and open data

Read-only JSON with stable IDs and ETags. Bulk dumps under an open licence. Time-series endpoints
for presence and availability. RSS and webhooks on status change.

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
  the ledger writes they produced so that parser improvements can be replayed over a recent window.
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
claiming, game listing and game pages, the three liveness feeds, ecosystem dashboard, read API,
hand-written reference pages for clients/codebases/protocols.

**Out, designed for but not built:** automated tracking of client and codebase releases; protocol
conformance matrices derived from measured handshakes; hosting-provider and tools catalogues;
webhooks beyond RSS; non-English UI (listings carry `LANGUAGE` from day one).

## 15. Open questions

1. **Name and domain.** *MuIndex* is a placeholder.
2. **Licence** for the codebase, and the licence for the published dataset (they need not match).
3. **Hosting and cost envelope**, which bounds probe frequency and retention.
4. **Retention policy** for `PresenceSample` before rollup, and the salt rotation period for §11
   aggregates.
5. **Auto-merge threshold** in §7.3 — needs calibration against real data, so ship conservative and
   tune.
6. **Whether the ecosystem dashboard's population figures are defensible enough to publish
   headline numbers**, given that unreachable ≠ zero and unclaimed games may under-report.
