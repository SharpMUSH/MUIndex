---
kind: codebase
slug: rom
title: ROM
summary: Der bekannteste Nachfahre von Merc und die Kampf-Engine, auf der ein großer Teil der Neunziger-MUDs aufgebaut wurde.
codebase: ROM
see-also: codebases/dikumud
see-also: codebases/smaug
see-also: protocols/mccp
---

ROM — *Rivers of MUD* — ist eine Ableitung von **Merc**, das selbst eine DikuMUD-Ableitung ist, und
es ist diejenige, die sich durchgesetzt hat. Sein Kampfmodell, sein Fertigkeits- und Zaubersystem
und sein Area-Format waren der Ausgangspunkt für eine enorme Zahl von Spielen in den Neunzigern und
danach, und besonders ROM 2.4 ist einer der meistgeforkten Quelltexte im Hobby.

Wie der Rest der Diku-Linie trägt es die ursprüngliche Credits-Auflage mit sich, und so nennt ein
Spiel, dessen Abstammung sich sonst nicht ermitteln lässt, auf seinem Anmeldebildschirm oft Diku,
Merc und ROM.

## Wie es von außen aussieht

MSSP, CHARSET und **MCCP2**, auf dem Spiel, das wir gemessen haben.

ROM ist der Server, an dem dieses Projekt seinen eigenen Kompressionsfehler nachgewiesen hat. Unsere
Abfrage handelte MCCP2 aus, der Server begann korrekt zu komprimieren, und die Telnet-Bibliothek,
auf die wir angewiesen sind, hat den Strom nie entpackt — der Verbindungsbildschirm kam also als
Wand aus Ersatzzeichen an, und wir haben das kurzzeitig als Schuld des Spiels verbucht. Die Nutzlast
ließ sich mit einem gewöhnlichen zlib-Aufruf sauber dekomprimieren, und das machte die Sache
eindeutig. Es wurde upstream behoben; die Geschichte steht auf der Seite
[MCCP](/reference/protocols/mccp), weil sie ein gutes Beispiel für einen Defekt ist, der von außen
genau wie ein kaputtes Spiel aussieht.
