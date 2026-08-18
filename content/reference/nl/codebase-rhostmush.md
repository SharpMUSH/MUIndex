---
kind: codebase
slug: rhostmush
title: RhostMUSH
summary: Een MUSH-server die bekendstaat om een diep rechtenmodel en een grote ingebouwde functieverzameling. Geen MSSP; beantwoordt een WHO vóór het inloggen.
codebase: RhostMUSH
home: https://github.com/RhostMUSH/trunk
see-also: codebases/pennmush
see-also: codebases/tinymux
see-also: codebases/cobramush
---

RhostMUSH is de vierde van de veelgebruikte servers die van TinyMUSH afstammen, en degene met het
meest uitgewerkte administratieve model: zijn rechten- en vlaggensysteem is aanzienlijk fijnmaziger
dan dat van zijn verwanten, en dat is de gebruikelijke reden dat een spel ervoor kiest.

Zijn ingebouwde functiebibliotheek is groot, en softcode die voor Rhost geschreven is gaat vaak niet
schoon over naar PennMUSH of TinyMUX zonder dat de delen herschreven worden die functies gebruikten
die de andere niet hebben.

## Hoe het er van buitenaf uitziet

Geen MSSP. Een `WHO` vóór het inloggen die met een telling antwoordt. Er wordt over CHARSET
onderhandeld.

Die combinatie — geen MSSP, een werkende `WHO` — is het handschrift van de MUSH-familie, en het is
de reden dat deze site het inlogscherm überhaupt peilt. Op grond van ons eigen onderzoek zijn de
MSSP- en de `WHO`-familie vrijwel disjunct: 28 codebases publiceren een telling via MSSP, zeven via
`WHO`, en slechts twee via beide.
