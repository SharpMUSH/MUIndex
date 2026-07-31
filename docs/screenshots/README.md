# Screenshots — 30 July 2026

Captured from the running site at `31f5864`, rendering from `FixtureGameQueries`. Every value in the
fixture came off a real server, which is why the hard states appear at all.

| File | What it shows |
|---|---|
| `01-home-feeds.png` | The three liveness feeds. The *came back* card is the one place the site raises its voice. |
| `02-games-listing.png` | The listing, with a measured zero, an unknown count and an archived game distinguishable at a glance. |
| `03-game-page.png` | The whole game page: ANSI frame, heatmap, reachable strip, capability matrix, provenance. |
| `04-plain-mode.png` | `?plain=1` — the same facts as words. |
| `05-archive.png` | The archive as a section rather than a bin. |
| `06-mobile-game.png` | 390px. Single column, heatmap keeps all 168 cells. |

## What to look for

**The game page leads with the game.** Mark, name, description, address — then the connect screen
under *"what you see when you connect"*, which is what it is: the first piece of evidence, not the
masthead. Leading with the art would hand the top of every page to whatever a stranger's server sends.

**`GMCP` sorts to the top of the capability matrix and is labelled `DISAGREES`.** Its MSSP says
`claimed`, six years old; the handshake has never offered it. That disagreement is the single most
useful thing the matrix can say, so it cannot be scrolled past.

**The heatmap has three states and they are told apart by shape.** Filled is counted — *including a
measured zero*. Hatched is probed-but-uncountable (see Friday). Empty is not reachable (Wednesday's
twelve-hour band). Greyscale printing is a valid rendering of this grid.

**The reachable strip has four states, not three.** The fourth is *not measured* — the strip is 90
days wide, and a game found last Tuesday has no history before that. Painting those days unreachable
would record our ignorance as their measurement.

**Ages are relative and stale ones are marked.** `created 2009 ◇ 6y` is not wrong, it is old, and the
page says so rather than presenting it as current.

**It is called *reachable*, never *uptime*.** We measured a socket from one vantage point; we did not
measure whether the game was up.

## The plain rendering is the test

`04-plain-mode.png` is not a courtesy. If a fact cannot survive there, its graphic on the main page
was decoration. Compare:

```
Wed — peak 13 at 20:00, 12 hours not reachable
Fri — peak 16 at 20:00, nobody on 05:00-11:59, 1 hour probed but uncountable

  counted   = we got in and read a number, including a measured zero
  uncounted = we got in and no number could be read
  no data   = we could not reach the game in that hour at all
```

Three states, in words, with no colour to carry them.
