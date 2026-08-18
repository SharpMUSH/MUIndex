---
kind: client
slug: atlantis
title: Atlantis
summary: Ein Client nur für macOS, langlebig und lange in der Beta. Sein Skripting ist als nicht mehr funktionierend dokumentiert, und das ist das eine ehrliche „Nein“ in diesem Abschnitt.
home: https://www.riverdark.net/atlantis/
platform: macOS
capability: screen reader | unknown |
capability: TLS | yes | https://www.riverdark.net/atlantis/history.php
capability: UTF-8 | yes | https://www.riverdark.net/atlantis/history.php
capability: MCCP | yes | https://www.riverdark.net/atlantis/history.php
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | no | https://www.riverdark.net/atlantis/
see-also: clients/mudlet
see-also: protocols/charset
---

Atlantis ist ein nativer macOS-Client, den es seit Mac OS X 10.3 gibt und der in der Catalina-Zeit
auf 64 Bit aktualisiert wurde. Er beherrscht die Zeichensatz-Aushandlung nach RFC 2066 und Unicode,
was besser ist, als sein Alter vermuten ließe, und er kann MCCP und SSL.

## Das eine „Nein“ in diesem Abschnitt

Sein Skripting war Perl über die CamelBones-Brücke, und die Startseite des Projekts selbst sagt,
dass es nicht mehr funktioniert — Apples Umgang mit Perl hat sich geändert, und der Autor der
Bibliothek ist vor einigen Jahren gestorben. Das ist ein *belegtes Fehlen*, und das ist etwas
anderes als ein Unbekannt; es ist die einzige Zelle im ganzen Client-Abschnitt, die so etwas trägt.
Überall sonst lautete die ehrliche Antwort, dass wir es nicht feststellen konnten.

## Alles, was wir nicht feststellen konnten

Die Versionsgeschichte ist vollständig und öffentlich und nennt **MCCP**, **SSL** und
**Zeichensatz-Aushandlung** — und nennt nie GMCP, MSDP, ATCP oder MSP. MXP taucht einmal auf, als
etwas, das für eine Version nach 1.0.0 vorgesehen war, die nicht gekommen ist.

In der Skripting-API gibt es einen Perl-Aufruf `Atlantis::Speak()`, und es wäre leicht, das als
Screenreader-Unterstützung zu lesen. Das ist es nicht: Es ist ein per Skript ausgelöster Aufruf zur
Sprachausgabe in einem Skriptsystem, von dem das Projekt sagt, dass es nicht funktioniert.
VoiceOver, „accessible“ und „screen reader“ kommen weder auf der Startseite noch auf der
Downloadseite noch in der vollständigen Versionsgeschichte noch im archivierten Benutzerhandbuch
vor.

Der aktuelle Download ist 0.9.9.8, nominell immer noch eine Beta, ohne dass irgendwo auf der Website
ein Veröffentlichungsdatum stünde.
