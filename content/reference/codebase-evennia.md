---
kind: codebase
slug: evennia
title: Evennia
summary: A Python framework rather than a finished game. Two Evennia games can have nothing in common but the plumbing.
codebase: Evennia
home: https://www.evennia.com/
see-also: codebases/aresmush
see-also: collaborative-roleplay
see-also: protocols/gmcp
---

Evennia is a **MU\* framework**, not a game — which is the first thing to know about it and the
thing that makes comparing Evennia games to each other unhelpful. It is a Python library built on
Django and Twisted that gives you accounts, objects, rooms, commands, a persistence layer and the
network stack, and then expects you to write the game.

The consequence is that "runs Evennia" tells you far less about a game than "runs PennMUSH" does.
There are combat MUDs on Evennia and there are roleplay games on Evennia and they share no
vocabulary. Two Evennia games may not have a single command in common.

For a developer who already knows Python this is the shortest path from nothing to a running world,
and it is where a good share of new games since the mid-2010s have started.

## What it looks like from outside

Evennia offers **MSSP**, and it publishes a player count through it. On the game we measured it also
negotiated **MCCP2** — compression — which is characteristic of a stack that took its telnet
seriously.

Because Evennia is a framework, what a given game negotiates is partly the game's decision. The
adoption figures on the protocol pages are counts of what servers actually offered us, not of what
the framework can do, and for Evennia those two are further apart than for most.
