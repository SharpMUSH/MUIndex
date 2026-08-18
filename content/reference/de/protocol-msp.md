---
kind: protocol
slug: msp
title: MSP
summary: Das MUD Sound Protocol — der Server nennt eine Klangdatei, und der Client spielt sie ab. Alt, einfach und leicht mit zwei anderen Dingen zu verwechseln.
protocol: MSP
home: https://www.zuggsoft.com/zmud/msp.htm
see-also: protocols/mxp
see-also: clients/vipmud
---

Mit MSP kann ein Server einen Client bitten, einen Klang abzuspielen: eine in Klammern gesetzte
Anweisung, die eine Datei, eine Lautstärke, eine Wiederholungszahl und eine URL nennt, von der sie zu
holen ist, falls der Client sie nicht hat. Es wird über Telnet-Option 90 ausgehandelt und kann von
Servern, die überhaupt nichts aushandeln, auch in band im Textstrom gesendet werden.

Es ist wirklich alt und wird wirklich noch benutzt — Umgebungsklang in einem Textspiel wirkt stärker,
als es klingt, und für Spieler, die die Audiohinweise eines Clients statt seiner Anzeige nutzen, ist
er mehr als Zierde.

## Drei Dinge, die es nicht ist

Die Client-Tabellen in diesem Abschnitt mussten hier vorsichtig sein, und es lohnt sich
aufzuschreiben, warum:

- **MCMP** — das Mud Client Media Protocol — ist ein anderes Protokoll mit einer ähnlichen Aufgabe.
  Mindestens ein Client implementiert MCMP und nicht MSP, und das eine als das andere zu lesen würde
  eine Behauptung in eine Tabelle setzen, die niemand aufgestellt hat.
- **Der eigene Skriptaufruf eines Clients zum Abspielen eines Klangs** ist nicht MSP. Er spielt eine
  lokale Datei ab, wenn ein Skript es sagt; MSP ist ein Server, der einem Client sagt, was er
  abspielen soll.
- **Unterstützung durch ein mitgeliefertes Plugin ist als solche zu benennen.** Bei einem Client
  kommt die MSP-Unterstützung als Plugin, das ausdrücklich keine Telnet-Aushandlung betreibt; das
  funktioniert bei Servern, die MSP in band senden, und nicht bei Servern, die erwarten, es
  auszuhandeln.

## Was wir messen

Server, die Telnet-Option 90 anbieten. Weil MSP häufig ohne Aushandlung in band gesendet wird, liegt
dieser Wert um einen Betrag unter der tatsächlichen Verbreitung, den wir nicht schätzen können — das
ist eine Grenze dessen, was ein Handshake sehen kann, und kein Befund über das Protokoll.
