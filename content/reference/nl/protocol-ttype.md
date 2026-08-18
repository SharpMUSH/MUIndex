---
kind: protocol
slug: ttype
title: TTYPE en MTTS
summary: Hoe een client een server vertelt wat hij is en wat hij kan — inclusief, als de client dat wil zeggen, dat er een schermlezer in gebruik is.
protocol: TTYPE
home: https://www.mudhalla.net/tintin/protocols/mtts/
see-also: protocols/charset
see-also: clients/tintin
see-also: clients/blightmud
---

TTYPE is telnet-optie 24, uit RFC 1091: de server vraagt de client welke terminal hij is, en de
client antwoordt. Historisch was het antwoord `VT100` of `ANSI`.

**MTTS** — de Mud Terminal Type Standard — legt daar een afspraak overheen. Een client antwoordt drie
keer: zijn naam, zijn terminaltype, en dan `MTTS <bitmask>`, waarbij de bits mogelijkheden opgeven.
256 kleuren, true colour, UTF-8, MNES, MSP over out-of-band — en, opvallend genoeg,
**`MTTS_SCREEN_READER`**.

## Het schermlezerbit

Bij die laatste is het de moeite waard even stil te staan, want het is de enige plek in de
protocolstapel van deze hobby waar toegankelijkheid een eersterangsbegrip is.

Een client die het zet, vertelt de server dat er een schermlezer in gebruik is, en een server die dat
opmerkt kan zich aanpassen: ASCII-kunst onderdrukken, de decoratieve kaderlijnen rond een
kamerbeschrijving weglaten, een tabel anders opmaken. Zowel [TinTin++](/reference/clients/tintin) als
[Blightmud](/reference/clients/blightmud) adverteert het, en [Mudlet](/reference/clients/mudlet)
heeft er een instelling voor.

Of een bepaald spel er iets mee doet is een andere vraag, en niet een die deze site kan meten — we
kunnen een server niet vragen wat hij anders zou doen.

## Wat een crawler hier verplicht is

Een crawler maakt zichzelf via TTYPE bekend, en dat hoort ook. De onze doet dat, met een URL met
informatie, zodat een beheerder die zijn logboeken leest kan achterhalen wie er verbinding met zijn
spel gemaakt heeft en hoe hij ons kan vragen te stoppen. Een crawler die `ANSI` antwoordt en verder
niets, is anoniem van opzet, en daar is geen goede reden voor.

## Wat we meten

Servers die met ons over TTYPE onderhandeld hebben. Let op: dit is een van de weinige opties waarbij
*wij* de partij zijn die gevraagd wordt, dus een cijfer hier is een telling van servers die de moeite
namen te vragen.
