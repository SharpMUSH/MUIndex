---
kind: protocol
slug: atcp
title: ATCP
summary: De voorloper van GMCP. Out-of-bandgegevens met een losser payloadformaat, grotendeels achterhaald, en nog altijd onderhandeld door servers die het nooit verwijderd hebben.
protocol: ATCP
see-also: protocols/gmcp
see-also: protocols/msdp
see-also: clients/mudlet
---

ATCP — het Achaea Telnet Client Protocol — is telnet-optie 200, en het is de plek waar het idee om
gestructureerde gegevens naast MUD-tekst te sturen voor het eerst breed werd uitgerold. Een server
stuurt een modulenaam en een payload; de client routeert die.

Het payloadformaat is losser dan de JSON van [GMCP](/reference/protocols/gmcp), en dat is in wezen
waarom GMCP het vervangen heeft. Clients die ATCP ondersteunen documenteren het tegenwoordig
doorgaans als verouderd en verwijzen je door naar GMCP.

## Waarom het er nog is

Omdat er niets stukgaat door het aan te laten staan. Een server die ATCP in 2008 implementeerde en er
in 2014 GMCP bij deed, onderhandelt meestal nog over allebei, en een client die allebei ondersteunt
neemt wat hem aangeboden wordt.

Voor een nieuwe implementatie is er geen reden om het te kiezen.

## Wat we meten

Servers die telnet-optie 200 aanboden in een handshake die we waargenomen hebben. Een laag cijfer is
hier te verwachten en gaat over ouderdom en verder over niets.
