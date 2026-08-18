---
kind: orientation
slug: connecting
title: Hoe je verbinding maakt
summary: Een host, een poort en telnet. Wat het adres op een spelpagina betekent en wat je ermee doet.
see-also: mush-mud-muck-moo
see-also: protocols/tls
see-also: protocols/charset
---

Elk spel dat hier vermeld staat antwoordt op een **host en een poort**, en het protocol eronder is
telnet — wat in de praktijk een kale TCP-verbinding betekent met een beetje optionele onderhandeling
erbovenop.

    telnet mush.pennmush.org 4201

Dat werkt, en op veel systemen staat het al geïnstalleerd. Het is ook een slechte manier om te
spelen: de `telnet` van het systeem heeft geen noemenswaardige controle over lokale echo, geen
logboek, geen geschiedenis, en hij verminkt alles boven ASCII. Het is het juiste gereedschap om te
controleren of een spel in de lucht is, en het verkeerde om er een avond in door te brengen.

## Wat het adres op een spelpagina je vertelt

Elke spelpagina somt de adressen op die we gemeten hebben, en markeert de adressen waarbij **TLS**
waargenomen is. Een spel met een TLS-poort is een spel waarmee je versleuteld verbinding kunt maken;
het poortnummer is meestal een ander dan dat van de gewone poort.

Waar een spel meerdere poorten heeft, is dat vaak dezelfde wereld die op verschillende manieren
bereikt wordt en niet verschillende spellen. Wij vermelden wat we gemeten hebben en gokken niet welke
de officiële is.

## Een client kiezen

Het onderdeel [clients](/reference) heeft voor elk een pagina, met een tabel met mogelijkheden. De
drie dingen die het controleren waard zijn voordat je iets installeert:

- **Doet hij UTF-8?** Als het spel niet alleen Engels is, kom je dit op je eerste avond tegen.
- **Doet hij TLS?** Alleen van belang als het spel het aanbiedt, maar verschillende doen dat
  inmiddels.
- **Gebruik je een schermlezer: documenteert het project ondersteuning daarvoor?** Dit is de rij die
  het vaakst ontbreekt in clientvergelijkingen, dus het is de eerste rij van de onze — en waar
  niemand een antwoord heeft vastgesteld, staat er *onbekend*.

## Als er niets antwoordt

Een spel dat niet antwoordt is niet noodzakelijk weg. Spellen verhuizen van host, DNS verloopt, en
firewalls hebben hun eigen mening. Deze site bewaart elk spel dat ze ooit gemeten heeft — ook de
spellen die jaren geleden gestopt zijn met antwoorden — en blijft wekelijks aankloppen, dus het
[archief](/archive) is de plek om te kijken voordat je iets concludeert.
