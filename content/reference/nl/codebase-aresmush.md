---
kind: codebase
slug: aresmush
title: AresMUSH
summary: Een moderne rollenspelserver in Ruby, met een webfront-end en scènegereedschap ingebouwd in plaats van in softcode geschreven.
codebase: AresMUSH
home: https://aresmush.com/
see-also: collaborative-roleplay
see-also: codebases/pennmush
see-also: codebases/evennia
---

AresMUSH is de nieuwste veelgebruikte server die zich volledig op **gezamenlijk rollenspel** richt,
en neemt een andere positie in dan de TinyMUSH-lijn die hij opvolgt. Waar een PennMUSH-spel zijn
scènesysteem, zijn personagebladen en zijn takenwachtrij opbouwt uit softcode die geschreven is door
wie er toevallig was, levert Ares die als kant-en-klare functies mee en verwacht het dat de staf van
een spel ze instelt in plaats van programmeert.

Het komt met een **webportaal** — personagewiki's, scènelogs, forums en het spel zelf, allemaal
bereikbaar vanuit een browser — wat voor een genre waarin mensen de logs achteraf lezen een verschil
in soort is en niet in graad.

De configuratie staat in YAML; uitbreidingen zijn Ruby-plug-ins. Er is geen programmeertaal in het
spel voor spelers, en dat is de ruil: minder touw, minder touwgerelateerd letsel, en minder van de
improviserende bouwcultuur waar de MUSH-lijn haar naam aan dankt.

## Hoe het er van buitenaf uitziet

Geen MSSP. Het beantwoordt een `WHO` vóór het inloggen, en dat antwoord is een **lijst per speler**
in plaats van een kaal getal, dat onze parser op structuur telt. Op het spel dat we gemeten hebben
werden geen telnet-opties onderhandeld.

Kies je tussen dit en PennMUSH voor een nieuw rollenspel, dan is de vraag ruwweg of je een systeem
wilt dat je instelt of een systeem dat je schrijft.
