---
kind: codebase
slug: dikumud
title: DikuMUD
summary: The root of the combat-MUD family. Levels, classes, equipment and area files — and a licence that shaped a generation of derivatives.
codebase: DikuMUD
home: https://dikumud.com/
see-also: codebases/circlemud
see-also: codebases/rom
see-also: codebases/smaug
see-also: mush-mud-muck-moo
---

DikuMUD, written at Datalogisk Institut at the University of Copenhagen and released in 1991, is the
ancestor of most of what people mean when they say "MUD" without qualification. Levels, character
classes, hit points, mobs, equipment slots, an area file format a builder writes offline — the whole
vocabulary comes from here, and games that have never seen Diku source still inherit its shape.

Its licence is part of the story. Diku was free to use but forbade charging for access and required
the original credits to be displayed, and that clause is why "the Diku credits" appear on the login
screen of games several forks removed from it.

The direct descendants — **Merc**, then **ROM**, **CircleMUD**, **SMAUG**, **tbaMUD** and dozens of
others — account for a large fraction of every MUD listing that has ever existed.

## What it looks like from outside

The Diku family is the **MSSP** family. Where the MUSH side publishes a count through a login-screen
`WHO` and offers no MSSP at all, Diku-line servers overwhelmingly answer telnet option 70 with a
structured report, and that is where their numbers here come from.

**MCCP2** — stream compression — is also common in this family, and it is worth knowing that a
client which negotiates it but cannot inflate the stream receives the entire connect screen as
binary noise. That was a real defect in this project's own telnet library and it is fixed; see
[MCCP](/reference/protocols/mccp).
