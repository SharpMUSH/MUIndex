---
kind: client
slug: blightmud
title: Blightmud
summary: Ein moderner Terminal-Client in Rust, mit Lua-Skripting, eingebauter Sprachausgabe und einem Screenreader-Modus, der sich beim Server selbst ankündigt.
home: https://github.com/Blightmud/Blightmud
platform: Linux
platform: macOS
platform: Windows (WSL only)
capability: screen reader | yes | https://github.com/Blightmud/Blightmud
capability: TLS | yes | https://github.com/Blightmud/Blightmud
capability: UTF-8 | yes | https://github.com/Blightmud/Blightmud
capability: MCCP | yes | https://github.com/Blightmud/Blightmud
capability: GMCP | yes | https://github.com/Blightmud/Blightmud
capability: MSDP | yes | https://github.com/Blightmud/Blightmud
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/Blightmud/Blightmud
see-also: clients/tintin
see-also: clients/mudlet
see-also: protocols/ttype
---

Blightmud ist ein Terminal-Client in Rust, GPL 3, und einer der am aktivsten veröffentlichten
Clients in diesem Abschnitt. Skripting ist Lua. Er läuft nur im Terminal: Es gibt keinen nativen
Windows-Build, und Windows-Nutzer betreiben ihn unter WSL.

## Barrierefreiheit

Blightmud hat hier drei verschiedene Stücke, was mehr ist, als eine einzelne Zeile tragen kann:

- Einen **screenreader-freundlichen Modus** (`--reader-mode` oder die Einstellung `reader_mode`),
  der die Terminal-Oberfläche in etwas verwandelt, dem ein Screenreader folgen kann. Den
  Statusbereich unterstützt er nicht.
- **Eingebaute Sprachausgabe**, als optionale Kompilierung, mit einer Lua-API, die ein Skript
  benutzen kann — darunter ein `tts.gag()`, um eine passende Zeile vom Vorlesen auszunehmen. Die
  Dokumentation ist offen darin, dass die eigene Sprachausgabe zusammen mit einem Screenreader nicht
  immer eine glückliche Verbindung ist.
- **Automatische MTTS-Ankündigung**: Im Reader-Modus oder bei aktivierter Sprachausgabe fügt er
  `MTTS_SCREEN_READER` zu dem hinzu, was er dem Server über sich selbst mitteilt, damit ein Spiel,
  dem das wichtig ist, sich anpassen kann.

Wie bei TinTin++ wird kein bestimmter Screenreader genannt, das ist also ein dokumentierter Modus
und keine geprüfte Verträglichkeit mit einem Produkt.

## Wo die Tabelle unbekannt sagt

**MXP**, **MSP** und **ATCP** kommen weder in der README des Projekts noch in seiner mitgelieferten
Hilfe vor. **MCCP** ist als v2 dokumentiert; ob auch v1 behandelt wird, haben wir nicht
festgestellt.
