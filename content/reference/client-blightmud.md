---
kind: client
slug: blightmud
title: Blightmud
summary: A modern terminal client in Rust, with Lua scripting, built-in text-to-speech and a screen-reader mode that announces itself to the server.
home: https://github.com/Blightmud/Blightmud
platform: Linux
platform: macOS
platform: Windows (WSL only)
capability: screen reader | yes | https://github.com/Blightmud/Blightmud
capability: TLS | yes | https://github.com/Blightmud/Blightmud
capability: UTF-8 | yes | https://github.com/Blightmud/Blightmud
capability: MCCP | yes | https://github.com/Blightmud/Blightmud
capability: GMCP | yes | https://github.com/Blightmud/Blightmud
capability: MSDP | yes | https://github.com/Blightmud/Blightmud
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/Blightmud/Blightmud
see-also: clients/tintin
see-also: clients/mudlet
see-also: protocols/ttype
---

Blightmud is a terminal client written in Rust, GPL 3, and among the most actively released clients
in this section. Scripting is Lua. It is terminal-only: there is no native Windows build, and
Windows users run it under WSL.

## Accessibility

Blightmud has three distinct pieces here, which is more than a single row can carry:

- A **screen-reader-friendly mode** (`--reader-mode`, or the `reader_mode` setting) that changes the
  terminal UI to something a reader can follow. It does not support the status area.
- **Built-in text-to-speech**, as an optional compile, with a Lua API a script can use — including a
  `tts.gag()` for suppressing a matched line from being spoken. The documentation is candid that
  running its TTS alongside a screen reader is not always a happy combination.
- **Automatic MTTS advertisement**: in reader mode or with TTS enabled, it adds
  `MTTS_SCREEN_READER` to what it tells the server about itself, so a game that cares can adapt.

As with TinTin++, no particular screen reader is named, so this is a documented mode rather than
tested compatibility with a product.

## Where the table says unknown

**MXP**, **MSP** and **ATCP** appear nowhere in the project's README or its bundled help. **MCCP** is
documented as v2; whether v1 is also handled we did not establish.
