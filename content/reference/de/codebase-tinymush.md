---
kind: codebase
slug: tinymush
title: TinyMUSH
summary: Der Vorfahr der MUSH-Linie, auf dem noch Spiele laufen. Er hat diesem Crawler beigebracht, dass dessen eigene Aushandlungsbytes den nächsten Befehl kaputtmachen können, den er sendet.
codebase: TinyMUSH
home: https://github.com/TinyMUSH/TinyMUSH
see-also: codebases/pennmush
see-also: codebases/tinymux
see-also: mush-mud-muck-moo
---

TinyMUSH ist das, wovon die Linie PennMUSH, TinyMUX, RhostMUSH und CobraMUSH allesamt abstammt, und
es ist immer noch im Einsatz. Die Entwicklung ist eher still als abwesend.

## Wie es von außen aussieht

Kein MSSP. Ein `WHO` vor der Anmeldung, das mit einem Satz der Form `0 Players logged in, 22
record, no maximum.` antwortet.

## Der Fehler, den es bei uns gefunden hat

TinyMUSH ist hier einen Absatz wert, weil es das Spiel ist, das einen Defekt im eigenen Crawler
dieser Website aufgedeckt hat, und die Korrektur ist eine gute Veranschaulichung dessen, was
„gemessen“ heißen soll.

Unsere Abfrage las TinyMUSH wochenlang als *Zählung unbekannt*. Die Vermutung zu den Akten war, dass
seine Antwort keinen abschließenden Zeilenumbruch habe. Sie hat einen. Vom Draht mitgeschnitten, war
die wirkliche Ursache unsere: **TinyMUSH wertet an seinem Anmeldebildschirm kein Telnet aus**, also
landen die drei Bytes `IAC DO MSSP`, die wir beim Verbinden senden, in seinem Eingabepuffer, als
hätte jemand sie getippt. Die nächste Zeile, die es liest, ist nicht `WHO`, sondern drei
Steuerbytes gefolgt von `WHO`, und das ist kein Befehl, den es hat — also zeigt es seinen
Verbindungsbildschirm erneut an und sagt nichts über Spieler.

Die Abfrage sendet nach dem Aushandeln nun einen leeren Zeilenumbruch und verwirft, was immer daraus
folgt, denn diese Ausgabe ist eine Reaktion auf Bytes, die *wir* zu senden gewählt haben, und ist
daher weder der Verbindungsbildschirm des Spiels noch seine Antwort. TinyMUSH wird jetzt korrekt
gelesen, und die Abfrage war in einem Drittel der Zeit fertig.

Ein Verzeichnis, das nicht nachgeprüft hätte, hätte „dieses Spiel meldet seine Spieler nicht“
veröffentlicht, solange es besteht, und der Satz wäre über uns gewesen.
