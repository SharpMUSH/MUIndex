---
kind: protocol
slug: mxp
title: MXP
summary: Het MUD eXtension Protocol — HTML-achtige opmaak in de tekststroom, goed voor klikbare links, afbeeldingen en formulieren. Breed gespecificeerd, ongelijkmatig geïmplementeerd.
protocol: MXP
home: https://www.zuggsoft.com/zmud/mxp.htm
see-also: protocols/pueblo
see-also: clients/mushclient
see-also: clients/mudlet
---

MXP bouwt een kleine, HTML-achtige opmaaktaal in de tekst die een server stuurt: `<send>` voor een
klikbaar commando, `<a href>` voor een link, elementen voor kleur en lettertype, en een mechanisme
waarmee een server eigen tags kan definiëren. Het onderhandelt op telnet-optie 91.

Het ontwerpprobleem zit er inherent in en is interessant: de opmaak reist in dezelfde stroom als de
tekst, dus een server moet oppassen met tekst die er *uitziet* als opmaak, en een client moet
oppassen met wat hij weergeeft. Precies daarom definieert MXP beveiligingsniveaus — een tag die
binnenkomt in een regel chat van een andere speler is niet hetzelfde als een tag die de server zelf
uitgestuurd heeft.

## Klikbaarheid is waarom mensen het willen

Waar MXP in de praktijk vooral voor gebruikt wordt, is `north` en namen van voorwerpen veranderen in
dingen waarop je kunt klikken. Voor een nieuwe speler scheelt dat aanzienlijk, en daarom blijft het
protocol geïmplementeerd worden ondanks zijn complexiteit.

## Pueblo is die andere

[Pueblo](/reference/protocols/pueblo) is ouder dan MXP en doet soortgelijk werk met een andere
aanpak, die letterlijker de vorm van HTML heeft. Een client die het ene ondersteunt, ondersteunt het
andere vaak niet, en de twee zijn makkelijk te verwarren bij het lezen van een functielijst — een
fout waarmee we in de clienttabellen in dit onderdeel hebben moeten oppassen.

## Wat we meten

Servers die telnet-optie 91 aanboden in een handshake die we waargenomen hebben. Er wordt minder vaak
over MXP onderhandeld dan over de out-of-bandprotocollen, deels doordat veel van zijn waarde
gerealiseerd wordt door servers die de opmaak gewoon uitsturen en hopen, zonder ergens over te
onderhandelen — en dat kunnen wij niet zien.
