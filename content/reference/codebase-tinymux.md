---
kind: codebase
slug: tinymux
title: TinyMUX
summary: The other big MUSH server. Softcode close enough to PennMUSH's to argue about, no MSSP at all, and a pre-login WHO that works.
codebase: TinyMUX
home: https://www.tinymux.org/
see-also: codebases/pennmush
see-also: codebases/tinymush
see-also: codebases/rhostmush
see-also: mush-mud-muck-moo
---

TinyMUX is the second of the two servers most established roleplay MUSHes run, and for many
players the choice between it and PennMUSH is a matter of which one their game's staff learned
first. Versions read as `2.12` and similar.

Like PennMUSH it descends from TinyMUSH, and its softcode is close enough that a builder moving
between the two is translating rather than relearning. The differences are real — function
libraries, some parsing corners, the `@`-command set — and are exactly the kind of thing that makes
moving a database between them a project rather than an export.

## What it looks like from outside

**No MSSP.** TinyMUX offers the option not at all, which puts it with AresMUSH, MUCK, RhostMUSH,
CobraMUSH and TinyMUSH on the side of the hobby that an MSSP-only directory simply cannot see. Its
player count comes from a `WHO` at the login screen, which it answers with a plain count.

It does negotiate CHARSET, which is how it comes out ahead of most of its relatives on non-ASCII
text.

## Where the counts come from

If you are comparing this site's number for a TinyMUX game against another directory's, note that
we read the login-screen `WHO` and most crawlers do not. A directory built on MSSP alone reports
these games as having no count at all, or does not list them.
