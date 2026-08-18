---
kind: protocol
slug: pueblo
title: Pueblo
summary: Het oudere schema voor HTML in een MUD, afkomstig uit de gelijknamige client. Nog altijd ondersteund door clients aan de MUSH-kant, en stelselmatig verward met MXP.
protocol: PUEBLO
home: https://pueblo.sourceforge.net/
see-also: protocols/mxp
see-also: clients/beipmu
---

Pueblo kwam halverwege de jaren negentig voort uit de gelijknamige client en koos een directe aanpak
om MUD-tekst rijker te maken: laat de server **HTML** sturen, en laat de client die weergeven. Een
server kondigt Pueblo-ondersteuning aan in een regel bij het verbinden; de client antwoordt, en vanaf
dat moment mag de stroom opmaak bevatten.

Het bereikte de MUSH-kant van de hobby meer dan de MUD-kant, en MUSH-servers die het ondersteunen
doen dat over het algemeen nog steeds.

## Niet MXP

[MXP](/reference/protocols/mxp) is het latere schema en het breder geïmplementeerde. Ze doen
soortgelijk werk en zijn niet uitwisselbaar, en de Pueblo-ondersteuning van een client lezen als
MXP-ondersteuning — of andersom — is de allermakkelijkste fout bij het samenstellen van een
clientvergelijking. Daarom houden de clientpagina's in dit onderdeel ze uit elkaar, en waar een
project het ene documenteert en het andere niet, zegt het andere *onbekend*.

## Wat we meten

De handshake van Pueblo is geen telnet-optie in de gebruikelijke zin, dus wat we waarnemen is smaller
dan bij de onderhandelde protocollen, en een laag cijfer hier moet gelezen worden als een uitspraak
over ons zicht erop en niet over de uitrol.
