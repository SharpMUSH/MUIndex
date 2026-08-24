# The face a game published — 2026-08-24

Two complaints, one subject: `/games` shows no icon beside a name, and a game's own page shows one
for hardly anybody. They have separate causes and this is both of them.

Supersedes two paragraphs of
[2026-08-15-owner-overrides-design.md](2026-08-15-owner-overrides-design.md) §4, which are quoted
where they are reversed. Nothing in that document was wrong when it was written; two of its rules
turned out to have consequences it did not price.

## 1. The listing lost its plate on purpose, and gets it back

PR #89 removed `GamePlate` from the listing row, with a reason that is a real one:

> No plate on a row. The handoff's listing is identity, measurement and freshness and nothing else —
> a 36px square per row is a fourth column of furniture down a list five hundred long.

It left `.row-main .plate` in the stylesheet and `HasIcon` on `GameSummary`, so what shipped was a
listing whose CSS still described a plate no markup drew.

The plate is back, with `Fallback="true"`, and the decision behind that flag is the one worth stating.
A game we hold no icon for gets its monogram — which is a mark this site invented, on a site whose
whole claim is that it invents nothing. The reason it is nonetheless right here and wrong on the game
page:

- **A page has one plate.** At 96px, next to the game's name and above everything we measured, an
  invented monogram reads as the game's own mark. `Fallback` stays off there.
- **A listing has five hundred.** The plate's job is a left edge the eye can run down; before it,
  every row began with a different-length name and the only way to find a game was to read them. An
  edge present on one row in twenty is not an edge, it is furniture — precisely PR #89's objection,
  arriving by the other road. Roughly 4% of the catalogue has cached bytes, so "icon where we hold
  one, nothing otherwise" is the ragged version of exactly the thing PR #89 objected to.

The monogram is drawn identically for "declared no `ICON`" and "we could not fetch the one it
declared", which is unchanged and is rule 5: only the first is a fact about the game.

Geometry: `.row-main` carries five tracks now — the plate, then identity, measurement, freshness. The
plate spans both lines and takes `align-self: start`, so the meta line runs under the name rather than
under the square. Checked at 1400px and 430px; nothing scrolls sideways at either.

## 2. The queue could not move

The game page renders an icon correctly wherever we hold bytes. We held bytes for 20 games out of the
87 that declare an `ICON`, and had fetched nothing new for six days.

`IIconStore.DueAsync` ranked candidates:

```sql
ORDER BY (i.source_url IS DISTINCT FROM d.url) DESC, i.fetched_at NULLS FIRST
```

Both keys read `game_icon`. A game we have never fetched has no `game_icon` row, so it scores `true`
on the first key and `NULL` on the second — **identically to every other never-fetched game**. `LIMIT
20` over a 67-way tie is the same twenty rows every pass, and since

> A failed fetch writes nothing at all — no row, no marker, no attempt counter

nothing ever broke the tie. Production logs, over three hours:

```
12 http://luminarimud.com/images/luminarimud.bmp     (six passes, two games)
 6 http://www.tamedhon.de/img/td_wappen.gif
 6 http://www.dragonfiremud.com/favicon.ico
 ... fifteen distinct URLs, every pass, all failing
```

Forty-seven games were never attempted once. Alter Aeon publishes a 64×64 PNG that answers 200 to
this day and has never been asked for.

### The marker, and why it does not breach rule 5

`icon_attempt` (migration 0035) holds `(game_id, url, attempted_at, failures, next_attempt_at)`. It
is bookkeeping about **this site's** afternoon: it reaches no page, no API field, no change feed and
no ordering a reader sees. Rule 5 protects a game's public record from our failures, and this is not
one — the same argument that makes `game_icon` a cache rather than an exception to §7.5 makes this
one too, and it may be emptied at the cost of one wasted pass.

Two things follow from having it:

- **The queue advances.** Candidates sort by `coalesce(attempted_at, fetched_at)`, oldest first, so a
  candidate just tried goes to the back whatever it holds. `game_id` is the final tiebreak, so a pass
  is reproducible rather than the planner's choice of the day.
- **Dead URLs back off.** `IconRefresher.Backoff` doubles from one pass (30 minutes) and caps at the
  staleness window (7 days). Fifteen web servers were being asked 48 times a day, indefinitely, by
  someone they never heard of, for an image that was not there.

A URL that changes resets both: a new address is a new question, and the previous address's luck says
nothing about it. A success deletes the row, inside `UpsertAsync` rather than at the call site — a
success that left the count standing would lengthen the next failure's back-off by everything before
it.

### 304 is not a failure

Folding "unchanged" in with "could not fetch" was harmless while nothing was recorded and is not now:
a server honouring the ETag we sent it would be filed as one that had gone away, backed off, and —
since a 304 writes no row — left permanently stale while being punished for saying so. `FetchAsync`
returns `IconFetchOutcome` with three members rather than a nullable icon with two.

## 3. What counts as an image

`ImageHeader` read PNG, GIF, WebP and JPEG. Of the 67 declared-but-uncached icons, **30 name a
`favicon.ico`** and 3 a `.bmp` — more than name any other single format. Both are now read, by header
only, in keeping with §4.2's no-decoder position:

- **ICO** is a container: a six-byte directory header then one 16-byte entry per image, each stating
  its size in a single byte where `0` means 256. The largest entry is reported, because a multi-size
  file is one file the browser picks from and reporting the smallest would let a 256×256 image past a
  ceiling that exists to bound what we store. Type 1 only; type 2 is a cursor. Served as
  `image/x-icon` rather than the registered `image/vnd.microsoft.icon`: under `nosniff` the type has
  to be one every browser actually renders.
- **BMP** carries two dimensions after a DIB header whose own length says how wide they are — 16-bit
  in the 12-byte `BITMAPCOREHEADER`, signed 32-bit in everything later, where a negative height means
  top-down storage rather than a negative size.

SVG stays refused, for the reason it always was.

## 4. One redirect, ruled on

> Redirects are not followed — a redirect is a second address the gate did not rule on.

True, and the cost of it: **13 of the 67** answer 3xx, nearly all an `http` URL in `mush.cnf` with an
`https` web server since put in front of it.

`AllowAutoRedirect` stays `false`. `IconFetcher` follows exactly one hop itself, and the point of
doing it there is that the loop runs `IHostScopeGuard.InspectAsync` again on the target: the objection
was never "a second request" but "an address nobody ruled on", and this address is ruled on the same
way the first was. §7.2's TOCTOU limitation applies to the second hop exactly as it applies to the
first, and must not be restated as airtight here either.

One rather than a chain, because each further hop is another address to clear for a decoration, and a
chain longer than one is somebody's tracker or somebody's loop far more often than it is a moved logo.
The bound is a hop count, so a redirect loop terminates for the same reason. `Location` is resolved
against the address that answered (it is allowed to be relative, and usually is) and must be `http` or
`https`. The URL stored on the icon is the one the `ICON` field named, never the one that answered —
it is compared against the field next pass, and storing the target would make an unmoved field look
moved every time.

## 5. What this is worth

Measured against the live catalogue on 2026-08-24, by fetching all 67 by hand:

| Change | Games it reaches |
|---|---|
| Unsticking the queue alone | ~16 (20 cached → ~36) |
| ICO and BMP | 11 more that answer 200 today |
| One redirect | up to 13 more |

The remainder are unreachable, 404, `http:///images/cm.jpg` with no host at all, or — in one case — a
1123×1123 PNG that the dimension ceiling refuses on purpose.

## 6. Testing

- `IconStorePostgresTests` — the starvation regression first: two games, `LIMIT 1`, and the one that
  failed must not come back ahead of the one never tried. It fails against the old `ORDER BY`.
  Alongside it: a back-off waited out, an attempt against an address the game has left delaying
  nothing, the failure count coming back with the candidate, and a fetched icon clearing what failed
  before it.
- `IconRefresherTests` — a failure written down with a growing distance, the doubling capped at the
  staleness window and not overflowing, and an unchanged icon recording neither.
- `IconFetcherTests` — one redirect followed *and both requests asserted*; a redirect into
  `169.254.169.254`, `127.0.0.1` and `10.0.0.5` refused with no second socket; a second redirect not
  followed; a `Location` we could not dial refused; a relative `Location` resolved; 304 as its own
  answer.
- `ImageHeaderTests` — ICO largest-entry and the `0` means 256 rule, a cursor refused, a directory
  promising more than it holds refused; BMP across all four DIB header lengths and a top-down height.
- `GamePlateTests` — every listing row draws a plate, exactly one per row, and no `img` points off
  this origin; the game page still invents no face for a game that published none.
