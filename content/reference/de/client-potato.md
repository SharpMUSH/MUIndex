---
kind: client
slug: potato
title: Potato MUSHclient
summary: Ein plattformübergreifender Tcl/Tk-Client, geschrieben für MUSH-Spieler. Gute Unterstützung für Kodierungen und eine Dokumentation, die zu den meisten Protokollen überhaupt nichts sagt.
home: https://www.potatomushclient.com/
platform: Windows
platform: Linux
platform: macOS
capability: screen reader | unknown |
capability: TLS | yes | https://github.com/potatomushclient/potato/wiki/ConfigureWorldsBasics
capability: UTF-8 | yes | https://github.com/potatomushclient/potato/wiki/Features
capability: MCCP | unknown |
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/potatomushclient/potato/wiki/FAQs
see-also: clients/beipmu
see-also: clients/mushclient
see-also: collaborative-roleplay
---

Potato ist ein Tcl/Tk-Client für das MUSH-Spiel — mehrere Welten, Spawn-Fenster und ein Satz von
Voreinstellungen, die davon ausgehen, dass Sie Posen tippen und keine Kampfbefehle. Er läuft aus
derselben Quelle unter Windows, Linux und macOS, wobei die macOS-Builds meist ein bis zwei Versionen
zurückliegen.

Er handelt die Zeichenkodierung aus und spricht volles Unicode, was für die MUSH-Seite des Hobbys
die Fähigkeit ist, auf die es in der Praxis am meisten ankommt.

Beachten Sie eine dokumentierte Einschränkung: Er unterstützt die Verbindung zu einem Port, der von
Beginn an SSL spricht, und seine eigene Konfigurationsseite sagt, dass ausgehandeltes SSL nach Art
von STARTTLS **nicht** unterstützt wird.

## Warum sechs Zeilen unbekannt sagen

Wir haben die Startseite des Projekts, seine Downloadseite, alle 103 Hilfedateien seines Wikis und
seinen gesamten Quellbaum nach GMCP, MSDP, MCCP, MXP, MSP und ATCP durchsucht. Zu keinem davon gibt
es eine dokumentierte Aussage. Es gibt *Code*, der einige davon berührt, und dieser Abschnitt macht
aus Code keine Fähigkeitsaussage — eine Tabelle, die auf Grundlage einer Konstante in einer
Header-Datei „ja“ sagt, gibt ein Versprechen ab, das das Projekt nie gegeben hat.

Die Screenreader-Zeile ist dieselbe Antwort, auf demselben Weg erreicht: Eine Suche ohne Beachtung
der Groß- und Kleinschreibung nach „screen reader“, „text-to-speech“, NVDA, JAWS, VoiceOver,
„accessibility“, „visually impaired“ und „blind“ über alles, was das Projekt veröffentlicht, ergab
überhaupt nichts. Das ist kein Befund über die Software.
