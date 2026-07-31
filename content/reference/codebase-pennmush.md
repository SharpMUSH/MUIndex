---
kind: codebase
slug: pennmush
title: PennMUSH
summary: The most widely deployed MUSH server. Softcode, a long release history, and one of only two codebases in our survey that answer both MSSP and a pre-login WHO.
codebase: PennMUSH
home: https://www.pennmush.org/
see-also: codebases/tinymux
see-also: codebases/rhostmush
see-also: codebases/cobramush
see-also: mush-mud-muck-moo
see-also: protocols/mssp
---

PennMUSH descends from TinyMUSH by way of a 1991 fork, and it is the server most long-running
roleplay MUSHes run. Its defining feature is **softcode**: a functional expression language, edited
from inside the game by anyone with the right bit set, in which a large fraction of any given
MUSH's behaviour is written. A PennMUSH game is not so much configured as programmed by its
players.

Versions read as `1.8.8p0` — a major, a minor and a patchlevel — and the patchlevel moves often.
Games frequently run a version several patchlevels behind, which is unremarkable.

## What it looks like from outside

PennMUSH is one of only two codebases in our own 38-server survey that answered *both* routes we
probe. It offers MSSP when asked, and it answers a `WHO` typed at the login screen, and on the game
we measured the two agreed — which is rarer than it sounds, and made PennMUSH the control we tested
other servers against.

The pre-login `WHO` matters beyond convenience: it is how the MUSH family publishes a player count
at all, since most of the rest of the family offers no MSSP whatsoever. See
[MSSP](/reference/protocols/mssp) for why that split is the reason this site probes four layers
rather than one.

CHARSET negotiation is normal on modern PennMUSH, which is why accented names survive the trip.

## Related servers

PennMUSH, **TinyMUX**, **RhostMUSH** and **CobraMUSH** are four servers with a common ancestor and
a shared vocabulary — a builder who knows one can read another's softcode with effort. They are not
compatible: a database does not move between them without a conversion, and function libraries
differ in ways that matter.

## SharpMUSH

A .NET reimplementation aiming at PennMUSH compatibility is in development, by the same author as
this site. Nothing on this page is measured from it, and it has no games in the catalogue.
