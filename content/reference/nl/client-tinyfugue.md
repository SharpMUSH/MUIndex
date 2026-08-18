---
kind: client
slug: tinyfugue
title: TinyFugue
summary: De klassieke UNIX-terminalclient. Upstream heeft sinds 2007 niets uitgebracht; een onderhouden fork draagt hem verder.
home: https://tinyfugue.sourceforge.net/
platform: Linux
platform: macOS
platform: BSD
capability: screen reader | unknown |
capability: TLS | yes | https://tinyfugue.sourceforge.net/
capability: UTF-8 | unknown |
capability: MCCP | yes | https://tinyfugue.sourceforge.net/
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://tinyfugue.sourceforge.net/
see-also: clients/tintin
see-also: clients/blightmud
---

TinyFugue — "tf" — is de terminalclient die een groot deel van de MUSH-wereld twee decennia lang
gebruikt heeft, met aparte deelvensters voor invoer en uitvoer, een eigen macrotaal, en een stel
gewoonten die verscheidene van zijn concurrenten overleefd hebben.

**Upstream ligt stil**: de laatste release is 5.0 bèta 8, van januari 2007. Hij bouwt nog steeds en
hij werkt nog steeds.

Een onderhouden fork, *TinyFugue Rebirth*, wordt actief uitgebracht en voegt GMCP, ATCP,
ondersteuning voor brede tekens via ICU, en scripting in Python en Lua naast de eigen macrotaal toe.
De tabel hierboven beschrijft **upstream**, want dat is waar "TinyFugue" naar verwijst; installeer
je vandaag, dan is de fork het eerst bekijken waard.

## De valstrik in de documentatie van deze client

Upstream heeft een documentatieonderwerp met de naam **"non-visual mode"**. Dat gaat niet over
hulptechnologie — het gaat erover de invoer op de onderste regel te houden — en het noemt nergens
een schermlezer, spraak of blinde gebruikers. Een mogelijkhedentabel die met trefwoorden bij elkaar
gezocht is, zou van die bestandsnaam een ja maken. Deze zegt onbekend, want dat is wat de
documentatie draagt.

UTF-8 is een antwoord van dezelfde vorm: de gedocumenteerde ondersteuning voor codering geldt de
8-bits ISO 8859-tekensets, en we vonden bij upstream in geen enkele richting een uitspraak over
UTF-8.
