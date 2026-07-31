---
kind: protocol
slug: gmcp
title: GMCP
summary: The Generic Mud Communication Protocol — structured JSON messages alongside the text, and the out-of-band channel most modern clients build against.
protocol: GMCP
home: https://www.mudhalla.net/tintin/protocols/gmcp/
see-also: protocols/msdp
see-also: protocols/atcp
see-also: clients/mudlet
---

GMCP is telnet option 201. Once negotiated, the server can send **structured data out of band**:
a package name and a JSON payload, arriving in the same stream as the text but not part of it.

`Char.Vitals { "hp": 412, "maxhp": 500 }` is the canonical example. A client can drive a health bar
from that without scraping the prose for numbers, which is the entire point — a status display built
on pattern-matching the text breaks the day a game changes its prompt, and one built on GMCP does
not.

The package namespace is conventional rather than standardised. `Char`, `Room`, `Comm` and `Client`
are widely used; beyond that, games invent what they need, and a client generally has to be told what
a given game sends.

## Why it displaced ATCP

GMCP is the successor to [ATCP](/reference/protocols/atcp), which did the same job with a
looser payload format. JSON was the improvement, and the migration was largely complete by the
mid-2010s. A game supporting both is not unusual; a new game supporting only ATCP would be.

## What we measure

A game counts here when **its server offered GMCP in a handshake we observed**. That is a different
claim from a game's MSSP saying `GMCP 1`, which is what most protocol tables in this hobby are built
on, and the two disagree regularly.

One measurement note from our own history: for a period we could not see GMCP on servers that also
negotiated [MCCP](/reference/protocols/mccp), because our telnet library negotiated compression
without inflating the stream and everything after the compression marker was noise to us. At least
one server in our survey turned out to speak GMCP all along. If a figure on this page looks low for
a family you know well, that class of defect is the first thing to suspect — in us, not in them.
