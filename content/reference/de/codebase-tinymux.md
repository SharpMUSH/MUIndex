---
kind: codebase
slug: tinymux
title: TinyMUX
summary: Der andere große MUSH-Server. Softcode nah genug an dem von PennMUSH, um darüber zu streiten, gar kein MSSP und ein funktionierendes WHO vor der Anmeldung.
codebase: TinyMUX
home: https://www.tinymux.org/
see-also: codebases/pennmush
see-also: codebases/tinymush
see-also: codebases/rhostmush
see-also: mush-mud-muck-moo
---

TinyMUX ist der zweite der beiden Server, auf denen die meisten etablierten Rollenspiel-MUSHes
laufen, und für viele Spieler ist die Wahl zwischen ihm und PennMUSH eine Frage dessen, welchen die
Spielleitung ihres Spiels zuerst gelernt hat. Versionen lesen sich als `2.12` und ähnlich.

Wie PennMUSH stammt es von TinyMUSH ab, und sein Softcode ist nah genug, dass jemand, der zwischen
beiden wechselt, übersetzt statt neu zu lernen. Die Unterschiede sind real — Funktionsbibliotheken,
einige Ecken des Parsens, der Satz der `@`-Befehle — und genau die Art von Sache, die das
Verschieben einer Datenbank zwischen beiden zu einem Projekt macht statt zu einem Export.

## Wie es von außen aussieht

**Kein MSSP.** TinyMUX bietet die Option überhaupt nicht an, was es zusammen mit AresMUSH, MUCK,
RhostMUSH, CobraMUSH und TinyMUSH auf jene Seite des Hobbys stellt, die ein Verzeichnis auf reiner
MSSP-Basis schlicht nicht sehen kann. Seine Spielerzahl kommt von einem `WHO` am Anmeldebildschirm,
das es mit einer schlichten Zählung beantwortet.

CHARSET handelt es hingegen aus, und damit kommt es bei nicht-ASCII-Text besser weg als die meisten
seiner Verwandten.

## Woher die Zählungen stammen

Wenn Sie die Zahl dieser Website für ein TinyMUX-Spiel mit der eines anderen Verzeichnisses
vergleichen, beachten Sie: Wir lesen das `WHO` am Anmeldebildschirm, und die meisten Crawler tun das
nicht. Ein Verzeichnis, das allein auf MSSP baut, meldet diese Spiele als solche ganz ohne Zählung —
oder führt sie nicht auf.
