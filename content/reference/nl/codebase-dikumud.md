---
kind: codebase
slug: dikumud
title: DikuMUD
summary: De wortel van de familie gevecht-MUD's. Levels, klassen, uitrusting en areabestanden — en een licentie die een generatie afgeleiden vormgaf.
codebase: DikuMUD
home: https://dikumud.com/
see-also: codebases/circlemud
see-also: codebases/rom
see-also: codebases/smaug
see-also: mush-mud-muck-moo
---

DikuMUD, geschreven aan het Datalogisk Institut van de Universiteit van Kopenhagen en uitgebracht in
1991, is de voorouder van het meeste waar mensen op doelen als ze zonder nadere aanduiding "MUD"
zeggen. Levels, personageklassen, hit points, mobs, uitrustingsplekken, een areabestandsformaat dat
een bouwer offline schrijft — de hele woordenschat komt hiervandaan, en spellen die nooit
Diku-broncode gezien hebben erven nog altijd zijn vorm.

De licentie is deel van het verhaal. Diku was vrij te gebruiken maar verbood geld vragen voor
toegang en eiste dat de oorspronkelijke credits getoond werden, en door die clausule verschijnen "de
Diku-credits" op het inlogscherm van spellen die er verscheidene forks van verwijderd liggen.

De directe afstammelingen — **Merc**, daarna **ROM**, **CircleMUD**, **SMAUG**, **tbaMUD** en
tientallen andere — vormen een groot deel van elke MUD-lijst die ooit bestaan heeft.

## Hoe het er van buitenaf uitziet

De Diku-familie is de **MSSP**-familie. Waar de MUSH-kant een telling publiceert via een `WHO` op
het inlogscherm en helemaal geen MSSP aanbiedt, beantwoorden servers uit de Diku-lijn overweldigend
vaak telnet-optie 70 met een gestructureerd rapport, en daar komen hun getallen hier vandaan.

**MCCP2** — streamcompressie — komt in deze familie ook veel voor, en het is de moeite waard te
weten dat een client die erover onderhandelt maar de stream niet kan uitpakken het hele
verbindingsscherm als binaire ruis ontvangt. Dat was een echt gebrek in de eigen telnet-bibliotheek
van dit project en het is hersteld; zie [MCCP](/reference/protocols/mccp).
