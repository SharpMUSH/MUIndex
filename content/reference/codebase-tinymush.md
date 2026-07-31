---
kind: codebase
slug: tinymush
title: TinyMUSH
summary: The ancestor of the MUSH line, still running games. It taught this crawler that its own negotiation bytes can break the next command it sends.
codebase: TinyMUSH
home: https://github.com/TinyMUSH/TinyMUSH
see-also: codebases/pennmush
see-also: codebases/tinymux
see-also: mush-mud-muck-moo
---

TinyMUSH is where the line PennMUSH, TinyMUX, RhostMUSH and CobraMUSH all descend from, and it is
still deployed. Development is quiet rather than absent.

## What it looks like from outside

No MSSP. A pre-login `WHO` that answers with a sentence of the form `0 Players logged in, 22
record, no maximum.`

## The bug it found in us

TinyMUSH is worth a paragraph here because it is the game that exposed a defect in this site's own
crawler, and the correction is a good illustration of what "measured" is supposed to mean.

Our probe read TinyMUSH as *count unknown* for weeks. The guess on file was that its reply had no
trailing newline. It does. Captured off the wire, the real cause was ours: **TinyMUSH does not
parse telnet at its login screen**, so the three bytes of `IAC DO MSSP` we send on connect land in
its input buffer as though somebody had typed them. The next line it reads is not `WHO` but three
control bytes followed by `WHO`, which is not a command it has — so it redisplays its connect
screen and says nothing about players.

The probe now sends a bare newline after negotiating and discards whatever that produces, because
that output is a reaction to bytes *we* chose to send and is therefore neither the game's connect
screen nor its answer. TinyMUSH reads correctly now, and the probe finished in a third of the time.

A directory that had not checked would have published "this game does not report its players" for
as long as it existed, and the sentence would have been about us.
