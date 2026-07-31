# Screenshots — 31 July 2026

Captured from the running site against **a real PostgreSQL catalogue**, populated by `mui-crawl`
against live servers: sixteen games, one probe each. Nothing here is a fixture. The site prefers a
database and falls back to the demo fixture only when none is configured — and when it does, every
page carries a banner saying so, because a reader who cannot tell a measurement from a fixture is
being misled by exactly the mechanism this project exists to replace.

| File | What it shows |
|---|---|
| `01-home-feeds.png` | The three liveness feeds, off a first crawl: sixteen games *newly discovered*. |
| `02-games-listing.png` | The listing. Measured counts, measured zeroes, and games whose handshake offered nothing. |
| `03-game-page.png` | Virtustan MUD — the whole game page, and the game that led the crawler to two more endpoints. |
| `04-plain-mode.png` | `?plain=1` — the same facts as words. |
| `05-archive.png` | The archive. Empty, and saying so: nothing has been dark long enough. |
| `06-mobile-game.png` | 390px. Single column, heatmap keeps all 168 cells. |

## What to look for

**Every page is one probe old, and the page says so rather than pretending otherwise.** This is what
makes the set worth keeping: it is the site's *first day*, which is the state every real game enters
in, and it is where three sentences turned out to be lying.

**"Reachable 100.0% of the 1 day we have measured."** The fraction's denominator has always been
observed time; the sentence used to widen it to "of the last 90 days". Right number, claim
eighty-nine days wider than the evidence.

**The heatmap's empty cells read *no measurement in that hour*.** They used to read "not reachable —
no measurement at all", and the summary said "167 hours across the week could not be measured — the
game was not reachable", about a game measured once and found perfectly reachable. A failed probe
writes no presence row, so silence there cannot tell an outage of theirs from a gap of ours.
Reachability is the strip's question, and the strip is derived from intervals that can.

**"What the game says about itself" contains only things the game said.** `banner_hash` — a digest
*we* compute, 64 hex characters wide — used to sit at the top of that panel, off the edge of its
column.

**The three states are still three states.** Filled is counted, including a measured zero (see
`eldertaleonline.com`, which answered and had nobody on). Hatched is probed-and-uncountable. Empty is
no measurement. Greyscale printing is a valid rendering of this grid.

**It is called *reachable*, never *uptime*.** We measured a socket from one vantage point; we did not
measure whether the game was up.

## The plain rendering is the test

`04-plain-mode.png` is not a courtesy. If a fact cannot survive there, its graphic on the main page
was decoration — and the plain surface carried the same wrong sentence, in the same words, which is
what makes it a real parity check rather than a second implementation.

```
  counted   = we got in and read a number, including a measured zero
  uncounted = we got in and no number could be read
  no data   = we have no measurement for that hour
```

Three states, in words, with no colour to carry them.

## Reproducing

```bash
mui-crawl --connection "…" --seed mush.pennmush.org:4201 --seed mud.kharkov.org:3000 …
MUI_POSTGRES="…" dotnet run --project src/MUI.Web
```
