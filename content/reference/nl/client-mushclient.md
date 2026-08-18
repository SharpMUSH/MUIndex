---
kind: client
slug: mushclient
title: MUSHclient
summary: De al lang gevestigde Windows-client. Vijf scripttalen, een plug-inarchitectuur waar het grootste deel van zijn protocolondersteuning in zit, en een releasegeschiedenis die vertraagd is.
home: https://www.mushclient.com/
platform: Windows
platform: Linux (Wine)
capability: screen reader | unknown |
capability: TLS | unknown |
capability: UTF-8 | unknown |
capability: MCCP | yes | https://www.mushclient.com/mushclient/mccp.htm
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | yes | https://www.mushclient.com/gmcp
capability: MXP | yes | https://www.mushclient.com/mushclient/doc/general/features.html
capability: MSP | yes | https://github.com/nickgammon/mushclient/blob/master/plugins/msp.xml
capability: scripting | yes | https://www.mushclient.com/mushclient/doc/general/features.html
see-also: clients/mudlet
see-also: clients/potato
see-also: protocols/mccp
---

MUSHclient is de Windows-client van Nick Gammon, onder de MIT-licentie, en lange tijd het
standaardantwoord voor iedereen op Windows. Er wordt in gescript met Lua, VBScript, JScript,
PerlScript en Python, en veel van wat hij doet wordt gedragen door plug-ins in plaats van door de
kern — wat een echte architectuurkeuze is en tegelijk de reden dat verscheidene rijen hierboven
lastiger te beantwoorden zijn dan ze lijken.

De laatste getagde release is **5.06, uit maart 2019**. Er wordt nog steeds aan de repository
gecommit, en er zijn release notes voor een 5.07 die niet uitgebracht is.

## Waarom zoveel rijen onbekend zeggen

Bij elk daarvan is het eerlijke antwoord "we konden het niet vaststellen", en de redenen
verschillen:

- **GMCP** — de eigen pagina van het project erover toont een *voorbeeld* van een plug-in die je zou
  kunnen schrijven, niet een functie die de client heeft. Dat is iets anders dan ondersteuning
  uitleveren, dus de cel zegt onbekend in plaats van ja.
- **TLS** — de gedocumenteerde methode is een extern `stunnel`-proces. Een commit die TLS op basis
  van OpenSSL toevoegde belandde in 2026 op de master-branch en zit in geen enkele release, dus er
  is niets dat een gebruiker vandaag kan installeren en waar wij naar kunnen wijzen.
- **UTF-8** — CHARSET-onderhandeling komt voor in de niet-uitgebrachte 5.07-notities en nergens waar
  wij het in de documentatie van een uitgebrachte versie konden vinden.
- **MSDP** — niets in welke richting dan ook.
- **Schermlezer** — er wordt een tekst-naar-spraak-plug-in op basis van Windows SAPI met de client
  meegeleverd, en dat is niet hetzelfde als ondersteuning voor schermlezers. Er is geen paragraaf
  over toegankelijkheid in de handleiding, en de auteur heeft in zijn eigen forum beschreven waarom
  het uitvoervenster lastig werkbaar is voor een lezer: het kent geen begrip van een huidige regel.
  We konden geen antwoord vaststellen, dus de tabel geeft er geen.

Geen van deze is een *nee*. Verscheidene zijn heel goed mogelijk ja en we konden het niet aantonen.
