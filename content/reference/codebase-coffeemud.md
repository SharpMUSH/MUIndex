---
kind: codebase
slug: coffeemud
title: CoffeeMUD
summary: A MUD server in Java, with the largest MSSP report of anything we have probed and an unusually broad protocol surface.
codebase: CoffeeMUD
home: https://www.coffeemud.net/
see-also: codebases/dikumud
see-also: protocols/mssp
---

CoffeeMUD is a Java MUD server with an unusually wide feature surface — it ships with its own web
server, mail, forums and a large class and skill system, and it is one of the few servers in the
hobby not written in C.

It is actively maintained, which by the standards of this part of the catalogue is worth saying out
loud.

## What it looks like from outside

MSSP and **MCCP2**, and CoffeeMUD is one of only three servers out of twenty we tried that also
answered the *plaintext* `MSSP-REQUEST` form — a variant that predates the telnet option and is
still occasionally seen.

Its MSSP report is the largest we have measured: **47 fields**, including `PORT` reported nine
separate times for nine separate ports. That is not a malformation. MSSP variables are lists, and a
crawler that flattens a multi-valued `PORT` into one string produces the integer `80234201` out of
`"80" "23" "4201"` — which is a bug this project shipped and fixed, and the reason the parser here
keeps values as lists throughout.
