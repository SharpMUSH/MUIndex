---
kind: protocol
slug: gmcp
title: GMCP
summary: Het Generic Mud Communication Protocol — gestructureerde JSON-berichten naast de tekst, en het out-of-bandkanaal waar de meeste moderne clients tegenaan bouwen.
protocol: GMCP
home: https://www.mudhalla.net/tintin/protocols/gmcp/
see-also: protocols/msdp
see-also: protocols/atcp
see-also: clients/mudlet
---

GMCP is telnet-optie 201. Zodra erover onderhandeld is, kan de server **gestructureerde gegevens out
of band** sturen: een pakketnaam en een JSON-payload, die in dezelfde stroom aankomen als de tekst
maar er geen deel van uitmaken.

`Char.Vitals { "hp": 412, "maxhp": 500 }` is het klassieke voorbeeld. Een client kan daar een
levensbalk mee aansturen zonder het proza af te struinen op getallen, en dat is precies de bedoeling
— een statusweergave die op patroonherkenning in de tekst gebouwd is, breekt op de dag dat een spel
zijn prompt verandert, en een die op GMCP gebouwd is niet.

De naamruimte van de pakketten berust op gewoonte en niet op een standaard. `Char`, `Room`, `Comm` en
`Client` zijn breed in gebruik; daarbuiten verzinnen spellen wat ze nodig hebben, en een client moet
meestal verteld worden wat een bepaald spel stuurt.

## Waarom het ATCP verdrongen heeft

GMCP is de opvolger van [ATCP](/reference/protocols/atcp), dat hetzelfde werk deed met een losser
payloadformaat. JSON was de verbetering, en halverwege de jaren 2010 was de overstap grotendeels
voltooid. Een spel dat allebei ondersteunt is niet ongewoon; een nieuw spel dat alleen ATCP
ondersteunt zou dat wel zijn.

## Wat we meten

Een spel telt hier mee wanneer **zijn server GMCP aanbood in een handshake die we waargenomen
hebben**. Dat is een andere bewering dan dat de MSSP van een spel `GMCP 1` zegt, waar de meeste
protocoltabellen in deze hobby op gebouwd zijn, en die twee wijken geregeld van elkaar af.

Eén kanttekening bij de meting, uit onze eigen geschiedenis: een tijdlang konden we GMCP niet zien op
servers die ook over [MCCP](/reference/protocols/mccp) onderhandelden, doordat onze
telnet-bibliotheek over compressie onderhandelde zonder de stroom uit te pakken, en alles na het
compressiemarkeerpunt was voor ons ruis. Van minstens één server in ons onderzoek bleek dat hij al
die tijd GMCP sprak. Als een cijfer op deze pagina laag lijkt voor een familie die je goed kent, is
dat soort defect het eerste om te verdenken — bij ons, niet bij hen.
