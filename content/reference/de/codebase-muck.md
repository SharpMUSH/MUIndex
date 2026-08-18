---
kind: codebase
slug: muck
title: MUCK
summary: Ein TinyMUD-Nachfahre mit einer eigenen Forth-artigen Sprache im Spiel und einer sozialen Kultur, die sich von der MUSH-Seite unterscheidet.
codebase: MUCK
home: https://www.fuzzball.org/
see-also: mush-mud-muck-moo
see-also: codebases/tinymush
see-also: codebases/moo
---

MUCK — in der Praxis fast immer **Fuzzball MUCK** — ist eher ein Geschwister der MUSH-Linie als ihr
Nachfahre: Beide stammen von TinyMUD ab, und beide setzen eine Programmiersprache ins Spiel hinein.

Die Sprache ist der sichtbare Unterschied. MUF (*Multi-User Forth*) ist stapelbasiert und liest sich
überhaupt nicht wie MUSH-Softcode; wer in der einen geübt ist, ist in der anderen Anfänger. Darüber
sitzt MPI, eine kleinere, eingebettete Ausdruckssprache für das, was auf einem MUSH der Softcode
täte.

Kulturell ist MUCK die Heimat eines großen Teils der geselligen und der Fandom-Welten dieses Hobbys.
Diese Spiele sind eher um Anwesenheit und Gespräch herum gebaut als um Szenen mit einem Anfang und
einem Ende, was ein echter Unterschied zur Rollenspiel-MUSH-Tradition ist und keine Frage des
Themas.

## Wie es von außen aussieht

Kein MSSP. Ein `WHO` vor der Anmeldung, das mit einer Zählung antwortet. Auf dem Spiel, das wir
gemessen haben, wurden keine Telnet-Optionen ausgehandelt — und eine Einzelheit aus der Erhebung ist
festzuhalten: Seine `WHO`-Antwort endete auf ein Leerzeichen ohne Zeilenumbruch, und genau so etwas
bringt einen naiven Parser dazu, gar nichts zu melden.
