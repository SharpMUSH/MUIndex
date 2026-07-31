---
kind: codebase
slug: cobramush
title: CobraMUSH
summary: A PennMUSH fork with its own division and power model. Small deployment, still answering.
codebase: CobraMUSH
home: https://cobramush.org/
see-also: codebases/pennmush
see-also: codebases/rhostmush
---

CobraMUSH forked from PennMUSH and added a *division* model — a hierarchy of administrative
authority with delegable powers, in place of the flat wizard/royalty distinction its parent uses.
Games that want to hand out slices of staff authority without handing out everything are its
constituency.

Softcode written for PennMUSH mostly runs, and the differences concentrate in exactly the area the
fork was about.

## What it looks like from outside

No MSSP, a working pre-login `WHO`, and no telnet options negotiated at all on the game we
measured. That last part is not a criticism: a server that negotiates nothing is a server that
cannot get negotiation wrong, and plain text over a plain socket is the thing every client in this
hobby handles.
