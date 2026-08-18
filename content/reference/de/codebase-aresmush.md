---
kind: codebase
slug: aresmush
title: AresMUSH
summary: Ein moderner Rollenspiel-Server in Ruby, mit Web-Oberfläche und eingebauten Szenen-Werkzeugen statt Softcode.
codebase: AresMUSH
home: https://aresmush.com/
see-also: collaborative-roleplay
see-also: codebases/pennmush
see-also: codebases/evennia
---

AresMUSH ist der neueste weit verbreitete Server, der ausdrücklich auf **gemeinsames Rollenspiel**
zielt, und er bezieht eine andere Position als die TinyMUSH-Linie, deren Nachfolge er antritt. Wo
ein PennMUSH-Spiel sein Szenensystem, seine Charakterbögen und seine Job-Warteschlange aus Softcode
baut, den geschrieben hat, wer gerade da war, liefert Ares all das als fertige Funktionen mit und
erwartet von der Spielleitung, sie zu konfigurieren statt sie zu programmieren.

Es bringt ein **Web-Portal** mit — Charakter-Wikis, Szenen-Logs, Foren und das Spiel selbst, alles
aus einem Browser erreichbar —, was für ein Genre, in dem die Logs hinterher gelesen werden, ein
Unterschied der Art und nicht bloß des Grades ist.

Konfiguriert wird in YAML; Erweiterungen sind Ruby-Plugins. Für Spieler gibt es keine
Programmiersprache im Spiel, und das ist der Handel: weniger Strick, weniger Unfälle mit dem Strick
und weniger von jener improvisierenden Baukultur, nach der die MUSH-Linie benannt ist.

## Wie es von außen aussieht

Kein MSSP. Es beantwortet ein `WHO` vor der Anmeldung, und die Antwort ist eine **Liste pro
Spieler** statt einer nackten Zahl; unser Parser zählt sie anhand ihrer Struktur. Auf dem Spiel, das
wir gemessen haben, wurden keine Telnet-Optionen ausgehandelt.

Wenn Sie für ein neues Rollenspiel zwischen diesem und PennMUSH wählen, lautet die Frage ungefähr,
ob Sie ein System wollen, das Sie konfigurieren, oder eines, das Sie schreiben.
