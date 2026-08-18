---
kind: protocol
slug: msdp
title: MSDP
summary: Das Mud Server Data Protocol — dieselbe Aufgabe wie GMCP, erledigt mit einer kompakten binären Kodierung und einem Mechanismus zur Erkundung, den GMCP nicht hat.
protocol: MSDP
home: https://www.mudhalla.net/tintin/protocols/msdp/
see-also: protocols/gmcp
see-also: clients/tintin
see-also: clients/blightmud
---

MSDP ist Telnet-Option 69 und löst dasselbe Problem wie [GMCP](/reference/protocols/gmcp):
strukturierte Daten neben dem Text zu senden, damit ein Client die Prosa nicht nach Zahlen abgrasen
muss.

Die Unterschiede sind zwei. Die Kodierung von MSDP ist **binär und kompakt** — Variablen und Werte
werden mit einzelnen Steuerbytes markiert statt in JSON verpackt —, und MSDP definiert ein Gespräch
zur **Erkundung**: Ein Client kann mit `LIST` nach `COMMANDS`, `REPORTABLE_VARIABLES` und so weiter
fragen und bekommt gesagt, was ein bestimmtes Spiel unterstützt. GMCP hat dafür kein Gegenstück,
weshalb ein GMCP-Client in der Regel je Spiel konfiguriert werden muss.

In der Praxis hat GMCP sich in der Verbreitung durchgesetzt, und MSDP hält sich in den Servern und
Clients, die es implementiert haben, oft neben GMCP.

## Was wir messen

Ein Spiel zählt hier, wenn sein Server MSDP in einem von uns beobachteten Handshake angeboten hat.
Wie bei jedem Wert in diesem Abschnitt ist das eine positive Beobachtung, und der Rest ist nicht ihr
Gegenteil — ein Spiel, das nicht gezählt ist, implementiert MSDP womöglich nicht, oder wir haben
seinen Handshake einfach noch nicht gelesen.
