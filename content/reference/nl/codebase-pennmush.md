---
kind: codebase
slug: pennmush
title: PennMUSH
summary: De meest verspreide MUSH-server. Softcode, een lange releasegeschiedenis, en een van slechts twee codebases in ons onderzoek die zowel MSSP als een WHO vóór het inloggen beantwoorden.
codebase: PennMUSH
home: https://www.pennmush.org/
see-also: codebases/tinymux
see-also: codebases/rhostmush
see-also: codebases/cobramush
see-also: mush-mud-muck-moo
see-also: protocols/mssp
---

PennMUSH stamt via een fork uit 1991 af van TinyMUSH, en het is de server waarop de meeste
langlopende rollenspel-MUSHes draaien. Zijn bepalende kenmerk is **softcode**: een functionele
expressietaal, van binnen het spel bewerkt door iedereen met de juiste bit gezet, waarin een groot
deel van het gedrag van een willekeurige MUSH geschreven is. Een PennMUSH-spel wordt niet zozeer
ingesteld als wel geprogrammeerd door zijn spelers.

Versies lezen als `1.8.8p0` — een major, een minor en een patchlevel — en de patchlevel beweegt
vaak. Spellen draaien geregeld een versie die enkele patchlevels achterloopt, wat niets bijzonders
is.

## Hoe het er van buitenaf uitziet

PennMUSH is een van slechts twee codebases in ons eigen onderzoek onder 38 servers die *beide*
routes beantwoordde die wij peilen. Het biedt MSSP aan wanneer erom gevraagd wordt, en het
beantwoordt een `WHO` die op het inlogscherm getypt wordt, en op het spel dat we gemeten hebben
waren die twee het eens — wat zeldzamer is dan het klinkt, en waardoor PennMUSH de controle werd
waartegen we andere servers getoetst hebben.

De `WHO` vóór het inloggen doet meer dan gemak: het is de manier waarop de MUSH-familie überhaupt
een spelerstelling publiceert, aangezien de rest van de familie meestal geen enkele MSSP aanbiedt.
Zie [MSSP](/reference/protocols/mssp) voor waarom die scheiding de reden is dat deze site vier lagen
peilt in plaats van één.

CHARSET-onderhandeling is normaal op moderne PennMUSH, en daarom overleven namen met accenten de
reis.

## Verwante servers

PennMUSH, **TinyMUX**, **RhostMUSH** en **CobraMUSH** zijn vier servers met een gemeenschappelijke
voorouder en een gedeelde woordenschat — een bouwer die de ene kent, kan met moeite de softcode van
een andere lezen. Ze zijn niet compatibel: een database gaat niet zonder conversie van de een naar
de ander, en de functiebibliotheken verschillen op manieren die uitmaken.

## SharpMUSH

Een herimplementatie in .NET die op compatibiliteit met PennMUSH mikt is in ontwikkeling, door
dezelfde auteur als deze site. Niets op deze pagina is daaraan gemeten, en het heeft geen spellen in
de catalogus.
