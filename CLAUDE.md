# CLAUDE.md — MUIndex agent brief

Read this first, then read [`docs/specs/2026-07-30-mu-directory-design.md`](docs/specs/2026-07-30-mu-directory-design.md),
which is the authoritative design.

## What this is

**MUIndex** (short form **MUI**) is an information site for the MU\* hobby — MUSHes, MUDs, MUCKs,
MOOs — whose distinguishing property is that **its data is measured rather than asserted**. Every
displayed fact carries how it was obtained and how old it is.

**Nothing is implemented.** The repository holds the design, a design brief for a site-design
session, and a solution skeleton with the few types that were specified concretely enough to write
down. There is no plan yet.

## The four rules that generate everything else

1. **Measured beats declared, and both are shown.** A game's MSSP saying `GMCP 1` is an assertion;
   the server offering GMCP in the telnet handshake is an observation. When they disagree, that
   disagreement is the interesting fact and must not be hidden.
2. **Zero players is not the same fact as unreachable.** A failed probe writes an availability
   transition and *no* presence sample, so downtime leaves a gap in the activity heatmap rather than
   a run of zeroes that would render as a running-but-dead game. Conflating them is the worst single
   bug this codebase could ship.
3. **Nothing is ever deleted.** Archiving removes a game from the default listing, the rankings and
   the "active today" figure — and from nothing else. Its page, URL, history and change feed survive,
   it keeps being probed weekly forever, and one successful probe restores it.
4. **Parsers never fabricate.** An unreadable `WHO` yields `WhoConfidence.Unknown`, never zero.

## Never

- Add a vote, star, rating or "recommend" affordance. Rankings are computed from measured data. This
  is not a feature gap; it is the thing that killed Top Mud Sites.
- Build forums, reviews, wikis, comments or player profiles.
- Persist player names. `WHO` is parsed in memory; aggregates use salted hashes with a rotating salt.
- Let `MUI.Catalog` reference `MUI.Crawl`. The writers that consume a probe result must not know a
  socket exists — that one-way arrow is what keeps every downstream behaviour testable against a
  captured `ProbeResult` fixture with no network involved.
- Credit MSSP `CREATED` toward archive grace. It is one hand-typed line of `mush.cnf` and crediting
  it would make the threshold trivially gameable.

## Building and testing

- **.NET 10**. `TreatWarningsAsErrors` is `true` solution-wide, so a clean build is a real signal.
- **Tests are TUnit on Microsoft.Testing.Platform** (`Exe` projects). `dotnet test` does **not**
  work — .NET 10 dropped VSTest. Run each suite directly, and keep the `</dev/null` so the test host
  does not hang waiting on stdin.

```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
```

Four suites: Catalog, Crawl, Discovery, Web.

## Prior art in the org

`SharpMUSH/SharpMUTerm` already contains a working MSSP crawler (`src/SharpMUTerm.Crawler`) with
host modelling, a crawl frontier, backoff and MSSP parsing, tested in
`tests/SharpMUTerm.Crawler.Tests`. Read it before writing a probe engine here — some of it is
directly liftable, and where it diverges from this spec the divergence is worth understanding rather
than reinventing.

`TelnetNegotiationCore` is first-party too: the probe engine is that library pointed outward, so a
gap in it is a PR rather than a workaround.

## Conventions

- Branch from `main`; open a **PR**. Do not commit directly to `main`.
- Follow `.editorconfig`: file-scoped namespaces, 4-space C#, LF line endings.
- Keep commits focused with clear messages.
