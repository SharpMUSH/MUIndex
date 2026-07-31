---
kind: codebase
slug: muck
title: MUCK
summary: A TinyMUD descendant with its own Forth-like in-game language, and a social culture distinct from the MUSH side.
codebase: MUCK
home: https://www.fuzzball.org/
see-also: mush-mud-muck-moo
see-also: codebases/tinymush
see-also: codebases/moo
---

MUCK — in practice almost always **Fuzzball MUCK** — is a sibling of the MUSH line rather than a
descendant of it: both come from TinyMUD, and both put a programming language inside the game.

The language is the visible difference. MUF (*Multi-User Forth*) is stack-based and reads nothing
like MUSH softcode; a builder fluent in one is a beginner in the other. Above it sits MPI, a smaller
inline expression language used for the things softcode would do on a MUSH.

Culturally, MUCK is the home of a large part of the hobby's social and fandom worlds. Those games
tend to be built around presence and conversation rather than around scenes with a start and an end,
which is a real difference from the roleplay MUSH tradition and not a matter of theme.

## What it looks like from outside

No MSSP. A pre-login `WHO` that answers with a count. No telnet options negotiated on the game we
measured — and one detail from the survey worth keeping: its `WHO` reply ended in a trailing space
with no newline, which is the kind of thing that makes a naive parser report nothing at all.
