# CLAUDE.md — MUIndex agent brief

Read this first, then [`docs/specs/2026-07-30-mu-directory-design.md`](docs/specs/2026-07-30-mu-directory-design.md),
which is the authoritative design. [`docs/design-handoff.html`](docs/design-handoff.html) is the
delivered visual design — open it in a browser; it is JS-bundled, so extract the text rather than
reading the file raw.

## What this is

**MUIndex** (short form **MUI**) is an information site for the MU\* hobby — MUSHes, MUDs, MUCKs,
MOOs — whose distinguishing property is that **its data is measured rather than asserted**. Every
displayed fact carries how it was obtained and how old it is.

**The truth engine works; it is not yet joined end to end.** `MUI.Crawl` probes real servers,
`MUI.Catalog` persists what they said to Postgres, `MUI.Discovery` schedules and de-duplicates them,
and `MUI.Web` renders it. 511 tests, Postgres exercised in CI rather than skipped.

**Read `docs/codebase-survey-2026-07-30.md` before changing the probe or the parsers.** It records
one live game per codebase across 38 of them, and nearly every rule in `MUI.Crawl` traces to a row in
it. Five of the six real defects this project has found came from running against real servers rather
than from reasoning about them — a false zero on a busy DIKU, `NAME "PennMUSH"` collapsing identity,
an archived game's heatmap hatched, MCCP negotiated but never inflated, and our own `IAC DO 70`
poisoning the next command we sent. **Probe something before you theorise about it.**

## The five rules that generate everything else

1. **Measured beats declared, and both are shown.** A game's MSSP saying `GMCP 1` is an assertion;
   the server offering GMCP in the telnet handshake is an observation. When they disagree, that
   disagreement is the interesting fact and must not be hidden. This is why `GameField` is keyed
   `(game, field, **source**)` — one row per field cannot hold both sides of a disagreement.
2. **An hour has three states, not two.** Probed-with-a-count is a filled cell (*including* a
   measured zero — we got in and nobody was there). Probed-but-uncountable is hatched. **Not
   measured** is empty. Collapsing the middle case into either neighbour is the worst bug this
   codebase could ship: a game whose `DOING` header is customised past our parser would render as
   permanently dark while running perfectly well. The third state is *not measured*, never *not
   reachable* — a failed probe writes no presence row at all (`PresenceWriter`), so an empty cell
   covers an hour we could not reach and an hour we never probed alike, and it may not name a cause.
   It said "the game was not reachable" until a real crawl put it beside a game measured once and
   found perfectly reachable, described as down for 167 hours of the week. Reachability is the
   strip's question and comes from intervals, which can tell the two apart.
3. **Nothing is ever deleted.** Archiving removes a game from the default listing, the rankings and
   the "active today" figure — and from nothing else. Its page, URL, history and change feed survive,
   it keeps being probed forever, and one successful probe restores it.
4. **Parsers never fabricate.** An unreadable `WHO` yields unknown, never zero.
5. **Never record a decision of ours as a measurement of theirs.** A scope refusal is not downtime.
   An unparseable `WHO` is not zero players. Our security policy and our parser's limits must never
   appear in a game's public record as facts about the game. **The same rule reads forward into every
   sentence a surface writes**: a percentage whose denominator is what we observed may not be
   presented as a fraction of a 90-day window, and a graphic's empty state may not be given a cause.
   Both shipped, and both were found the first time the site rendered a real crawl instead of the
   fixture — a fixture is written by someone who already knows what each panel is supposed to say.

## Vocabulary

**Reachable, never *uptime*** — schema, API, code and copy. We measure a socket from one vantage
point at intervals; we did not measure whether the game was up, and a game with a routing problem to
our host is unreachable and perfectly alive.

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
- **Add a `Refused` member to `ProbeOutcome`, or dress a scope refusal as `ProbeResult.Failed(…)`.**
  A refusal happens *before* a probe exists, and `FailureCause.Refused` already means the far end
  sent an RST — a real measurement of a real host. Conflating them is unrecoverable downstream. The
  guard belongs to whatever owns the dial.
- **Commit a harvested dataset.** The backfill importer is code and belongs here; its output does
  not. No mirrored listing file, no seed list of real hosts scraped from a directory, no snapshot of
  anyone's catalogue. The import is a *one-time* operation an operator runs once against the one
  deployment — not a startup step, not a scheduled job, not something a clone reproduces (spec §7.6).
  Test fixtures are exempt: a handful of hand-written rows exercising a parser is a test input, and a
  copy of a third party's catalogue is not, whatever it is named or how small it starts.
- **Ship invented data without saying so on the page.** `MUI.Web` reads Postgres when
  `MUI_POSTGRES` (or `ConnectionStrings:MUIndex`) is set. With neither it still starts, on the
  fixture — and then every page carries the demo banner, because a reader who cannot tell a
  measurement from a fixture is being misled by exactly the mechanism this project exists to replace.
- Publish an absolute "how many people play MU\*" figure. Shares ship; totals do not (spec §15.7).

## Security: the gate is on the address, not the name

`REFERRAL` is attacker-controlled. Refusing `10.0.0.5` and `localhost` by inspection is **not
enough** — `games.example.com` with an A record pointing at `127.0.0.1` or `169.254.169.254` passes
a name check and the socket goes somewhere it must never go. Every dial resolves first and is
refused unless **every** returned address is globally routable; a mixed answer refuses the whole
target rather than picking the good one. See spec §7.2, including the time-of-check-to-time-of-use
limitation, which is real and must not be restated as airtight.

## Stack

One ASP.NET Core deployable — public site, owner dashboard, read API — with the crawler as an
in-process `BackgroundService` gated on a Postgres advisory lock so replicas do not multiply it.
**PostgreSQL** (Npgsql + Dapper, SQL migrations, **no EF Core**).

Postgres was chosen knowing the referral graph exists: it is shallow and small, order 10k edges,
almost all queries one hop, with recursion needed only for §7.2's subtree prune. Presence
partitioning, interval arithmetic and faceted counts dominate and are where graph stores are
weakest. The revisit trigger is written down in spec §4.13 — do not reopen it without one.

## Building and testing

- **.NET 10**. `TreatWarningsAsErrors` is `true` solution-wide, so a clean build is a real signal.
- **Tests are TUnit on Microsoft.Testing.Platform** (`Exe` projects). `dotnet test` does **not**
  work — .NET 10 dropped VSTest. Run each suite directly, and keep the `</dev/null` so the test host
  does not hang waiting on stdin.

```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
```

Five suites: Catalog, Crawl, Crawler, Discovery, Web. Add a new one to **both** `MUIndex.slnx` and
`.github/workflows/ci.yml`, which runs each suite explicitly. Catalog and Crawler both want a real
PostgreSQL, so CI's Linux leg sets `MUI_REQUIRE_POSTGRES` and a missing container runtime fails
rather than skips.

`mui-crawl` runs crawl cycles against a real database and prints what landed — the counterpart to
`mui-probe`, which prints what one server said:

```bash
MUI_CRAWL_POSTGRES=… dotnet run -c Release --project src/MUI.Crawler.Cli -- \
  --seed mush.pennmush.org:4201 --seed aardmud.org:4000
```

## MUIndex owns its crawler

**There is no shared library, and this was tried.** An extraction from `SharpMUSH/SharpMUTerm` was
built and abandoned: TelnetNegotiationCore 2.6.5 had already absorbed most of what was worth
sharing, and the remainder — an in-memory bounded-run frontier — is the wrong shape for a permanent
database-backed registry that never retires a host. The repository that would have produced the
package is archived and nothing was ever published. **Do not propose it again without new
information.**

`SharpMUTerm`'s crawler is still worth *reading* as a tested reference — `src/SharpMUTerm.Crawler`
and `src/SharpMUTerm.Core/Telnet/Mssp`, especially `MsspHost`'s referral parsing and scope
classification. Read it, then write MUIndex's own. Do not copy files across.

`TelnetNegotiationCore` is first-party, so **a gap in it is a PR rather than a workaround here**.
Never carry a compensating hack for library behaviour; fix it upstream and take the new version.

## Conventions

- Branch from `main`; open a **PR**. Do not commit directly to `main`.
- Follow `.editorconfig`: file-scoped namespaces, 4-space C#, LF line endings.
- Keep commits focused with clear messages.
- **Stage by explicit path. Never `git add -A` or `git commit -a`.** Several agents have shared this
  working tree and a blanket stage has already swept another agent's half-finished files into an
  unrelated commit. If you are one of several agents, use a worktree.
