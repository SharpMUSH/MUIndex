---
kind: codebase
slug: coffeemud
title: CoffeeMUD
summary: Ein MUD-Server in Java, mit dem größten MSSP-Bericht von allem, was wir abgefragt haben, und einer ungewöhnlich breiten Protokollfläche.
codebase: CoffeeMUD
home: https://www.coffeemud.net/
see-also: codebases/dikumud
see-also: protocols/mssp
---

CoffeeMUD ist ein MUD-Server in Java mit ungewöhnlich breitem Funktionsumfang — er bringt einen
eigenen Webserver, Mail, Foren sowie ein großes Klassen- und Fertigkeitensystem mit und ist einer
der wenigen Server im Hobby, die nicht in C geschrieben sind.

Er wird aktiv gepflegt, was nach den Maßstäben dieses Teils des Katalogs der ausdrücklichen
Erwähnung wert ist.

## Wie es von außen aussieht

MSSP und **MCCP2**, und CoffeeMUD ist einer von nur drei Servern unter zwanzig, die wir probiert
haben, der auch die *Klartext*-Form `MSSP-REQUEST` beantwortet hat — eine Variante, die älter ist
als die Telnet-Option und immer noch gelegentlich vorkommt.

Sein MSSP-Bericht ist der größte, den wir gemessen haben: **47 Felder**, darunter `PORT`, neunmal
getrennt gemeldet für neun getrennte Ports. Das ist keine Fehlbildung. MSSP-Variablen sind Listen,
und ein Crawler, der ein mehrwertiges `PORT` zu einer einzigen Zeichenkette plattdrückt, macht aus
`"80" "23" "4201"` die ganze Zahl `80234201` — ein Fehler, den dieses Projekt ausgeliefert und
behoben hat, und der Grund, warum der Parser hier Werte durchgehend als Listen behält.
