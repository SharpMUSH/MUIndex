---
kind: codebase
slug: moo
title: MOO
summary: Object-oriented, edited entirely from inside, and as much a research and teaching platform as a game engine.
codebase: MOO
home: https://www.ipomoea.org/moo/
see-also: mush-mud-muck-moo
see-also: codebases/muck
---

MOO — *MUD, Object-Oriented* — takes the "the world edits itself" idea further than anything else
in the hobby. LambdaMOO, the original server, ships a small C core and a database; essentially
everything a user experiences is written **in the MOO language, inside the running database, by the
people using it**. There is no source file for a room.

That property gave MOOs a life outside games. Through the nineties they were used for teaching,
conferencing and research — Diversity University, BioMOO, Jay's House — and the technical
literature about MOO is disproportionately academic for a codebase in this space.

Deployment today is small but genuinely non-zero, and the servers that remain have often been
running continuously for decades.

## What it looks like from outside

No MSSP, and no `WHO` we could parse on the game we measured. What it did have was a sentence in
its connect screen reading *"one of three players are active"* — which is where the spelled-out
number reader in this crawler comes from. A digits-only parser sees no count there at all, and
would have reported that game as unknown for ever.
