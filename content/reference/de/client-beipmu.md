---
kind: client
slug: beipmu
title: BeipMU
summary: Ein Windows-Client für die MUSH-Seite des Hobbys, mit Screenreader-Unterstützung im Ausgabefenster und Pueblo statt MXP.
home: https://beipdev.github.io/BeipMU/
platform: Windows
capability: screen reader | yes | https://github.com/BeipDev/BeipMU/blob/master/Assets/Changes.txt
capability: TLS | yes | https://beipdev.github.io/BeipMU/
capability: UTF-8 | yes | https://beipdev.github.io/BeipMU/
capability: MCCP | unknown |
capability: GMCP | yes | https://github.com/BeipDev/BeipMU/blob/master/Documentation/GMCP.md
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://beipdev.github.io/BeipMU/
see-also: clients/mushclient
see-also: clients/potato
see-also: collaborative-roleplay
---

BeipMU ist ein Windows-Client unter MIT-Lizenz, aktiv veröffentlicht, und einer der wenigen, die mit
Blick auf MUSH-artiges Spiel gebaut sind statt auf Kampf-MUDs — mehrere Eingabefenster,
Spawn-Fenster und eine Textmaschine, die lange Absätze erwartet. Skripting ist standardmäßig
JavaScript, weitere ActiveScript-Engines sind verfügbar.

## Barrierefreiheit

Das Ausgabefenster implementiert die Windows-Schnittstelle `IAccessible`, bewusst als Schritt hin zu
einer Benutzbarkeit für sehbehinderte Spieler eingebaut, und es gibt eine Trigger-Aktion **Speak**
für Sprachausgabe. Nirgends wird ein bestimmter Screenreader genannt, und ein Kapitel zur
Barrierefreiheit gibt es in der Dokumentation nicht.

Eine Warnung, falls Sie nachsehen: Eine Seite in der projekteigenen Dokumentation sagt immer noch,
BeipMU könne keine Sprachsynthese nutzen. Diese Seite ist veraltet — das Änderungsprotokoll und die
Issue-Kommentare des Betreuers selbst sind beide jünger.

## Zwei leichte Irrtümer über diesen Client

**BeipMU implementiert MCMP, nicht MSP.** Das sind verschiedene Protokolle mit ähnlichen Namen und
ähnlichen Zwecken, und das eine als das andere zu lesen hieße, eine Behauptung in diese Tabelle zu
setzen, die niemand aufgestellt hat. Die MSP-Zeile sagt deshalb unbekannt.

**Es unterstützt Pueblo, nicht MXP.** Pueblo ist das ältere Verfahren für HTML in einem MUD und MXP
das spätere; BeipMU dokumentiert einfache Pueblo-Stile und anklickbare Links. Zu MXP ließ sich weder
das eine noch das andere feststellen.
