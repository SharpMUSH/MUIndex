---
kind: codebase
slug: evennia
title: Evennia
summary: Eher ein Python-Framework als ein fertiges Spiel. Zwei Evennia-Spiele können außer dem Unterbau nichts gemeinsam haben.
codebase: Evennia
home: https://www.evennia.com/
see-also: codebases/aresmush
see-also: collaborative-roleplay
see-also: protocols/gmcp
---

Evennia ist ein **MU\*-Framework**, kein Spiel — das ist das Erste, was man darüber wissen muss, und
das ist es, was den Vergleich von Evennia-Spielen untereinander wenig hilfreich macht. Es ist eine
Python-Bibliothek auf Basis von Django und Twisted, die Ihnen Accounts, Objekte, Räume, Befehle,
eine Persistenzschicht und den Netzwerk-Stack gibt und dann erwartet, dass Sie das Spiel schreiben.

Die Folge ist, dass „läuft auf Evennia“ weit weniger über ein Spiel aussagt als „läuft auf
PennMUSH“. Es gibt Kampf-MUDs auf Evennia und es gibt Rollenspiele auf Evennia, und sie teilen kein
Vokabular. Zwei Evennia-Spiele haben womöglich keinen einzigen Befehl gemeinsam.

Wer Python bereits kennt, hat hier den kürzesten Weg von nichts zu einer laufenden Welt, und dort
hat ein guter Teil der neuen Spiele seit Mitte der 2010er begonnen.

## Wie es von außen aussieht

Evennia bietet **MSSP** an und veröffentlicht darüber eine Spielerzahl. Auf dem Spiel, das wir
gemessen haben, hat es außerdem **MCCP2** ausgehandelt — Kompression —, was für einen Stack
charakteristisch ist, der sein Telnet ernst genommen hat.

Weil Evennia ein Framework ist, ist das, was ein bestimmtes Spiel aushandelt, zum Teil die
Entscheidung des Spiels. Die Verbreitungszahlen auf den Protokollseiten zählen, was Server uns
tatsächlich angeboten haben, nicht, was das Framework kann, und bei Evennia liegen diese beiden
weiter auseinander als bei den meisten.
