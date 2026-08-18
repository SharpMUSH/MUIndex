---
kind: protocol
slug: charset
title: CHARSET
summary: Die Telnet-Option aus RFC 2066, mit der eine Kodierung vereinbart wird. Der Grund, warum die Namen eines Spiels ihre Akzente über die Strecke retten, und die Ursache einiger subtiler Fehler, wenn sie fehlt.
protocol: CHARSET
home: https://www.rfc-editor.org/rfc/rfc2066
see-also: protocols/ttype
see-also: connecting
see-also: codebases/tinymux
---

CHARSET ist Telnet-Option 42, festgelegt in RFC 2066. Die eine Seite bietet eine Liste von
Zeichensätzen an, die andere wählt einen aus, und beide sind sich danach einig, wie Bytes auf Zeichen
abgebildet werden.

In der Praxis einigt sich die Aushandlung auf **UTF-8** oder findet gar nicht erst statt. Die
MUSH-Familie handelt sie merklich häufiger aus als die MUD-Familie — TinyMUX, RhostMUSH und PennMUSH
tun es alle —, was eine Bevölkerung widerspiegelt, die Prosa schreibt, in der Namen vorkommen.

## Was ohne sie passiert

Ein Client muss raten, und geraten wird üblicherweise entweder ASCII oder Latin-1. Rät er ASCII, wird
jedes Byte oberhalb von 0x7F zu einem Fragezeichen; rät er Latin-1 bei einem UTF-8-Server, wird aus
jedem Zeichen mit Akzent ein Paar Satzzeichen. Beide Fehler sehen aus wie ein Fehler des Spiels und
sind keiner.

Für einen Crawler wird das an einer bestimmten Stelle unangenehm. Unsere eigene Telnet-Bibliothek
setzt ihre aktuelle Kodierung standardmäßig auf ASCII, und dieser Standard ist nicht folgenlos — mit
ihm wird jedes Byte dekodiert, bei jedem Server, der CHARSET nie aushandelt, und das sind die
meisten. Aus diesem Grund geben wir ihn bewusst selbst vor.

## Die eine Stelle, die CHARSET nicht erreicht

MSSP-Feldnamen und -Werte werden als ASCII dekodiert, gleich worauf sich CHARSET geeinigt hat, denn
eine Subnegotiation ist ein Befehl und kein Text, und die Spezifikation begrenzt CHARSET auf Text.
Das ist vertretbar konform und es ist verlustbehaftet: Ein Spiel, dessen MSSP-`NAME` `Café Noir`
lautet, meldet `Caf? Noir`, und die ursprünglichen Bytes sind weg, bevor irgendetwas unter unserer
Kontrolle sie zu sehen bekommt.

Wenn Sie auf dieser Website in einem angegebenen Feld ein verstümmeltes Zeichen sehen und in der
Ausgabe des Spiels selbst nicht, dann ist das der Grund, und von unserer Seite lässt es sich nicht
wiederherstellen.
