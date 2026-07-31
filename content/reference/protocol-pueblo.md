---
kind: protocol
slug: pueblo
title: Pueblo
summary: The older HTML-in-a-MUD scheme, from the client of the same name. Still supported by MUSH-side clients, and routinely confused with MXP.
protocol: PUEBLO
home: https://pueblo.sourceforge.net/
see-also: protocols/mxp
see-also: clients/beipmu
---

Pueblo came out of the client of the same name in the mid-nineties and took a direct approach to
enhancing MUD text: let the server send **HTML**, and let the client render it. A server announces
Pueblo support in a line at connect; the client replies, and from then on the stream may carry
markup.

It reached the MUSH side of the hobby more than the MUD side, and MUSH servers that support it
generally still do.

## Not MXP

[MXP](/reference/protocols/mxp) is the later scheme and the more widely implemented one. They do a
similar job and are not compatible, and reading a client's Pueblo support as MXP support — or the
reverse — is the single easiest mistake to make when compiling a client comparison. The client pages
in this section keep them separate for that reason, and where a project documents one and not the
other, the other says *unknown*.

## What we measure

Pueblo's handshake is not a telnet option in the usual sense, so what we observe is narrower than
for the negotiated protocols, and a low figure here should be read as a statement about our
visibility rather than about deployment.
