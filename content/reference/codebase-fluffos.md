---
kind: codebase
slug: fluffos
title: FluffOS
summary: The maintained MudOS successor, and the driver most surviving LPMud games run on. The game is written in LPC, not in C.
codebase: FluffOS
home: https://www.fluffos.info/
see-also: codebases/dikumud
see-also: mush-mud-muck-moo
---

The LPMud tradition splits the world differently from Diku. There is a **driver** — a C program that
runs an object-oriented interpreter — and a **mudlib**, which is the entire game, written in **LPC**
and loaded by the driver. Rooms, combat, commands and the login sequence are all mudlib objects; the
driver knows about none of them.

That makes an LPMud closer in spirit to a MUSH than its combat systems suggest: the game is written
in a language that lives inside the game, and two LPMuds sharing a driver may share nothing else.

**MudOS** was the dominant driver for years; **FluffOS** is its maintained continuation and is what
a running LP game is most likely to be on today. Well-known mudlibs — Nightmare, Lima, Discworld's
own — are separate projects again.

## What it looks like from outside

MSSP and **MCCP2** on the FluffOS game we measured. MudOS was one of only two codebases in our
survey to answer *both* MSSP and a login-screen `WHO`, though the `WHO` it gave was a per-player
listing rather than a count.

Because the mudlib is the game, what any particular LP game negotiates is a mudlib decision as much
as a driver one — the adoption figures on the protocol pages count what servers actually offered us,
which for this family is a weaker signal about the codebase than it is elsewhere.
