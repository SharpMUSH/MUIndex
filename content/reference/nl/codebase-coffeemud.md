---
kind: codebase
slug: coffeemud
title: CoffeeMUD
summary: Een MUD-server in Java, met het grootste MSSP-rapport van alles wat we gepeild hebben en een ongewoon breed protocoloppervlak.
codebase: CoffeeMUD
home: https://www.coffeemud.net/
see-also: codebases/dikumud
see-also: protocols/mssp
---

CoffeeMUD is een MUD-server in Java met een ongewoon breed functieoppervlak — hij komt met een eigen
webserver, mail, forums en een groot klassen- en vaardighedensysteem, en het is een van de weinige
servers in de hobby die niet in C geschreven is.

Hij wordt actief onderhouden, wat naar de maatstaven van dit deel van de catalogus het hardop zeggen
waard is.

## Hoe het er van buitenaf uitziet

MSSP en **MCCP2**, en CoffeeMUD is een van de slechts drie servers van de twintig die we probeerden
die ook de *platte-tekstvorm* `MSSP-REQUEST` beantwoordde — een variant die ouder is dan de
telnet-optie en die je nog af en toe tegenkomt.

Zijn MSSP-rapport is het grootste dat we gemeten hebben: **47 velden**, waaronder `PORT` dat negen
keer afzonderlijk gemeld wordt voor negen afzonderlijke poorten. Dat is geen misvorming.
MSSP-variabelen zijn lijsten, en een crawler die een meerwaardige `PORT` platslaat tot één string
produceert het getal `80234201` uit `"80" "23" "4201"` — een bug die dit project heeft uitgeleverd
en hersteld, en de reden dat de parser hier waarden overal als lijsten bewaart.
