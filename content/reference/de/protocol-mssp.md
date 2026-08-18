---
kind: protocol
slug: mssp
title: MSSP
summary: Das Mud Server Status Protocol — wie ein Spiel einem Crawler von sich erzählt. Alles, was es meldet, ist angegeben und nicht gemessen, und diese Website hält beides auseinander.
protocol: MSSP
home: https://www.mudhalla.net/tintin/protocols/mssp/
see-also: protocols/gmcp
see-also: codebases/dikumud
see-also: codebases/pennmush
---

MSSP ist Telnet-Option 70. Ein Crawler sendet `IAC DO MSSP`; ein Server, der es unterstützt,
antwortet mit einer Tabelle aus Name/Wert-Paaren, die ihn beschreibt — Name, Spielerzahl, Codebase,
Uptime, Hostname, Port, Genre und was er sonst noch veröffentlichen möchte.

Es ist das, was diesem Hobby am nächsten an einen maschinenlesbaren Verzeichniseintrag herankommt,
und es ist der Grund, warum es mehrere Verzeichnisse überhaupt gibt.

## Alles in einem MSSP-Bericht ist eine Behauptung

Das ist der Punkt, in dem sich diese Website von jedem etablierten Verzeichnis unterscheidet. Ein
MSSP-Bericht ist das Spiel, das Ihnen *von sich erzählt*. `GMCP 1` in einer MSSP-Tabelle heißt, dass
jemand eine `1` in eine Konfigurationsdatei getippt hat, womöglich im Jahr 2011. Es ist kein Beleg
dafür, dass der Server GMCP anbietet, und die beiden widersprechen sich oft genug, um interessant zu
sein.

Deshalb werden Tatsachen aus MSSP hier als **angegeben** ausgewiesen, und wo wir dieselbe Tatsache
messen können — eine Fähigkeit, indem wir sehen, ob die Option tatsächlich ausgehandelt wird —, wird
beides nebeneinander gezeigt, jedes mit seinem Alter. Ein Spiel, dessen MSSP seit sechs Jahren GMCP
angibt und es kein einziges Mal in einem Handshake angeboten hat, ist eine Tatsache, die man kennen
sollte, und nirgendwo sonst ist sie zu finden.

Das eine Feld, das wir bewusst überhaupt nicht anrechnen, ist `CREATED`. Es ist eine einzelne von
Hand getippte Zeile, und sie irgendwo anzurechnen würde das Betreffende trivial manipulierbar machen.

## Wer darauf antwortet

MSSP ist die Antwort der **Diku- und LP-Welt**. In unserer eigenen Erhebung über 38 Codebases
veröffentlichten 28 eine Spielerzahl über MSSP und sieben über ein `WHO` am Anmeldebildschirm, und
nur zwei taten beides — die zwei Familien sind nahezu disjunkt. AresMUSH, TinyMUX, MUCK, RhostMUSH,
CobraMUSH und TinyMUSH bieten überhaupt kein MSSP.

Das ist der empirische Grund dafür, vier Schichten abzufragen statt einer: **Ein Crawler, der allein
auf MSSP baut, kann den größten Teil der MUSH-Familie nicht sehen**, und die ist ein großer Teil des
Hobbys und der größte Teil des Publikums, für das diese Website gedacht ist.

## Fragen, nicht warten

Sehr viele Server, die MSSP vollständig unterstützen, bieten es von sich aus nie an — sie antworten
auf `IAC DO MSSP` und sagen sonst nichts. Ein Crawler, der mit `IAC WILL NAWS` eröffnet und wartet,
meldet diese Spiele daher als solche, die nichts veröffentlichen, und das ist eine Behauptung über
den Server, gemacht aus dem eigenen Schweigen des Crawlers. Wir senden `IAC DO MSSP` beim Verbinden.

## Die Klartextform

Es gibt eine ältere Variante, bei der ein Client am Anmeldebildschirm wörtlich die Zeile
`MSSP-REQUEST` sendet. Wir haben sie gemessen: Von zwanzig versuchten Spielen antworteten drei — und
alle drei antworteten auch auf Telnet-Option 70, sie erreichte also nichts, was die Option nicht
schon erreichte. Acht Server lasen die Anfrage als **Charakternamen** und sagten das auch, wobei
einer der Anmeldeversuche verbraucht wurde, die einem Fremden zustehen. Wir senden sie nicht.
