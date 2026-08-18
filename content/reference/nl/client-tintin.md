---
kind: client
slug: tintin
title: TinTin++
summary: Een terminalclient met een eigen scripttaal, op elk platform inclusief telefoons, en een gedocumenteerde schermlezermodus.
home: https://tintin.mudhalla.net/
platform: Linux
platform: macOS
platform: Windows
platform: Android
platform: iOS
capability: screen reader | yes | https://tintin.mudhalla.net/manual/screen_reader.php
capability: TLS | yes | https://github.com/scandum/tintin
capability: UTF-8 | yes | https://github.com/scandum/tintin
capability: MCCP | yes | https://tintin.mudhalla.net/
capability: GMCP | yes | https://tintin.mudhalla.net/manual/event.php
capability: MSDP | yes | https://tintin.mudhalla.net/manual/msdp.php
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/scandum/tintin
see-also: clients/blightmud
see-also: clients/mudlet
see-also: protocols/msdp
see-also: protocols/ttype
---

TinTin++ is een client voor de opdrachtregel, GPL 3, wordt actief uitgebracht, en draait op meer
plekken dan wat dan ook hier — waaronder Android en iOS. De scripttaal is een eigen taal, beknopt,
en tot heel veel in staat; een aanzienlijk deel van wat andere clients in de grafische interface
doen is hier een `#config`-regel.

Dezelfde auteur onderhoudt de protocolspecificaties voor **MSSP** en **MSDP**, en daarom halen
zoveel van de protocolpagina's in dit onderdeel dezelfde site aan.

## Toegankelijkheid

TinTin++ heeft een eigen handleidingpagina voor de **schermlezermodus** (`#config screen reader on`,
of `-s` bij het starten). Die aanzetten doet twee dingen: het verwijdert of verandert visuele
elementen die hardop voorgelezen nergens op slaan, en het meldt het gebruik van een schermlezer aan
de server via [MTTS](/reference/protocols/ttype), zodat een spel zijn eigen uitvoer kan aanpassen.

Dat is een gedocumenteerde modus, geen bewering dat er met een bepaalde lezer getest is — op de
pagina wordt geen product genoemd. Het is merkbaar zwakker bewijs dan een client die de lezers noemt
waarmee hij werkt, en merkbaar sterker dan niets.

## Waar de tabel onbekend zegt

Voor **MXP** en **MSP** bestaan er allebei scripts uit de gemeenschap op de site van het project, en
een script is niet de client die een protocol ondersteunt — dat van MXP zegt ronduit dat het
misschien niet op elke MUD werkt. Ingebouwde ondersteuning voor een van beide is niet vastgesteld.
Over **ATCP** vonden we in geen enkele richting iets; merk op dat ATCP grotendeels vervangen is door
GMCP, en dat ondersteunt TinTin++ wel.
