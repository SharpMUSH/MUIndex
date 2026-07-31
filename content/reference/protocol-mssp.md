---
kind: protocol
slug: mssp
title: MSSP
summary: The Mud Server Status Protocol — how a game tells a crawler about itself. Everything it reports is declared, not measured, and this site keeps the two apart.
protocol: MSSP
home: https://www.mudhalla.net/tintin/protocols/mssp/
see-also: protocols/gmcp
see-also: codebases/dikumud
see-also: codebases/pennmush
---

MSSP is telnet option 70. A crawler sends `IAC DO MSSP`; a server that supports it replies with a
table of name/value pairs describing itself — name, player count, codebase, uptime, hostname, port,
genre, and whatever else it cares to publish.

It is the closest thing this hobby has to a machine-readable directory entry, and it is the reason
several directories exist at all.

## Everything in an MSSP report is an assertion

This is the point on which this site differs from every incumbent. An MSSP report is the game
*telling you* about itself. `GMCP 1` in an MSSP table means somebody typed `1` into a configuration
file, possibly in 2011. It is not evidence that the server offers GMCP, and the two disagree often
enough to be interesting.

So MSSP-derived facts are labelled **declared** here, and where we can measure the same fact — a
capability, by seeing whether the option is actually negotiated — both are shown, side by side, with
an age on each. A game whose MSSP has declared GMCP for six years and has never once offered it in a
handshake is a fact worth knowing, and there is nowhere else you can find it.

The one field we deliberately do not credit at all is `CREATED`. It is a single hand-typed line, and
crediting it toward anything would make that thing trivially gameable.

## Who answers it

MSSP is the **Diku and LP** answer. In our own 38-codebase survey, 28 published a player count
through MSSP and seven through a login-screen `WHO`, and only two did both — the two families are
very nearly disjoint. AresMUSH, TinyMUX, MUCK, RhostMUSH, CobraMUSH and TinyMUSH offer no MSSP
whatsoever.

That is the empirical case for probing four layers rather than one: **a crawler built on MSSP alone
cannot see most of the MUSH family**, which is a large part of the hobby and most of this site's
intended audience.

## Ask, do not wait

A great many servers that fully support MSSP will never volunteer it — they answer `IAC DO MSSP` and
say nothing otherwise. A crawler that opens with `IAC WILL NAWS` and waits therefore reports those
games as publishing nothing, which is a claim about the server made out of the crawler's own silence.
We send `IAC DO MSSP` on connect.

## The plaintext form

There is an older variant in which a client sends the literal line `MSSP-REQUEST` at the login
screen. We measured it: of twenty games tried, three answered — and all three also answered telnet
option 70, so it reached nothing the option did not already reach. Eight servers read the request as
a **character name** and said so, spending one of the login attempts a stranger is allowed. We do not
send it.
