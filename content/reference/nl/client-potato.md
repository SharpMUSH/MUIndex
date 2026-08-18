---
kind: client
slug: potato
title: Potato MUSHclient
summary: Een platformonafhankelijke Tcl/Tk-client geschreven voor MUSH-spelers. Goede ondersteuning voor codering, en documentatie die over de meeste protocollen helemaal niets zegt.
home: https://www.potatomushclient.com/
platform: Windows
platform: Linux
platform: macOS
capability: screen reader | unknown |
capability: TLS | yes | https://github.com/potatomushclient/potato/wiki/ConfigureWorldsBasics
capability: UTF-8 | yes | https://github.com/potatomushclient/potato/wiki/Features
capability: MCCP | unknown |
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/potatomushclient/potato/wiki/FAQs
see-also: clients/beipmu
see-also: clients/mushclient
see-also: collaborative-roleplay
---

Potato is een Tcl/Tk-client gebouwd voor MUSH-spel — meerdere werelden, spawn windows, en een set
standaardinstellingen die ervan uitgaat dat je poses typt en geen gevechtscommando's. Hij draait
vanuit dezelfde broncode op Windows, Linux en macOS, waarbij de macOS-builds meestal een versie of
twee achterlopen.

Hij onderhandelt over tekencodering en spreekt volledig Unicode, wat voor de MUSH-kant van de hobby
de mogelijkheid is die er in de praktijk het meest toe doet.

Let op één gedocumenteerde beperking: hij ondersteunt verbinden met een poort die vanaf het begin
SSL is, en zijn eigen configuratiepagina zegt dat onderhandelde SSL in STARTTLS-stijl **niet**
ondersteund wordt.

## Waarom zes rijen onbekend zeggen

We hebben de homepage van het project, zijn downloadpagina, alle 103 helpbestanden van zijn wiki en
zijn hele bronboom doorzocht op GMCP, MSDP, MCCP, MXP, MSP en ATCP. Er is over geen ervan een
gedocumenteerde uitspraak. Er is wél *code* die enkele ervan aanraakt, en dit onderdeel maakt van
code geen uitspraak over een mogelijkheid — een tabel die "ja" zegt op grond van een constante in
een headerbestand doet een belofte die het project nooit gedaan heeft.

De rij over schermlezers is hetzelfde antwoord, langs dezelfde weg bereikt: een zoektocht zonder
onderscheid tussen hoofd- en kleine letters naar "screen reader", "text-to-speech", NVDA, JAWS,
VoiceOver, "accessibility", "visually impaired" en "blind" door alles wat het project publiceert,
leverde helemaal niets op. Dat is geen bevinding over de software.
