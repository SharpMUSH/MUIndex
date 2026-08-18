---
kind: protocol
slug: mssp
title: MSSP
summary: Het Mud Server Status Protocol — hoe een spel een crawler over zichzelf vertelt. Alles wat het meldt is opgegeven, niet gemeten, en deze site houdt die twee uit elkaar.
protocol: MSSP
home: https://www.mudhalla.net/tintin/protocols/mssp/
see-also: protocols/gmcp
see-also: codebases/dikumud
see-also: codebases/pennmush
---

MSSP is telnet-optie 70. Een crawler stuurt `IAC DO MSSP`; een server die het ondersteunt antwoordt
met een tabel van naam/waarde-paren die hem beschrijft — naam, spelerstelling, codebase, uptime,
hostnaam, poort, genre, en wat hij verder ook wil publiceren.

Het komt het dichtst in de buurt van wat deze hobby aan een machineleesbare gidsvermelding heeft, en
het is de reden dat verschillende gidsen überhaupt bestaan.

## Alles in een MSSP-rapport is een bewering

Dit is het punt waarop deze site van elke gevestigde gids verschilt. Een MSSP-rapport is het spel dat
je over zichzelf *vertelt*. `GMCP 1` in een MSSP-tabel betekent dat iemand `1` in een
configuratiebestand getypt heeft, misschien wel in 2011. Het is geen bewijs dat de server GMCP
aanbiedt, en de twee wijken vaak genoeg van elkaar af om interessant te zijn.

Feiten die uit MSSP komen krijgen hier dus het label **opgegeven**, en waar we hetzelfde feit kunnen
meten — een mogelijkheid, door te kijken of er werkelijk over de optie onderhandeld wordt — worden
beide getoond, naast elkaar, elk met een ouderdom erbij. Een spel waarvan de MSSP al zes jaar GMCP
opgeeft en het nog nooit één keer in een handshake aangeboden heeft, is een feit dat het weten waard
is, en nergens anders is het te vinden.

Het ene veld dat we bewust in het geheel niet laten meetellen is `CREATED`. Het is één met de hand
ingetypte regel, en het ergens voor laten meetellen zou dat iets triviaal manipuleerbaar maken.

## Wie het beantwoordt

MSSP is het antwoord van **Diku en LP**. In ons eigen onderzoek onder 38 codebases publiceerden er 28
een spelerstelling via MSSP en zeven via een `WHO` op het inlogscherm, en maar twee deden allebei —
de twee families zijn vrijwel disjunct. AresMUSH, TinyMUX, MUCK, RhostMUSH, CobraMUSH en TinyMUSH
bieden helemaal geen MSSP aan.

Dat is het empirische argument om vier lagen te peilen in plaats van één: **een crawler die alleen op
MSSP gebouwd is, kan het grootste deel van de MUSH-familie niet zien**, en dat is een groot deel van
de hobby en het merendeel van het beoogde publiek van deze site.

## Vragen, niet afwachten

Heel veel servers die MSSP volledig ondersteunen zullen het nooit uit zichzelf aanbieden — ze
beantwoorden `IAC DO MSSP` en zeggen verder niets. Een crawler die opent met `IAC WILL NAWS` en dan
wacht, meldt die spellen dus als spellen die niets publiceren, en dat is een bewering over de server
die voortkomt uit het zwijgen van de crawler zelf. Wij sturen `IAC DO MSSP` bij het verbinden.

## De vorm in platte tekst

Er bestaat een oudere variant waarin een client bij het inlogscherm letterlijk de regel
`MSSP-REQUEST` stuurt. We hebben het gemeten: van de twintig geprobeerde spellen antwoordden er drie
— en alle drie beantwoordden ook telnet-optie 70, dus het bereikte niets wat de optie niet al
bereikte. Acht servers lazen het verzoek als een **personagenaam** en zeiden dat ook, waarmee een van
de inlogpogingen opging die een vreemde krijgt. Wij sturen het niet.
