---
kind: codebase
slug: rhostmush
title: RhostMUSH
summary: Ein MUSH-Server, bekannt für ein tiefes Rechtemodell und einen großen Satz eingebauter Funktionen. Kein MSSP; beantwortet ein WHO vor der Anmeldung.
codebase: RhostMUSH
home: https://github.com/RhostMUSH/trunk
see-also: codebases/pennmush
see-also: codebases/tinymux
see-also: codebases/cobramush
---

RhostMUSH ist der vierte der weit verbreiteten, von TinyMUSH abstammenden Server und der mit dem
ausgefeiltesten Verwaltungsmodell: Sein Rechte- und Flag-System ist erheblich feinkörniger als das
seiner Verwandten, und das ist der übliche Grund, aus dem ein Spiel sich für ihn entscheidet.

Seine Bibliothek eingebauter Funktionen ist groß, und für Rhost geschriebener Softcode lässt sich
oft nicht sauber nach PennMUSH oder TinyMUX übertragen, ohne die Stellen umzuschreiben, die
Funktionen benutzt haben, die es dort nicht gibt.

## Wie es von außen aussieht

Kein MSSP. Ein `WHO` vor der Anmeldung, das mit einer Zählung antwortet. CHARSET wird ausgehandelt.

Diese Kombination — kein MSSP, ein funktionierendes `WHO` — ist die Signatur der MUSH-Familie, und
sie ist der Grund, warum diese Website den Anmeldebildschirm überhaupt abfragt. Nach dem Befund
unserer eigenen Erhebung sind die MSSP- und die `WHO`-Familie nahezu disjunkt: 28 Codebases
veröffentlichen eine Zählung über MSSP, sieben über `WHO` und nur zwei über beides.
