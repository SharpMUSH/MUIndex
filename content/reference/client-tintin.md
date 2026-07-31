---
kind: client
slug: tintin
title: TinTin++
summary: A terminal client with its own scripting language, on every platform including phones, and a documented screen-reader mode.
home: https://tintin.mudhalla.net/
platform: Linux
platform: macOS
platform: Windows
platform: Android
platform: iOS
capability: screen reader | yes | https://tintin.mudhalla.net/manual/screen_reader.php
capability: TLS | yes | https://github.com/scandum/tintin
capability: UTF-8 | yes | https://github.com/scandum/tintin
capability: MCCP | yes | https://tintin.mudhalla.net/
capability: GMCP | yes | https://tintin.mudhalla.net/manual/event.php
capability: MSDP | yes | https://tintin.mudhalla.net/manual/msdp.php
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/scandum/tintin
see-also: clients/blightmud
see-also: clients/mudlet
see-also: protocols/msdp
see-also: protocols/ttype
---

TinTin++ is a command-line client, GPL 3, actively released, and it runs in more places than
anything else here — including Android and iOS. Its scripting language is its own, terse, and
capable of a great deal; a substantial amount of what other clients do in the GUI is a `#config`
line here.

The same author maintains the protocol specifications for **MSSP** and **MSDP**, which is why so
many of the protocol pages in this section cite the same site.

## Accessibility

TinTin++ has a dedicated manual page for **screen reader mode** (`#config screen reader on`, or
`-s` at startup). Enabling it does two things: it removes or alters visual elements that make no
sense read aloud, and it reports screen-reader use to the server through
[MTTS](/reference/protocols/ttype), so a game can adapt its own output.

That is a documented mode, not a claim of testing with a particular reader — no product is named on
the page. It is meaningfully weaker evidence than a client that names the readers it works with, and
meaningfully stronger than nothing.

## Where the table says unknown

**MXP** and **MSP** both have community scripts on the project's site, and a script is not the
client supporting a protocol — the MXP one says outright that it may not work on every MUD. Native
support for either was not established. **ATCP** we found nothing on either way; note that ATCP is
largely superseded by GMCP, which TinTin++ does support.
