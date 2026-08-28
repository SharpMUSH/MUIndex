# CLAUDE.md — MUIndex agent brief

Read this first. [`docs/design-handoff.html`](docs/design-handoff.html) is the delivered visual
design — open it in a browser; it is JS-bundled, so extract the text rather than reading the file
raw.

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
- **Type `WHO` at a game that has already published its count.** MSSP `PLAYERS`, or a count the
  connect screen states about itself, is the answer — asking again is noise on somebody's console,
  and an operator was right to say so. `TelnetProbe.PublishedCount` is the one place that decides
  it, and it shares `MsspPlayers` with `PresenceChoice` so the count that buys the silence is the
  same count that gets published: **not asking must imply publishing**, or the probe reaches
  `who_not_offered` and writes our own restraint down as *the game answers no pre-login WHO* (rule
  5). The screen rung additionally needs a protocol signal in the session — for a server that
  negotiates nothing, a parseable `WHO` is its only §7.8 evidence of being a game at all, and
  talking ourselves out of asking would cost it its listing.
- Let `MUI.Catalog` reference `MUI.Crawl`. The writers that consume a probe result must not know a
  socket exists — that one-way arrow is what keeps every downstream behaviour testable against a
  captured `ProbeResult` fixture with no network involved.
- Credit MSSP `CREATED` toward archive grace. It is one hand-typed line of `mush.cnf` and crediting
  it would make the threshold trivially gameable.
- **Add a `Refused` member to `ProbeOutcome`, or dress a scope refusal as `ProbeResult.Failed(…)`.**
  A refusal happens *before* a probe exists, and `FailureCause.Refused` already means the far end
  sent an RST — a real measurement of a real host. Conflating them is unrecoverable downstream. The
  guard belongs to whatever owns the dial.
- **Commit a harvested dataset, or bring the backfill importer back onto `main`.** Both are one
  rule (spec §7.6): the import is a *one-time* operation an operator runs once against the one
  deployment — not a startup step, not a scheduled job, not something a clone reproduces — so neither
  its output nor its machinery belongs in the shipped tree. No mirrored listing file, no seed list of
  real hosts scraped from a directory, no snapshot of anyone's catalogue; and no fetchers, no HTML
  parsers for third-party sites, no etiquette gate. Four parsers for sites we intend never to fetch
  again, carried in CI for ever, rot silently and read as a supported feature. **The importer lives
  on the local `import/one-time` branch**; running the import means checking it out.
  - Test fixtures were never the problem: a handful of hand-written rows exercising a parser is a
    test input, and a copy of a third party's catalogue is not, whatever it is named.
- **Import a value from a one-time scrape, or record a game's origin as a fact about the game.**
  Both halves are narrower than they were, and the narrowing is deliberate. The **backfill** still
  takes **host and port and nothing else** (spec §7.6): no `imported_measured`/`imported_asserted`
  field source, no imported presence or availability row, no `import_provenance` table, no
  half-weight archive grace. A **standing, authenticated source** may write values, under its own
  weak rung, because it is not the thing §7.6 argued against — `i3_mudlist` (migration 0023) and
  `ares_central` (migration 0032) are the two, both below `mssp`, neither above `staff` or `owner`.
  The distinction that licenses this: §7.6's objection is that a game's origin is **not one fact**
  (the catalogue is cross-checked against several directories and any game worth listing is in more
  than one, so "imported from MudStats" names whichever fetch ran first, not the game); that a game
  exists is **public information**; and the point of the seed is to **start with a lot of games and
  then gather our own data**. A hub that answers on a schedule, for one codebase, with credentials
  its maintainer issued, is a live source, not a fetch that happened once.
  And `discovered_via` (migration 0033) records **which channel first told this site about an
  address, and when** — a dated statement about our own crawl. It is set once, at the moment a
  target row is created, and the registry's own `ON CONFLICT` is what makes it write-once. **It may
  never be rendered as a badge, shortened to a source name, or read as exclusivity**: the sentence
  on the page always carries the date, because the channel alone reads as a claim about where the
  game came from, which is not a thing we know. `IntervalOrigin` survives as a one-member enum on
  purpose — an undifferentiated total cannot be split back apart if another party's measurements are
  ever ingested.
- **Compile in a claim about somebody else's consent.** `ContactedMaintainer` defaulted to `true`
  for MudStats with a comment stating the maintainer had been approached; nobody had emailed them,
  and a 143-page crawl went out on the strength of it. The instruction it was written from was *do
  the MudStats import* — a decision about our priorities, not a fact about a third party. A gate like
  that is satisfied by a caller who can make the claim (`--contacted MudStats`), never by a default.
- **Ship invented data without saying so on the page.** `MUI.Web` reads Postgres when
  `MUI_POSTGRES` (or `ConnectionStrings:MUIndex`) is set. With neither it still starts, on the
  fixture — and then every page carries the demo banner, because a reader who cannot tell a
  measurement from a fixture is being misled by exactly the mechanism this project exists to replace.
- Publish an absolute "how many people play MU\*" figure. Shares ship; totals do not (spec §15.7).

## The two HTTP clients, and why they are not the fetchers the rule above forbids

`IconFetcher` retrieves the image a game's `ICON` field names, so the site can serve it from its own
origin instead of hot-linking it — which would hand every reader's address to a third party for a
decoration (§11). **This is not the "no fetchers, no HTML parsers for third-party sites" rule being
bent.** That rule is about *harvesting somebody's catalogue*: reading a directory's pages to acquire
data we then present as ours. This fetches one image, from a URL the game itself published, at the
game's own request, and presents it as theirs.

The constraints are not optional and live on the registration in `IconEndpoint.AddMuiIcons`: a typed
client through `IHttpClientFactory` (**never** `new HttpClient()` — the factory is what bounds the
handler's lifetime, and a pinned DNS answer on the one component that fetches attacker-chosen URLs
compounds §7.2's TOCTOU gap), `AllowAutoRedirect = false` because a redirect is a second address the
gate never ruled on, the body read to a ceiling rather than trusted to `Content-Length`, and the
content type read from the bytes by `ImageHeader` rather than from any header. **SVG is refused** —
it is a document that can carry script, and serving one from our origin is an XSS hole with an image
tag in front of it. `ImageHeader` parses headers and does not decode: no image library, no decoder
attack surface reached by an owner-supplied URL. PNG, JPEG, GIF, WebP, ICO and BMP — the last two
because `favicon.ico` is what a MU\* operator has to hand, and more of the catalogue's declared
`ICON`s name one than name anything else.

**The handler still follows no redirect; `IconFetcher` follows exactly one, and runs the gate again
on the target.** The difference is the whole point — an address nobody ruled on versus an address
ruled on the same way the first one was — and it is why the hop lives in the fetcher rather than in
`AllowAutoRedirect`. One rather than a chain: each further hop is another address to clear for a
decoration, and a chain longer than one is somebody's tracker or somebody's loop far more often than
it is a moved logo. What refusing them outright cost, measured: thirteen of the sixty-seven games
with a declared `ICON` and no cached image answered 3xx, nearly all an `http` URL in `mush.cnf` with
an `https` web server since put in front of it.

`AresGamesClient` reads the AresCentral games list — one authenticated GET, hourly, against a
documented API whose maintainer issued this deployment credentials. **This is not that rule being
bent either.** The rule is about *harvesting somebody's catalogue*: one-time machinery pointed at a
third party's HTML, carried in CI for years after the fetch it existed for, rotting silently while
reading as a supported feature. This is a standing source read for as long as the site runs — the
same shape as the Intermud-3 gateway already here — and §7.6's etiquette clause names asking for a
documented API as the thing to do *in preference to* scraping. There is no one-time import in it and
nothing belongs on `import/one-time`.

The same constraints apply, because they are about how an `HttpClient` is held rather than about
what is fetched: through `IHttpClientFactory` (**never** `new HttpClient()`), `AllowAutoRedirect =
false` since a redirect is a second address nobody ruled on, and the body read to a ceiling rather
than trusted to `Content-Length`. **A *named* client here rather than `IconFetcher`'s typed one**, and
that difference is load-bearing: a typed client is registered transient, and the only consumer is a
singleton `BackgroundService`, which would resolve exactly one and hold its handler — and that
handler's DNS answer — for the life of the process, defeating the pooled rotation the factory exists
to provide. `AresService` asks the factory for a client per pass;
`AresServiceOptionsTests.ThePassTakesAClientFactoryRatherThanAClient` enforces it by reflection. The URL here is ours and constant rather than
owner-supplied, so the DNS-pinning argument is weaker than `IconFetcher`'s — the registration follows
the same pattern anyway, so there is one way to do this in the tree. `mui-crawl --ares` is the single
exception and says so at the call site: a process that makes one request and exits *is* the handler's
lifetime.

`game_icon` and `icon_attempt` are the **two tables here that may be dropped and refilled**. The fact
is the `ICON` field; those are bytes fetched from the URL it names and our own notes about fetching
them, so §7.5's "nothing is ever deleted" does not reach either. A failed fetch writes **nothing
about the game** — no field, no API value, no change-feed entry — and the page renders the monogram
it would have rendered anyway.

It does write down **that we tried, and when to try again**, and that is a reversal of the original
rule ("no row, no marker, no attempt counter") worth knowing the reason for. Rule 5 is about a game's
public record, and none of `icon_attempt` reaches one. What the absolute version produced instead was
a queue that could not move: `DueAsync` ranked candidates by what `game_icon` held, a game never
fetched held nothing, so every such game tied with every other on both sort keys — and `LIMIT 20`
over a tie is the same twenty rows for ever. Production ran that way for six days, re-fetching the
same fifteen dead URLs every thirty minutes while forty-seven games with a perfectly good declared
`ICON` were never attempted once. **A cache with no record of failure cannot tell a candidate it just
failed from one it has never seen.** The marker is also the back-off, which is the part the operators
of those fifteen web servers would have cared about.

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

`mui-crawl` is deliberately not baked into the deployed image (Dockerfile), so administering a running
deployment has meant ssh in and `docker compose run --entrypoint mui-crawl`. `/mcp` is the
authenticated alternative: an MCP endpoint (`ModelContextProtocol.AspNetCore`, Streamable HTTP)
mounted inside `MUI.Web` itself — `src/MUI.Web/Mcp/` — that reuses the same library services the CLI
uses (`OptOutGate`, `ICrawlTargetRepository`, `NpgsqlGameFieldStore`, the deployment's own singleton
`CrawlCycle`) rather than reviving the excluded CLI image. It is gated behind `MUI_MCP_TOKEN`, a
shared bearer secret checked in constant time; unset, every request fails authentication (fail
closed — see `docs/deploy.md`'s "Administering the site over MCP"). Ten tools, mirroring the CLI:
`crawl_seed_add`, `crawl_opt_out_record`, `crawl_opt_out_check`, `crawl_due_targets`,
`crawl_run_cycle`, `crawl_summary`, plus four capabilities of its own — `game_field_set`, a staff override
(`FieldSource.Staff`) of one `GameField` row, for fixing a mis-parsed value by hand without raw SQL;
`game_rename` (also `mui-crawl --rename`), which writes `NAME` through that same staff override
and then takes `SlugMinter`'s immediate, no-grace mint-and-rename path — the one a verified owner's
own rename already takes (spec §5.7) — for a game with no owner or where staff has decided what it is
called; and `game_merge` (also `mui-crawl --merge --because`), which drains one `duplicate_review`
pair by hand (spec §7.3) through the same `ReviewMergeService` the CLI uses — folding the loser into
the winner, resolving an open review naming that pair if one exists, and refusing on a redirect chain
or an already-absorbed loser the same way the schema itself refuses. The old slug redirects to the
new page for ever; `game_field_set` on `NAME` alone still does not do this, and says so. And
`game_keep_distinct` (also `mui-crawl --distinct --because`), §7.3's **other** verdict: this pair is
two games. Nothing moves and neither page changes — the `duplicate_review` row is resolved with the
reason beside it, which is the only thing that stops the pair being asked about again. Without it the
queue only ever grew: on 2026-08-21, thirty-one of the sixty-one open rows were pairs correctly left
unmerged and impossible to clear, and a queue whose false positives cannot be cleared stops being
read.

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
