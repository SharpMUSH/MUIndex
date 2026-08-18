---
kind: codebase
slug: pennmush
title: PennMUSH
summary: Der am weitesten verbreitete MUSH-Server. Softcode, eine lange Veröffentlichungsgeschichte und eine von nur zwei Codebases in unserer Erhebung, die sowohl MSSP als auch ein WHO vor der Anmeldung beantworten.
codebase: PennMUSH
home: https://www.pennmush.org/
see-also: codebases/tinymux
see-also: codebases/rhostmush
see-also: codebases/cobramush
see-also: mush-mud-muck-moo
see-also: protocols/mssp
---

PennMUSH stammt über einen Fork von 1991 von TinyMUSH ab, und es ist der Server, auf dem die meisten
langlebigen Rollenspiel-MUSHes laufen. Sein bestimmendes Merkmal ist **Softcode**: eine funktionale
Ausdruckssprache, von innerhalb des Spiels bearbeitet von jedem, der das passende Bit gesetzt hat,
und in ihr ist ein großer Teil des Verhaltens jedes einzelnen MUSH geschrieben. Ein PennMUSH-Spiel
wird weniger konfiguriert als von seinen Spielern programmiert.

Versionen lesen sich als `1.8.8p0` — eine Hauptversion, eine Nebenversion und ein Patchlevel —, und
der Patchlevel bewegt sich oft. Spiele laufen häufig mit einer Version, die mehrere Patchlevel
zurückliegt, was nicht weiter bemerkenswert ist.

## Wie es von außen aussieht

PennMUSH ist eine von nur zwei Codebases in unserer eigenen Erhebung über 38 Server, die *beide*
Wege beantwortet hat, die wir abfragen. Es bietet MSSP an, wenn man fragt, und es beantwortet ein
`WHO`, das am Anmeldebildschirm getippt wird, und auf dem Spiel, das wir gemessen haben, stimmten
die beiden überein — was seltener ist, als es klingt, und PennMUSH zu der Kontrolle machte, an der
wir andere Server geprüft haben.

Das `WHO` vor der Anmeldung ist mehr als eine Bequemlichkeit: Es ist überhaupt die Art, wie die
MUSH-Familie eine Spielerzahl veröffentlicht, denn der größte Teil der übrigen Familie bietet gar
kein MSSP an. Unter [MSSP](/reference/protocols/mssp) steht, warum diese Spaltung der Grund dafür
ist, dass diese Website vier Schichten abfragt statt einer.

CHARSET-Aushandlung ist auf modernem PennMUSH normal, weshalb Namen mit Akzenten die Reise
überstehen.

## Verwandte Server

PennMUSH, **TinyMUX**, **RhostMUSH** und **CobraMUSH** sind vier Server mit gemeinsamem Vorfahren
und gemeinsamem Vokabular — wer einen kennt, kann den Softcode eines anderen mit Mühe lesen.
Kompatibel sind sie nicht: Eine Datenbank wandert nicht ohne Konvertierung von einem zum anderen,
und die Funktionsbibliotheken unterscheiden sich auf Weisen, die zählen.

## SharpMUSH

Eine .NET-Neuimplementierung mit dem Ziel der PennMUSH-Kompatibilität ist in Entwicklung, von
demselben Autor wie diese Website. Nichts auf dieser Seite ist an ihr gemessen, und sie hat keine
Spiele im Katalog.
