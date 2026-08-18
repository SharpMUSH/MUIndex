---
kind: client
slug: mushclient
title: MUSHclient
summary: Der seit Langem etablierte Windows-Client. Fünf Skriptsprachen, eine Plugin-Architektur, in der der Großteil seiner Protokollunterstützung lebt, und eine Veröffentlichungsgeschichte, die sich verlangsamt hat.
home: https://www.mushclient.com/
platform: Windows
platform: Linux (Wine)
capability: screen reader | unknown |
capability: TLS | unknown |
capability: UTF-8 | unknown |
capability: MCCP | yes | https://www.mushclient.com/mushclient/mccp.htm
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | yes | https://www.mushclient.com/gmcp
capability: MXP | yes | https://www.mushclient.com/mushclient/doc/general/features.html
capability: MSP | yes | https://github.com/nickgammon/mushclient/blob/master/plugins/msp.xml
capability: scripting | yes | https://www.mushclient.com/mushclient/doc/general/features.html
see-also: clients/mudlet
see-also: clients/potato
see-also: protocols/mccp
---

MUSHclient ist Nick Gammons Windows-Client, MIT-lizenziert, und über eine lange Strecke die
Standardantwort für alle unter Windows. Er skriptet in Lua, VBScript, JScript, PerlScript und
Python, und vieles von dem, was er tut, tragen Plugins statt des Kerns — was eine echte
architektonische Entscheidung ist und zugleich der Grund, warum sich mehrere Zeilen oben schwerer
beantworten lassen, als sie aussehen.

Die letzte getaggte Veröffentlichung ist **5.06 vom März 2019**. Ins Repository wird weiterhin
committet, und es gibt Release Notes für ein 5.07, das nicht ausgeliefert wurde.

## Warum so viele Zeilen unbekannt sagen

Bei jeder einzelnen davon lautet die ehrliche Antwort „wir konnten es nicht feststellen“, und die
Gründe sind verschieden:

- **GMCP** — die projekteigene Seite dazu zeigt ein *Beispiel*-Plugin, das man schreiben könnte,
  keine Funktion, die der Client hat. Das ist etwas anderes als ausgelieferte Unterstützung, also
  ist die Zelle unbekannt statt ja.
- **TLS** — der dokumentierte Weg ist ein externer `stunnel`-Prozess. Ein Commit, der TLS über
  OpenSSL hinzufügt, ist 2026 im master-Branch gelandet und in keiner Veröffentlichung enthalten, es
  gibt also nichts, was heute jemand installieren könnte und worauf wir zeigen könnten.
- **UTF-8** — die CHARSET-Aushandlung taucht in den Notizen zum unveröffentlichten 5.07 auf und
  nirgends, wo wir sie in der Dokumentation einer ausgelieferten Version hätten finden können.
- **MSDP** — nichts in die eine oder andere Richtung.
- **Screenreader** — ein Plugin für Sprachausgabe über Windows SAPI wird mit dem Client
  ausgeliefert, und das ist nicht dasselbe wie Screenreader-Unterstützung. Im Handbuch gibt es
  keinen Abschnitt zur Barrierefreiheit, und der Autor hat in seinem eigenen Forum beschrieben,
  warum das Ausgabefenster für einen Screenreader schwer zu handhaben ist: Es kennt keine aktuelle
  Zeile. Wir konnten keine Antwort feststellen, also gibt die Tabelle keine.

Keines davon ist ein *Nein*. Mehrere sind vermutlich ein Ja, und wir konnten es nicht zeigen.
