---
kind: codebase
slug: tinymux
title: TinyMUX
summary: De andere grote MUSH-server. Softcode die dicht genoeg bij die van PennMUSH ligt om over te ruziën, helemaal geen MSSP, en een WHO vóór het inloggen die werkt.
codebase: TinyMUX
home: https://www.tinymux.org/
see-also: codebases/pennmush
see-also: codebases/tinymush
see-also: codebases/rhostmush
see-also: mush-mud-muck-moo
---

TinyMUX is de tweede van de twee servers waarop de meeste gevestigde rollenspel-MUSHes draaien, en
voor veel spelers is de keuze tussen dit en PennMUSH een kwestie van welke de staf van hun spel het
eerst geleerd heeft. Versies lezen als `2.12` en dergelijke.

Net als PennMUSH stamt het af van TinyMUSH, en de softcode ligt zo dicht bij elkaar dat een bouwer
die tussen de twee wisselt aan het vertalen is en niet aan het herleren. De verschillen zijn echt —
functiebibliotheken, een aantal hoeken van de parsing, de verzameling `@`-commando's — en het zijn
precies het soort dingen die het verplaatsen van een database tussen de twee eerder een project
maken dan een export.

## Hoe het er van buitenaf uitziet

**Geen MSSP.** TinyMUX biedt de optie helemaal niet aan, wat het samen met AresMUSH, MUCK,
RhostMUSH, CobraMUSH en TinyMUSH aan die kant van de hobby zet die een gids op MSSP alleen simpelweg
niet kan zien. Zijn spelerstelling komt van een `WHO` op het inlogscherm, die het met een kale
telling beantwoordt.

Er wordt wél over CHARSET onderhandeld, en daarmee komt het op niet-ASCII-tekst beter uit de bus dan
de meeste van zijn verwanten.

## Waar de tellingen vandaan komen

Vergelijk je het getal van deze site voor een TinyMUX-spel met dat van een andere gids, houd er dan
rekening mee dat wij de `WHO` op het inlogscherm lezen en de meeste crawlers niet. Een gids die op
MSSP alleen gebouwd is, meldt deze spellen als hadden ze helemaal geen telling, of vermeldt ze niet.
