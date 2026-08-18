---
kind: codebase
slug: evennia
title: Evennia
summary: Een Python-framework in plaats van een afgerond spel. Twee Evennia-spellen kunnen niets gemeen hebben behalve het leidingwerk.
codebase: Evennia
home: https://www.evennia.com/
see-also: codebases/aresmush
see-also: collaborative-roleplay
see-also: protocols/gmcp
---

Evennia is een **MU\*-framework**, geen spel — dat is het eerste wat je erover moet weten en het is
wat Evennia-spellen onderling vergelijken zinloos maakt. Het is een Python-bibliotheek gebouwd op
Django en Twisted die je accounts, objecten, kamers, commando's, een persistentielaag en de
netwerkstack geeft, en die vervolgens verwacht dat jij het spel schrijft.

Het gevolg is dat "draait op Evennia" je veel minder over een spel vertelt dan "draait op PennMUSH"
doet. Er zijn gevecht-MUD's op Evennia en er zijn rollenspellen op Evennia en ze delen geen enkele
woordenschat. Twee Evennia-spellen hebben mogelijk geen enkel commando gemeen.

Voor een ontwikkelaar die al Python kent is dit de kortste weg van niets naar een draaiende wereld,
en het is waar een flink deel van de nieuwe spellen sinds het midden van de jaren 2010 begonnen is.

## Hoe het er van buitenaf uitziet

Evennia biedt **MSSP** aan, en publiceert daarmee een spelerstelling. Op het spel dat we gemeten
hebben onderhandelde het ook over **MCCP2** — compressie — wat kenmerkend is voor een stack die zijn
telnet serieus nam.

Omdat Evennia een framework is, is wat een bepaald spel onderhandelt deels de beslissing van dat
spel. De adoptiecijfers op de protocolpagina's zijn tellingen van wat servers ons daadwerkelijk
aangeboden hebben, niet van wat het framework kan, en voor Evennia liggen die twee verder uit elkaar
dan voor de meeste.
