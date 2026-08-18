---
kind: protocol
slug: pueblo
title: Pueblo
summary: Das ältere Verfahren, HTML in ein MUD zu bringen, aus dem gleichnamigen Client. Von Clients der MUSH-Seite weiterhin unterstützt und regelmäßig mit MXP verwechselt.
protocol: PUEBLO
home: https://pueblo.sourceforge.net/
see-also: protocols/mxp
see-also: clients/beipmu
---

Pueblo ging Mitte der Neunziger aus dem gleichnamigen Client hervor und verfolgte einen direkten
Ansatz, MUD-Text aufzuwerten: Der Server soll **HTML** senden und der Client es darstellen. Ein
Server kündigt seine Pueblo-Unterstützung beim Verbinden in einer Zeile an; der Client antwortet, und
von da an darf der Strom Auszeichnung tragen.

Es erreichte die MUSH-Seite des Hobbys stärker als die MUD-Seite, und MUSH-Server, die es
unterstützen, tun das in der Regel weiterhin.

## Nicht MXP

[MXP](/reference/protocols/mxp) ist das spätere Verfahren und das breiter implementierte. Sie
erledigen eine ähnliche Aufgabe und sind nicht kompatibel, und die Pueblo-Unterstützung eines Clients
als MXP-Unterstützung zu lesen — oder umgekehrt — ist der mit Abstand am leichtesten zu machende
Fehler, wenn man einen Client-Vergleich zusammenstellt. Deshalb halten die Client-Seiten in diesem
Abschnitt beides getrennt, und wo ein Projekt das eine dokumentiert und das andere nicht, steht beim
anderen *unbekannt*.

## Was wir messen

Der Handshake von Pueblo ist keine Telnet-Option im üblichen Sinn; was wir beobachten, ist also enger
als bei den ausgehandelten Protokollen, und ein niedriger Wert ist hier als Aussage über unsere Sicht
zu lesen und nicht über die Verbreitung.
