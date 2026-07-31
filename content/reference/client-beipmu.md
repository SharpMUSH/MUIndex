---
kind: client
slug: beipmu
title: BeipMU
summary: A Windows client aimed at the MUSH side of the hobby, with screen-reader support in the output window and Pueblo rather than MXP.
home: https://beipdev.github.io/BeipMU/
platform: Windows
capability: screen reader | yes | https://github.com/BeipDev/BeipMU/blob/master/Assets/Changes.txt
capability: TLS | yes | https://beipdev.github.io/BeipMU/
capability: UTF-8 | yes | https://beipdev.github.io/BeipMU/
capability: MCCP | unknown |
capability: GMCP | yes | https://github.com/BeipDev/BeipMU/blob/master/Documentation/GMCP.md
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://beipdev.github.io/BeipMU/
see-also: clients/mushclient
see-also: clients/potato
see-also: collaborative-roleplay
---

BeipMU is a MIT-licensed Windows client, actively released, and one of the few built with MUSH-style
play in mind rather than combat MUDs — multiple input windows, spawn windows, and a text engine that
expects long paragraphs. Scripting is JavaScript by default, with other ActiveScript engines
available.

## Accessibility

The output window implements Windows' `IAccessible` interface, added deliberately as a step toward
usability for visually impaired players, and there is a **Speak** trigger action for text-to-speech.
No particular screen reader is named anywhere, and there is no accessibility chapter in the
documentation.

One caution if you go looking: a page in the project's own documentation still says BeipMU cannot
use speech synthesis. That page is out of date — the changelog and the maintainer's own issue
comments both post-date it.

## Two easy mistakes about this client

**BeipMU implements MCMP, not MSP.** They are different protocols with similar names and similar
purposes, and reading one as the other would put a claim in this table that nobody made. The MSP row
therefore says unknown.

**It supports Pueblo, not MXP.** Pueblo is the older HTML-in-a-MUD scheme and MXP is the later one;
BeipMU documents basic Pueblo styles and clickable links. MXP was not established either way.
