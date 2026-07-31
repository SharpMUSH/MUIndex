---
kind: client
slug: mudlet
title: Mudlet
summary: Cross-platform, Lua-scripted, and the client with the most thoroughly documented screen-reader support in this section.
home: https://www.mudlet.org/
platform: Windows
platform: macOS
platform: Linux
capability: screen reader | yes | https://wiki.mudlet.org/w/Manual:Screen_Readers
capability: TLS | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: UTF-8 | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MCCP | unknown |
capability: GMCP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MSDP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: ATCP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MXP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MSP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: scripting | yes | https://github.com/Mudlet/Mudlet
see-also: clients/blightmud
see-also: clients/tintin
see-also: protocols/gmcp
see-also: connecting
---

Mudlet is a graphical client with a mapper, a package system and a Lua API that most of its own
feature set is written against. It is GPL, actively released, and the usual recommendation for
someone starting on a modern combat MUD.

## Accessibility

This is the client with the strongest documented case in this section, and it is worth spelling out
what "documented" means here, because it is unusual.

Mudlet has a **manual chapter on screen readers**, per-operating-system pages naming Narrator, NVDA
and JAWS on Windows, Orca on Linux and VoiceOver on macOS, an in-client `mudlet access on` command,
and an option to announce incoming game text through the reader. It also has a setting that
advertises screen-reader use to the server over MTTS, so a game can adapt if it wants to.

It is also candid about where it does not work well: its own Windows page says JAWS does not read
the output window the way other readers do, and recommends Narrator or NVDA instead. A project that
publishes the case where its accessibility support is impractical is giving you better information
than one that publishes a tick.

## Where the table says unknown

**MCCP.** Mudlet's source implements MCCP v1 and v2, but the manual's supported-protocols page does
not list it, and this section's rule is that a capability claim cites the project's own
documentation. Reading a constant out of a header is not the same act, so the cell says unknown.

## Note on encoding

Mudlet's default server-data encoding is ASCII rather than UTF-8, and CHARSET negotiation arrived
in 4.10. If a game's text comes out wrong on a fresh profile, that setting is the first place to
look.
