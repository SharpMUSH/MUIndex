---
kind: codebase
slug: aresmush
title: AresMUSH
summary: A modern roleplay server written in Ruby, with a web front end and scene tools built in rather than softcoded.
codebase: AresMUSH
home: https://aresmush.com/
see-also: collaborative-roleplay
see-also: codebases/pennmush
see-also: codebases/evennia
---

AresMUSH is the newest server in wide use aimed squarely at **collaborative roleplay**, and it takes
a different position from the TinyMUSH line it succeeds. Where a PennMUSH game builds its scene
system, its character sheets and its job queue out of softcode written by whoever was around, Ares
ships those as features and expects a game's staff to configure rather than program them.

It comes with a **web portal** — character wikis, scene logs, forums and the game itself, all
reachable from a browser — which for a genre where people read the logs afterwards is a substantial
difference in kind rather than in degree.

Configuration is in YAML; extensions are Ruby plugins. There is no in-game programming language for
players, which is the trade: less rope, less rope-related injury, and less of the improvisational
building culture that the MUSH line is named for.

## What it looks like from outside

No MSSP. It answers a pre-login `WHO`, and the answer is a **per-player list** rather than a bare
number, which our parser counts by structure. No telnet options were negotiated on the game we
measured.

If you are choosing between this and PennMUSH for a new roleplay game, the question is roughly
whether you want a system you configure or a system you write.
