---
kind: protocol
slug: mxp
title: MXP
summary: Das MUD eXtension Protocol — HTML-ähnliche Auszeichnung im Textstrom, die anklickbare Links, Bilder und Formulare möglich macht. Umfassend spezifiziert, ungleichmäßig implementiert.
protocol: MXP
home: https://www.zuggsoft.com/zmud/mxp.htm
see-also: protocols/pueblo
see-also: clients/mushclient
see-also: clients/mudlet
---

MXP bettet eine kleine, HTML-ähnliche Auszeichnungssprache in den Text ein, den ein Server sendet:
`<send>` für einen anklickbaren Befehl, `<a href>` für einen Link, Elemente für Farbe und Schrift und
einen Mechanismus, mit dem ein Server eigene Tags definieren kann. Es wird über Telnet-Option 91
ausgehandelt.

Sein Entwurfsproblem steckt in ihm selbst und ist interessant: Die Auszeichnung reist im selben Strom
wie der Text, ein Server muss also auf Text achtgeben, der wie Auszeichnung *aussieht*, und ein
Client muss achtgeben, was er darstellt. Genau deshalb definiert MXP Sicherheitsstufen — ein Tag, das
in einer Chatzeile von einem anderen Spieler ankommt, ist nicht dasselbe wie ein Tag, das der Server
selbst ausgegeben hat.

## Anklickbarkeit ist der Grund, warum man es haben will

Das meiste, wofür MXP tatsächlich benutzt wird, ist, `north` und Gegenstandsnamen in etwas zu
verwandeln, das man anklicken kann. Für einen neuen Spieler ist das ein erheblicher Unterschied, und
deshalb wird das Protokoll trotz seiner Komplexität immer wieder implementiert.

## Pueblo ist das andere

[Pueblo](/reference/protocols/pueblo) ist älter als MXP und erledigt eine ähnliche Aufgabe mit einem
anderen, buchstäblicher an HTML angelehnten Ansatz. Ein Client, der das eine unterstützt, unterstützt
häufig das andere nicht, und beim Lesen einer Funktionsliste sind die beiden leicht zu verwechseln —
ein Fehler, vor dem wir uns in den Client-Tabellen dieses Abschnitts hüten mussten.

## Was wir messen

Server, die Telnet-Option 91 in einem von uns beobachteten Handshake angeboten haben. MXP wird
seltener ausgehandelt als die Out-of-Band-Protokolle, zum Teil deshalb, weil ein großer Teil seines
Nutzens von Servern eingelöst wird, die die Auszeichnung einfach senden und hoffen, ohne überhaupt
auszuhandeln — und das können wir nicht sehen.
