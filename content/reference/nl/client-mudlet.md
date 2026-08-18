---
kind: client
slug: mudlet
title: Mudlet
summary: Platformonafhankelijk, met Lua-scripting, en de client met de grondigst gedocumenteerde ondersteuning voor schermlezers in dit onderdeel.
home: https://www.mudlet.org/
platform: Windows
platform: macOS
platform: Linux
capability: screen reader | yes | https://wiki.mudlet.org/w/Manual:Screen_Readers
capability: TLS | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: UTF-8 | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MCCP | unknown |
capability: GMCP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MSDP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: ATCP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MXP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: MSP | yes | https://wiki.mudlet.org/w/Manual:Supported_Protocols
capability: scripting | yes | https://github.com/Mudlet/Mudlet
see-also: clients/blightmud
see-also: clients/tintin
see-also: protocols/gmcp
see-also: connecting
---

Mudlet is een grafische client met een kaartfunctie, een pakketsysteem en een Lua-API waartegen het
grootste deel van zijn eigen functies geschreven is. Hij is GPL, wordt actief uitgebracht, en is de
gebruikelijke aanbeveling voor wie op een moderne gevecht-MUD begint.

## Toegankelijkheid

Dit is de client met de sterkste gedocumenteerde papieren in dit onderdeel, en het is de moeite
waard uit te spellen wat "gedocumenteerd" hier betekent, want het is ongewoon.

Mudlet heeft een **hoofdstuk over schermlezers in de handleiding**, pagina's per besturingssysteem
die Narrator, NVDA en JAWS op Windows, Orca op Linux en VoiceOver op macOS bij naam noemen, een
commando `mudlet access on` in de client zelf, en een optie om binnenkomende speltekst via de lezer
aan te kondigen. Er is ook een instelling die het gebruik van een schermlezer via MTTS aan de server
aankondigt, zodat een spel zich kan aanpassen als het dat wil.

Het is ook openhartig over waar het niet goed werkt: de eigen Windows-pagina zegt dat JAWS het
uitvoervenster niet leest zoals andere lezers dat doen, en beveelt in plaats daarvan Narrator of
NVDA aan. Een project dat het geval publiceert waarin zijn toegankelijkheidsondersteuning onwerkbaar
is, geeft je betere informatie dan een project dat een vinkje publiceert.

## Waar de tabel onbekend zegt

**MCCP.** De broncode van Mudlet implementeert MCCP v1 en v2, maar de pagina met ondersteunde
protocollen in de handleiding noemt het niet, en de regel in dit onderdeel is dat een uitspraak over
een mogelijkheid de eigen documentatie van het project aanhaalt. Een constante uit een headerbestand
lezen is niet dezelfde handeling, dus de cel zegt onbekend.

## Aantekening over codering

De standaardcodering van Mudlet voor servergegevens is ASCII in plaats van UTF-8, en
CHARSET-onderhandeling kwam in 4.10. Komt de tekst van een spel er verkeerd uit op een vers profiel,
dan is die instelling de eerste plek om te kijken.
