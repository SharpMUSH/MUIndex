---
kind: codebase
slug: fluffos
title: FluffOS
summary: De onderhouden opvolger van MudOS, en de driver waarop de meeste overgebleven LPMud-spellen draaien. Het spel is in LPC geschreven, niet in C.
codebase: FluffOS
home: https://www.fluffos.info/
see-also: codebases/dikumud
see-also: mush-mud-muck-moo
---

De LPMud-traditie deelt de wereld anders in dan Diku. Er is een **driver** — een C-programma dat een
objectgeoriënteerde interpreter draait — en een **mudlib**, en die is het hele spel, geschreven in
**LPC** en geladen door de driver. Kamers, gevecht, commando's en de inlogreeks zijn allemaal
mudlib-objecten; de driver weet van geen van alle.

Dat maakt een LPMud in geest dichter bij een MUSH dan zijn gevechtssystemen doen vermoeden: het spel
is geschreven in een taal die binnen het spel leeft, en twee LPMuds die een driver delen delen
mogelijk niets anders.

**MudOS** was jarenlang de dominante driver; **FluffOS** is de onderhouden voortzetting ervan en is
waar een draaiend LP-spel vandaag het meest waarschijnlijk op draait. Bekende mudlibs — Nightmare,
Lima, die van Discworld zelf — zijn opnieuw aparte projecten.

## Hoe het er van buitenaf uitziet

MSSP en **MCCP2** op het FluffOS-spel dat we gemeten hebben. MudOS was een van slechts twee
codebases in ons onderzoek die *zowel* MSSP als een `WHO` op het inlogscherm beantwoordde, al was de
`WHO` die het gaf een opsomming per speler in plaats van een telling.

Omdat de mudlib het spel is, is wat een bepaald LP-spel onderhandelt evenzeer een beslissing van de
mudlib als van de driver — de adoptiecijfers op de protocolpagina's tellen wat servers ons
daadwerkelijk aangeboden hebben, wat voor deze familie een zwakker signaal over de codebase is dan
elders.
