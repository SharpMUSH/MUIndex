---
kind: codebase
slug: dikumud
title: DikuMUD
summary: Die Wurzel der Kampf-MUD-Familie. Level, Klassen, Ausrüstung und Area-Dateien — und eine Lizenz, die eine ganze Generation von Ableitungen geprägt hat.
codebase: DikuMUD
home: https://dikumud.com/
see-also: codebases/circlemud
see-also: codebases/rom
see-also: codebases/smaug
see-also: mush-mud-muck-moo
---

DikuMUD, geschrieben am Datalogisk Institut der Universität Kopenhagen und 1991 veröffentlicht, ist
der Vorfahr des meisten von dem, was gemeint ist, wenn jemand ohne nähere Bestimmung „MUD“ sagt.
Level, Charakterklassen, Trefferpunkte, Mobs, Ausrüstungsplätze, ein Area-Dateiformat, das ein
Builder offline schreibt — das ganze Vokabular kommt von hier, und Spiele, die nie Diku-Quelltext
gesehen haben, erben trotzdem seine Form.

Seine Lizenz gehört zur Geschichte. Diku war frei nutzbar, verbot aber, Geld für den Zugang zu
verlangen, und verlangte, die ursprünglichen Credits anzuzeigen; diese Klausel ist der Grund, warum
„die Diku-Credits“ auf dem Anmeldebildschirm von Spielen stehen, die mehrere Forks von ihm entfernt
sind.

Die direkten Nachfahren — **Merc**, dann **ROM**, **CircleMUD**, **SMAUG**, **tbaMUD** und Dutzende
weitere — machen einen großen Teil jedes MUD-Verzeichnisses aus, das je existiert hat.

## Wie es von außen aussieht

Die Diku-Familie ist die **MSSP**-Familie. Während die MUSH-Seite eine Zählung über ein `WHO` am
Anmeldebildschirm veröffentlicht und überhaupt kein MSSP anbietet, beantworten Server der Diku-Linie
ganz überwiegend die Telnet-Option 70 mit einem strukturierten Bericht, und daher stammen ihre
Zahlen hier.

**MCCP2** — Stromkompression — ist in dieser Familie ebenfalls verbreitet, und es lohnt sich zu
wissen, dass ein Client, der sie aushandelt, den Strom aber nicht entpacken kann, den gesamten
Verbindungsbildschirm als binäres Rauschen erhält. Das war ein echter Defekt in der eigenen
Telnet-Bibliothek dieses Projekts und ist behoben; siehe [MCCP](/reference/protocols/mccp).
