---
kind: orientation
slug: connecting
title: Wie man sich verbindet
summary: Ein Host, ein Port und Telnet. Was die Adresse auf einer Spielseite bedeutet und was man mit ihr macht.
see-also: mush-mud-muck-moo
see-also: protocols/tls
see-also: protocols/charset
---

Jedes hier verzeichnete Spiel antwortet auf einem **Host und einem Port**, und das Protokoll darunter
ist Telnet — was in der Praxis eine rohe TCP-Verbindung mit ein wenig optionaler Aushandlung
obendrauf bedeutet.

    telnet mush.pennmush.org 4201

Das funktioniert, und auf vielen Systemen ist es schon installiert. Es ist auch eine schlechte Art zu
spielen: Das `telnet` des Systems hat keine nennenswerte Steuerung des lokalen Echos, kein
Protokoll, keine Historie, und es verstümmelt alles oberhalb von ASCII. Es ist das richtige Werkzeug,
um zu prüfen, ob ein Spiel antwortet, und das falsche, um einen Abend darin zu verbringen.

## Was Ihnen die Adresse auf einer Spielseite sagt

Jede Spielseite führt die Endpunkte auf, die wir gemessen haben, und kennzeichnet jeden, bei dem
**TLS** beobachtet wurde. Ein Spiel mit einem TLS-Port ist ein Spiel, zu dem Sie verschlüsselt
verbinden können; die Portnummer ist meist eine andere als die des einfachen Ports.

Wo ein Spiel mehrere Ports hat, sind das häufig verschiedene Wege zu derselben Welt und nicht
verschiedene Spiele. Wir führen auf, was wir gemessen haben, und raten nicht, welcher der maßgebliche
ist.

## Einen Client wählen

Der Abschnitt [Clients](/reference) hat für jeden eine Seite, mit einer Fähigkeitstabelle. Die drei
Dinge, die zu prüfen sich lohnt, bevor Sie irgendetwas installieren:

- **Kann er UTF-8?** Wenn das Spiel nicht nur englisch ist, kommt das schon am ersten Abend auf.
- **Kann er TLS?** Zählt nur, wenn das Spiel es anbietet, aber mehrere tun das inzwischen.
- **Wenn Sie einen Screenreader benutzen: Dokumentiert das Projekt Unterstützung dafür?** Das ist die
  Zeile, die in Client-Vergleichen am häufigsten fehlt, deshalb ist sie bei uns die erste — und wo
  niemand eine Antwort festgestellt hat, steht dort *unbekannt*.

## Wenn nichts antwortet

Ein Spiel, das nicht antwortet, ist nicht zwangsläufig verschwunden. Spiele ziehen auf andere Hosts
um, DNS-Einträge laufen aus, und Firewalls haben ihre Meinungen. Diese Website behält jedes Spiel,
das sie je gemessen hat — auch die, die vor Jahren aufgehört haben zu antworten —, und klopft weiter
wöchentlich an; das [Archiv](/archive) ist also der Ort, an dem man nachsieht, bevor man irgendetwas
schlussfolgert.
