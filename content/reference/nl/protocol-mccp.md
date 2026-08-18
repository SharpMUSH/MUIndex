---
kind: protocol
slug: mccp
title: MCCP
summary: Compressie van de stroom. Goedkoop, breed uitgerold, en het protocol dat de leerzaamste bug in de geschiedenis van dit project opleverde.
protocol: MCCP
home: https://www.mudhalla.net/tintin/protocols/mccp/
see-also: codebases/rom
see-also: codebases/dikumud
see-also: protocols/gmcp
---

MCCP comprimeert de stroom van server naar client met zlib. Versie 1 is telnet-optie 85 en is
feitelijk historisch; **versie 2** is optie 86 en is waarover moderne servers onderhandelen. Nadat de
server `IAC SB MCCP2 IAC SE` gestuurd heeft, hoort elke byte die volgt bij één doorlopende
zlib-stroom.

Het is een echte besparing op een tekstprotocol — MUD-uitvoer laat zich uitzonderlijk goed
comprimeren — en het is gangbaar in de Diku- en LP-families, waar ruwweg een derde van de codebases
in ons onderzoek erover onderhandelt.

## Hoe het misgaat, en waarom dat hier uitmaakt

Een client die over MCCP2 onderhandelt en de stroom vervolgens niet uitpakt, ontvangt **binaire
rommel vanaf het compressiemarkeerpunt**. Geen foutmelding, geen verbroken verbinding: het
verbindingsscherm komt binnen als een muur van vervangingstekens, en alles daarna — het
`WHO`-antwoord, elke latere MSSP, de hele sessie — is verloren.

Dit is niet hypothetisch. Onze eigen telnet-bibliotheek deed precies dat. Ze onderhandelde over de
optie, vuurde haar callback voor ‘compressie ingeschakeld’ af, en pakte geen enkele byte uit. De
payload liet zich met een gewone zlib-aanroep zonder problemen decomprimeren, en dat maakte
ondubbelzinnig duidelijk dat de servers gelijk hadden en wij niet. Dertien van de achtendertig
codebases in ons onderzoek waren geraakt, en zolang het duurde konden we niet waarnemen waarover die
servers *na* het begin van de compressie onderhandelden — dus ons beeld van hun mogelijkheden stelde
ze te laag voor.

Het is upstream opgelost. Een vervolgdefect — de inflater die per leesbewerking opnieuw wordt
aangemaakt in plaats van voor de duur van de verbinding bewaard te blijven, wat halverwege een groot
verbindingsscherm misgaat — is gemeld en staat open, en treft de staart van de grootste schermen.

Twee dingen die een lezer hieruit mee moet nemen. **Een protocolcijfer op deze pagina is net zozeer
een meting van onze crawler als van de hobby**, en waar we weten dat het fout is geweest, zeggen we
dat. En schrijf je een client: over MCCP onderhandelen is makkelijk, en het correct uitpakken is waar
het werk zit.
