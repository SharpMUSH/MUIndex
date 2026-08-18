---
kind: codebase
slug: tinymush
title: TinyMUSH
summary: De voorouder van de MUSH-lijn, met nog steeds draaiende spellen. Het leerde deze crawler dat zijn eigen onderhandelingsbytes het volgende commando kunnen breken dat hij stuurt.
codebase: TinyMUSH
home: https://github.com/TinyMUSH/TinyMUSH
see-also: codebases/pennmush
see-also: codebases/tinymux
see-also: mush-mud-muck-moo
---

TinyMUSH is waar de lijn van PennMUSH, TinyMUX, RhostMUSH en CobraMUSH allemaal van afstamt, en het
is nog steeds in gebruik. De ontwikkeling is stil, niet afwezig.

## Hoe het er van buitenaf uitziet

Geen MSSP. Een `WHO` vóór het inloggen die antwoordt met een zin in de vorm
`0 Players logged in, 22 record, no maximum.`

## De bug die het in ons vond

TinyMUSH is hier een alinea waard omdat het het spel is dat een gebrek in de eigen crawler van deze
site blootlegde, en de correctie is een goede illustratie van wat "gemeten" hoort te betekenen.

Onze peiling las TinyMUSH wekenlang als *telling onbekend*. De gok die op papier stond was dat zijn
antwoord geen afsluitend regeleinde had. Dat heeft het wel. Van de lijn geplukt bleek de echte
oorzaak bij ons te liggen: **TinyMUSH ontleedt geen telnet op zijn inlogscherm**, dus de drie bytes
`IAC DO MSSP` die wij bij het verbinden sturen belanden in zijn invoerbuffer alsof iemand ze getypt
had. De volgende regel die het leest is niet `WHO` maar drie stuurbytes gevolgd door `WHO`, en dat
is geen commando dat het kent — dus toont het zijn verbindingsscherm opnieuw en zegt het niets over
spelers.

De peiling stuurt nu een kaal regeleinde na het onderhandelen en gooit weg wat dat oplevert, want
die uitvoer is een reactie op bytes die *wij* verkozen te sturen en is daarom noch het
verbindingsscherm van het spel, noch zijn antwoord. TinyMUSH leest nu correct, en de peiling was in
een derde van de tijd klaar.

Een gids die het niet nagekeken had, zou "dit spel meldt zijn spelers niet" gepubliceerd hebben
zolang het bestond, en die zin zou over ons gegaan zijn.
