---
kind: codebase
slug: rom
title: ROM
summary: De bekendste afstammeling van Merc, en de gevechtsengine waarop een groot deel van de MUD's uit de jaren negentig gebouwd is.
codebase: ROM
see-also: codebases/dikumud
see-also: codebases/smaug
see-also: protocols/mccp
---

ROM — *Rivers of MUD* — is een afgeleide van **Merc**, dat zelf een DikuMUD-afgeleide is, en het is
degene die is blijven hangen. Zijn gevechtsmodel, zijn vaardigheden- en spreukensysteem en zijn
areaformaat waren het beginpunt voor een enorm aantal spellen door de jaren negentig heen en daarna,
en ROM 2.4 in het bijzonder is een van de meest geforkte stukken broncode in de hobby.

Zoals de rest van de Diku-lijn draagt het de oorspronkelijke crediteringseis mee, dus een spel
waarvan je de afstamming niet anders kunt vaststellen, noemt vaak Diku, Merc en ROM op zijn
inlogscherm.

## Hoe het er van buitenaf uitziet

MSSP, CHARSET en **MCCP2**, op het spel dat we gemeten hebben.

ROM is de server waaraan dit project zijn eigen compressiebug heeft aangetoond. Onze peiling
onderhandelde MCCP2, de server begon correct te comprimeren, en de telnet-bibliotheek waar we van
afhangen pakte de stream nooit uit — dus kwam het verbindingsscherm aan als een muur van
vervangingstekens en hebben we dat even als de schuld van het spel vastgelegd. De payload liet zich
schoon uitpakken met een standaard zlib-aanroep, en dat maakte het onmiskenbaar. Het is upstream
hersteld; het verhaal staat op de pagina [MCCP](/reference/protocols/mccp), omdat het een goed
voorbeeld is van een gebrek dat er van buitenaf precies uitziet als een kapot spel.
