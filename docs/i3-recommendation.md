# What to do with Intermud-3

A recommendation, written 2026-08-16, after joining `*i4` as `MUIndex` and measuring what the
network actually hands over. Every number below was read off the live network or out of production,
not estimated.

## Where the listings stand

432 unarchived games. Of 428 with a presence sample in the last three days:

| | measured | |
|---|---:|---|
| MSSP | 108 | the game's own `PLAYERS` |
| WHO | 77 | parsed off the connect screen |
| banner | 3 | a count in the ASCII art |
| **unmeasurable** | **240** | 122 `who_unparseable`, 118 `who_not_offered` |

**56% of the catalogue has no player count.** Reading a sample of the stored payloads, roughly 210
of those 240 are one problem: `WHO` is not a connect-screen command outside the TinyMUD lineage, so
the login prompt consumes the word as a character name. No parser fixes that.

The descriptive side is thinner than it looks. `CODEBASE` is present on **109 of 432** games and
comes only from MSSP; a game without MSSP has no codebase recorded at all.

## What I3 actually hands over

179 mudlist entries, pushed as deltas and free of any probe. Fill rates measured across all of them:

| Field | Filled | Notes |
|---|---:|---|
| `name`, `host`, `driver`, `mudlib`, `mud_type`, `open_status`, `status`, `services` | 179/179 | `host` is always an IP literal |
| `port` | 177/179 | |
| `tcp_port` | 133/179 | I3 out-of-band |
| `admin_email` | 125/179 | published network-wide by the mud |
| `udp_port` | 57/179 | |

`driver` carries a **version** — `CoffeeMud v5.11.0.1`, `FluffOS v2.23-ds03`, `DGD 1.4.1` — which
MSSP's `CODEBASE` frequently does not. 126 of the 179 are up; 177 advertise `who`.

And `who-req` returns an array of users with idle times, so a count is the length of a list rather
than a pattern match on somebody's ASCII art. Verified live: Nightfall 5, Dead Souls Dev 3, The Zone
0 — the last being a measured zero, not a silence.

## How much of it is ours

All 711 crawl targets resolved (566 answered DNS) and intersected against the 179 I3 hosts:

```
exact match on (resolved ip, port):   20
same host, different port:            25
not in MUIndex at all:               134   — 85 of them up
```

Of the 20 exact matches, **8 are dark today** — `who_not_offered` on Nightfall, Lost Souls, Multi
MUD, Dragon's Den, DarkeMUD, Omen; `who_unparseable` on Frontiers and The Way of the Force. The
other 12 already measure through MSSP.

So the honest split is: I3 rescues **8 known games**, cross-checks 12, and offers **85 live games we
have never seen**. The discovery number is an order of magnitude larger than the rescue number, and
that should drive the ordering of the work.

## What I3 must not be used for

These follow from rules already written down, and are listed here because each one is individually
tempting.

- **`status` must not write an availability interval.** It is the mud's claim to a router, not our
  measurement of a socket. Recording it as reachability is rule 5 exactly — our information about
  their state presented as our observation of it. It is fine for deciding whether to *ask*.
- **`driver` / `mudlib` / `mud_type` must not become displayed `game_field` values.** MSSP's
  `CODEBASE` is the game telling us on a socket we opened; a mudlist entry is the game telling a
  router at its last startup, which may have been months ago, relayed to us. Second-hand and
  undated is a different kind of claim, and the site's whole premise is that it does not blur those.
- **`admin_email` must not be stored.** 125 real addresses, published by their owners to a MUD
  network for MUD purposes. Harvesting them into a directory's database is not what they were
  published for, and no listing gets better for it.
- **Player names must not be persisted**, exactly as with telnet `WHO`. The user array is read to be
  counted and dropped.

## Recommendation

### 1. Bind I3 muds to games explicitly, in a table — *do this first, everything blocks on it*

I3 identifies a mud by name and address; MUIndex identifies a game by the hostname a player types.
The only mechanical join is `(resolved IP, port)`, and DNS moves, so re-deriving the binding every
cycle would make a game's I3 identity silently change under it.

Add `i3_mud (mud_name primary key, game_id, bound_by, first_seen_at, last_seen_at)`. Bind when
`(resolved IP, port)` matches exactly one game endpoint. **Leave the 25 same-IP-different-port cases
unbound** — they are genuinely ambiguous between one game on a second port and two games on one box,
and a wrong binding attributes one game's players to another, which is worse than no count.

Cheap second signal for later: the I3 `name` against the game's existing `NAME`.

### 2. Seed discovery from the mudlist — **host and port and nothing else**

This is the largest single win available and it fits an existing rule verbatim. Spec §7.6 already
says the backfill contributes addresses only, because "every fact on this site is measured by this
crawler". Treat I3 the same way: 134 new `(host, port)` pairs, 85 of them live, go into
`crawl_target` and the ordinary probe discovers everything else about them.

Two details:

- Addresses arrive as IP literals, so §7.2's scope gate applies directly with no name to resolve —
  simpler than the normal path, not harder. Refuse anything not globally routable as usual.
- The game's real hostname arrives later from its own MSSP `HOSTNAME`, which is the right way round.

Expected effect: roughly a 20% larger catalogue, from one packet, with no new etiquette surface —
these games published their address to a public network specifically so that other participants
would find them.

### 3. Run a `who` cycle for bound muds

Gate on the mudlist's own words: up, and advertising `who`. Write one `presence_sample` per answer
under `FieldSource.I3`, with `i3_no_reply` for silence. All of this is built and tested; what is
missing is the loop.

Pace it deliberately. I3 has no `CRAWL DELAY` equivalent, so pick one: **no more than one `who` per
mud per 30 minutes**, spread across the cycle rather than issued in a burst. 177 muds at that rate
is a packet every ten seconds, which is nothing to a router and visible to nobody.

Expected effect: 8 dark games become countable now, and any of the 85 newly discovered games that
bind later become countable without a probe that can count them.

### 4. Consider, later: idle-time aggregates

`who-reply` gives an idle time per user, which nothing else in the catalogue has. "How many of those
players are actually active" is a genuinely new statistic and no other directory has it.

Two boundaries if this is ever built. It is an aggregate over a snapshot and must not become an
identity: **no unique-player estimate**, which is unmeasurable in principle here and was deliberately
removed. And it would exist only for I3 games, so it is a per-game detail and never a cross-catalogue
ranking — a leaderboard that silently only ranks LP muds is worse than no leaderboard.

## Sequence and cost

| Step | Effort | Unlocks |
|---|---|---|
| 1. `i3_mud` binding table + matcher | small | everything else |
| 2. mudlist → `crawl_target` seeds | small | ~85 new live games |
| 3. `who` cycle → presence | small–medium | 8 dark games now, more as (2) lands |
| 4. idle aggregates | medium | a statistic nobody else has |

Steps 1–3 are days, not weeks, because the protocol layer is done: `MUI.I3` speaks to the gateway,
`I3PresenceChoice` turns an answer into a reading, `FieldSource.I3` and `i3_no_reply` are in the
schema and proven against Postgres. What remains is scheduling and identity, both of which are
ordinary work in `MUI.Crawler` and `MUI.Discovery`.

## Risks worth stating

- **The sidecar is a beta and not ours.** Its `mudlist` method and its own state file already
  disagree about field names, and its synchronous `who` return is broken. We read the event and
  accept both mudlist shapes; both facts are pinned by tests against captured payloads. If it drifts
  further, the native C# client is roughly seven packet types and stays the fallback.
- **The name is spent.** `MUIndex` is registered on `*i4` permanently — mudlist entries are never
  removed, only marked down. The clean exit, should it ever be wanted, is a `shutdown` packet with
  `restart_delay` over seven days.
- **Two series per game.** A game on both pipes accumulates a telnet count and an I3 count that may
  legitimately differ, because each reflects the visibility rules of the path it came down. This is
  not a bug to reconcile; it is two labelled vantage points, and the surfaces need to say which is
  which rather than average them.
