---
kind: protocol
slug: charset
title: CHARSET
summary: De telnet-optie uit RFC 2066 om een codering af te spreken. De reden dat namen met accenten in een spel de reis overleven, en de bron van een paar subtiele fouten wanneer hij ontbreekt.
protocol: CHARSET
home: https://www.rfc-editor.org/rfc/rfc2066
see-also: protocols/ttype
see-also: connecting
see-also: codebases/tinymux
---

CHARSET is telnet-optie 42, gespecificeerd in RFC 2066. De ene kant biedt een lijst met tekensets
aan, de andere kiest er een, en daarna zijn beide het eens over hoe bytes op tekens worden afgebeeld.

In de praktijk komt de onderhandeling uit op **UTF-8** of vindt ze helemaal niet plaats. De
MUSH-familie onderhandelt er merkbaar vaker over dan de MUD-familie — TinyMUX, RhostMUSH en PennMUSH
doen het alle drie — en dat past bij een bevolking die proza schrijft met namen erin.

## Wat er gebeurt zonder

Een client moet raden, en de gebruikelijke gok is ASCII of Latin-1. Gok ASCII en elke byte boven 0x7F
wordt een vraagteken; gok Latin-1 op een UTF-8-server en elk letterteken met een accent wordt twee
leestekens. Beide fouten zien eruit als de schuld van het spel en zijn dat niet.

Voor een crawler bijt dit op één bepaalde plek. Onze eigen telnet-bibliotheek zet haar huidige
codering standaard op ASCII, en die standaardwaarde is niet onschuldig — het is waarmee elke byte
gedecodeerd wordt, voor elke server die nooit over CHARSET onderhandelt, en dat zijn de meeste.
Daarom stellen we hem bewust zelf in.

## De ene plek waar CHARSET niet komt

MSSP-veldnamen en -waarden worden als ASCII gedecodeerd, ongeacht waar CHARSET op uitkwam, want een
subonderhandeling is een commando en geen tekst, en de specificatie beperkt CHARSET tot tekst. Dat is
verdedigbaar conform en het is verliesgevend: een spel waarvan de MSSP-`NAME` `Café Noir` is, meldt
`Caf? Noir`, en de oorspronkelijke bytes zijn weg voordat iets wat wij in de hand hebben ze ziet.

Zie je op deze site een verminkt teken in een opgegeven veld en niet in de uitvoer van het spel zelf,
dan is dat de reden, en van onze kant valt het niet meer te herstellen.
