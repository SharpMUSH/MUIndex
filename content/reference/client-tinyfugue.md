---
kind: client
slug: tinyfugue
title: TinyFugue
summary: The classic UNIX terminal client. Upstream has not released since 2007; a maintained fork carries it forward.
home: https://tinyfugue.sourceforge.net/
platform: Linux
platform: macOS
platform: BSD
capability: screen reader | unknown |
capability: TLS | yes | https://tinyfugue.sourceforge.net/
capability: UTF-8 | unknown |
capability: MCCP | yes | https://tinyfugue.sourceforge.net/
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://tinyfugue.sourceforge.net/
see-also: clients/tintin
see-also: clients/blightmud
---

TinyFugue — "tf" — is the terminal client a large part of the MUSH world used for two decades, with
separate panes for input and output, a macro language of its own, and a set of habits that have
outlived several of its competitors.

**Upstream is dormant**: the last release is 5.0 beta 8, from January 2007. It still builds and it
still works.

A maintained fork, *TinyFugue Rebirth*, is actively released and adds GMCP, ATCP, wide-character
support through ICU, and Python and Lua scripting alongside the native macro language. The table
above describes **upstream**, because that is what "TinyFugue" resolves to; if you are installing
today, the fork is worth looking at first.

## The trap in this client's documentation

Upstream has a documentation topic called **"non-visual mode"**. It is not about assistive
technology — it concerns keeping input confined to the bottom line — and it mentions no screen
reader, no speech and no blind users anywhere. A capability table assembled by keyword search would
turn that filename into a yes. This one says unknown, because that is what the documentation
supports.

UTF-8 is the same shape of answer: the documented encoding support is for 8-bit ISO 8859 character
sets, and we found no upstream statement about UTF-8 either way.
