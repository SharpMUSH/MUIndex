---
kind: codebase
slug: fluffos
title: FluffOS
summary: Der gepflegte MudOS-Nachfolger und der Driver, auf dem die meisten überlebenden LPMud-Spiele laufen. Das Spiel ist in LPC geschrieben, nicht in C.
codebase: FluffOS
home: https://www.fluffos.info/
see-also: codebases/dikumud
see-also: mush-mud-muck-moo
---

Die LPMud-Tradition teilt die Welt anders auf als Diku. Es gibt einen **Driver** — ein C-Programm,
das einen objektorientierten Interpreter ausführt — und eine **Mudlib**, die das gesamte Spiel ist,
in **LPC** geschrieben und vom Driver geladen. Räume, Kampf, Befehle und der Anmeldeablauf sind
allesamt Mudlib-Objekte; der Driver weiß von keinem davon.

Das rückt ein LPMud dem Geist nach näher an ein MUSH heran, als seine Kampfsysteme vermuten lassen:
Das Spiel ist in einer Sprache geschrieben, die im Spiel selbst lebt, und zwei LPMuds mit demselben
Driver teilen möglicherweise sonst nichts.

**MudOS** war jahrelang der vorherrschende Driver; **FluffOS** ist seine gepflegte Fortsetzung und
das, worauf ein laufendes LP-Spiel heute am ehesten läuft. Bekannte Mudlibs — Nightmare, Lima,
Discworlds eigene — sind wiederum eigene Projekte.

## Wie es von außen aussieht

MSSP und **MCCP2** auf dem FluffOS-Spiel, das wir gemessen haben. MudOS war eine von nur zwei
Codebases in unserer Erhebung, die *sowohl* MSSP als auch ein `WHO` am Anmeldebildschirm beantwortet
haben, wobei das `WHO` allerdings eine Auflistung pro Spieler statt einer Zählung lieferte.

Weil die Mudlib das Spiel ist, ist das, was ein bestimmtes LP-Spiel aushandelt, ebenso sehr eine
Entscheidung der Mudlib wie eine des Drivers — die Verbreitungszahlen auf den Protokollseiten
zählen, was Server uns tatsächlich angeboten haben, was für diese Familie ein schwächeres Signal
über die Codebase ist als anderswo.
