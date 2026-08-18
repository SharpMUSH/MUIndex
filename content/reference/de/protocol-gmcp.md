---
kind: protocol
slug: gmcp
title: GMCP
summary: Das Generic Mud Communication Protocol — strukturierte JSON-Nachrichten neben dem Text und der Out-of-Band-Kanal, gegen den die meisten modernen Clients entwickeln.
protocol: GMCP
home: https://www.mudhalla.net/tintin/protocols/gmcp/
see-also: protocols/msdp
see-also: protocols/atcp
see-also: clients/mudlet
---

GMCP ist Telnet-Option 201. Ist sie einmal ausgehandelt, kann der Server **strukturierte Daten out of
band** senden: einen Paketnamen und eine JSON-Nutzlast, die im selben Strom ankommen wie der Text,
aber nicht Teil von ihm sind.

`Char.Vitals { "hp": 412, "maxhp": 500 }` ist das kanonische Beispiel. Ein Client kann daraus eine
Lebensanzeige speisen, ohne die Prosa nach Zahlen abzugrasen, und genau darum geht es — eine
Statusanzeige, die auf Mustererkennung im Text beruht, geht an dem Tag kaputt, an dem ein Spiel
seinen Prompt ändert, und eine auf GMCP gebaute nicht.

Der Namensraum der Pakete beruht auf Konvention und nicht auf einem Standard. `Char`, `Room`, `Comm`
und `Client` sind weit verbreitet; darüber hinaus erfinden Spiele, was sie brauchen, und einem Client
muss in der Regel gesagt werden, was ein bestimmtes Spiel sendet.

## Warum es ATCP verdrängt hat

GMCP ist der Nachfolger von [ATCP](/reference/protocols/atcp), das dieselbe Aufgabe mit einem
lockereren Nutzlastformat erledigte. JSON war die Verbesserung, und Mitte der 2010er-Jahre war der
Umstieg weitgehend vollzogen. Ein Spiel, das beides unterstützt, ist nichts Ungewöhnliches; ein neues
Spiel, das nur ATCP unterstützt, wäre es.

## Was wir messen

Ein Spiel zählt hier, wenn **sein Server GMCP in einem von uns beobachteten Handshake angeboten
hat**. Das ist eine andere Behauptung als ein MSSP eines Spiels, das `GMCP 1` sagt — worauf die
meisten Protokolltabellen in diesem Hobby aufgebaut sind —, und die beiden widersprechen sich
regelmäßig.

Eine Anmerkung zur Messung aus unserer eigenen Geschichte: Eine Zeit lang konnten wir GMCP auf
Servern nicht sehen, die auch [MCCP](/reference/protocols/mccp) aushandelten, denn unsere
Telnet-Bibliothek handelte die Kompression aus, ohne den Strom zu dekomprimieren, und alles nach der
Kompressionsmarke war für uns Rauschen. Von mindestens einem Server in unserer Erhebung stellte sich
heraus, dass er die ganze Zeit GMCP sprach. Wenn ein Wert auf dieser Seite für eine Familie niedrig
aussieht, die Sie gut kennen, ist diese Art von Defekt das Erste, was zu vermuten ist — bei uns,
nicht bei ihnen.
