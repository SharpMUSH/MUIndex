# Codebase survey — 30 July 2026

One live game probed per version-ignoring codebase, sourced from MudStats' world pages.
**38 codebases, 38 answered, 0 failures.** Every count below is what our own probe
observed; the `mudstats` column is what MudStats reported for the same game.

This file exists because every parser decision in `MUI.Crawl` traces to a row in it. It is evidence,
not documentation — when a rule here looks arbitrary, the server that caused it is named.

## The headline: the two families are nearly disjoint

| Route | Codebases |
|---|---|
| Count via **MSSP** | 28 |
| Count via **WHO** | 7 |
| Both | 2 — PennMUSH, MudOS |
| Neither (no count) | 5 — Ew, Midnight Sun, MOO, NakedMud, TinyMUSH |

**MSSP is the DIKU/LP answer; WHO is the MU\* answer, and they barely overlap.** AresMUSH, TinyMUX,
MUCK, RhostMUSH, CobraMUSH and TinyMUSH all offer *no* MSSP at all and all answer a pre-login `WHO`.
That is the empirical case for the four-layer probe: an MSSP-only crawler is blind to most of the
MUSH family, which is precisely this project's audience.

## Observations

| Codebase | Target | MSSP | PLAYERS | WHO | banner | mudstats | negotiated |
|---|---|---|---|---|---|---|---|
| AresMUSH | `bekindrewind.aresmush.com:4201` | NotOffered | — | PerPlayer 34 | — | 33 | (none observed) |
| TinyMUX | `darcness.net:4201` | NotOffered | — | Count 32 | — | 28 | CHARSET |
| CoffeeMUD | `coffeemud.net:2327` | Received | 19 | Unknown | — | 19 | MCCP2, MSSP |
| LPMud | `playdecay.com:3003` | Received | 12 | Unknown | — | 12 | MCCP2, MSSP |
| Emlen | `play.ropmud.com:4443` | Received | 10 | Unknown | — | 10 | GMCP, MSSP |
| Merc | `170.187.150.187:1111` | Received | 10 | Unknown | — | 10 | MSSP |
| Evennia | `play.arxmush.org:3000` | Received | 8 | Unknown | — | 8 | MCCP2, MSSP |
| SMAUG | `tsosmud.org:7070` | Received | 9 | Unknown | — | 8 | MSSP |
| DikuMUD | `nonamemud.duckdns.org:4000` | Received | 7 | Unknown | — | 7 | MCCP2, MSSP |
| Ew | `resort.org:2323` | NotOffered | — | Unknown | — | 6 | EOR |
| Midnight Sun | `midnightsun2.org:3000` | NotOffered | — | Unknown | — | 6 | (none observed) |
| PennMUSH | `game.starwarsrebirth.com:9999` | Received | 5 | Count 5 | — | 5 | CHARSET, MSSP |
| MUCK | `equestria.kaitain.org:2700` | NotOffered | — | Count 5 | — | 5 | (none observed) |
| RhostMUSH | `darkmetal2039.com:2039` | NotOffered | — | Count 5 | — | 5 | CHARSET |
| Riftforge | `162.243.50.82:4000` | Received | 4 | Unknown | — | 4 | GMCP, MSSP |
| ROM | `realms.reichel.net:4000` | Received | — | Unknown | — | 3 | CHARSET, MCCP2, MSSP |
| LuminariMUD | `luminarimud.com:4100` | Received | 2 | Unknown | — | 3 | MSSP |
| CobraMUSH | `bigdamn.com:7777` | NotOffered | — | Count 3 | — | 3 | (none observed) |
| MudOS | `eternalfantasy.org:3333` | Received | 3 | PerPlayer ? | — | 3 | MSSP |
| NarutoMUD Engine | `narutofor.us:4545` | Received | 3 | Unknown | — | 3 | MCCP2, MSSP |
| MOO | `www.czaralex.com:7777` | NotOffered | — | Unknown | — | 2 | (none observed) |
| GWM | `gundam.vineyard.haus:9876` | Received | 2 | Unknown | — | 2 | MCCP2, MSSP |
| LastOutpost | `last-outpost.com:4000` | Received | 2 | Unknown | — | 2 | CHARSET, EOR, MSSP |
| Nightmare III | `sunderingshadows.com:8080` | Received | 1 | Unknown | — | 2 | CHARSET, MSSP |
| CircleMUD | `4dimensions.org:6000` | Received | 1 | Unknown | — | 1 | MSSP |
| CD | `dg.demonsgate.org:3011` | Received | 1 | Unknown | — | 1 | MSSP |
| Epiphany  [development] | `drakkos.co.uk:6789` | Received | 1 | Unknown | — | 1 | MCCP2, MSSP |
| FluffOS | `coremud.org:4000` | Received | 2 | Unknown | — | 1 | MCCP2, MSSP |
| NakedMud | `15.157.195.64:4000` | NotOffered | — | Unknown | — | 1 | (none observed) |
| TinyMUSH | `chaos.caile.org:4444` | NotOffered | — | Count 0 † | — | 0 | (none observed) |
| Anatolia | `crusify.kharkov.org:5000` | Received | 0 | Unknown | — | 0 | MCCP2, MSSP |
| Dark City | `godwars.net:3000` | Received | 0 | Unknown | — | 0 | MCCP2, MSSP |
| Enrym | `play.enrym.com:4000` | Received | 0 | Unknown | — | 0 | MSSP |
| EternityMUD | `eternitymud.com:23` | Received | 0 | Unknown | 3 | 0 | EOR, MSSP |
| IME | `iberia.jdai.pt:5900` | Received | 0 | Unknown | — | 0 | MCCP2, MSSP |
| LoFP | `lofp.metavert.io:4000` | Received | 0 | Unknown | — | 0 | GMCP, MCCP2, MSSP |
| Mindcloud3 | `ianshirm.genesismuds.com:2251` | Received | 0 | Unknown | — | 0 | MSSP |
| tbaMUD | `mud.virtustan.net:8888` | Received | 0 | Unknown | — | 0 | MSSP |

† Re-measured after the negotiation-residue fix below; it read `Unknown` in the original run.

## What a codebase says about its own ancestry — re-probed 15 August 2026

`CodebaseLineage` groups codebases into the traditions they descend from, and the map is compiled in
rather than read at runtime, so every row in it has to be somebody's decision. This is the evidence
for the rows that are not common knowledge.

The nineteen codebases above that the map did not place were re-probed. **Seven answer the question
themselves.**

| Codebase | `CODEBASE` | `FAMILY` | Also on the wire |
|---|---|---|---|
| Emlen | `EmlenMud` | **DikuMUD** | |
| NarutoMUD Engine | `NarutoMUD Engine` | **DikuMUD** | |
| LastOutpost | `LastOutpost 4.59` | **DikuMUD** | connect screen: "Last Outpost DikuMUD" |
| Midnight Sun | `Midnight Sun` | **LPMud** | |
| CD | `CD.06.06` | **LPMud** | |
| Epiphany | `Epiphany v1.2.15 [development]` | **LPMud** | "LPmud version : FluffOS v2.26 (MeekOS version)" |
| Ew | — | — | connect screen: "Based on EW-Too by S. Marsh" |

**Six more declare their independence, which is the same answer the map was already giving them.**
Evennia, Riftforge, Enrym (`Enrym (custom Node.js)`), EternityMUD, IME and LoFP all publish
`FAMILY Custom`; CoffeeMUD publishes `FAMILY CoffeeMUD`. That is worth recording precisely because
it is a null result: the abstention on Evennia and CoffeeMUD was made on the hobby's history, and
the servers agree with it in their own words.

**Six offer no MSSP at all and stay unplaced**: Ew, GWM, Nightmare III, NakedMud, Dark City,
Mindcloud3. Ew's connect screen names EW-Too, which is a codebase rather than a lineage, so it is
not enough on its own.

### And nine of the eighty-two codebases on the live dashboard were sentences

Read off `mu-index.com/ecosystem` the same day. `LoginCommandReading` returned a whole line whenever
that line *mentioned* a codebase, and the line was stored as the game's `CODEBASE`:

> `The MOO is currently running version 1.8.3+47 of the LambdaMOO server code.` — and three more like
> it, one per LambdaMOO, each with a bar of its own on the chart
> `Tapestries MUCK Copyright 1991-2020 by tapestries.fur.com. All rights reserved.`
> `This MUCK is rated NC-17. If you are not 18 or are offended by this, type 'QUIT'`
> `Welcome to Pegasus, Currently running TinyMUCK2.3b2! Maintained by`
> `" ATT="src height width border=0 ismap=0" EMPTY> Welcome to The Original Tolkien Middle-earth
> MUSH! …` — 697 characters of Pueblo-tagged connect screen from a PennMUSH

The reader now keeps the codebase and discards the line: six of the nine resolve to a name and a
version (`LambdaMOO 1.8.3+47`, `PennMUSH 1.7.1`, `TinyMUCK 2.3b2`) and three are refused outright,
because "MUCK" alone is a placeholder and a copyright year is not a release. A line that *begins*
with a codebase is still kept entire — `TinyMUX 2.14.0.4 #22` is one value, not a name with prose
stuck to it.

The LambdaMOO case had a second cause worth naming: `lambdamoo` was not in the reader's marker list,
so extraction produced `MSSP`-placeholder `MOO` and the choice was the whole sentence or nothing.

**The fold was too narrow as well**, which is a separate fault in the same chart. Taking only a
trailing version token left `MUX` published four ways and `RhostMUSH` four; it now cuts at the first
version-like word, so a build tag appended after a version (`MUX 2.13.0.0-MP MPARK-ST`) goes with it.
Across the live catalogue that is **82 raw values to 63 families**, with `Alter Aeon`, `Dead Souls`,
`Materia Magica` and `Midnight Sun` intact.

Two consequences for the code, both of which the map now handles:

- **`CD.06.06` and `Epiphany v1.2.15 [development]` do not fold.** `CodebaseFamily.Of` removes a
  trailing version *token*; a version behind a dot or followed by a bracketed qualifier survives into
  the key and misses the map. Two of nineteen is not a rare shape.
- **A declared `FAMILY` is evidence for writing a row and never a way to fill the facet.** Read at
  runtime it would place a game wherever its own config file said and mix our classification with
  their assertion in one column, with no way to tell which a given row was. It is read here, once,
  by a person.

## What this survey changed

- **Spelled-out counts are read.** `resort.org:2323` says "There are seven people connected." and a
  MOO says "one of three players are active." Both were unparseable against a digits-only pattern.
  Bounded to twenty, because past that nobody spells it out and an open-ended word-number parser is
  a liability.
- **`people` and `folks` count as players.** "Players" is not the only noun a server reaches for.
- **`active` joins the connectivity vocabulary**, which is what makes the MOO sentence legible.

## The `INFO` block states a count, and we were binning it — measured 16 August 2026

`mui-probe`, two live servers, unedited:

```
$ mui-probe mush.pennmush.org 4201        $ mui-probe game.convergencemush.org 10000
mssp          Received via TelnetOption70 mssp          NotOffered via None
who           Count → 20 players          who           Count → 70 players
codebase      PennMUSH 1.8.8p0            codebase      RhostMUSH 4.27.3
info count    20                          info count    70
  | ### Begin INFO 1.1                      | ### Begin INFO 1
  | Name: M*U*S*H                           | Name: Convergence MUSH
  | Address: http://client.pennmush…        | Uptime: Tue Sep 16 23:39:43 2025
  | Uptime: Thu Jan 05 02:26:36 2023        | Connected: 70
  | Connected: 20                           | Size: 1944
  | Size: 14088                             | Version: RhostMUSH 4.27.3
  | Version: PennMUSH 1.8.8p0               | ### End INFO
  | ### End INFO
```

Three things this measured that reasoning would have got wrong.

**`INFO_VERSION` is already not `1.1` in the wild.** Convergence writes `### Begin INFO 1`. A reader
keyed on today's version string would have measured M\*U\*S\*H and silently missed Convergence — which
is the game the `INFO` name reader was written for in the first place.

**`Connected:` agrees with the live `WHO`** on both servers, 20 and 70. In PennMUSH both that line and
MSSP's `PLAYERS` come from one `count_players()` call, and the WHO agreement is the same claim seen
from a third side. That is the evidence for placing `info` on §5.2's ladder as equal-trust with
`mssp` — and therefore for putting it *below* `mssp`, where it cannot change a value already recorded
against a game that measures today.

**`Uptime:` is a ctime string, not a timestamp.** `Thu Jan 05 02:26:36 2023`. Anything here that went
looking for "the number on the line" rather than for a named label would read a year as a count.

Of 428 games sampled over three days in production, 240 were recorded unmeasurable — 122
`who_unparseable`, 118 `who_not_offered`. The MUSH-family games among them with no MSSP and a
`DOING` header softcoded past our parser were carrying their exact count in the probe's own payload
the entire time.

## Known gaps, stated rather than fixed

- **~~TinyMUSH is lost by the probe, not the parser~~ — fixed, and the guessed cause was wrong.**
  `chaos.caile.org:4444` answers `0 Players logged in, 22 record, no maximum.`, the parser reads that
  correctly in isolation, and the probe reported unknown. The guess recorded here was that the reply
  is not newline-terminated. It is: captured off the wire, the reply ends `no maximum.\r\n`.

  The real cause is that **our own `IAC DO 70` is what breaks the next command we send.** TinyMUSH
  does not parse telnet at its login screen, so the three negotiation bytes land in its line buffer
  as if typed; the next line it reads is not `WHO` but `\xff\xfd\x46WHO`, which is not a command it
  has, so it redisplays the connect screen and says nothing about players. Isolated by sending each
  prefix separately: `IAC WILL NAWS` alone leaves `WHO` working, `IAC DO 70` alone breaks it, and a
  bare `\r\n` between the two restores it. The server replies to no `IAC` at all, which is the same
  fact seen from the other side.

  The probe now sends an empty line after negotiating and before asking, and discards whatever that
  produces — it is a reaction to bytes we chose to send, so it is neither the game's connect screen
  nor its answer. TinyMUSH reads `Count 0` and the probe costs 2.0s instead of 6.2s.

- **Unterminated final lines are real, and were a separate bug.** They are not what cost us TinyMUSH,
  but the concern was sound: TelnetNegotiationCore submits a line only on a newline, so a hanging
  prompt never reaches `OnSubmit` at all. Five of twelve reference servers end a phase unterminated —
  `aardmud.org:4000` with `What be thy name, adventurer?` and `Name:`, `realms.reichel.net:4000` with
  `By what name do you wish to be known?`, `resort.org:2323` with `Please enter a name:` on *both* its
  banner and its `WHO` reply, `equestria.kaitain.org:2700` with a trailing space. The probe now closes
  each phase by feeding the interpreter the newline the server omitted, so the line is assembled by
  the library exactly as if it had arrived. That matters beyond tidiness: the guard that stops a busy
  DIKU being read as a measured zero works by recognising a login prompt, and a login prompt is
  precisely the kind of line a server leaves hanging.
- **`NakedMud` answers a menu** — `Please enter A or C:` — and has no WHO to give. MudStats reports 1
  player for it, so it has a route we do not. Worth finding out which.
- **~~ROM's MSSP report contained bytes that broke text handling~~ — retracted, and it turned out to
  be something much worse.** ROM's MSSP is well-formed (`\x01NAME\x02Abysmal Realms MUD\x01PLAYERS\x024…`)
  and parses correctly; the missing `PLAYERS` in the first survey run was an artifact of piping
  binary through `grep`, not a failure. See **MCCP2** below for what the binary actually was.
- **`EternityMUD` disagrees with itself**: MSSP says `PLAYERS 0`, its connect screen says 3. Exactly
  why the banner count is ranked last of the three sources.

## The plaintext `MSSP-REQUEST` form — 3 answered of 20, and 8 read it as a name

**Measured, and deliberately not implemented here.** The plaintext form belongs in
TelnetNegotiationCore, where it is filed as **issue #61**; `CLAUDE.md`'s rule is that a gap in that
library is a PR rather than a compensating hack in this repository. This section is the evidence for
whoever writes that PR — the probe carries none of it.

Twenty games were sent the literal line `MSSP-REQUEST` at their login screen. Three answered with a
well-formed `MSSP-REPLY-START` / tab-separated / `MSSP-REPLY-END` report:

| Game | Fields | Notes |
|---|---|---|
| `coffeemud.net:2327` (CoffeeMUD) | 47 | `PORT` reported **nine times** — 2330, 2329, 2326, 2328, 2327, 2325, 2324, 2323, 23 |
| `narutofor.us:4545` (NarutoMUD Engine) | 59 | |
| `162.243.50.82:4000` (Riftforge) | 18 | "Mortals and Monsters"; `CRAWL DELAY -1` |

**All three also answer telnet option 70**, so on this sample the plaintext form was the only route to
**no** game at all — it added nothing option 70 did not already reach. That is the case for not
carrying an implementation of it here while #61 is open: it would be code and tests buying zero
coverage today, duplicating a first-party dependency, and needing deletion when the library ships it.

And the cost of asking is measured rather than theorised. Eight servers read the request as a
character name and said so, spending one of the login attempts a stranger allows us:

> `Illegal name, try another.` — `realms.reichel.net:4000`, `tsosmud.org:7070`
> `Invalid name, please try another.` — `mud.virtustan.net:8888`, `4dimensions.org:6000`
> `Illegal name, please try another.` — `nonamemud.duckdns.org:4000`
> `Invalid account name, please try another.` — `luminarimud.com:4100`
> `'MSSP-REQUEST' does not exist.` — `eternitymud.com:23`
> `Mssp-request is not a valid name choice for sundering shadows.` — `sunderingshadows.com:8080`

## MSSP variables are lists, and seven of them were all we kept

Two separate losses, both ours rather than the library's. `MSSPConfig.Variables` has been the
lossless record since upstream PR #56 — an ordered name → value-**list** map — and the probe was
flattening it to one string per name and then keeping seven names. Measured on `alteraeon.com:23`:
**58 variables**, of which `PORT` has four values (23, 3000, 3010, 3224) and `GAMEPLAY` three
(Social, Hack and Slash, Adventure). `REFERRAL` is the same shape and is the whole basis of crawl
discovery. Joining values with `", "` was worse than dropping them, because an MSSP value may contain
a comma and the joined string cannot be split back apart.

## Live counts versus MudStats

Small differences throughout — AresMUSH 34 vs 33, TinyMUX 32 vs 28, SMAUG 9 vs 8, LuminariMUD 2 vs 3
— all consistent with ordinary churn between two observations minutes apart, not with misreporting.
No server in this sample published a count that contradicted its own other sources, except
EternityMUD above.

## The MCCP2 bug — TelnetNegotiationCore does not decompress

**Every server that negotiates MCCP2 gives us garbage text.** 13 of the 38 codebases surveyed do:
CoffeeMUD, LPMud, Evennia, DikuMUD, ROM, NarutoMUD Engine, GWM, Epiphany, FluffOS, Anatolia,
Dark City, IME, LoFP.

Proven on `realms.reichel.net:4000` (ROM):

1. Accept `IAC WILL MCCP2` with `IAC DO MCCP2`. The server emits `IAC SB MCCP2 IAC SE` at byte 18
   and everything after it is zlib.
2. That payload decompresses cleanly with a plain `zlib.decompressobj()` into the game's ASCII-art
   connect screen — so the server is correct and the stream is valid.
3. Our probe's banner begins `48 c7 8c 52 5d 6b`, **byte-for-byte the compressed payload**. It is
   handed to the text path undecompressed and decoded as UTF-8, which is why it renders as a wall of
   `U+FFFD` replacement characters.

The library negotiates the option and fires `OnCompressionEnabled`, so it believes compression is
active — it simply does not inflate the stream afterwards.

**Still true at TelnetNegotiationCore 2.7.0, re-measured on `realms.reichel.net:4000`.** Registering
`MCCPProtocol` yields one "line" of 162 characters that is 37% printable ASCII; declining yields 18
lines that are 100% printable. `OnCompressionEnabled` fires with `v2 enabled=True` in the first case,
so the library is certain it negotiated and equally certain it need not inflate.

**This is tracked upstream as issue #62 and is not worked around here.** Nothing in `MUI.Crawl`
inflates a stream, reimplements MCCP, or compensates for the library in any way — the probe simply
does not register the plugin, which is a **stopgap pending #62 and not a design position**. Two things
follow that a later reader needs:

- **Re-register MCCP the moment #62 ships.** It is a one-line change and it is waiting on nothing else.
- **The decline has a real cost, and it is a hole in layer 1.** We no longer observe that a server
  *offers* MCCP, because the library only reports the option on acceptance. So "no MCCP" in the table
  above is, for the 13 codebases concerned, our own silence rather than a measurement of theirs — the
  one place in this survey where that is true, and it closes when #62 does.

**Why MSSP still worked:** we send `IAC DO 70` immediately on connect, and the server answers before
compression starts. Anything arriving *after* the MCCP2 marker is lost — which on these 13 codebases
is the entire connect screen, the whole `WHO` reply, and any later MSSP.

This is upstream, not ours, and worth fixing there rather than working around here: MCCP is one of
the most widely deployed MU\* options and a client that cannot read a compressed stream cannot read
a third of the hobby.

## Resolved — TelnetNegotiationCore 2.8.0 inflates

Filed as upstream issue #62, fixed, and shipped in **2.8.0**. MUIndex takes the new version and
**MCCP is registered again**; the stopgap decline is gone and so is the hole in layer 1 it opened.

Re-measured on the same servers that proved the bug, with compression accepted:

| Target | Negotiated | Banner |
|---|---|---|
| `realms.reichel.net:4000` | CHARSET, **MCCP2**, MSSP | 18 readable lines |
| `coffeemud.net:2327` | **MCCP2**, MSSP | 2 |
| `play.arxmush.org:3000` | **MCCP2**, MSSP | 11 |
| `nonamemud.duckdns.org:4000` | GMCP, **MCCP2**, MSSP | 8 |
| `mush.pennmush.org:4201` (control, no MCCP) | CHARSET, MSSP | 21, `WHO` → 13 |

On 2.7.0 the first four returned a single line of roughly 37% printable bytes. Negotiating compression
*and* reading the text is a combination that was not previously possible.

Two things came back with it. We can once more observe that a server **offers** MCCP, which is a
capability the decline had cost us. And `nonamemud.duckdns.org` turns out to speak **GMCP** — a
protocol we could not see while its stream was arriving as raw zlib, so the survey's original row for
it understated what that server does.

### Follow-on: 2.8.0 inflates the first chunk and stops

Filed as [TNC #66](https://github.com/HarryCordewener/TelnetNegotiationCore/issues/66). Measured
during the first end-to-end crawl into a real database: two of four MCCP2 servers abort mid-stream
with *"the peer's compressed stream is not valid zlib"*, and the two that fail are the two with the
largest connect screens.

It is **not** concurrency — identical at 1 and 4 probes in flight. The same failing server succeeds
when the session is short enough to finish inside one read, which points at an inflater re-created
per chunk rather than kept for the connection. An MCCP2 stream is one continuous zlib stream, so a
fresh inflater meets mid-stream bytes where it expects a header.

**MCCP stays registered.** This is materially better than 2.7.0 — the handshake and the MSSP report
arrive intact and the first chunk of text is readable, where before the whole stream was noise. What
is lost is the tail. Not worked around here, for the same reason as last time.

## 2026-08-20 — pre-login prompts (`LoginPromptGate`)

A survey of all 900 `connect_screen`/`banner` rows in production found ~95 games gating their real
connect screen behind a question or menu a blind Return does not reliably answer, followed by a
second, dedicated sweep of the same 900 rows specifically for player-count reveal mechanisms (full
detail, quoted per game, in the branch-local `docs/login-prompt-scan/` — not committed, see the plan
at `docs/superpowers/plans/2026-08-20-crawler-login-prompt-negotiation.md`). Measured examples driving
each `LoginPromptGate` category: **Colour** — `cthulhumud.com:8889`, `mud.arcadia.net:4000` (both
require the explicit letter `y`, not a blank default — the reason a blind Return was never enough).
**ScreenReader** — `mud.harshlands.net:5555` and `eotmud.com:4000` (the two live phrasings map Y/N to
opposite meanings, which is why this category still only ever sends blank). **PressEnter** —
`play.ropmud.com:4443`, `vormud.genesismuds.com:7777`, plus a cluster of Korean `toox.co.kr`/
`dolba.net` games using "엔터를 누르십시오" idioms. **AgeGate** — `202.103.21.247:8888` and
`112.124.8.59:6666`, both asking the identical Chinese question, suggesting a shared codebase or
operator. **Charset (UTF-8 only)** — `dreamland.rocks:9000`, `hiervard.ru:4000`. **WhoMenu** —
`batmud.bat.org:23`, `zombiemud.org:3000`, `discworld.starturtle.net:4242`, and (after the second
sweep widened the pattern) `eternitymud.com:23` (numbered, not lettered — `2. who is playing`),
`taurosmud.com:5005` (dot-leader separator — `[2]....See who is currently logged in.`), and
`legendmud.org:9999` (connectivity word before "who" rather than after — `[4] List immortals online
who can help`).

**Deliberately not covered, and why:**
- Charset menus offering only non-UTF-8 encodings (e.g. the Cyrillic-codepage-only menus, and the
  GB/BIG5 toggles) — picking between two non-UTF-8 encodings is a guess, which rule 5 forbids
  recording as a fact; these stay on the existing staff `CHARSET` override.
- Charset menus that *do* offer UTF-8 but not as a per-line option — `mud.pkuxkx.net:8080` prints
  `Input 1 for GBK, 2 for UTF8, 3 for BIG5`, all on one line with no token punctuation, so
  `MenuOptions` finds no options to read. It is left alone for want of a parseable structure, not for
  want of a UTF-8 choice; it too stays on the staff override.
- `LoginPromptGate.PressEnterPattern`'s Korean branch matches "누르" (press) but not "입력" (input) —
  `3-third-eye-harmony` (`toox.co.kr:6000`, "[엔터]를 입력하세요") is not recognised. Low volume (one
  game surveyed); add the alternation if a second example turns up.
- Who's-online menu options whose label doesn't say "who" (or doesn't pair it with an
  online/playing/logged-in/on-line word) at all: `midnightsun2.org:3000`'s `w  -  Shows the current
  users.` and `20.61.5.183:8008` (mythosmud)'s themed `U - See who walks the Isles` (has "who", but no
  connectivity word follows it — "walks the Isles" isn't one). Not widened: relaxing either
  requirement risks matching an ordinary sentence that merely mentions "who" or "users" in passing,
  which is exactly what `AWhoMentionThatIsNotAMenuOptionIsNotAWhoMenu` guards against — the fix would
  need per-game label vocabulary, which is a maintenance treadmill this parser has deliberately stayed
  off of (see `WhoParser`'s own remarks on why it reads structure rather than per-dialect wording).
- **Prose command hints are not a gap.** By far the most common WHO-adjacent shape in the survey —
  dozens of TinyMUX/PennMUSH-family games print a sentence like "Use the WHO command to find out who
  is online currently" (`furrymuck.com:8888`) or a RhostMUSH-style `)) To see who is on ----------=>
  WHO ((` (`galaxy.silvren.com:*`, several games) instead of a selectable menu. These need no new
  code: a prose hint does not classify as `WhoMenu`, so `TelnetProbe` reaches its ordinary Phase 3 and
  sends the literal `WHO` exactly as it always did — every one of these games is already asked. (The
  one session that does *not* send a literal `WHO` is one where a `WhoMenu` option was selected and
  has already answered the question.) Recorded here so a future reader
  doesn't mistake the volume of this shape in the survey for an uncovered case.
- **~~Banner-stated player counts in vocabulary the parsers don't recognise~~ — done, and measured
  rather than guessed.** Eight real banner strings were put to the parser before anything changed;
  three already read, five did not. `Connectivity` gained `on-line` and `in the realm`, `People` gained
  `adventurers` and `mortals`, a worded twin of the bare-`on` shape was added, and `BannerCount` gained
  one narrow labelled alternative requiring noun + `on` + separator + digits (so `on` cannot read as a
  preposition — "3 messages on the board" is not a population). Verified live: `zombiemud.org:3000`
  yields 33 and `lusternia.com:5000` yields 11, both of which yielded nothing before.

  **`wizards`, `immortals` and `staff` are deliberately still not people-nouns.** That absence is
  load-bearing: it is what makes zombiemud's "There are currently 33 mortals and 4 wizards online."
  read as 33 rather than as two competing figures `BannerCount.Find` would refuse. Summing them is not
  on offer — that would be our arithmetic presented as their statement.

- **Still not read, and left that way on purpose:** counts spelled in Chinese numerals
  (`112.124.8.59:6666`'s "二百一十八位玩家"), game-specific nouns with no general meaning
  (`lostsouls.org:23`'s "atmai"), and counts split across roles where neither clause states a total.

- **A paginated `WHO` roster must never be row-counted.** `batmud.bat.org:23` answers its menu with a
  roster carrying no total and a pager — `More (18%) [qpbns?]`. Counting the rows we can see would
  publish 24 of roughly 130 as a measurement. `WhoParser` returns `Unknown` for it, which is rule 4
  working, not a gap to close: an unreadable count is honest, a truncated one is a fabrication that
  looks exactly like a real number.

### Verified against live servers, 2026-08-20

Every category was run against a real game and diffed against a build of `origin/main`. Two defects
surfaced that no fixture had reached — a menu line with several options on it was answered with the
wrong token (`batmud.bat.org:23`, `zombiemud.org:3000`), and the blank flush line ended the session on
a gated DIKU that had just been sent its answer (`mud.arcadia.net:4000`). Both fixed and re-verified.

| game | category | before | after |
|---|---|---|---|
| `mud.harshlands.net:5555` | screen-reader | 95 chars, still gated | 293 chars, cleared |
| `vormud.genesismuds.com:7777` | press-enter | 272 chars / 7 lines | 4221 chars / 43 lines |
| `mud.arcadia.net:4000` | colour | 870 chars, `LoginPrompt` | 1382 chars, `LoginPrompt` |
| `zombiemud.org:3000` | who-menu + count | no count | banner count 33 |
| `lusternia.com:5000` | banner count | no count | banner count 11 |
| `mush.pennmush.org:4201` | control | `Count` 19 | `Count` 19 |
| `chaos.caile.org:4444` | control | `Count` 0 | `Count` 0 |
| `aardmud.org:4000` | control | banner count | banner count |

Probing the same hosts repeatedly in quick succession gets connections refused — the failures that
it produces are caused by remote-end rate limiting, not by a probe defect. Space repeat runs out, or read a batch
of simultaneous `Failed` results with suspicion before believing them.

## The empty flush is load-bearing for far more than TinyMUSH — measured 2026-08-20

This file has said since July that the flush exists because `chaos.caile.org:4444` leaves our
`IAC DO 70` in its line buffer, so the next line reads as `\xff\xfd\x46WHO`. That is true, and it is
where the fix came from — but it was an origin story, never a census, and it has been quoted since as
though one server were the whole reason. It is not.

**The defect still reproduces**, four TelnetNegotiationCore versions later. Against
`chaos.caile.org:4444`, sending `IAC DO 70` and then `WHO`:

| | reply |
|---|---|
| no flush | 1644 bytes — the connect screen again; the command was never understood |
| blank line first | 85 bytes — `0 Players logged in, 22 record, no maximum.` |

**And it is not one server.** Eight games were sampled at random from those that currently yield a
`who`-sourced count *and* negotiate no telnet option at all (480 games negotiate nothing; 128 of them
produce a WHO count). Each was dialled twice, once with the flush and once without:

| host | without | with |
|---|---|---|
| `darkgift.mushpark.com:6251` | 2352 B, redisplay | **117 B, WHO answer** |
| `wackymoo.jellybean.co.uk:7777` | no answer | **227 B, WHO answer** |
| `pegasusmuck.com:4242` | no answer | **215 B, WHO answer** |
| `lit.klfree.cz:7680` | answers | answers |
| `moo.srtmoo.net:8492` | answers | answers |
| `galaxy.silvren.com:6666` | answers | answers |
| `ranmamuck.dyndns.org:1212` | answers | answers |
| `scalesmuck.com:8887` | answers | answers |

Three of eight, across MUSH, MOO and MUCK — a cross-family behaviour, not a TinyMUSH quirk. At that
rate the 128 non-negotiating games with WHO counts imply **roughly 40–50 counts lost** if the flush
were removed. The sample is small enough that the number is indicative rather than exact; the
direction is not in doubt.

**So the flush stays, and deleting it is off the table.** The only safe refinement is the conditional
one — skip it where the server has *positively demonstrated* it parses telnet, since the residue it
clears cannot exist there. Measured upside for that: 32 games, of which most would gain a more precise
unmeasurable *reason* (`who_login_prompt` rather than `who_not_offered`) rather than a count, since
they are the DIKU family that reads `WHO` as a character name. `tdome.nukefire.org:4000` is the worked
example — skip the flush, ask `WHO`, and it answers `Password:` followed by a space, then `IAC WILL ECHO` and `IAC GA`.

One caution for whoever picks that up: the discriminator must be `OfferedOptions` (protocols observed
**active**), not the presence of a `capability.*.measured` field. That field is written `false` when we
look and find nothing, so keying on its existence reports every game as negotiating —
`chaos.caile.org` included, which is the one server this section proves must keep the flush.

### The conditional skip, implemented and measured — 2026-08-20

The flush is now withheld from a server that has **positively demonstrated** it parsed our
negotiation, and sent to every other. Two things count as demonstration: a prompt answer already went
out on this session (that line did the flushing), or a protocol was observed active — `Observations.Supported`, the internal set that
reaches callers as `ProbeResult.OfferedOptions`.

Isolated crawl of exactly the affected population — the 35 games that answered, negotiated a
protocol, and had never been asked `WHO` because the flush ended the session first:

| after | games |
|---|---|
| `who_unparseable` — asked, answer unreadable | 17 |
| `who_login_prompt` — asked, and eaten as a character name | 3 |
| **counted** | **2** |
| `who_not_offered` — still never asked | 12 |

**22 of 35 now reach `WHO`, two of them yielding a player count, and nothing was lost** — every one of
the 35 already had no count to lose. A separate 250-game random crawl over the same build lost two
counts, both `coffeemud.net` ports, and both returned `PLAYERS = 17` when probed alone: that is our
own concurrency against five ports of one host, not the change.

`tdome.nukefire.org:4000` is the worked example end to end. Before: `who NotAsked`, session over in
1.7s. After: the flush is withheld, the session survives, `WHO` is asked, and the server answers
`Password:` followed by a space, then `IAC WILL ECHO` and `IAC GA` — read as `who_login_prompt`, and **the first live
observation of a Go-Ahead prompt marker this crawler has ever recorded** (`prompts marked`).

**Two cautions for anyone changing this.**

The signal is *observed protocol activity*, not a clean "did it parse IAC" fact, so it is
timing-sensitive: a server whose only protocol announces itself late reads as no evidence and is
flushed. That is why the test is written as positive evidence — every uncertain case falls to today's
behaviour, and the 12 games above that still say `who_not_offered` are that fallback working rather
than failing.

And the empty flush is no longer empty in the only case it is still sent. A server that did not parse
our negotiation is holding those IAC bytes as typing, so the line it receives carries them — which is
why `AServerThatHangsUpOnTheFlushLineStillCountsAsHavingAnswered` now models a server dropping us on
an *unrecognisable* line rather than a blank one. A genuinely blank line is no longer reachable.

