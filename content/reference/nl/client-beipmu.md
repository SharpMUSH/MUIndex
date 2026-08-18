---
kind: client
slug: beipmu
title: BeipMU
summary: Een Windows-client gericht op de MUSH-kant van de hobby, met ondersteuning voor schermlezers in het uitvoervenster en Pueblo in plaats van MXP.
home: https://beipdev.github.io/BeipMU/
platform: Windows
capability: screen reader | yes | https://github.com/BeipDev/BeipMU/blob/master/Assets/Changes.txt
capability: TLS | yes | https://beipdev.github.io/BeipMU/
capability: UTF-8 | yes | https://beipdev.github.io/BeipMU/
capability: MCCP | unknown |
capability: GMCP | yes | https://github.com/BeipDev/BeipMU/blob/master/Documentation/GMCP.md
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://beipdev.github.io/BeipMU/
see-also: clients/mushclient
see-also: clients/potato
see-also: collaborative-roleplay
---

BeipMU is een Windows-client onder de MIT-licentie, die actief uitgebracht wordt, en een van de
weinige die gebouwd is met MUSH-achtig spel voor ogen in plaats van met gevecht-MUD's — meerdere
invoervensters, spawn windows, en een tekstengine die lange alinea's verwacht. Scripting gaat
standaard in JavaScript, met andere ActiveScript-engines beschikbaar.

## Toegankelijkheid

Het uitvoervenster implementeert de `IAccessible`-interface van Windows, bewust toegevoegd als stap
richting bruikbaarheid voor slechtziende spelers, en er is een **Speak**-triggeractie voor
tekst-naar-spraak. Nergens wordt een bepaalde schermlezer genoemd, en er is geen hoofdstuk over
toegankelijkheid in de documentatie.

Eén waarschuwing als je gaat zoeken: een pagina in de eigen documentatie van het project zegt nog
steeds dat BeipMU geen spraaksynthese kan gebruiken. Die pagina is verouderd — de changelog en de
eigen issue-reacties van de onderhouder dateren beide van later.

## Twee makkelijke misverstanden over deze client

**BeipMU implementeert MCMP, niet MSP.** Het zijn verschillende protocollen met vergelijkbare namen
en vergelijkbare doelen, en de een als de ander lezen zou een bewering in deze tabel zetten die
niemand gedaan heeft. De MSP-rij zegt daarom onbekend.

**Hij ondersteunt Pueblo, niet MXP.** Pueblo is het oudere schema voor HTML in een MUD en MXP het
latere; BeipMU documenteert basale Pueblo-stijlen en klikbare links. Over MXP is het niet
vastgesteld, in welke richting dan ook.
