---
kind: codebase
slug: rom
title: ROM
summary: Merc's best-known descendant, and the combat engine a large share of nineties MUDs were built on.
codebase: ROM
see-also: codebases/dikumud
see-also: codebases/smaug
see-also: protocols/mccp
---

ROM — *Rivers of MUD* — is a derivative of **Merc**, which is itself a DikuMUD derivative, and it is
the one that stuck. Its combat model, its skill and spell system and its area format were the
starting point for an enormous number of games through the nineties and after, and ROM 2.4 in
particular is one of the most-forked pieces of source in the hobby.

Like the rest of the Diku line it carries the original credits requirement, so a game whose lineage
you cannot otherwise establish will often name Diku, Merc and ROM on its login screen.

## What it looks like from outside

MSSP, CHARSET and **MCCP2**, on the game we measured.

ROM is the server this project proved its own compression bug against. Our probe negotiated MCCP2,
the server correctly began compressing, and the telnet library we depend on never inflated the
stream — so the connect screen arrived as a wall of replacement characters and we briefly recorded
that as the game's fault. The payload decompressed cleanly with a stock zlib call, which is what
made it unambiguous. It was fixed upstream; the story is on the [MCCP](/reference/protocols/mccp)
page, because it is a good example of a defect that looks exactly like a broken game from outside.
