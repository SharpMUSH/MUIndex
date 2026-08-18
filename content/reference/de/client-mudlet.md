---
kind: client
slug: mudlet
title: Mudlet
summary: Plattformübergreifend, mit Lua skriptbar und der Client mit der am gründlichsten dokumentierten Screenreader-Unterstützung in diesem Abschnitt.
home: https://www.mudlet.org/
platform: Windows
platform: macOS
platform: Linux
capability: screen reader | yes | https://wiki.mudlet.org/w/Manual:Screen_Readers
capability: TLS | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: UTF-8 | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MCCP | unknown |
capability: GMCP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MSDP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: ATCP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MXP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MSP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: scripting | yes | https://github.com/Mudlet/Mudlet
see-also: clients/blightmud
see-also: clients/tintin
see-also: protocols/gmcp
see-also: connecting
---

Mudlet ist ein grafischer Client mit Kartenwerkzeug, einem Paketsystem und einer Lua-API, gegen die
der größte Teil seines eigenen Funktionsumfangs geschrieben ist. Er steht unter der GPL, wird aktiv
veröffentlicht und ist die übliche Empfehlung für alle, die mit einem modernen Kampf-MUD anfangen.

## Barrierefreiheit

Das ist der Client mit dem stärksten dokumentierten Fall in diesem Abschnitt, und es lohnt sich
auszubuchstabieren, was „dokumentiert“ hier heißt, denn es ist ungewöhnlich.

Mudlet hat ein **Handbuchkapitel zu Screenreadern**, Seiten je Betriebssystem, die Narrator, NVDA
und JAWS unter Windows, Orca unter Linux und VoiceOver unter macOS nennen, einen Befehl
`mudlet access on` im Client und eine Option, eingehenden Spieltext über den Screenreader ansagen zu
lassen. Es gibt außerdem eine Einstellung, die dem Server über MTTS die Nutzung eines Screenreaders
mitteilt, damit ein Spiel sich anpassen kann, wenn es will.

Es ist auch offen darin, wo es nicht gut funktioniert: Die eigene Windows-Seite sagt, dass JAWS das
Ausgabefenster nicht so vorliest wie andere Screenreader, und empfiehlt stattdessen Narrator oder
NVDA. Ein Projekt, das den Fall veröffentlicht, in dem seine Unterstützung für Barrierefreiheit
untauglich ist, gibt Ihnen bessere Auskunft als eines, das ein Häkchen veröffentlicht.

## Wo die Tabelle unbekannt sagt

**MCCP.** Mudlets Quelltext implementiert MCCP v1 und v2, aber die Seite des Handbuchs zu den
unterstützten Protokollen führt es nicht auf, und die Regel dieses Abschnitts lautet, dass eine
Fähigkeitsaussage die Dokumentation des Projekts selbst zitiert. Eine Konstante aus einer
Header-Datei zu lesen ist nicht derselbe Vorgang, also sagt die Zelle unbekannt.

## Hinweis zur Kodierung

Mudlets voreingestellte Kodierung für Serverdaten ist ASCII und nicht UTF-8, und die
CHARSET-Aushandlung kam in 4.10 hinzu. Wenn der Text eines Spiels in einem frischen Profil falsch
herauskommt, ist diese Einstellung die erste Stelle, an der man nachsieht.
