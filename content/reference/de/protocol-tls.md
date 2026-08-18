---
kind: protocol
slug: tls
title: TLS
summary: Verschlüsselte Verbindungen. Meist ein eigener Port statt einer ausgehandelten Umstellung, und die eine Fähigkeit auf dieser Website, die wir durch Verbinden prüfen und nicht durch Fragen.
protocol: TLS
see-also: connecting
see-also: protocols/charset
see-also: clients/potato
---

Telnet ist Klartext. Alles, was Sie an ein MU\* senden — auch Ihr Passwort —, quert das Netz lesbar
für alles auf dem Weg, sofern das Spiel nicht TLS anbietet.

In diesem Hobby heißt TLS fast immer **ein zweiter Port, der vom ersten Byte an TLS spricht**, und
nicht eine Umstellung im laufenden Strom. Ein Spiel mit einem einfachen Port auf 4201 und einem
TLS-Port auf 4202 ist die übliche Form. Es gibt eine ausgehandelte Variante, und sie ist selten
genug, dass die Dokumentation mindestens eines Clients ausdrücklich sagt, sie werde nicht
unterstützt.

## Warum die Spielseiten das besonders kennzeichnen

TLS ist die eine Fähigkeit auf dieser Website, die dadurch feststeht, dass wir es *getan* haben: Ein
Endpunkt ist als TLS gekennzeichnet, weil wir gegen ihn einen TLS-Handshake abgeschlossen haben. Da
wird nichts gefragt, und es gibt kein Feld, in dem sich etwas angeben ließe — das macht es zur
saubersten Messung im Katalog.

Das ist auch der Grund, warum der TLS-Port eines Spiels und sein einfacher Port als getrennte
Endpunkte geführt und nicht zusammengelegt werden. Es sind verschiedene Messungen verschiedener
Dinge.

## Praktischer Rat

Wenn ein Spiel, das Sie spielen, einen TLS-Port anbietet, benutzen Sie ihn. Wenn nicht und es Ihnen
wichtig ist, fragen Sie nach — für einen Administrator ist es wenig Arbeit, und dass es ihn nicht
überall gibt, liegt meist daran, dass niemand gefragt hat, und nicht daran, dass jemand etwas dagegen
hätte.

Prüfen Sie, ob Ihr Client es unterstützt, bevor Sie sich darauf verlassen. Mehrere im Abschnitt
[Clients](/reference) tun es; mindestens einer dokumentiert stattdessen einen Behelf mit einem
externen `stunnel`-Prozess, der funktioniert und mehr Einrichtung ist, als die meisten Leute auf sich
nehmen.
