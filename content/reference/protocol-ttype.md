---
kind: protocol
slug: ttype
title: TTYPE and MTTS
summary: How a client tells a server what it is and what it can do — including, if the client chooses to say so, that a screen reader is in use.
protocol: TTYPE
home: https://www.mudhalla.net/tintin/protocols/mtts/
see-also: protocols/charset
see-also: clients/tintin
see-also: clients/blightmud
---

TTYPE is telnet option 24, from RFC 1091: the server asks the client what terminal it is, and the
client answers. Historically the answer was `VT100` or `ANSI`.

**MTTS** — the Mud Terminal Type Standard — layers a convention on top. A client answers three times:
its name, its terminal type, and then `MTTS <bitmask>`, where the bits declare capabilities. 256
colours, true colour, UTF-8, MNES, MSP over out-of-band — and, notably, **`MTTS_SCREEN_READER`**.

## The screen-reader bit

That last one is worth pausing on, because it is the only place in this hobby's protocol stack where
accessibility is a first-class concept.

A client that sets it is telling the server that a screen reader is in use, and a server that
notices can adapt: suppress ASCII art, drop the decorative box-drawing around a room description,
change how a table is laid out. Both [TinTin++](/reference/clients/tintin) and
[Blightmud](/reference/clients/blightmud) advertise it, and [Mudlet](/reference/clients/mudlet) has a
setting for it.

Whether any given game acts on it is a different question, and not one this site can measure — we
cannot ask a server what it would do differently.

## What a crawler owes here

A crawler identifies itself through TTYPE, and it should. Ours does, with an information URL, so an
administrator reading their logs can find out who has been connecting to their game and how to ask
us to stop. A crawler that answers `ANSI` and nothing else is anonymous by design, and there is no
good reason for that.

## What we measure

Servers that negotiated TTYPE with us. Note that this is one of the few options where *we* are the
side being asked, so a figure here is a count of servers that cared to ask.
