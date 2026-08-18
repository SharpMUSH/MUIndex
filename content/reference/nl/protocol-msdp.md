---
kind: protocol
slug: msdp
title: MSDP
summary: Het Mud Server Data Protocol — hetzelfde werk als GMCP, gedaan met een compacte binaire codering en een ontdekkingsmechanisme dat GMCP niet heeft.
protocol: MSDP
home: https://www.mudhalla.net/tintin/protocols/msdp/
see-also: protocols/gmcp
see-also: clients/tintin
see-also: clients/blightmud
---

MSDP is telnet-optie 69, en het lost hetzelfde probleem op als [GMCP](/reference/protocols/gmcp):
gestructureerde gegevens naast de tekst sturen, zodat een client geen proza hoeft af te struinen op
getallen.

De verschillen zijn er twee. De codering van MSDP is **binair en compact** — variabelen en waarden
worden met losse stuurbytes gemarkeerd in plaats van in JSON verpakt — en MSDP definieert een gesprek
voor **ontdekking**: een client kan met `LIST` naar `COMMANDS`, `REPORTABLE_VARIABLES` enzovoort
vragen, en te horen krijgen wat een bepaald spel ondersteunt. GMCP heeft daar geen equivalent voor,
en daarom moet een GMCP-client meestal per spel ingesteld worden.

In de praktijk heeft GMCP het gewonnen op adoptie en houdt MSDP stand in de servers en clients die
het geïmplementeerd hebben, vaak naast GMCP.

## Wat we meten

Een spel telt hier mee wanneer zijn server MSDP aanbood in een handshake die we waargenomen hebben.
Zoals bij elk cijfer in dit onderdeel is dat een positieve waarneming, en de rest is niet het
tegendeel ervan — een spel dat niet meetelt, implementeert MSDP misschien niet, of we hebben zijn
handshake simpelweg nog niet gelezen.
