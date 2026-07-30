# MuIndex

An information site for the MU\* hobby — MUSHes, MUDs, MUCKs, MOOs — whose distinguishing property
is that **its data is measured rather than asserted**. Every fact on a game's page carries how it
was obtained and how old it is.

Working name. Nothing is implemented yet.

## Documents

- [`docs/specs/2026-07-30-mu-directory-design.md`](docs/specs/2026-07-30-mu-directory-design.md) —
  the system design. Authoritative.
- [`docs/design-brief.md`](docs/design-brief.md) — input to a site-design session; the areas that
  need deciding before front-end work begins.

## Shape

One ASP.NET Core deployable: public site, owner dashboard, and read API, with the crawler running
in-process as a `BackgroundService` against a shared database. The probe engine is built on
[TelnetNegotiationCore](https://github.com/HarryCordewener/TelnetNegotiationCore) — the crawler is
that library pointed outward.

Four catalogues, weighted heavily toward the first: **games** (fully automated), **clients**,
**codebases**, **protocols** (hand-written in v1, automated later).

## Not this

No forums, reviews, wikis, comments, or chat. No user ratings and no vote-driven rankings of any
kind. No player profiles. We do not host games and we are not a web client.
