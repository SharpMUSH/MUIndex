---
kind: orientation
slug: mush-mud-muck-moo
title: MUSH, MUD, MUCK, MOO — was die Wörter bedeuten
summary: Vier Wörter für vier Traditionen, von denen keine ein Genre ist. Was sie Ihnen tatsächlich sagen.
see-also: collaborative-roleplay
see-also: connecting
see-also: codebases/pennmush
see-also: codebases/aresmush
see-also: codebases/muck
see-also: codebases/moo
see-also: codebases/evennia
---

Jedes dieser Wörter benennt eine **Familie von Serversoftware**, keine Art von Spiel. Das ist das mit
Abstand Nützlichste, was man über sie wissen kann, und deshalb wird „Ist das ein MUSH oder ein MUD?“
so oft schlecht beantwortet: Die ehrliche Antwort lautet meist *beides, und die Frage, die Sie
meinten, ging um die Kultur*.

## MUD

Der älteste Begriff und heute der weiteste. Er begann als *Multi-User Dungeon* — Bartles und
Trubshaws Spiel von 1978 — und war Mitte der Neunziger das Sammelwort für jede textbasierte
Mehrspielerwelt.

Eng gebraucht meint er die **DikuMUD- und LPMud-Linien**: Server, die um Stufen, Kampf, Ausrüstung
und eine Gebietsdatei herum gebaut sind, in der ein Erbauer die Räume im Voraus beschrieben hat. Wenn
jemand sagt „Ich spiele ein MUD“ und etwas Bestimmtes meint, ist es meist das.

Das Verzeichnis kann Ihnen jede Linie für sich zeigen: [die DikuMUD-Spiele](/games?lineage=DikuMUD)
und [die LPMud-Spiele](/games?lineage=LPMud).

## MUSH

*Multi-User Shared Hallucination*, aus der TinyMUD-Linie. Die bestimmende Eigenschaft ist nicht das
Thema, sondern der **Softcode**: MUSH-Server bringen eine Programmiersprache mit, die Spieler von
innerhalb des Spiels benutzen; ein Spieler mit Baurechten legt damit Räume, Objekte und Verhalten an,
ohne eine Quelldatei anzufassen oder irgendetwas neu zu starten.

Diese eine Entwurfsentscheidung hat die Kultur hervorgebracht. MUSHes haben tendenziell wenig
automatisierte Systeme und viele menschliche — von der Spielleitung geführte Handlungsbögen,
geschriebene Szenen, Bewerbungsverfahren —, weil die Leute, die spielen, auch die Leute sind, die
bauen.

PennMUSH, TinyMUSH, TinyMUX, RhostMUSH, CobraMUSH und AresMUSH stehen alle in dieser Linie, und
keines von ihnen sagt das: MSSP kennt keinen Wert `MUSH`, den man veröffentlichen könnte, und außer
PennMUSH veröffentlicht keines überhaupt MSSP. Sie zu gruppieren ist daher etwas, das wir tun, und
nicht etwas, das wir lesen — weshalb [die MUSH-Spiele](/games?lineage=MUSH) überall, wo sie
auftauchen, als *abgeleitet* gekennzeichnet sind.

## MUCK

Ein TinyMUD-Abkömmling wie MUSH, mit eigenem Softcode (MUF, einer Forth-artigen Sprache) und einer
starken Tradition sozialer Welten und der Furry-Fandom-Welten. Technisch nah an MUSH; kulturell so
eigen, dass Leute, die beides spielen, sie nicht als dasselbe beschreiben würden —
[die MUCK-Spiele](/games?lineage=MUCK).

## MOO

*MUD, Object-Oriented*. Der reinste Ausdruck der Idee „das Spiel bearbeitet sich selbst“: Nahezu
alles in einem MOO ist in der Programmiersprache MOO geschrieben, von den Leuten, die es benutzen,
von innen heraus. LambdaMOO ist der Vorfahr, und MOOs waren historisch in Bildung und Forschung
ebenso beliebt wie im Spiel. [Die MOO-Spiele](/games?lineage=MOO).

## Was sollten Sie also tatsächlich fragen?

Drei Fragen leisten mehr als das Wort aus vier Buchstaben:

1. **Gibt es Kampf, und ist er automatisiert?** Das trennt die Diku/LP-Linie verlässlicher von der
   TinyMUD-Linie als jeder Name.
2. **Wer baut?** Nur die Spielleitung, oder jeder mit einem Bau-Bit?
3. **Ist das Spiel verabredet oder beiläufig?** Szenen nach Termin und gepostes Rollenspiel, oder
   einloggen und los?

Das Verzeichnis auf dieser Website kann Ihnen einen Teil der ersten Frage beantworten: Die
**Codebase**, die wir für ein Spiel gemessen haben, sagt Ihnen, aus welcher Tradition sein Server
kommt, und die Facette **Abstammung** ist dieselbe Antwort, filterbar gemacht. Die Kultur kann sie
Ihnen nicht sagen, und diese Seite wird nicht so tun als ob.

Eine Warnung zu dieser Facette, denn auf dieser Seite wird jemand ihr zum ersten Mal begegnen. Die
Codebase ist gemessen und die Abstammung nicht: Sie ist *unsere* Gruppierung dessen, was ein Spiel
uns mitgeteilt hat, geführt unter einer eigenen Kennzeichnung — **abgeleitet** — neben *gemessen* und
*angegeben*. Wo eine Codebase keinen unumstrittenen Vorfahren hat, wird sie aus jeder Abstammung
herausgelassen statt unter der nächstbesten einsortiert, und mehrere dieser Spiele stimmen uns mit
eigenen Worten zu und veröffentlichen `FAMILY Custom`.
