---
kind: client
slug: blightmud
title: Blightmud
summary: Een moderne terminalclient in Rust, met Lua-scripting, ingebouwde tekst-naar-spraak en een schermlezermodus die zichzelf aan de server bekendmaakt.
home: https://github.com/Blightmud/Blightmud
platform: Linux
platform: macOS
platform: Windows (WSL only)
capability: screen reader | yes | https://github.com/Blightmud/Blightmud
capability: TLS | yes | https://github.com/Blightmud/Blightmud
capability: UTF-8 | yes | https://github.com/Blightmud/Blightmud
capability: MCCP | yes | https://github.com/Blightmud/Blightmud
capability: GMCP | yes | https://github.com/Blightmud/Blightmud
capability: MSDP | yes | https://github.com/Blightmud/Blightmud
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/Blightmud/Blightmud
see-also: clients/tintin
see-also: clients/mudlet
see-also: protocols/ttype
---

Blightmud is een terminalclient geschreven in Rust, GPL 3, en behoort tot de actiefst uitgebrachte
clients in dit onderdeel. Scripting gaat in Lua. Hij draait alleen in de terminal: er is geen native
Windows-build, en Windows-gebruikers draaien hem onder WSL.

## Toegankelijkheid

Blightmud heeft hier drie afzonderlijke onderdelen, en dat is meer dan één rij kan dragen:

- Een **schermlezervriendelijke modus** (`--reader-mode`, of de instelling `reader_mode`) die de
  terminalinterface verandert in iets wat een lezer kan volgen. Het statusgebied wordt niet
  ondersteund.
- **Ingebouwde tekst-naar-spraak**, als optionele compilatie, met een Lua-API die een script kan
  gebruiken — inclusief een `tts.gag()` om te voorkomen dat een gevonden regel uitgesproken wordt.
  De documentatie is er openhartig over dat zijn TTS naast een schermlezer draaien niet altijd een
  gelukkige combinatie is.
- **Automatische MTTS-aankondiging**: in schermlezermodus of met TTS aan voegt hij
  `MTTS_SCREEN_READER` toe aan wat hij de server over zichzelf vertelt, zodat een spel dat erom
  geeft zich kan aanpassen.

Net als bij TinTin++ wordt geen bepaalde schermlezer genoemd, dus dit is een gedocumenteerde modus
en geen geteste compatibiliteit met een product.

## Waar de tabel onbekend zegt

**MXP**, **MSP** en **ATCP** komen nergens voor in de README van het project of in de meegeleverde
help. **MCCP** is gedocumenteerd als v2; of v1 ook aangekund wordt hebben we niet vastgesteld.
