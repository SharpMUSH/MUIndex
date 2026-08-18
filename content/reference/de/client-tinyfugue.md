---
kind: client
slug: tinyfugue
title: TinyFugue
summary: Der klassische UNIX-Terminal-Client. Upstream hat seit 2007 nichts mehr veröffentlicht; ein gepflegter Fork führt ihn weiter.
home: https://tinyfugue.sourceforge.net/
platform: Linux
platform: macOS
platform: BSD
capability: screen reader | unknown |
capability: TLS | yes | https://tinyfugue.sourceforge.net/
capability: UTF-8 | unknown |
capability: MCCP | yes | https://tinyfugue.sourceforge.net/
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://tinyfugue.sourceforge.net/
see-also: clients/tintin
see-also: clients/blightmud
---

TinyFugue — „tf“ — ist der Terminal-Client, den ein großer Teil der MUSH-Welt zwei Jahrzehnte lang
benutzt hat, mit getrennten Bereichen für Eingabe und Ausgabe, einer eigenen Makrosprache und einem
Satz von Gewohnheiten, die mehrere seiner Konkurrenten überlebt haben.

**Upstream ruht**: Die letzte Veröffentlichung ist 5.0 Beta 8 vom Januar 2007. Es baut noch immer,
und es funktioniert noch immer.

Ein gepflegter Fork, *TinyFugue Rebirth*, wird aktiv veröffentlicht und ergänzt GMCP, ATCP,
Unterstützung für Breitzeichen über ICU sowie Python- und Lua-Skripting neben der eigenen
Makrosprache. Die Tabelle oben beschreibt **Upstream**, denn dorthin führt „TinyFugue“; wenn Sie
heute etwas installieren, lohnt sich zuerst ein Blick auf den Fork.

## Die Falle in der Dokumentation dieses Clients

Upstream hat ein Dokumentationsthema namens **„non-visual mode“**. Darin geht es nicht um
Hilfstechnik — es geht darum, die Eingabe auf die unterste Zeile zu beschränken —, und es erwähnt
nirgends einen Screenreader, keine Sprachausgabe und keine blinden Nutzer. Eine Fähigkeitstabelle,
die per Stichwortsuche zusammengestellt wird, würde aus diesem Dateinamen ein Ja machen. Diese hier
sagt unbekannt, denn das ist es, was die Dokumentation hergibt.

UTF-8 ist eine Antwort derselben Form: Die dokumentierte Unterstützung für Kodierungen betrifft
8-Bit-Zeichensätze nach ISO 8859, und wir haben von Upstream keine Aussage zu UTF-8 gefunden, weder
in die eine noch in die andere Richtung.
