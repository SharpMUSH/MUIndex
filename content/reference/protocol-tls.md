---
kind: protocol
slug: tls
title: TLS
summary: Encrypted connections. Usually a separate port rather than a negotiated upgrade, and the one capability on this site we verify by connecting rather than by asking.
protocol: TLS
see-also: connecting
see-also: protocols/charset
see-also: clients/potato
---

Telnet is plaintext. Everything you send a MU\* — including your password — crosses the network
readable by anything on the path, unless the game offers TLS.

In this hobby TLS almost always means **a second port that speaks TLS from the first byte**, not an
in-band upgrade. A game with a plain port on 4201 and a TLS port on 4202 is the common shape. There
is a negotiated variant, and it is rare enough that at least one client's documentation explicitly
says it is not supported.

## Why the game pages mark this specially

TLS is the one capability on this site established by *doing it*: an endpoint is marked TLS because
we completed a TLS handshake against it. There is no asking involved and no field to declare, which
makes it the cleanest measurement in the catalogue.

That is also why a game's TLS port and its plain port are listed as separate endpoints rather than
merged. They are different measurements of different things.

## Practical advice

If a game you play offers a TLS port, use it. If it does not and you care, ask — it is a small
amount of work for an administrator, and the reason it is not universal is mostly that nobody has
asked rather than that anybody objects.

Check whether your client supports it before you rely on it. Several in the [clients](/reference)
section do; at least one documents a workaround with an external `stunnel` process instead, which
works and is more setup than most people will do.
