---
kind: protocol
slug: tls
title: TLS
summary: Versleutelde verbindingen. Meestal een aparte poort in plaats van een onderhandelde opwaardering, en de enige mogelijkheid op deze site die we vaststellen door verbinding te maken in plaats van door te vragen.
protocol: TLS
see-also: connecting
see-also: protocols/charset
see-also: clients/potato
---

Telnet is platte tekst. Alles wat je naar een MU\* stuurt — je wachtwoord inbegrepen — gaat over het
netwerk, leesbaar voor alles wat onderweg meekijkt, tenzij het spel TLS aanbiedt.

In deze hobby betekent TLS bijna altijd **een tweede poort die vanaf de eerste byte TLS spreekt**, en
niet een opwaardering in band. Een spel met een gewone poort op 4201 en een TLS-poort op 4202 is de
gebruikelijke vorm. Er bestaat een onderhandelde variant, en die is zeldzaam genoeg dat de
documentatie van minstens één client uitdrukkelijk zegt dat hij niet ondersteund wordt.

## Waarom de spelpagina's dit apart markeren

TLS is de enige mogelijkheid op deze site die vastgesteld wordt door het te *doen*: een adres wordt
als TLS gemarkeerd omdat we er een TLS-handshake mee voltooid hebben. Er komt geen vragen aan te pas
en er is geen veld om iets in op te geven, wat het de zuiverste meting in de catalogus maakt.

Daarom worden de TLS-poort en de gewone poort van een spel ook als aparte adressen vermeld en niet
samengevoegd. Het zijn verschillende metingen van verschillende dingen.

## Praktisch advies

Biedt een spel dat je speelt een TLS-poort aan, gebruik hem dan. Doet het dat niet en vind je het
belangrijk, vraag er dan om — het is weinig werk voor een beheerder, en dat het niet overal is, komt
vooral doordat niemand erom gevraagd heeft en niet doordat iemand bezwaar maakt.

Controleer of je client het ondersteunt voordat je erop vertrouwt. Verschillende in het onderdeel
[clients](/reference) doen dat; minstens één documenteert in plaats daarvan een omweg met een extern
`stunnel`-proces, wat werkt en meer opzetwerk is dan de meeste mensen zullen doen.
