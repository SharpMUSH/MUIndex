---
kind: orientation
slug: mush-mud-muck-moo
title: MUSH, MUD, MUCK, MOO — what the words mean
summary: Four words for four traditions, none of which is a genre. What they actually tell you.
see-also: collaborative-roleplay
see-also: connecting
see-also: codebases/pennmush
see-also: codebases/evennia
---

Every one of these words names a **family of server software**, not a kind of game. That is the
single most useful thing to know about them, and it is why "is this a MUSH or a MUD?" is so often
answered badly: the honest answer is usually *both, and the question you meant was about the
culture*.

## MUD

The oldest term, and now the broadest. It began as *Multi-User Dungeon* — Bartle and Trubshaw's 1978
game — and by the mid-nineties was the umbrella word for every text-based multiplayer world.

Used narrowly, it means the **DikuMUD and LPMud lines**: servers built around levels, combat,
equipment and an area file describing rooms a builder wrote in advance. If somebody says "I play a
MUD" and means something specific, this is usually it.

## MUSH

*Multi-User Shared Hallucination*, from the TinyMUD line. The defining property is not the theme but
the **softcode**: MUSH servers ship a programming language players use from inside the game, so a
player with build permissions creates rooms, objects and behaviour without touching a source file or
restarting anything.

That one design decision produced the culture. MUSHes tend to be sparse on automated systems and
dense on human ones — staff-run plots, written scenes, application processes — because the people
playing are also the people building.

## MUCK

A TinyMUD descendant like MUSH, with its own softcode (MUF, a Forth-like language) and a strong
tradition of social and furry-fandom worlds. Technically close to MUSH; culturally distinct enough
that people who play both would not describe them as the same thing.

## MOO

*MUD, Object-Oriented*. The purest expression of the "the game edits itself" idea: nearly everything
in a MOO is written in the MOO programming language by the people using it, from inside. LambdaMOO
is the ancestor, and MOOs have historically been as popular in education and research as in games.

## So what should you actually ask?

Three questions do more work than the four-letter word:

1. **Is there combat, and is it automated?** This separates the Diku/LP line from the TinyMUD line
   more reliably than any name.
2. **Who builds?** Staff-only, or anyone with a build bit?
3. **Is play scheduled or ambient?** Appointment-based scenes and posed roleplay, or log in and go?

The listing on this site can answer part of the first question for you: the **codebase** we measured
for a game tells you which tradition its server comes from. It cannot tell you the culture, and this
page will not pretend otherwise.
