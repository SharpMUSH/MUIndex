---
kind: protocol
slug: charset
title: CHARSET
summary: RFC 2066's telnet option for agreeing an encoding. The reason a game's accented names survive the trip, and the source of some subtle failures when it is absent.
protocol: CHARSET
home: https://www.rfc-editor.org/rfc/rfc2066
see-also: protocols/ttype
see-also: connecting
see-also: codebases/tinymux
---

CHARSET is telnet option 42, specified in RFC 2066. One side offers a list of character sets, the
other picks one, and both then agree on how bytes map to characters.

In practice the negotiation settles on **UTF-8** or is not held at all. The MUSH family negotiates it
noticeably more than the MUD family — TinyMUX, RhostMUSH and PennMUSH all do — which reflects a
population that writes prose with names in it.

## What happens without it

A client has to guess, and the usual guess is either ASCII or Latin-1. Guess ASCII and every byte
above 0x7F becomes a question mark; guess Latin-1 on a UTF-8 server and every accented character
becomes two pieces of punctuation. Both failures look like the game's fault and are not.

For a crawler this bites in a specific place. Our own telnet library defaults its current encoding to
ASCII, and that default is not inert — it is what every byte is decoded with, for every server that
never negotiates CHARSET, which is most of them. We seed it deliberately for that reason.

## The one place CHARSET does not reach

MSSP field names and values are decoded as ASCII regardless of what CHARSET settled on, because a
subnegotiation is a command rather than text and the specification scopes CHARSET to text. That is
arguably conformant and it is lossy: a game whose MSSP `NAME` is `Café Noir` reports `Caf? Noir`, and
the original bytes are gone before anything we control sees them.

If you see a mangled character in a declared field on this site and not in the game's own output,
that is why, and it is not recoverable from our side.
