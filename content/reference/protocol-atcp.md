---
kind: protocol
slug: atcp
title: ATCP
summary: GMCP's predecessor. Out-of-band data with a looser payload, largely superseded, and still negotiated by servers that never removed it.
protocol: ATCP
see-also: protocols/gmcp
see-also: protocols/msdp
see-also: clients/mudlet
---

ATCP — the Achaea Telnet Client Protocol — is telnet option 200, and it is where the idea of sending
structured data alongside MUD text was first widely deployed. A server sends a module name and a
payload; the client routes it.

Its payload format is looser than [GMCP](/reference/protocols/gmcp)'s JSON, which is essentially why
GMCP replaced it. Clients that support ATCP now generally document it as deprecated and point you at
GMCP instead.

## Why it is still here

Because nothing breaks by leaving it on. A server that implemented ATCP in 2008 and added GMCP in
2014 usually still negotiates both, and a client that supports both will take whichever it is
offered.

For a new implementation there is no reason to choose it.

## What we measure

Servers offering telnet option 200 in a handshake we observed. A low figure here is expected and is
about age rather than about anything else.
