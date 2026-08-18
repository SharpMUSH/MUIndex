---
kind: protocol
slug: ttype
title: TTYPE und MTTS
summary: Wie ein Client einem Server sagt, was er ist und was er kann — auch, sofern der Client sich dafür entscheidet, dass ein Screenreader im Einsatz ist.
protocol: TTYPE
home: https://www.mudhalla.net/tintin/protocols/mtts/
see-also: protocols/charset
see-also: clients/tintin
see-also: clients/blightmud
---

TTYPE ist Telnet-Option 24, aus RFC 1091: Der Server fragt den Client, was für ein Terminal er ist,
und der Client antwortet. Historisch lautete die Antwort `VT100` oder `ANSI`.

**MTTS** — der Mud Terminal Type Standard — legt darüber eine Konvention. Ein Client antwortet
dreimal: mit seinem Namen, mit seinem Terminaltyp und dann mit `MTTS <bitmask>`, wobei die Bits
Fähigkeiten angeben. 256 Farben, True Color, UTF-8, MNES, MSP über den Out-of-Band-Kanal — und,
bemerkenswert, **`MTTS_SCREEN_READER`**.

## Das Screenreader-Bit

Bei dem letzten lohnt es sich innezuhalten, denn es ist die einzige Stelle im Protokollstapel dieses
Hobbys, an der Barrierefreiheit ein Konzept erster Klasse ist.

Ein Client, der es setzt, teilt dem Server mit, dass ein Screenreader im Einsatz ist, und ein Server,
der das bemerkt, kann sich anpassen: ASCII-Grafik unterdrücken, den schmückenden Rahmen aus
Linienzeichen um eine Raumbeschreibung weglassen, eine Tabelle anders anordnen. Sowohl
[TinTin++](/reference/clients/tintin) als auch [Blightmud](/reference/clients/blightmud) geben es an,
und [Mudlet](/reference/clients/mudlet) hat eine Einstellung dafür.

Ob ein bestimmtes Spiel darauf reagiert, ist eine andere Frage, und keine, die diese Website messen
kann — wir können einen Server nicht fragen, was er anders machen würde.

## Was ein Crawler hier schuldet

Ein Crawler weist sich über TTYPE aus, und das soll er auch. Unserer tut es, mit einer URL zur
Information, damit ein Administrator beim Lesen seiner Logs herausfinden kann, wer sich mit seinem
Spiel verbunden hat und wie er uns bitten kann aufzuhören. Ein Crawler, der `ANSI` antwortet und
sonst nichts, ist von Haus aus anonym, und dafür gibt es keinen guten Grund.

## Was wir messen

Server, die TTYPE mit uns ausgehandelt haben. Beachten Sie, dass dies eine der wenigen Optionen ist,
bei denen *wir* die gefragte Seite sind; ein Wert hier ist also eine Zählung der Server, die
überhaupt gefragt haben.
