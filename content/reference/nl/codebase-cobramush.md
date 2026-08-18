---
kind: codebase
slug: cobramush
title: CobraMUSH
summary: Een PennMUSH-fork met een eigen model van divisies en bevoegdheden. Kleine verspreiding, antwoordt nog steeds.
codebase: CobraMUSH
home: https://cobramush.org/
see-also: codebases/pennmush
see-also: codebases/rhostmush
---

CobraMUSH is van PennMUSH afgesplitst en voegde een *divisie*model toe — een hiërarchie van
administratief gezag met delegeerbare bevoegdheden, in plaats van het vlakke onderscheid tussen
wizard en royalty dat zijn ouder hanteert. Spellen die stukjes stafbevoegdheid willen uitdelen
zonder alles uit te delen, zijn zijn publiek.

Softcode die voor PennMUSH geschreven is draait grotendeels, en de verschillen zitten geconcentreerd
in precies het gebied waar de fork om begonnen was.

## Hoe het er van buitenaf uitziet

Geen MSSP, een werkende `WHO` vóór het inloggen, en op het spel dat we gemeten hebben werden er
helemaal geen telnet-opties onderhandeld. Dat laatste is geen kritiek: een server die nergens over
onderhandelt is een server die onderhandeling niet fout kan doen, en platte tekst over een platte
socket is het ene ding dat elke client in deze hobby aankan.
