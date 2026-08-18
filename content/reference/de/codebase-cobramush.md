---
kind: codebase
slug: cobramush
title: CobraMUSH
summary: Ein PennMUSH-Fork mit eigenem Divisions- und Befugnismodell. Geringe Verbreitung, antwortet weiterhin.
codebase: CobraMUSH
home: https://cobramush.org/
see-also: codebases/pennmush
see-also: codebases/rhostmush
---

CobraMUSH ist ein Fork von PennMUSH und hat ein *Division*-Modell hinzugefügt — eine Hierarchie
administrativer Autorität mit delegierbaren Befugnissen anstelle der flachen Unterscheidung zwischen
wizard und royalty, die das Elternprojekt verwendet. Seine Klientel sind Spiele, die Teile der
Leitungsbefugnis abgeben wollen, ohne alles abzugeben.

Für PennMUSH geschriebener Softcode läuft größtenteils, und die Unterschiede ballen sich genau in
dem Bereich, um den es beim Fork ging.

## Wie es von außen aussieht

Kein MSSP, ein funktionierendes `WHO` vor der Anmeldung und überhaupt keine ausgehandelten
Telnet-Optionen auf dem Spiel, das wir gemessen haben. Der letzte Punkt ist kein Vorwurf: Ein
Server, der nichts aushandelt, ist ein Server, der beim Aushandeln nichts falsch machen kann, und
einfacher Text über einen einfachen Socket ist genau das, womit jeder Client in diesem Hobby
umgeht.
