---
kind: client
slug: potato
title: Potato MUSHclient
summary: A cross-platform Tcl/Tk client written for MUSH players. Good encoding support, and a documentation set that says nothing at all about most protocols.
home: https://www.potatomushclient.com/
platform: Windows
platform: Linux
platform: macOS
capability: screen reader | unknown |
capability: TLS | yes | https://github.com/potatomushclient/potato/wiki/ConfigureWorldsBasics
capability: UTF-8 | yes | https://github.com/potatomushclient/potato/wiki/Features
capability: MCCP | unknown |
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/potatomushclient/potato/wiki/FAQs
see-also: clients/beipmu
see-also: clients/mushclient
see-also: collaborative-roleplay
---

Potato is a Tcl/Tk client built for MUSH play — multiple worlds, spawn windows, and a set of
defaults that assume you are typing poses rather than combat commands. It runs on Windows, Linux and
macOS from the same source, with the macOS builds usually a version or two behind.

It negotiates character encoding and speaks full Unicode, which for the MUSH side of the hobby is
the capability that matters most in practice.

Note one documented limitation: it supports connecting to a port that is SSL from the start, and its
own configuration page says STARTTLS-style negotiated SSL is **not** supported.

## Why six rows say unknown

We searched the project's home page, its downloads page, all 103 of its wiki help files and its
entire source tree for GMCP, MSDP, MCCP, MXP, MSP and ATCP. There is no documented statement about
any of them. There is *code* that touches some of them, and this section does not turn code into a
capability claim — a table that says "yes" on the strength of a constant in a header is making a
promise the project never made.

The screen-reader row is the same answer reached the same way: a case-insensitive sweep for "screen
reader", "text-to-speech", NVDA, JAWS, VoiceOver, "accessibility", "visually impaired" and "blind"
across everything the project publishes returned nothing at all. That is not a finding about the
software.
