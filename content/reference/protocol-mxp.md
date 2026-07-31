---
kind: protocol
slug: mxp
title: MXP
summary: The MUD eXtension Protocol — HTML-like markup in the text stream, giving clickable links, images and forms. Widely specified, unevenly implemented.
protocol: MXP
home: https://www.zuggsoft.com/zmud/mxp.htm
see-also: protocols/pueblo
see-also: clients/mushclient
see-also: clients/mudlet
---

MXP embeds a small, HTML-like markup language in the text a server sends: `<send>` for a clickable
command, `<a href>` for a link, colour and font elements, and a mechanism for a server to define its
own tags. It negotiates on telnet option 91.

Its design problem is inherent and interesting: the markup travels in the same stream as the text,
so a server has to be careful about text that *looks* like markup, and a client has to be careful
about what it will render. MXP defines security levels for exactly this reason — a tag arriving in a
line of chat from another player is not the same as a tag the server emitted itself.

## Clickability is the reason people want it

Most of what MXP is actually used for is turning `north` and item names into things you can click.
For a new player that is a substantial difference, and it is why the protocol keeps being
implemented despite its complexity.

## Pueblo is the other one

[Pueblo](/reference/protocols/pueblo) predates MXP and does a similar job with a different, more
literally HTML-shaped approach. A client that supports one frequently does not support the other,
and the two are easy to confuse when reading a feature list — which is a mistake we have had to be
careful about in the client tables in this section.

## What we measure

Servers offering telnet option 91 in a handshake we observed. MXP is less commonly negotiated than
the out-of-band protocols, partly because much of its value is realised by servers that simply emit
the markup and hope, without negotiating at all — which we cannot see.
