---
kind: orientation
slug: connecting
title: How to connect
summary: A host, a port, and telnet. What the address on a game page means and what to do with it.
see-also: mush-mud-muck-moo
see-also: protocols/tls
see-also: protocols/charset
---

Every game listed here answers on a **host and a port**, and the protocol underneath is telnet —
which in practice means a raw TCP connection with a small amount of optional negotiation on top.

    telnet mush.pennmush.org 4201

That works, and on many systems it is already installed. It is also a poor way to play: the system
`telnet` has no local echo control worth the name, no logging, no history, and it will mangle
anything above ASCII. It is the right tool for checking that a game is up and the wrong one for
spending an evening in.

## What the address on a game page tells you

Each game page lists the endpoints we have measured, and marks any where **TLS** was observed. A
game with a TLS port is a game you can connect to encrypted; the port number is usually different
from the plain one.

Where a game has several ports, they are frequently the same world reached different ways rather
than different games. We list what we measured and do not guess at which is canonical.

## Choosing a client

The [clients](/reference) section has a page each, with a capability table. The three things worth
checking before you install anything:

- **Does it do UTF-8?** If the game is not English-only, this will come up on your first evening.
- **Does it do TLS?** Only matters if the game offers it, but several now do.
- **If you use a screen reader, does the project document support for it?** This is the row most
  often missing from client comparisons, so it is the first row of ours — and where nobody has
  established an answer, it says *unknown*.

## If nothing answers

A game that does not answer is not necessarily gone. Games move hosts, DNS lapses, and firewalls
have opinions. This site keeps every game it has ever measured — including the ones that stopped
answering years ago — and keeps knocking weekly, so the [archive](/archive) is the place to look
before concluding anything.
