---
kind: protocol
slug: mccp
title: MCCP
summary: Kompression des Datenstroms. Billig, weit verbreitet und das Protokoll, das den lehrreichsten Fehler in der Geschichte dieses Projekts hervorgebracht hat.
protocol: MCCP
home: https://www.mudhalla.net/tintin/protocols/mccp/
see-also: codebases/rom
see-also: codebases/dikumud
see-also: protocols/gmcp
---

MCCP komprimiert den Strom vom Server zum Client mit zlib. Version 1 ist Telnet-Option 85 und
praktisch historisch; **Version 2** ist Option 86 und das, was moderne Server aushandeln. Nachdem der
Server `IAC SB MCCP2 IAC SE` gesendet hat, gehört jedes folgende Byte zu einem einzigen durchgehenden
zlib-Strom.

Bei einem Textprotokoll ist das eine echte Ersparnis — MUD-Ausgabe lässt sich außerordentlich gut
komprimieren —, und in der Diku- und der LP-Familie ist es verbreitet: Rund ein Drittel der von uns
erhobenen Codebases handelt es aus.

## Der Fehlerfall, und warum er hier zählt

Ein Client, der MCCP2 aushandelt und den Strom dann nicht dekomprimiert, empfängt **ab der
Kompressionsmarke binären Müll**. Kein Fehler, kein Verbindungsabbruch: Der Verbindungsbildschirm
kommt als Wand aus Ersatzzeichen an, und alles danach — die `WHO`-Antwort, jedes spätere MSSP, die
ganze Sitzung — ist verloren.

Das ist nicht hypothetisch. Unsere eigene Telnet-Bibliothek hat genau das getan. Sie handelte die
Option aus, löste ihren Callback „Kompression aktiviert“ aus und dekomprimierte kein einziges Byte.
Die Nutzlast ließ sich mit einem gewöhnlichen zlib-Aufruf sauber entpacken, und das machte es
eindeutig, dass die Server im Recht waren und wir nicht. Dreizehn der achtunddreißig Codebases in
unserer Erhebung waren betroffen, und solange das andauerte, konnten wir nicht beobachten, was diese
Server *nach* dem Beginn der Kompression aushandelten — unsere Aufzeichnung ihrer Fähigkeiten blieb
also hinter dem zurück, was sie konnten.

Es wurde upstream behoben. Ein Folgedefekt — der Inflater wird pro Lesevorgang neu erzeugt, statt für
die Verbindung erhalten zu bleiben, was mitten in einem großen Verbindungsbildschirm scheitert — ist
gemeldet und offen und betrifft das Ende der größten Bildschirme.

Zwei Dinge sollte ein Leser daraus mitnehmen. **Ein Protokollwert auf dieser Seite ist ebenso sehr
eine Messung unseres Crawlers wie eine des Hobbys**, und wo wir wissen, dass er falsch war, sagen wir
es. Und wenn Sie einen Client schreiben: MCCP auszuhandeln ist leicht, die Arbeit steckt darin, es
richtig zu dekomprimieren.
