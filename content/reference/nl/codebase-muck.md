---
kind: codebase
slug: muck
title: MUCK
summary: Een TinyMUD-afstammeling met een eigen Forth-achtige taal in het spel, en een sociale cultuur die van de MUSH-kant verschilt.
codebase: MUCK
home: https://www.fuzzball.org/
see-also: mush-mud-muck-moo
see-also: codebases/tinymush
see-also: codebases/moo
---

MUCK — in de praktijk vrijwel altijd **Fuzzball MUCK** — is eerder een broer of zus van de MUSH-lijn
dan een afstammeling ervan: beide komen van TinyMUD, en beide zetten een programmeertaal binnen in
het spel.

De taal is het zichtbare verschil. MUF (*Multi-User Forth*) is stapelgebaseerd en leest in niets als
MUSH-softcode; een bouwer die de ene vloeiend beheerst is beginner in de andere. Daarboven zit MPI,
een kleinere taal voor inline-expressies die gebruikt wordt voor de dingen die softcode op een MUSH
zou doen.

Cultureel is MUCK het thuis van een groot deel van de sociale en fandomwerelden van de hobby. Die
spellen zijn meestal gebouwd rond aanwezigheid en gesprek in plaats van rond scènes met een begin en
een eind, wat een echt verschil met de rollenspel-MUSH-traditie is en geen kwestie van thema.

## Hoe het er van buitenaf uitziet

Geen MSSP. Een `WHO` vóór het inloggen die met een telling antwoordt. Geen telnet-opties
onderhandeld op het spel dat we gemeten hebben — en één detail uit het onderzoek is het bewaren
waard: zijn `WHO`-antwoord eindigde op een spatie zonder regeleinde, en dat is het soort ding
waardoor een naïeve parser helemaal niets meldt.
