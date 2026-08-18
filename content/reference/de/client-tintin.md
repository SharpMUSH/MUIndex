---
kind: client
slug: tintin
title: TinTin++
summary: Ein Terminal-Client mit eigener Skriptsprache, auf jeder Plattform einschließlich Telefonen, und mit einem dokumentierten Screenreader-Modus.
home: https://tintin.mudhalla.net/
platform: Linux
platform: macOS
platform: Windows
platform: Android
platform: iOS
capability: screen reader | yes | https://tintin.mudhalla.net/manual/screen_reader.php
capability: TLS | yes | https://github.com/scandum/tintin
capability: UTF-8 | yes | https://github.com/scandum/tintin
capability: MCCP | yes | https://tintin.mudhalla.net/
capability: GMCP | yes | https://tintin.mudhalla.net/manual/event.php
capability: MSDP | yes | https://tintin.mudhalla.net/manual/msdp.php
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/scandum/tintin
see-also: clients/blightmud
see-also: clients/mudlet
see-also: protocols/msdp
see-also: protocols/ttype
---

TinTin++ ist ein Kommandozeilen-Client, GPL 3, aktiv veröffentlicht, und er läuft an mehr Orten als
alles andere hier — einschließlich Android und iOS. Seine Skriptsprache ist seine eigene, knapp und
zu sehr viel fähig; ein erheblicher Teil dessen, was andere Clients in der Oberfläche machen, ist
hier eine `#config`-Zeile.

Derselbe Autor pflegt die Protokollspezifikationen für **MSSP** und **MSDP**, weshalb so viele der
Protokollseiten in diesem Abschnitt dieselbe Website zitieren.

## Barrierefreiheit

TinTin++ hat eine eigene Handbuchseite zum **Screenreader-Modus** (`#config screen reader on` oder
`-s` beim Start). Ihn einzuschalten bewirkt zweierlei: Es entfernt oder verändert visuelle Elemente,
die vorgelesen keinen Sinn ergeben, und es meldet dem Server die Nutzung eines Screenreaders über
[MTTS](/reference/protocols/ttype), damit ein Spiel seine eigene Ausgabe anpassen kann.

Das ist ein dokumentierter Modus und keine Aussage über einen Test mit einem bestimmten Screenreader
— auf der Seite wird kein Produkt genannt. Das ist deutlich schwächer als ein Client, der die
Screenreader benennt, mit denen er funktioniert, und deutlich stärker als nichts.

## Wo die Tabelle unbekannt sagt

Zu **MXP** und **MSP** gibt es beide Male Community-Skripte auf der Website des Projekts, und ein
Skript ist nicht dasselbe wie ein Client, der ein Protokoll unterstützt — das MXP-Skript sagt
rundheraus, dass es womöglich nicht auf jedem MUD funktioniert. Native Unterstützung für eines von
beiden ließ sich nicht feststellen. Zu **ATCP** haben wir weder das eine noch das andere gefunden;
zu beachten ist, dass ATCP weitgehend von GMCP abgelöst ist, das TinTin++ sehr wohl unterstützt.
