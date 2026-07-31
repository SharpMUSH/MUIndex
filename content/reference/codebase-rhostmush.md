---
kind: codebase
slug: rhostmush
title: RhostMUSH
summary: A MUSH server known for a deep permissions model and a large built-in function set. No MSSP; answers a pre-login WHO.
codebase: RhostMUSH
home: https://github.com/RhostMUSH/trunk
see-also: codebases/pennmush
see-also: codebases/tinymux
see-also: codebases/cobramush
---

RhostMUSH is the fourth of the TinyMUSH-descended servers in wide use, and the one with the most
elaborate administrative model: its permission and flag system is considerably finer-grained than
its relatives', which is the usual reason a game chooses it.

Its built-in function library is large, and softcode written for Rhost often does not port cleanly
to PennMUSH or TinyMUX without rewriting the parts that used functions the others do not have.

## What it looks like from outside

No MSSP. A pre-login `WHO` that answers with a count. CHARSET is negotiated.

That combination — no MSSP, a working `WHO` — is the MUSH family's signature, and it is why this
site probes the login screen at all. On the evidence of our own survey the MSSP and `WHO` families
are nearly disjoint: 28 codebases publish a count through MSSP, seven through `WHO`, and only two
through both.
