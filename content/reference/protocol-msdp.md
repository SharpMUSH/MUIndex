---
kind: protocol
slug: msdp
title: MSDP
summary: The Mud Server Data Protocol — the same job as GMCP, done with a compact binary encoding and a discovery mechanism GMCP lacks.
protocol: MSDP
home: https://www.mudhalla.net/tintin/protocols/msdp/
see-also: protocols/gmcp
see-also: clients/tintin
see-also: clients/blightmud
---

MSDP is telnet option 69, and it solves the same problem as [GMCP](/reference/protocols/gmcp):
sending structured data alongside the text so a client does not have to scrape prose for numbers.

The differences are two. MSDP's encoding is **binary and compact** — variables and values are marked
with single control bytes rather than wrapped in JSON — and MSDP defines a **discovery**
conversation: a client can ask `LIST` for `COMMANDS`, `REPORTABLE_VARIABLES` and so on, and be told
what a given game supports. GMCP has no equivalent, which is why a GMCP client generally has to be
configured per game.

In practice GMCP won on adoption and MSDP persists in the servers and clients that implemented it,
often alongside GMCP.

## What we measure

A game counts here when its server offered MSDP in a handshake we observed. As with every figure in
this section, that is a positive observation and the remainder is not its opposite — a game not
counted may not implement MSDP, or may simply not have had its handshake read by us yet.
