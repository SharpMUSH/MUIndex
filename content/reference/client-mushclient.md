---
kind: client
slug: mushclient
title: MUSHclient
summary: The long-established Windows client. Five scripting languages, a plugin architecture that most of its protocol support lives in, and a release history that has slowed.
home: https://www.mushclient.com/
platform: Windows
platform: Linux (Wine)
capability: screen reader | unknown |
capability: TLS | unknown |
capability: UTF-8 | unknown |
capability: MCCP | yes | https://www.mushclient.com/mushclient/mccp.htm
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | yes | https://www.mushclient.com/gmcp
capability: MXP | yes | https://www.mushclient.com/mushclient/doc/general/features.html
capability: MSP | yes | https://github.com/nickgammon/mushclient/blob/master/plugins/msp.xml
capability: scripting | yes | https://www.mushclient.com/mushclient/doc/general/features.html
see-also: clients/mudlet
see-also: clients/potato
see-also: protocols/mccp
---

MUSHclient is Nick Gammon's Windows client, MIT-licensed, and for a long stretch the default answer
for anyone on Windows. It scripts in Lua, VBScript, JScript, PerlScript and Python, and much of what
it does is carried by plugins rather than by the core — which is a genuine architectural choice and
also the reason several rows above are harder to answer than they look.

The last tagged release is **5.06, from March 2019**. The repository is still being committed to,
and there are release notes for a 5.07 that has not shipped.

## Why so many rows say unknown

Every one of them is a case where the honest answer is "we could not establish it", and the reasons
differ:

- **GMCP** — the project's own page on it presents an *example* plugin you could write, not a
  feature the client has. That is different from shipping support, so the cell is unknown rather
  than yes.
- **TLS** — the documented method is an external `stunnel` process. A commit adding OpenSSL-backed
  TLS landed on the master branch in 2026 and is not in any release, so there is nothing a user can
  install today that we can point at.
- **UTF-8** — CHARSET negotiation appears in the unreleased 5.07 notes and nowhere we could find in
  a shipped version's documentation.
- **MSDP** — nothing either way.
- **Screen reader** — a text-to-speech plugin using Windows SAPI ships with the client, and that is
  not the same thing as screen-reader support. There is no accessibility section in the manual, and
  the author has described in his own forum why the output window is hard for a reader to work
  with: it has no concept of a current line. We could not establish an answer, so the table does not
  give one.

None of these is a *no*. Several may well be yes and we could not show it.
