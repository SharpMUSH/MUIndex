---
kind: protocol
slug: atcp
title: ATCP
summary: Der Vorgänger von GMCP. Out-of-Band-Daten mit lockererer Nutzlast, weitgehend abgelöst und immer noch von Servern ausgehandelt, die es nie entfernt haben.
protocol: ATCP
see-also: protocols/gmcp
see-also: protocols/msdp
see-also: clients/mudlet
---

ATCP — das Achaea Telnet Client Protocol — ist Telnet-Option 200, und hier wurde die Idee, neben dem
MUD-Text strukturierte Daten zu senden, zum ersten Mal breit eingesetzt. Ein Server sendet einen
Modulnamen und eine Nutzlast; der Client leitet sie weiter.

Sein Format für die Nutzlast ist lockerer als das JSON von [GMCP](/reference/protocols/gmcp), und im
Wesentlichen ist das der Grund, warum GMCP es abgelöst hat. Clients, die ATCP unterstützen, führen es
heute in der Regel als veraltet und verweisen stattdessen auf GMCP.

## Warum es immer noch da ist

Weil nichts kaputtgeht, wenn man es angeschaltet lässt. Ein Server, der ATCP 2008 implementiert und
2014 GMCP ergänzt hat, handelt meist immer noch beides aus, und ein Client, der beides unterstützt,
nimmt das, was ihm angeboten wird.

Für eine neue Implementierung gibt es keinen Grund, es zu wählen.

## Was wir messen

Server, die Telnet-Option 200 in einem von uns beobachteten Handshake angeboten haben. Ein niedriger
Wert ist hier zu erwarten und sagt etwas über das Alter aus und sonst über nichts.
