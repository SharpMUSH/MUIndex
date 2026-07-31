---
kind: client
slug: atlantis
title: Atlantis
summary: A macOS-only client, long-lived and long in beta. Its scripting is documented as no longer working, which is the one honest "no" in this section.
home: https://www.riverdark.net/atlantis/
platform: macOS
capability: screen reader | unknown |
capability: TLS | yes | https://www.riverdark.net/atlantis/history.php
capability: UTF-8 | yes | https://www.riverdark.net/atlantis/history.php
capability: MCCP | yes | https://www.riverdark.net/atlantis/history.php
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | no | https://www.riverdark.net/atlantis/
see-also: clients/mudlet
see-also: protocols/charset
---

Atlantis is a native macOS client that has been around since Mac OS X 10.3 and was updated for
64-bit in the Catalina era. It handles RFC 2066 character-set negotiation and Unicode, which is
better than its age would suggest, and it does MCCP and SSL.

## The one "no" in this section

Its scripting was Perl, through the CamelBones bridge, and the project's own home page says it no
longer works — Apple's handling of Perl changed and the library's author died some years ago. That
is a *sourced absence*, which is a different thing from an unknown, and it is the only cell in the
whole client section that carries one. Everywhere else the honest answer was that we could not
establish it.

## Everything we could not establish

The version history is complete and public and mentions **MCCP**, **SSL** and **charset
negotiation** — and never mentions GMCP, MSDP, ATCP or MSP. MXP appears once, as something intended
for a version after 1.0.0, which has not arrived.

There is a Perl `Atlantis::Speak()` call in the scripting API, and it would be easy to read that as
screen-reader support. It is not: it is a scripted text-to-speech call in a scripting system the
project says does not work. VoiceOver, "accessible" and "screen reader" appear on none of the home
page, the downloads page, the full version history, or the archived user guide.

The current download is 0.9.9.8, still nominally a beta, with no release date published anywhere on
the site.
