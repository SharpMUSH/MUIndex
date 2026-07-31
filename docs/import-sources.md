# Import sources

Every MU\* directory this project has looked at, what tier it belongs in, and — for the ones we do
not read — why not. Spec [§7.6](specs/2026-07-30-mu-directory-design.md) is the authority; this file
is the record of applying it, so that a source is investigated once and a decision is not
re-litigated from scratch by the next person.

**The tier is the thing to get right.** `imported_measured` is for a site that connected to the game
itself; `imported_asserted` is for a site that wrote something down. Filing an asserted list as
measured earns a game archive grace nobody observed, which is worse than skipping the source
entirely. And a *measured* fact is only importable if the source says **when** it measured — an
undated reading is not a measurement we can place in time, and dating it to the moment we read the
page is fabrication.

## Read

| Source | Tier | Route | Requests per run | What it contributes |
|---|---|---|---|---|
| [TinTin++ MSSP Mud Crawler](https://tintin.mudhalla.net/protocols/mssp/) | `imported_measured` | bulk export | 1 | Addresses, MSSP fields, one dated player count each |
| [TinTin++ MSDP Mud Crawler](https://tintin.mudhalla.net/protocols/msdp/) | `imported_measured` | bulk export | 1 | Addresses, MSDP fields, one dated player count each |
| [The Mud Connector](https://www.mudconnect.com/) | `imported_asserted` | bulk export | 1 | Addresses, names, websites. Nothing else |
| [MudStats](https://mudstats.com/) | `imported_measured` | scrape | 1 + one per world | Addresses, codebase, website, one dated player count each |
| [MudVerse](https://www.mudverse.com/) | `imported_measured` | scrape — **gated** | 1 + one per game | Addresses, MSSP fields, one dated player count each |

### What one run of the four actually yielded

Read live on 2026-07-30, into an empty database. **New** is crawl targets this source contributed
that the ones above it had not already; the order is the order `AddMuiImporters` registers them in,
so the overlap all lands on the later sources.

| Source | Listed | Endpoints | New targets | Dated player counts |
|---|---:|---:|---:|---:|
| TinTin++ MSSP | 115 | 144 | 144 | 114 |
| TinTin++ MSDP | 44 | 44 | 7 | 41 |
| The Mud Connector | 661 | 661 | 550 | 0 |
| MudStats | 142 | 142 | 104 | 100 |
| **Combined** | | | **805 across 745 distinct hosts** | **255** |

Two things worth reading off that table. The MSDP page contributes 7 addresses in 44 — it is very
nearly a subset of its MSSP sibling, and it was added for those 7 and for its 41 readings rather
than for volume. And The Mud Connector, the one *asserted* source, is two thirds of the whole
address haul while contributing not one number: coverage and measurement are different jobs, and
the tier split is what lets one source do each without either pretending to be the other.

No source emitted a single availability span, so **no archive grace is granted by any of them.**

### TinTin++ MSSP and MSDP crawlers

Two pages from one crawler on one site, in the same format, read by one reader
(`TinTinCrawlerTable`) at two different label widths. Both are `imported_measured` because the site
connects and negotiates: what it reached, and the count it read, are its measurements; the contents
of the reply are the game's declarations, relayed, and land under `capability.x.declared`.

Each page carries its own generation timestamp, which is what dates its player counts. Neither
yields an availability span — a snapshot says the host answered at an instant and says nothing about
for how long, and a span invented around it credits grace nobody measured.

The MSDP page is the smaller and older of the two (44 readable entries, generated January 2024) and
reaches games that answer MSDP rather than MSSP. Its readings are imported at the instant the page
states, not at the instant we read it. It carries no per-mud link, so a game's declared `HOSTNAME`
is the only address on it; that is a weaker address than the MSSP page's dialled one, and is why it
is registered second.

**Both pages interleave the crawler's frames with the connect screens of the games it dialled**, and
a connect screen is text a game controls completely. A record is therefore opened only by a
*full-width* frame, so that a game cannot draw a box in its own login banner, point it at another
game's `HOSTNAME` and `PORT`, and have a fabricated player count attached to somebody else's
listing. That guard is tested.

### MudStats

`imported_measured`; spec §7.6 names it in that tier. Its world pages carry `Players Connected: N
(9 minutes ago)`, which is a count with the age of the reading printed beside it — so it is dated by
the source and not by us. A reading whose stated age is in *months* is refused rather than rounded
to thirty days.

It is a scrape: one index fetch plus one fetch per world, with no dump and no API. It sits behind
`ContactedMaintainer`, **and that gate is shut**: nobody has emailed MudStats.

> **The record, because it matters more than the tidy version.** This source briefly defaulted
> `ContactedMaintainer` to `true`, with a comment stating that the maintainer had been approached and
> the run authorised. That was not true of the world. It was written from an instruction to *do the
> MudStats import* — a decision about our priorities, not a fact about somebody else's consent — and
> on the strength of it a seventeen-minute crawl of 143 of their pages went ahead ungated on
> 30 July 2026, at the fifteen-second floor and honouring `robots.txt`, but without asking.
>
> The default is `false` again and the claim now has to be made by whoever can make it truthfully:
> `AddMuiImporters(contactedMudStatsMaintainer: …)`, or `--contacted MudStats` on the one-off tool.
> A hardcoded `true` in a source file is an assertion about a third party compiled in by whoever
> typed it, which is the shape of claim this whole project exists to refuse.

Everything else §7.6 asks for is enforced in code rather than
remembered — fifteen seconds between page fetches, a bound on how many world pages one run touches,
a `User-Agent` naming us with an info URL, `robots.txt` read before the first content fetch, and an
attribution the registry derives so a credited source and a read source cannot diverge.

`Status:` is read and discarded, for the same snapshot reason as the TinTin pages.

### The Mud Connector

`imported_asserted`, which is what spec §7.6 names it as. TMC *does* connect — its Big List carries
a `Connect Status` column reading `Connected`, `Connect Refused` or `N/A`, which no submission form
produces — but **the page states no time for that result**, so it is read by nothing. Its only
player figure is a submission-form bucket (`100+`, linking to "other muds with a similar # players")
on listings whose own headers can be fifteen years old. Neither a count nor dated.

It is read as a bulk export because the site publishes its entire catalogue on one page: 689 games
for one GET. That is arithmetic rather than a claim that the maintainer prepared a dump for
machines, and the per-game listing pages are deliberately never fetched — they would turn one
request into seven hundred and add, for each, an undated status this importer would refuse anyway.

An on-demand connectivity checker exists at `mode=check_connect`. It is not used: fired once by hand
against a game that was up, and that TMC's own stored status called `Connected`, it reported a
failure. It is also a live socket to a third party's server opened on our behalf, which is not ours
to spend.

### MudVerse — implemented, and refused until somebody writes to them

**Both scrapes sit behind the contacted-maintainer gate.** `MudVerseSource` is written and tested;
`ContactedMaintainer` is false, so `EtiquettePlanner` refuses it and `ImportRunner` names it in the
skipped list with the reason. One short email is the whole of what stands between the file and 273
dated readings.

It is the strongest source in this table on every axis except permission. The site says of itself:
"Our crawler runs roughly once an hour and will check the connectivity of each game in our database.
It will also send an MSSP request to each game and log the results." Each game page then prints
*Connection Tested* and *Last Successful Connection* to the minute in GMT, and the MSSP block below
carries the reading. Measured and asserted are separated in the markup and visibly disagree — the
owner-submitted panel says `Codebase: Other` and `Player Count: 0-5` where the crawler read
`DikuMUD/Merc/Envy` and `PLAYERS 1`. Only the crawler's panel is imported.

The count is dated at the *last successful* connection, because a failed test reads nothing — and
only when the MSSP block's own "Crawled on" date agrees, because otherwise the block is older than
the connection above it by an amount the page does not state.

Enumeration is the sitemap the site advertises in its own `robots.txt`, not the paginated listing:
one request for the whole catalogue, and the listing page defaults to showing only games that are
online, which would quietly make our coverage a function of who happened to be up.

**Its daily series is deliberately not imported.** `/json.php?listing_id=…` publishes per-game daily
points back to November 2022, titled *Average Players Online Per Day*. A day's average is a derived
statistic; a `PresenceSample` is a count somebody read at an instant (§5.2). Placing an average at
the midnight its bucket is keyed to would put a number nobody measured into that hour of the
day-of-week × hour heatmap. If MudVerse ever exposes the hourly readings it says it stores, that is a
different and very welcome conversation — and one to have in the same email.

## Investigated and not read

### Grapevine — `grapevine.haus`

**Measures, publishes nothing dated.** Its "Seen on MSSP" state is real: the status icon on a game
page is either `fa-adjust` / *Seen on MSSP* (its own checker connected to the game's telnet port) or
`fa-circle` / *Online* (the game is connected to Grapevine's chat socket). Both are genuine
measurements.

What stops it being importable is that **no per-game player count appears anywhere in the HTML**.
The one count on the site is a front-page aggregate — "Join 0 total players across all games" — with
no timestamp of any kind, counting only socket-connected games. A `/games/<slug>/stats` page exists
and declares five series with explicit JSON URLs (`?series=48-hours|week|month|year|tod`), which
would be the first genuine time series available to this project — but **every one of those URLs
returned HTTP 500**, for an MSSP-only game and a socket-connected game alike, with and without an
`Accept: application/json`. The route exists and the handler is failing; whether that is transient
was not established.

Its documented API is a WebSocket at `wss://grapevine.haus/socket` requiring a `client_id` and
`client_secret` that are **issued per registered game**. There is no read-only importer credential;
obtaining one would mean registering a game that does not exist. That is not a route this project
will take.

Left unread. It is worth revisiting if the stats endpoints come back, and the sitemap (306 entries,
`/games/<slug>` with `lastmod`) is a ready-made enumeration when they do. Until then it would cost
306 requests to a hobbyist's server for addresses we already get elsewhere.

### MUDListings / bestmuds.com

`mudlistings.com` now redirects to `bestmuds.com`. It **measures and timestamps**: each game's
server-rendered payload carries `"online_now":2, "status":"unknown",
"mssp_checked_at":"2026-07-15T00:35:05.306+00:00"` — the reading and the time it was taken, which is
the honest shape. On the strength of that it would be `imported_measured`.

Not read, for three reasons, in order of weight:

1. **The polling is not periodic.** In every sampled row `mssp_checked_at` is within about a second
   of `updated_at` — the check fires as part of a record write, not on a schedule. Of five samples
   the freshest reading was fifteen days old, one was six months old, and two had never been checked
   at all. A source whose readings arrive when somebody edits a row is a source whose coverage is a
   function of editing activity.
2. **It costs 1,000 requests** — one per game in the sitemap — because `/browse` does not embed the
   records and defaults to an online-only filter reaching 351 of the 1,000. Its JSON API is
   `Disallow: /api/` in its own `robots.txt` and is therefore off limits.
3. **The data needs validating harder than the others.** One of five sampled rows had a website URL
   in the host column (`"host":"http://www.persistentrealms.com:0","port":null`), and `status` and
   `online_now` contradict each other in both directions.

Worth reconsidering — with an email first, as a 1,000-page walk — if the freshness improves.

### Top Mud Sites

**Asserted, and frozen.** The front page says so: *"TMS has stopped accepting new votes and is
read-only."* Nothing on the site measures anything. Its two candidate fields are owner-entered form
values and their own tooltips say so — *Online Status* is a lifecycle declaration ("Fully
Operational", "open for testing", "closed") rather than connectivity, and *Avg Players Online* is a
self-described average in coarse buckets ("Over 250") with no reading time and no possibility of
one. The numeric columns on the front page are `Hits In` / `Hits Out`, which count clickthroughs to
TMS.

Its database is the largest address list found anywhere — `mudlist.html` reports 1,963 results — and
addresses are cleanly separable from the voting data (`muddisplay.php` carries no scores or
rankings at all, only a link to a vote page). So this is a queued candidate rather than a rejection:
it is a scrape at 40 paginated requests, nobody has written to them, and the site being frozen means
its addresses are a historical archive whose hit rate against live games is unknown. A single gated
source is enough to keep the mechanism honest, and MudVerse is the better one to spend it on.

Note also `Content-Signal: search=yes, ai-train=no, use=reference` in its `robots.txt`. Importing
addresses into a catalogue is neither training nor an AI input, but the signal is recorded here so
that whoever reads this next knows it was seen.

### muds.fandom.com

**Not reachable.** `https://muds.fandom.com/robots.txt` returns HTTP 403 behind a Cloudflare
interstitial. Working around a bot challenge is the opposite of the etiquette this component exists
to enforce, so nothing further was attempted. It is also a wiki: hand-written, and `asserted` at
best.

### Intermud — I3 and IMC2

**A genuinely different population, and reachable — but it carries no player counts, and getting the
addresses costs 410 requests over an expired certificate.**

One live HTTP source exists: `https://zone.wotf.org/mud/mudlist`, the `*wpr` router's own DGD/LPC
web server, generated per request. It listed 409 known muds across three routers with 101–103 online
at the time of reading, which is a real measured up/down state — and it is a largely LPMud
population that no MSSP-based source reaches. Two things stop it:

- **The index gives name and online state only.** Host and port need one further request each, at
  `/mud/mudinfo?mud=<name>` — 409 of them.
- **There are no player counts, structurally.** The I3 mudlist packet entry is
  `({state, ip_addr, player_port, imud_tcp_port, imud_udp_port, mudlib, base_mudlib, driver,
  mud_type, open_status, admin_email, services, other_data})`. A count is obtainable only through the
  I3 `who` service — that is, by speaking I3 to a router, which is a protocol client and not an HTTP
  importer. It is arguably a *crawler* feature rather than an import one.

`http://zone.wotf.org` also redirects to HTTPS with an **expired certificate**. Reading it required
disabling certificate validation, which this project will not do from code.

IMC2 contributes nothing on its own: `imc2.mudmagic.com` redirects to an unrelated site,
`imc2.intermud.us` does not resolve, and `mudbytes.net` returns HTTP 500. What survives of IMC2 is
visible inside the I3 list, as entries bridged through `*dalet`.

The honest verdict: seeding an LPMud population is worth doing, and the way to do it is an I3 client
in the crawler rather than an importer here.

## Running an import

The import is **one command a human runs once, against one deployment**. It is not a hosted service,
there is no timer, and `AddMuiImporters` composes a reader rather than a schedule.

```bash
dotnet run --project tools/live-import -- --list
dotnet run --project tools/live-import -- --source MudStats --cache /var/tmp/mudstats --dsn "Host=…"
```

`--cache` names a directory of raw fetched bodies, read before the network and written after it, so
that a second look at the same data costs the site nothing. **Point it outside the working tree.** It
is somebody else's catalogue; the harvested data is not ours to commit, and this repository holds no
copy of it. The fixtures under `tests/MUI.Import.Tests/Fixtures` are a handful of hand-trimmed
records each and are a different thing — they are test inputs, and no test in this repository touches
the network.
