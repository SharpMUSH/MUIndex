---
kind: protocol
slug: msp
title: MSP
summary: The MUD Sound Protocol — the server names a sound file and the client plays it. Old, simple, and easy to confuse with two other things.
protocol: MSP
home: https://www.zuggsoft.com/zmud/msp.htm
see-also: protocols/mxp
see-also: clients/vipmud
---

MSP lets a server ask a client to play a sound: a bracketed directive naming a file, a volume, a
repeat count and a URL to fetch it from if the client does not have it. It negotiates on telnet
option 90, and it can also be sent in-band in the text stream by servers that never negotiate
anything.

It is genuinely old and genuinely still used — ambient sound in a text game is a bigger effect than
it sounds like, and for players using a client's audio cues rather than its display it is more than
decoration.

## Three things it is not

The client tables in this section had to be careful here, and it is worth writing down why:

- **MCMP** — the Mud Client Media Protocol — is a different protocol doing a similar job. At least
  one client implements MCMP and not MSP, and reading one as the other would put a claim in a table
  that nobody made.
- **A client's own "play a sound" scripting call** is not MSP. It plays a local file when a script
  says so; MSP is a server telling a client what to play.
- **Bundled-plugin support is worth stating as such.** One client's MSP support ships as a plugin
  that explicitly does no telnet negotiation, which works on servers that send MSP in band and not on
  servers that expect to negotiate it.

## What we measure

Servers offering telnet option 90. Because MSP is frequently sent in band without negotiation, this
figure understates deployment by an amount we cannot estimate — which is a limitation of what a
handshake can see, and not a finding about the protocol.
