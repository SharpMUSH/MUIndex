---
kind: client
slug: vipmud
title: VIP Mud
summary: A commercial Windows client built for blind players from the ground up. It names seven screen readers — and publishes almost nothing about its protocol support.
home: https://www.gmagames.com/vipmud.shtml
platform: Windows
capability: screen reader | yes | https://www.gmagames.com/vipmud.shtml
capability: TLS | unknown |
capability: UTF-8 | unknown |
capability: MCCP | unknown |
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | yes | https://www.gmagames.com/vipmud.shtml
capability: scripting | yes | https://www.gmagames.com/vipmud.shtml
see-also: clients/mudlet
see-also: clients/blightmud
---

VIP Mud is the one client in this section whose *entire* design premise is accessibility. It is
commercial — thirty dollars, with a thirty-day full trial after which it keeps working with a
reduced feature set — and it is a Windows program.

It is the strongest accessibility claim here by a distance, and unusually it is specific. The
product page names **JAWS, Window-Eyes, System Access, NVDA, Cobra, SuperNova/Hal and Microsoft
SAPI** as working out of the box, and describes features that only make sense if you have thought
hard about the problem: different voices per window and per output type, gagging spam from speech
while still showing it, and several methods of suppressing ASCII art — which is the single most
hostile thing a MUD sends to a screen reader.

## Why the rest of the table is empty

Because the vendor publishes a marketing page and not a manual. Nothing on it mentions GMCP, MSDP,
MCCP, MXP, ATCP, TLS or character encoding; it describes the product as "a Telnet-based client" and
leaves it there. **Nine unknowns in a row is not a verdict on the software.** It is what a matrix
looks like when the only available source is one page, and publishing it as nine noes would be a
lie about a product that may well do all of it.

Two further things we could not establish: any release date for the current version, and whether it
is still under active development — the vendor was acquired in February 2025, and the product page
carries a 2016 copyright.
