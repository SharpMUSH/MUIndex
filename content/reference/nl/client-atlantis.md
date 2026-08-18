---
kind: client
slug: atlantis
title: Atlantis
summary: Een client die alleen op macOS draait, met een lang leven en een lange bèta. Van zijn scripting staat gedocumenteerd dat die niet meer werkt, en dat is de ene eerlijke "nee" in dit onderdeel.
home: https://www.riverdark.net/atlantis/
platform: macOS
capability: screen reader | unknown |
capability: TLS | yes | https://www.riverdark.net/atlantis/history.php
capability: UTF-8 | yes | https://www.riverdark.net/atlantis/history.php
capability: MCCP | yes | https://www.riverdark.net/atlantis/history.php
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | no | https://www.riverdark.net/atlantis/
see-also: clients/mudlet
see-also: protocols/charset
---

Atlantis is een native macOS-client die er al is sinds Mac OS X 10.3 en die in het Catalina-tijdperk
naar 64 bit is bijgewerkt. Hij kan overweg met tekensetonderhandeling volgens RFC 2066 en met
Unicode, wat beter is dan zijn leeftijd doet vermoeden, en hij doet MCCP en SSL.

## De ene "nee" in dit onderdeel

Zijn scripting liep via Perl, door de CamelBones-brug, en de eigen homepage van het project zegt dat
die niet meer werkt — Apple veranderde zijn omgang met Perl en de auteur van de bibliotheek is
enkele jaren geleden overleden. Dat is een *afwezigheid met bron*, en dat is iets anders dan een
onbekende; het is de enige cel in het hele clientonderdeel die er een draagt. Overal elders was het
eerlijke antwoord dat we het niet konden vaststellen.

## Alles wat we niet konden vaststellen

De versiegeschiedenis is volledig en openbaar en noemt **MCCP**, **SSL** en
**tekensetonderhandeling** — en noemt nergens GMCP, MSDP, ATCP of MSP. MXP komt één keer voor, als
iets dat bedoeld was voor een versie na 1.0.0, die er niet gekomen is.

Er is een Perl-aanroep `Atlantis::Speak()` in de scripting-API, en het zou makkelijk zijn die te
lezen als ondersteuning voor schermlezers. Dat is het niet: het is een gescripte
tekst-naar-spraakaanroep in een scriptingsysteem waarvan het project zegt dat het niet werkt.
VoiceOver, "toegankelijk" en "schermlezer" komen niet voor op de homepage, de downloadpagina, de
volledige versiegeschiedenis of de gearchiveerde gebruikershandleiding.

De huidige download is 0.9.9.8, nog altijd formeel een bèta, zonder dat er ergens op de site een
releasedatum gepubliceerd is.
