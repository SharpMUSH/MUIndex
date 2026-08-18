---
kind: protocol
slug: msp
title: MSP
summary: Het MUD Sound Protocol — de server noemt een geluidsbestand en de client speelt het af. Oud, eenvoudig, en makkelijk te verwarren met twee andere dingen.
protocol: MSP
home: https://www.zuggsoft.com/zmud/msp.htm
see-also: protocols/mxp
see-also: clients/vipmud
---

Met MSP kan een server een client vragen een geluid af te spelen: een instructie tussen haakjes die
een bestand noemt, een volume, een aantal herhalingen en een URL om het vandaan te halen als de
client het niet heeft. Het onderhandelt op telnet-optie 90, en het kan ook in band in de tekststroom
gestuurd worden door servers die nergens over onderhandelen.

Het is werkelijk oud en werkelijk nog in gebruik — omgevingsgeluid in een tekstspel doet meer dan het
klinkt, en voor spelers die de geluidssignalen van een client gebruiken in plaats van het beeld is
het meer dan versiering.

## Drie dingen die het niet is

De clienttabellen in dit onderdeel moesten hier voorzichtig zijn, en het is de moeite waard op te
schrijven waarom:

- **MCMP** — het Mud Client Media Protocol — is een ander protocol dat soortgelijk werk doet.
  Minstens één client implementeert MCMP en geen MSP, en het ene voor het andere lezen zou een
  bewering in een tabel zetten die niemand gedaan heeft.
- **De eigen scriptaanroep ‘speel een geluid af’ van een client** is geen MSP. Die speelt een lokaal
  bestand af wanneer een script dat zegt; bij MSP vertelt een server een client wat hij moet
  afspelen.
- **Ondersteuning via een meegeleverde plug-in is het waard om als zodanig te vermelden.** Bij één
  client komt de MSP-ondersteuning als een plug-in die uitdrukkelijk geen telnet-onderhandeling doet,
  wat werkt bij servers die MSP in band sturen en niet bij servers die verwachten dat erover
  onderhandeld wordt.

## Wat we meten

Servers die telnet-optie 90 aanbieden. Doordat MSP vaak in band gestuurd wordt zonder onderhandeling,
stelt dit cijfer de uitrol te laag voor met een hoeveelheid die we niet kunnen schatten — en dat is
een beperking van wat een handshake kan zien, geen bevinding over het protocol.
