---
kind: protocol
slug: mccp
title: MCCP
summary: Stream compression. Cheap, widely deployed, and the protocol that produced the most instructive bug in this project's history.
protocol: MCCP
home: https://www.mudhalla.net/tintin/protocols/mccp/
see-also: codebases/rom
see-also: codebases/dikumud
see-also: protocols/gmcp
---

MCCP compresses the server-to-client stream with zlib. Version 1 is telnet option 85 and is
effectively historical; **version 2** is option 86 and is what modern servers negotiate. After the
server sends `IAC SB MCCP2 IAC SE`, every byte that follows is part of one continuous zlib stream.

It is a real saving on a text protocol — MUD output compresses extremely well — and it is common in
the Diku and LP families, where roughly a third of the codebases we surveyed negotiate it.

## The failure mode, and why it matters here

A client that negotiates MCCP2 and then does not inflate the stream receives **binary garbage from
the compression marker onward**. Not an error, not a disconnection: the connect screen arrives as a
wall of replacement characters, and everything after it — the `WHO` reply, any later MSSP, the whole
session — is lost.

This is not hypothetical. Our own telnet library did exactly that. It negotiated the option, fired
its "compression enabled" callback, and never inflated a byte. The payload decompressed cleanly with
a stock zlib call, which is what made it unambiguous that the servers were correct and we were not.
Thirteen of the thirty-eight codebases in our survey were affected, and for the duration we could not
observe what those servers negotiated *after* compression started — so our record of their
capabilities understated them.

It was fixed upstream. A follow-on defect — the inflater being re-created per read rather than kept
for the connection, which fails partway through a large connect screen — is filed and open, and
affects the tail of the largest screens.

Two things a reader should take from this. **A protocol figure on this page is a measurement of our
crawler as much as of the hobby**, and where we know it has been wrong we say so. And if you are
writing a client: negotiating MCCP is easy and inflating it correctly is where the work is.
