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
| TinyMUSH | `chaos.caile.org:4444` | NotOffered | — | Unknown | — | 0 | (none observed) |
| Anatolia | `crusify.kharkov.org:5000` | Received | 0 | Unknown | — | 0 | MCCP2, MSSP |
| Dark City | `godwars.net:3000` | Received | 0 | Unknown | — | 0 | MCCP2, MSSP |
| Enrym | `play.enrym.com:4000` | Received | 0 | Unknown | — | 0 | MSSP |
| EternityMUD | `eternitymud.com:23` | Received | 0 | Unknown | 3 | 0 | EOR, MSSP |
| IME | `iberia.jdai.pt:5900` | Received | 0 | Unknown | — | 0 | MCCP2, MSSP |
| LoFP | `lofp.metavert.io:4000` | Received | 0 | Unknown | — | 0 | GMCP, MCCP2, MSSP |
| Mindcloud3 | `ianshirm.genesismuds.com:2251` | Received | 0 | Unknown | — | 0 | MSSP |
| tbaMUD | `mud.virtustan.net:8888` | Received | 0 | Unknown | — | 0 | MSSP |

## What this survey changed

- **Spelled-out counts are read.** `resort.org:2323` says "There are seven people connected." and a
  MOO says "one of three players are active." Both were unparseable against a digits-only pattern.
  Bounded to twenty, because past that nobody spells it out and an open-ended word-number parser is
  a liability.
- **`people` and `folks` count as players.** "Players" is not the only noun a server reaches for.
- **`active` joins the connectivity vocabulary**, which is what makes the MOO sentence legible.

## Known gaps, stated rather than fixed

- **TinyMUSH is lost by the probe, not the parser.** `chaos.caile.org:4444` answers
  `0 Players logged in, 22 record, no maximum.` and the parser reads that correctly in isolation —
  yet the probe reported unknown. The likely cause is that the reply is not newline-terminated, so
  `OnSubmit` never fires for the final line and the probe never sees it. **Unterminated final lines
  are invisible to a line-oriented callback**, and MU\* servers leave prompts unterminated constantly.
  This is the most valuable outstanding fix in the probe.
- **`NakedMud` answers a menu** — `Please enter A or C:` — and has no WHO to give. MudStats reports 1
  player for it, so it has a route we do not. Worth finding out which.
- **ROM's MSSP report contained bytes that broke text handling** on `realms.reichel.net:4000`; it
  reported `Received` with no `PLAYERS` parsed. Binary in an MSSP value is worth a fixture.
- **`EternityMUD` disagrees with itself**: MSSP says `PLAYERS 0`, its connect screen says 3. Exactly
  why the banner count is ranked last of the three sources.

## Live counts versus MudStats

Small differences throughout — AresMUSH 34 vs 33, TinyMUX 32 vs 28, SMAUG 9 vs 8, LuminariMUD 2 vs 3
— all consistent with ordinary churn between two observations minutes apart, not with misreporting.
No server in this sample published a count that contradicted its own other sources, except
EternityMUD above.
