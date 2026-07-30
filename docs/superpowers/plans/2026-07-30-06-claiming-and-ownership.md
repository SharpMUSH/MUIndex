# Claiming and Ownership Implementation Plan (Plan 06)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build spec §8 — the site issues a token, the owner proves control by publishing it through the game itself (MSSP field, connect-screen line, or DNS TXT), the crawler that already exists verifies it without ever probing harder, and a verified claim unlocks the owner dashboard's write surface, the archive ceiling, and the permanent identity beacon.

**Architecture:** The claim *domain* is pure and lives in `MUI.Catalog` — the token record, its four states, the three channels, the unambiguous alphabet an operator retypes by hand, and the diagnostic. Persistence is four new tables in `MUI.Storage` behind repository interfaces. The *behaviour* lives in `MUI.Discovery.Ownership`, the only project that may see both a `ProbeResult` and catalogue state: `ClaimVerifier` reads two of the three channels straight off a captured `ProbeResult` with no socket, and the third — DNS TXT — gets its own `IDnsTxtResolver`, because a TXT record is invisible to a telnet probe and the crawler genuinely cannot do it. Verification is a passenger on the crawl loop: `ClaimCycle` is called between the ingestor and the rescheduler and is structurally unable to reach the schedule at all.

**Tech Stack:** .NET 10, C# 14, TUnit on Microsoft.Testing.Platform, Npgsql 10 + Dapper 2 against PostgreSQL 17, `Testcontainers.PostgreSql` for repository tests, `DnsClient` 1.8.0 for the one channel that needs a resolver, `System.Security.Cryptography.RandomNumberGenerator` for token generation, ASP.NET Core minimal APIs for the claim and dashboard surfaces.

**Depends on: Plan 01 for `ProbeResult` (the connect screen and MSSP a verification reads); Plan 02 for `MUI.Storage`, the repositories and the migration runner; Plan 03 for the crawl schedule verification rides on and the identity matcher that consumes a verified token; Plan 05 for the web surface the claim pages are served from.**

**Spec:** `docs/specs/2026-07-30-mu-directory-design.md` — §8 is the core, plus §7.3 (the beacon), §7.5 (the ceiling), §6.2 (connect-screen suppression), §6.3 (the WHO-format override), §11 (politeness and opt-out), §5.1 (the precedence ladder an owner write goes through), §12 (bounded, cancellable I/O).

**Design:** `docs/design-handoff.html` §08 (claim flow) and §09 (owner dashboard) are the delivered copy and interaction design for this plan. Where they add detail the spec does not have, they are followed; where they disagree, the disagreement is recorded in *Cross-plan and cross-document reconciliation* below rather than being silently resolved.

---

## Global Constraints

These apply to every task in this plan without being repeated.

- **Target framework `net10.0`**, and `TreatWarningsAsErrors` is `true` solution-wide
  (`Directory.Build.props`). A build with a warning is a failed build.
- **Tests are TUnit on Microsoft.Testing.Platform** (`Exe` projects). **`dotnet test` does not
  work** — .NET 10 dropped VSTest. Run each suite directly and keep the `</dev/null`, which
  detaches stdin so the test host does not hang waiting on it:
  ```bash
  dotnet build MUIndex.slnx -c Release
  dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
  ```
- **Assertion idiom:** `await Assert.That(actual).IsEqualTo(expected);` — tests are
  `[Test] public async Task Name()` on a plain `public class`, no attributes on the class.
- **`.editorconfig`:** file-scoped namespaces, 4-space C# indentation, LF line endings, 2-space
  indentation for `csproj`/`props`/`json`/`yml`/`slnx`.
- **`MUI.Catalog` must NEVER reference `MUI.Crawl`.** The writers that consume a probe result must
  not know a socket exists; that one-way arrow is what keeps every downstream behaviour testable
  against a captured `ProbeResult` fixture with no network involved.
- **Never persist player names.** `WHO` is parsed in memory; aggregates use salted hashes with a
  rotating salt, so a unique-player estimate is possible while re-identification across salt epochs
  is not.
- **Parsers never fabricate.** An unreadable `WHO` yields `WhoConfidence.Unknown`, never zero.
- **Never read the ambient clock.** No static `UtcNow` or `Now`, on either `DateTimeOffset` or
  `DateTime`, anywhere in this plan — not in production code, not in a test, not at a call site to
  spare a type a constructor parameter. A `grep` for those two property names over this plan's
  snippets returns nothing, and that is meant to stay true. Time arrives through an injected
  `TimeProvider` (`time.GetUtcNow()`), and
  tests drive it with `ManualTimeProvider`. This plan is unusually exposed to it: a claim token is
  valid for fourteen days and is re-checked hourly, so almost everything here has a reason to want to
  know what time it is. Two corollaries:
  - **A type that does not need time does not take a `now` parameter "just in case".** An ignored
    `now` obliges every caller to produce one, and the shortest way to produce one is the ambient
    clock — which is how a deterministic type acquires a real clock at its call site and a suite
    acquires flakiness nobody can reproduce. When the type does need time, give it `TimeProvider`
    then; that is a smaller change than the one that would otherwise be needed.
  - **Measuring how long a real I/O bound actually took is the one exception, and it is `Stopwatch`,
    not the clock.** `Stopwatch` is monotonic and unaffected by clock adjustment, which is precisely
    the property an "under 5 seconds" assertion needs.
- **Vocabulary is "reachable", never "uptime"** — schema, API, code and copy alike (spec §5.7).
- **`tests/MUI.Discovery.Tests/Support` is one assembly shared with Plans 02, 03 and 04.** Extend the
  double that is already there; a second declaration of one is `CS0101`, not a merge conflict. Both
  names this plan was out of step on are now **settled and applied**: repository doubles are
  `InMemory<Thing>Repository` (this plan was the only one writing `Fake*`), and probe fixtures are
  Plan 03's `ProbeResults`, whose signature this plan's call sites were already written against.
  Service fakes that are not repositories — `FakeDnsTxtResolver`, `FakeProbe` — keep the `Fake`
  prefix, because they stub a collaborator rather than reimplement a store in memory.
  **Known duplication, recorded not resolved:** Plan 02 declares `ProbeFixtures` and Plan 03 declares
  `ProbeResults` in this same namespace, with different signatures. Two fixture builders for one type
  is a smell, but consolidating them is cross-plan churn better judged with the code in hand than
  from here — so use `ProbeResults`, and fold the two together during implementation.
- **Branch from `main`, open a PR, never commit directly to `main`.**
- **Any new test project goes into `MUIndex.slnx` AND `.github/workflows/ci.yml`**, which runs each
  suite as its own explicit step.
- **There is no shared MSSP package, and MUIndex shares no code with SharpMUTerm.** The MSSP domain
  types are MUIndex's own, in namespace `MUI.Crawl.Mssp` — `MsspData`, `MsspHost`, `MsspHostScope`,
  `MsspVariables`, `MsspPlaintextReply` — written by Plan 01 over `TelnetNegotiationCore` 2.7.0,
  which parses telnet option 70 itself. Never re-declare them locally, and never reach for a
  `SharpMU.Mssp` package: it was tried, abandoned, and never published.
- **Persistence is PostgreSQL 17 with Npgsql + Dapper and plain numbered `.sql` migration files
  applied by a small idempotent runner. No EF Core**, ever. Integration tests use
  `Testcontainers.PostgreSql`.

### Where the MSSP domain comes from — read this before Task 1

The shared-package decision was reversed: `SharpMU.Mssp` was never published,
`SharpMUSH/SharpMU.Mssp.Crawl` is archived, and MUIndex implements its own crawler end to end.
**All six plans' constraint blocks were corrected together**, so the bullet above is the current
rule rather than a quoted fossil with an erratum under it — one vocabulary, stated once, in six
places that agree.

The type names `MsspData`, `MsspHost`, `MsspVariables` are unchanged from the abandoned design; only
the namespace and the origin moved. Everywhere this plan writes `MsspData` it means
`MUI.Crawl.Mssp.MsspData`, and there is no `MsspSubnegotiationParser` at all — TNC 2.7.0 parses
telnet option 70 itself, and the only MSSP text MUIndex parses is the out-of-band plaintext
`MSSP-REQUEST` reply, via `MsspPlaintextReply.TryParse`.

**Two further constraints this plan adds for itself:**

- **No live DNS in any test, ever.** The verifier's tests use `FakeDnsTxtResolver`. The real
  `DnsTxtResolver`'s tests point it at a resolver on `127.0.0.1` that this repository starts —
  a loopback socket, not the internet.
- **A pending claim is never a reason to probe sooner.** §11's politeness contract has no exemption
  for someone waiting on a screen, and Task 8 makes that structural rather than a matter of care.

---

## Scope

**In:** token issue, lifecycle and regeneration; the three proof channels and the DNS resolver the
third one needs; verification riding the existing crawl schedule; the "not found after 3 probes"
diagnostic; the token persisting as the §7.3 identity beacon and the clone guard that role needs;
the archive ceiling for a claimed game; the owner dashboard's write surface (enrichment fields,
connect-screen suppression, WHO-format override, opt-out); multi-owner, transfer and an audit log;
the continuous MSSP linter scorecard; the HTTP endpoints all of that is driven from.

**Explicitly out of scope, and deliberately so:**

- **The owner-published SVG badge and the JSON endpoint** (§8's last line, design handoff §09's
  *badge & embed* panel). Both are *read* surfaces rendered from view models, and Plan 05 owns every
  read surface, its ETag discipline and its `?plain=1` parity test. Adding a second renderer here
  would be the second document Plan 05 exists to prevent. This plan writes the state the badge
  reads and stops there.
- **Sending mail.** §8 is explicit: "none requires the site to send mail". There is no SMTP
  dependency, no address collection for verification, and no email-confirmation step anywhere in
  this plan. The only address this system holds is MSSP `CONTACT`, which the game published itself.
- **Any third-party identity registry.** No OAuth provider, no Gravatar, no Discord verification, no
  keybase-style attestation. The proof is server or DNS access to the game being claimed, and
  nothing else counts.
- **Owner account authentication itself** — password hashing, session cookies, the login form. An
  `Owner` here is a handle plus an `IOwnerPrincipal` the web layer supplies; wiring a real
  authentication provider is a deployment concern with its own review, and Task 16 states the exact
  seam it plugs into.

---

## Cross-plan and cross-document reconciliation — read before Task 1

Six decisions that are **binding on this plan and on the plans named**, because the alternative is
two half-working halves that each look right in isolation.

### 1. `ClaimToken` versus `ClaimTokenBeacon` — the name collision, resolved

Plan 03 declares `public static class MUI.Discovery.ClaimTokenBeacon` — `const string MsspVariable`,
`const string ConnectScreenPrefix`, `static string? Read(ProbeResult)` — reading its literals from
`MUI.Catalog.ClaimVocabulary`. This plan needs the name `ClaimToken` for the issued record. Per
`CONTRACT-ADDENDUM.md` §5:

| | Plan 03 | Plan 06 (this plan) |
|---|---|---|
| Type | `MUI.Discovery.ClaimTokenBeacon` (**renamed** from `ClaimToken`) | `MUI.Catalog.ClaimToken` |
| Kind | `static class` | `sealed record` |
| Job | **Reads a beacon off a probe** — given a `ProbeResult`, what token, if any, is this host emitting? | **Models and issues a token** — who was it issued to, when does it expire, has it been proved, through which channel? |
| Members | `MsspVariable`, `ConnectScreenPrefix`, `Read(ProbeResult) → string?` | `Id`, `GameId`, `Value`, `State`, `IssuedAt`, `ExpiresAt`, `VerifiedVia`, `VerifiedAt`, `ProbesSinceIssue` |

`IdentityFields.ClaimToken` (the `game_field.field` string `claim_token`) and
`IdentityWeights.ClaimToken = 10.0` are unchanged.

**Plan 03 already carries these names**, so Task 4 pins the split rather than performing a rename.
Read Plan 03 before writing against it: it is the authority on this surface, and the beacon has
**exactly one call site** there — `ClaimTokenBeacon.Read(result)` in `IdentityMatcher.ResolveAsync`
(`src/MUI.Discovery/IdentityMatcher.cs`). The constants are asserted against `ClaimVocabulary` in
`IdentityCorpusTests`, which is where Plan 03 keeps its claim-token cases.

### 2. One wire vocabulary, declared once, in `MUI.Catalog`

Plan 03's beacon reader and this plan's verifier must agree about *where* a token appears, or a game
verifies through a channel that then emits no beacon — a silently half-working claim. Worse, the
**instructions page** must name the same MSSP variable the crawler looks for, or an operator does
exactly as told and is then informed their claim failed.

`MUI.Catalog` cannot see `MUI.Discovery`, so the constants cannot live on `ClaimTokenBeacon`. They
live in **`MUI.Catalog.ClaimVocabulary`**, which this plan owns and which both sides reference:
Plan 03's `ClaimTokenBeacon` reads from it rather than re-declaring the literals, and every piece of
operator-facing copy in Task 16 renders from it. Two declarations of one wire literal is exactly the
drift this project keeps designing against.

Task 4 carries the pinning test (`TheBeaconAndTheVerifierReadTheSameVocabulary`); Task 16 carries the
cheap one that matters most (`TheInstructionsNameTheVariableTheCrawlerActuallyLooksFor`).

### 3. The MSSP variable name, and the connect-screen form — two live disagreements with the delivered copy

**The MSSP variable.** Spec §8 says "an MSSP field" and names none. Plan 03 chose `MUINDEX CLAIM`.
The design handoff §08 shows the operator `mssp CONTACT_TOKEN/muidx-7f3a-c19e-4b02`.

**Resolution: `MUINDEX CLAIM` is canonical, `CONTACT_TOKEN` is an accepted alias.** The canonical name
is the one the dashboard renders, because `CONTACT_TOKEN` reads as a qualifier on MSSP's official
`CONTACT` variable and is not obviously ours; `MUINDEX CLAIM` cannot be mistaken for anything else and
matches how the crawler already identifies itself (`muindex-crawler` in TTYPE, `muindex` in MNES
`CLIENT_NAME`). The alias is accepted for verification because operators will follow a screenshot,
and telling someone their claim failed when they typed exactly what the design showed them is the
support mail §8's diagnostic exists to prevent.

**The connect-screen form.** Plan 03 declares `ConnectScreenPrefix = "MUINDEX-CLAIM:"`. The design
handoff §08 shows the operator a bare `muidx-7f3a-c19e-4b02` and says "Anywhere in the banner,
including the bottom where players will not notice".

**Resolution: both are accepted, and the labelled form is what the page shows.** Every banner reader
— Plan 03's and this plan's — tries the label first and then falls back to scanning for a
well-formed bare token (`ClaimTokenFormat.Pattern`), which is possible only because `muidx-` plus
three four-character groups from a fixed alphabet is not a string that occurs in a MUSH banner by
accident. The label is what the instructions render, because a bare token in a banner is
indistinguishable from noise to the admin who finds it six months later and wonders whether it is
safe to delete. The handoff's "anywhere, including where players will not notice" argument still
holds for the bare form, and the bare form still verifies.

**Both of these are copy decisions the coordinator may reverse in one line each** — swap the two
entries in `ClaimVocabulary.AcceptedMsspVariables`, or change which form Task 16 renders. They are
recorded here rather than reconciled silently, because a coordinator who has seen the delivered
design may well prefer the handoff's spelling in both cases.

### 4. A verified token is public, and that is a hole §7.3 does not close

The token is published in MSSP, in a connect screen, or in DNS — all three are readable by anyone who
connects or queries. It is also `IdentityWeights.ClaimToken = 10.0`, four times the auto-merge
threshold and decisive on its own. So a stranger can read a claimed game's token off its MSSP, put it
in their own MSSP, and be auto-merged into that game.

Secrecy cannot fix this: every channel §8 offers is a public one, by design, because the point is that
proving control costs no email round-trip. **The fix is a guard on the beacon's decisiveness, and it is
Task 10:** a token is decisive for the game that owns it, and a probe presenting a verified token from
a host that game has never been seen at is only decisive when the game's *known* endpoints are no
longer answering. A real host move looks exactly like that; a clone does not, because the original
keeps answering. Where the guard declines, the score falls back to the other signals and the verdict
is `Review` rather than `Merge` — which §7.3 already says is the right answer under uncertainty,
because both pages stay live.

`ClaimBeaconPolicy` is this plan's; Plan 03's `IdentityMatcher` consults it. Declared as a Plan 03
modification in item 6 below.

### 4a. A verified token MUST be mirrored into `game_field["claim_token"]`

Plan 03's `IdentityMatcher` compares candidates against the `game_field` row named by
`IdentityFields.ClaimToken` — the literal `claim_token` — **and against nothing else**. It never
reads `claim_token` the table. So a token held only in this plan's `IClaimRepository` leaves the
10.0-weighted signal permanently dead and §7.3's "a claimed game is never duplicated" silently not
holding, with every piece looking correctly wired.

**On successful verification the token value is written to `game_field` as field `claim_token` with
`FieldSource.Owner`, through `IGameFieldRepository` like any other field** — never by touching the
table. Task 10 owns this, gives it its own step, and pins it with the only test that can catch the
drift: verify a claim, then run `IdentityMatcher.ResolveAsync` over a later probe carrying the same
beacon and assert the game scores at `IdentityWeights.ClaimToken`.

The same fact read backwards: **revoking or expiring a claim must delete that row**, or a revoked
token keeps voting at weight 10.0 for ever. Task 10 pins that too.

### 4b. `ClaimVocabulary` as Plan 03 will reference it

```csharp
namespace MUI.Catalog;

/// <summary>
/// The wire vocabulary of a claim: the three places an operator may put the token, named once.
/// Read by <c>MUI.Discovery.ClaimTokenBeacon</c> and rendered into the claim flow's own
/// instructions, so an operator is told the same variable name the crawler looks for.
/// </summary>
public static class ClaimVocabulary
{
    public const string MsspVariable = "MUINDEX CLAIM";
    public const string ConnectScreenPrefix = "MUINDEX-CLAIM:";
    public const string DnsLabel = "_muindex";      // TXT at _muindex.<host>
}
```

Task 1 writes it, with the alias list, the opt-out vocabulary and `DnsNameFor` alongside. Plan 03
references it rather than re-declaring the literals.

### 5. Migration numbering

Plan 02 holds `0001`–`0005`, Plan 03 holds `0010`–`0013`, Plan 04 holds `0100`. **This plan takes the
`0020`–`0024` band**, which collides with nothing and sorts after Plan 03's tables (this plan's foreign
keys point at `game`, and its opt-out changes `crawl_target`'s reader).

| File | Table |
|---|---|
| `0020_claim_token.sql` | `claim_token` |
| `0021_claim_attempt.sql` | `claim_attempt` |
| `0022_owner.sql` | `game_owner` |
| `0023_owner_audit.sql` | `owner_audit` |
| `0024_owner_preferences.sql` | `owner_preferences`, `crawl_opt_out`, and `game.opted_out_at` |

### 6. What this plan changes in Plans 01, 02, 03 and 05

Each is small, each is unavoidable, and each is implemented by the task named.

| Plan | Change | Why | Task |
|---|---|---|---|
| 01 | `ProbeTarget` gains `bool SendWho { get; init; } = true;` and `string? WhoSummaryPattern { get; init; }`; `ProbeSession` honours both; `WhoParser.Parse` gains an optional `string? summaryPattern = null` | §6.3's owner override has to reach the probe — nothing downstream can apply it, because `ProbeResult` carries a `WhoReading` and not the transcript it was read from | 13 |
| 02 | `IGameRepository` gains `Task SetClaimedAsync(Guid id, bool isClaimed, CancellationToken ct)`, on `NpgsqlGameRepository` and `InMemoryGameRepository` | `Game.IsClaimed` is what `ArchiveSweeper` already feeds to `ArchivePolicy.GraceFor`; nothing could set it | 9 |
| 03 | `ClaimTokenBeacon` reads `MUI.Catalog.ClaimVocabulary` and `ClaimTokenFormat.Pattern` instead of re-declaring the literals | Items 1, 2 and 3 above | 4 |
| 03 | `IdentityMatcher` consults `ClaimBeaconPolicy` before scoring the claim-token signal | Item 4 above | 10 |
| 03 | `CrawlerService` gains a `ClaimCycle claims` constructor parameter, called in `ApplyAsync` between `ingestor.IngestAsync` and `RescheduleAsync` | Verification rides the schedule; that position is *why* it cannot change it | 8 |
| 03 | `CrawlerService` builds its `ProbeTarget` from `IOwnerPreferencesRepository`; `NpgsqlCrawlTargetRepository.DueAsync` excludes opted-out targets | §6.3's override and §11's opt-out | 13, 14 |
| 05 | Nothing. Plan 05 stays read-only; this plan adds its own write endpoints in `MUI.Web` under `/g/{slug}/claim` and `/dashboard/{slug}` | §8's surfaces are writes, and Plan 05's parity test greps *read* payloads | 16 |

---

## File structure

Everything this plan creates or touches, and what each file is responsible for.

### `src/MUI.Catalog` — pure domain, no dependencies

| File | Responsibility |
|---|---|
| `Claiming/ClaimToken.cs` | `ClaimTokenState`, `ClaimChannel`, `ClaimToken`, `ClaimAttempt` — the addendum §5 records, verbatim |
| `Claiming/ClaimTokenFormat.cs` | The unambiguous alphabet, the rendered shape, `New`, `IsWellFormed`, `Pattern` |
| `Claiming/ClaimVocabulary.cs` | `ClaimVocabulary` — the wire strings this plan, Plan 03 and the instructions page all read |
| `Claiming/ClaimDiagnostic.cs` | `ClaimDiagnostic` and `ClaimDiagnostics.For` — the after-three-probes report |
| `Ownership/Owner.cs` | `Owner`, `OwnerAuditActor`, `OwnerAuditEntry`, `OwnerActions` |
| `Ownership/OwnerPreferences.cs` | `WhoPreference`, `OwnerPreferences`, `OwnerEnrichmentFields` |
| `Ownership/MsspScorecard.cs` | `MsspFindingKind`, `MsspFinding`, `MsspScorecard` — the shape, not the analysis |

### `src/MUI.Storage` — schema and repositories

| File | Responsibility |
|---|---|
| `Migrations/0020_claim_token.sql` … `0024_owner_preferences.sql` | The five migrations of item 5 |
| `IClaimRepository.cs`, `NpgsqlClaimRepository.cs` | Tokens and attempts |
| `IOwnerRepository.cs`, `NpgsqlOwnerRepository.cs` | Owners, transfer, the audit log |
| `IOwnerPreferencesRepository.cs`, `NpgsqlOwnerPreferencesRepository.cs` | Per-game publishing preferences |
| `ICrawlOptOutRepository.cs`, `NpgsqlCrawlOptOutRepository.cs` | Opt-out for hosts with no game yet |
| `IGameRepository.cs` (modify) | `SetClaimedAsync` |

### `src/MUI.Discovery/Ownership` — behaviour; sees both a `ProbeResult` and catalogue state

| File | Responsibility |
|---|---|
| `ClaimTokenIssuer.cs` | Issue, regenerate, expire. One live token per game |
| `ClaimVerifier.cs` | The three channels, one at a time, recording every attempt |
| `ClaimCycle.cs` | The per-probe entry point the crawl loop calls. Cannot reach the schedule |
| `Dns/IDnsTxtResolver.cs` | `IDnsTxtResolver`, `DnsTxtLookup`, `DnsTxtStatus` |
| `Dns/DnsTxtResolver.cs` | The real resolver: bounded, cancellable, `DnsClient`-backed |
| `Dns/DnsClaimPoller.cs` | The one deliberately off-schedule check, hourly, for the token's life |
| `ClaimBeaconPolicy.cs` | Whether a presented token is decisive — item 4's guard |
| `OwnerFieldWriter.cs` | Owner enrichment through §5.1's ladder; confirmation without a change |
| `OwnerPreferenceService.cs` | Suppression, the WHO override, and how both reach the probe |
| `OptOutService.cs` | Dashboard, MSSP and DNS opt-out; honoured in one cycle, recorded |
| `MsspLinter.cs` | The continuous scorecard |

### `src/MUI.Web` — the surfaces

| File | Responsibility |
|---|---|
| `Claiming/ClaimEndpoints.cs` | `POST /g/{slug}/claim`, `GET /g/{slug}/claim`, `POST …/regenerate` |
| `Ownership/DashboardEndpoints.cs` | The dashboard's write surface |
| `Ownership/IOwnerPrincipal.cs` | The one seam a real authentication provider plugs into |

### Tests

| File | Responsibility |
|---|---|
| `tests/MUI.Catalog.Tests/Claiming/*.cs` | Token shape, alphabet, expiry, diagnostic |
| `tests/MUI.Storage.Tests/Claiming/*.cs` | The five migrations and four repositories, against a container |
| `tests/MUI.Discovery.Tests/Ownership/*.cs` | Everything behavioural, against fakes and `ProbeResult` fixtures |
| `tests/MUI.Discovery.Tests/Support/FakeDnsTxtResolver.cs` | The only resolver any behavioural test sees |
| `tests/MUI.Discovery.Tests/Support/LoopbackDnsServer.cs` | A real DNS TXT responder on `127.0.0.1:0`, for `DnsTxtResolver` alone |
| `tests/MUI.Discovery.Tests/Support/ClaimWorld.cs` | The rig: fake repositories, a fake probe, a fake clock |
| `tests/MUI.Web.Tests/Claiming/*.cs` | The endpoints |

---

### Task 1: The claim domain — records, the four states, and an alphabet a human retypes (spec §8)

An operator reads this token off a web page and types it into `mush.cnf` by hand. That single fact
decides the alphabet: `0`/`O` and `1`/`l`/`I` are out, because a claim that fails on a transcription
error produces exactly the support mail §8's diagnostic exists to prevent.

**Files:**
- Create: `src/MUI.Catalog/Claiming/ClaimToken.cs`
- Create: `src/MUI.Catalog/Claiming/ClaimTokenFormat.cs`
- Create: `src/MUI.Catalog/Claiming/ClaimVocabulary.cs`
- Create: `tests/MUI.Catalog.Tests/Claiming/ClaimTokenTests.cs`
- Create: `tests/MUI.Catalog.Tests/Claiming/ClaimTokenFormatTests.cs`

**Interfaces:**
- Consumes: nothing. `MUI.Catalog` has no dependencies and this task adds none.
- Produces:
  - `enum MUI.Catalog.ClaimTokenState { Pending, Verified, Expired, Revoked }`
  - `enum MUI.Catalog.ClaimChannel { Mssp, ConnectScreen, DnsTxt }`
  - `sealed record MUI.Catalog.ClaimToken(Guid Id, Guid GameId, string Value, ClaimTokenState State, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, ClaimChannel? VerifiedVia, DateTimeOffset? VerifiedAt, int ProbesSinceIssue)`
    with `static readonly TimeSpan Validity`, `bool IsExpired(DateTimeOffset now)`, `bool IsLive(DateTimeOffset now)`
  - `sealed record MUI.Catalog.ClaimAttempt(long Id, Guid TokenId, DateTimeOffset At, ClaimChannel Channel, bool Found, IReadOnlyList<string> MsspFieldsSeen)`
  - `static class MUI.Catalog.ClaimTokenFormat` with `Prefix`, `Alphabet`, `GroupCount`, `GroupLength`,
    `string New(RandomNumberGenerator? rng = null)`, `bool IsWellFormed(string? value)`,
    `string? FindIn(string? text)`, `Regex Pattern()`
  - `static class MUI.Catalog.ClaimVocabulary` with `MsspVariable`, `ConnectScreenPrefix`, `DnsLabel`,
    `AcceptedMsspVariables`, `OptOutMsspVariable`, `OptOutValue`, `string DnsNameFor(string host)`

- [ ] **Step 1: Write the failing test for the records**

Create `tests/MUI.Catalog.Tests/Claiming/ClaimTokenTests.cs`:

```csharp
namespace MUI.Catalog.Tests.Claiming;

/// <summary>
/// Spec §8. A token is issued for one game, is valid for fourteen days, and carries which of the
/// three channels proved it — because the diagnostic, the audit log and the beacon all need to say
/// how control was demonstrated, not merely that it was.
/// </summary>
public class ClaimTokenTests
{
    private static readonly DateTimeOffset Issued = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static ClaimToken Pending() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "muidx-a2b3-c4d5-e6f7",
            ClaimTokenState.Pending, Issued, Issued + ClaimToken.Validity,
            VerifiedVia: null, VerifiedAt: null, ProbesSinceIssue: 0);

    [Test]
    public async Task ATokenIsValidForFourteenDays()
    {
        // The design handoff's claim screen says "valid 14 days" in so many words, and the DNS
        // channel re-checks hourly "for 14 days" — one number, in one place.
        await Assert.That(ClaimToken.Validity).IsEqualTo(TimeSpan.FromDays(14));
        await Assert.That(Pending().ExpiresAt).IsEqualTo(Issued.AddDays(14));
    }

    [Test]
    public async Task ATokenIsNotExpiredOnTheLastDayAndIsExpiredAfterIt()
    {
        var token = Pending();

        await Assert.That(token.IsExpired(Issued)).IsFalse();
        await Assert.That(token.IsExpired(Issued.AddDays(14).AddSeconds(-1))).IsFalse();
        await Assert.That(token.IsExpired(Issued.AddDays(14))).IsTrue();
    }

    [Test]
    public async Task OnlyAPendingUnexpiredTokenIsLive()
    {
        // "Live" is the question every caller actually asks: may this token still be verified?
        // Verified, Revoked and Expired all answer no, for three different reasons.
        var token = Pending();

        await Assert.That(token.IsLive(Issued)).IsTrue();
        await Assert.That(token.IsLive(Issued.AddDays(20))).IsFalse();
        await Assert.That((token with { State = ClaimTokenState.Revoked }).IsLive(Issued)).IsFalse();
        await Assert.That((token with { State = ClaimTokenState.Expired }).IsLive(Issued)).IsFalse();
        await Assert.That((token with { State = ClaimTokenState.Verified }).IsLive(Issued)).IsFalse();
    }

    [Test]
    public async Task AVerifiedTokenRemembersWhichChannelProvedIt()
    {
        var verified = Pending() with
        {
            State = ClaimTokenState.Verified,
            VerifiedVia = ClaimChannel.DnsTxt,
            VerifiedAt = Issued.AddHours(3),
        };

        await Assert.That(verified.VerifiedVia).IsEqualTo(ClaimChannel.DnsTxt);
        await Assert.That(verified.VerifiedAt).IsEqualTo(Issued.AddHours(3));
    }

    [Test]
    public async Task AVerifiedTokenDoesNotStopBeingValidWhenItsWindowEnds()
    {
        // §7.3: the token persists after verification as the permanent identity beacon. The
        // fourteen days bound how long we will keep *looking*, not how long the claim lasts.
        var verified = Pending() with
        {
            State = ClaimTokenState.Verified,
            VerifiedVia = ClaimChannel.Mssp,
            VerifiedAt = Issued.AddHours(1),
        };

        await Assert.That(verified.State).IsEqualTo(ClaimTokenState.Verified);
        await Assert.That(verified.IsExpired(Issued.AddYears(3))).IsTrue();
        await Assert.That(verified.IsLive(Issued.AddYears(3))).IsFalse();
    }

    [Test]
    public async Task AnAttemptRecordsWhatWeSawAndNotJustThatWeLooked()
    {
        // This is the whole content of the §8 diagnostic: an operator who put the token in the
        // wrong variable can only tell if we say which variables were there.
        var attempt = new ClaimAttempt(1, Guid.CreateVersion7(), Issued, ClaimChannel.Mssp,
            Found: false, MsspFieldsSeen: ["CODEBASE", "CONTACT", "NAME", "PORT"]);

        await Assert.That(attempt.Found).IsFalse();
        await Assert.That(attempt.MsspFieldsSeen).Contains("CONTACT");
    }
}
```

- [ ] **Step 2: Write the failing test for the format and the vocabulary**

Create `tests/MUI.Catalog.Tests/Claiming/ClaimTokenFormatTests.cs`:

```csharp
using System.Security.Cryptography;

namespace MUI.Catalog.Tests.Claiming;

/// <summary>
/// The token is retyped by a human into <c>mush.cnf</c>, so the alphabet is chosen against
/// transcription rather than against density, and the rendered shape is self-identifying so a bare
/// token pasted into a connect screen can still be found (spec §8, §7.3).
/// </summary>
public class ClaimTokenFormatTests
{
    [Test]
    public async Task TheAlphabetHasNoCharacterAnyoneMisreads()
    {
        // 0/O and 1/l/I are the whole reason this is not hex or base64.
        foreach (var ambiguous in "01lIO")
        {
            await Assert.That(ClaimTokenFormat.Alphabet).DoesNotContain(ambiguous);
        }

        await Assert.That(ClaimTokenFormat.Alphabet.Distinct().Count())
            .IsEqualTo(ClaimTokenFormat.Alphabet.Length);
        await Assert.That(ClaimTokenFormat.Alphabet.All(char.IsLower) || ClaimTokenFormat.Alphabet.All(char.IsDigit)
            || ClaimTokenFormat.Alphabet.All(c => char.IsLower(c) || char.IsDigit(c))).IsTrue();
    }

    [Test]
    public async Task ANewTokenLooksLikeTheOneTheDesignShows()
    {
        // muidx-7f3a-c19e-4b02 — a prefix and three four-character groups.
        var token = ClaimTokenFormat.New();

        await Assert.That(token).StartsWith(ClaimTokenFormat.Prefix);
        await Assert.That(token.Length)
            .IsEqualTo(ClaimTokenFormat.Prefix.Length
                + ClaimTokenFormat.GroupCount * ClaimTokenFormat.GroupLength
                + (ClaimTokenFormat.GroupCount - 1));
        await Assert.That(token.Split('-').Length).IsEqualTo(ClaimTokenFormat.GroupCount + 1);
        await Assert.That(ClaimTokenFormat.IsWellFormed(token)).IsTrue();
    }

    [Test]
    public async Task TwoTokensAreNotTheSameToken()
    {
        var tokens = Enumerable.Range(0, 500).Select(_ => ClaimTokenFormat.New()).ToHashSet();

        await Assert.That(tokens.Count).IsEqualTo(500);
    }

    [Test]
    public async Task EveryCharacterComesFromTheAlphabet()
    {
        for (var i = 0; i < 200; i++)
        {
            var body = ClaimTokenFormat.New()[ClaimTokenFormat.Prefix.Length..].Replace("-", "");

            await Assert.That(body.All(ClaimTokenFormat.Alphabet.Contains)).IsTrue();
        }
    }

    [Test]
    public async Task TheGeneratorTakesItsBytesFromWhereverItIsToldTo()
    {
        // Injectable so a test can be deterministic; defaulted so no caller has to think about it.
        using var rng = RandomNumberGenerator.Create();

        await Assert.That(ClaimTokenFormat.IsWellFormed(ClaimTokenFormat.New(rng))).IsTrue();
    }

    [Test]
    public async Task AMalformedTokenIsNotWellFormed()
    {
        await Assert.That(ClaimTokenFormat.IsWellFormed(null)).IsFalse();
        await Assert.That(ClaimTokenFormat.IsWellFormed("")).IsFalse();
        await Assert.That(ClaimTokenFormat.IsWellFormed("7f3a-c19e-4b02")).IsFalse();
        await Assert.That(ClaimTokenFormat.IsWellFormed("muidx-7f3a-c19e")).IsFalse();
        await Assert.That(ClaimTokenFormat.IsWellFormed("muidx-7f3a-c19e-4b02-extra")).IsFalse();
        // 'o' and 'i' are not in the alphabet, so a token containing them was mistyped.
        await Assert.That(ClaimTokenFormat.IsWellFormed("muidx-o23a-c19e-4b02")).IsFalse();
    }

    [Test]
    public async Task ABareTokenIsFoundAnywhereInABanner()
    {
        // The design handoff tells an operator they may put it "anywhere in the banner, including
        // the bottom where players will not notice". That only works because the shape is
        // self-identifying.
        var token = ClaimTokenFormat.New();
        var banner = $"+={new string('=', 40)}=+\n  Welcome to Corvid.\n  {token}\n+={new string('=', 40)}=+";

        await Assert.That(ClaimTokenFormat.FindIn(banner)).IsEqualTo(token);
    }

    [Test]
    public async Task AnOrdinaryBannerContainsNoToken()
    {
        await Assert.That(ClaimTokenFormat.FindIn(
            "Welcome to Corvid. Type 'connect <name> <password>' or 'create'.")).IsNull();
        await Assert.That(ClaimTokenFormat.FindIn(null)).IsNull();
    }

    [Test]
    public async Task TheVocabularyNamesOneVariableAndAcceptsTheOneTheDesignShowed()
    {
        await Assert.That(ClaimVocabulary.MsspVariable).IsEqualTo("MUINDEX CLAIM");
        await Assert.That(ClaimVocabulary.AcceptedMsspVariables[0]).IsEqualTo(ClaimVocabulary.MsspVariable);
        await Assert.That(ClaimVocabulary.AcceptedMsspVariables).Contains("CONTACT_TOKEN");
    }

    [Test]
    public async Task ADnsNameIsTheLabelUnderTheGamesOwnHost()
    {
        await Assert.That(ClaimVocabulary.DnsNameFor("tidewater.example")).IsEqualTo("_muindex.tidewater.example");
        await Assert.That(ClaimVocabulary.DnsNameFor("Tidewater.Example.")).IsEqualTo("_muindex.tidewater.example");
    }

    [Test]
    public async Task AHostThatIsNotAHostHasNoDnsName()
    {
        await Assert.That(() => ClaimVocabulary.DnsNameFor("")).Throws<ArgumentException>();
        await Assert.That(() => ClaimVocabulary.DnsNameFor("  ")).Throws<ArgumentException>();
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'ClaimToken' could not be found`.

- [ ] **Step 4: Write the records**

Create `src/MUI.Catalog/Claiming/ClaimToken.cs`:

```csharp
namespace MUI.Catalog;

/// <summary>Where a claim token stands (spec §8).</summary>
public enum ClaimTokenState
{
    /// <summary>Issued, inside its window, and not yet seen on the wire.</summary>
    Pending,

    /// <summary>
    /// Seen through one of the three channels. The token is not discarded at this point: §7.3 makes
    /// it the permanent identity beacon, which is the concrete technical reason to claim.
    /// </summary>
    Verified,

    /// <summary>Fourteen days passed without the token appearing anywhere.</summary>
    Expired,

    /// <summary>Superseded by a regeneration, or withdrawn by staff or by a transfer.</summary>
    Revoked,
}

/// <summary>
/// The three places an owner may publish the token (spec §8). All three require server or DNS
/// access; none requires us to send mail or to trust a third-party registry.
/// </summary>
public enum ClaimChannel
{
    /// <summary>An MSSP variable — the easiest, because the crawler already reads MSSP.</summary>
    Mssp,

    /// <summary>A line on the connect screen — works on a codebase with no MSSP at all.</summary>
    ConnectScreen,

    /// <summary>
    /// A DNS TXT record. For operators who do not run the game daemon themselves — and the one
    /// channel a telnet probe cannot see, which is why this subsystem owns a resolver.
    /// </summary>
    DnsTxt,
}

/// <summary>
/// A token issued to one game, to be published through the game itself (spec §8).
/// </summary>
/// <param name="ProbesSinceIssue">
/// How many times we have looked since issuing. Drives the §8 diagnostic, which reports what we did
/// see once this reaches <see cref="ClaimDiagnostics.ProbesBeforeDiagnostic"/>.
/// </param>
public sealed record ClaimToken(
    Guid Id,
    Guid GameId,
    string Value,
    ClaimTokenState State,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    ClaimChannel? VerifiedVia,
    DateTimeOffset? VerifiedAt,
    int ProbesSinceIssue)
{
    /// <summary>
    /// How long we keep looking. Fourteen days is what the claim screen promises the operator in so
    /// many words, and what the hourly DNS re-check is bounded by.
    /// </summary>
    public static readonly TimeSpan Validity = TimeSpan.FromDays(14);

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Whether this token may still be verified: pending, and inside its window.</summary>
    public bool IsLive(DateTimeOffset now) => State is ClaimTokenState.Pending && !IsExpired(now);
}

/// <summary>
/// One look for one token through one channel (spec §8).
/// </summary>
/// <param name="MsspFieldsSeen">
/// Every MSSP variable the server reported on this visit, whether or not it was the one we wanted.
/// This is the entire content of the diagnostic: an operator who set the token on the wrong variable
/// cannot tell unless we show them the list they actually published.
/// </param>
public sealed record ClaimAttempt(
    long Id,
    Guid TokenId,
    DateTimeOffset At,
    ClaimChannel Channel,
    bool Found,
    IReadOnlyList<string> MsspFieldsSeen);
```

- [ ] **Step 5: Write the format**

Create `src/MUI.Catalog/Claiming/ClaimTokenFormat.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MUI.Catalog;

/// <summary>
/// How a claim token is rendered, and how one is found in text that is mostly not a token.
/// </summary>
/// <remarks>
/// <para>
/// <b>The alphabet is chosen against transcription, not against density.</b> An operator reads this
/// off a web page and types it into <c>mush.cnf</c>, so <c>0</c>/<c>O</c> and <c>1</c>/<c>l</c>/<c>I</c>
/// are excluded. Thirty characters over twelve positions is a shade under 59 bits, which is far past
/// anything guessable by a party who would then still have to publish it on the game's own server.
/// </para>
/// <para>
/// <b>The shape is self-identifying on purpose.</b> A bare token pasted anywhere into a connect screen
/// has to be findable without a label, because that is what the claim flow asks for; <c>muidx-</c>
/// followed by three groups from this alphabet is not a string a MUSH banner produces by accident.
/// </para>
/// </remarks>
public static class ClaimTokenFormat
{
    /// <summary>Marks the string as ours, in the one place a reader has no other context.</summary>
    public const string Prefix = "muidx-";

    /// <summary>Lower-case, digits and letters, minus every character anyone misreads.</summary>
    public const string Alphabet = "23456789abcdefghjkmnpqrstvwxyz";

    public const int GroupCount = 3;
    public const int GroupLength = 4;

    private static readonly Regex Compiled = new(
        $"{Prefix}[{Alphabet}]{{{GroupLength}}}(?:-[{Alphabet}]{{{GroupLength}}}){{{GroupCount - 1}}}",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(100));

    /// <summary>The pattern that finds a bare token in arbitrary text. Also read by Plan 3's beacon.</summary>
    public static Regex Pattern() => Compiled;

    /// <summary>A fresh token. Cryptographically random, rejection-sampled so the alphabet stays uniform.</summary>
    public static string New(RandomNumberGenerator? rng = null)
    {
        var builder = new StringBuilder(Prefix, Prefix.Length + GroupCount * (GroupLength + 1));

        for (var group = 0; group < GroupCount; group++)
        {
            if (group > 0)
            {
                builder.Append('-');
            }

            for (var position = 0; position < GroupLength; position++)
            {
                builder.Append(Alphabet[NextIndex(rng)]);
            }
        }

        return builder.ToString();
    }

    /// <summary>Whether a string is exactly one of our tokens and nothing else.</summary>
    public static bool IsWellFormed(string? value) =>
        value is not null
        && value.Length == Prefix.Length + GroupCount * GroupLength + (GroupCount - 1)
        && Compiled.IsMatch(value)
        && Compiled.Match(value).Length == value.Length;

    /// <summary>The first token embedded in <paramref name="text"/>, or null.</summary>
    public static string? FindIn(string? text) =>
        string.IsNullOrEmpty(text) ? null : Compiled.Match(text) is { Success: true } match ? match.Value : null;

    /// <summary>
    /// A uniform index into <see cref="Alphabet"/>. Rejection sampling rather than a modulo, because
    /// 256 is not a multiple of 30 and a modulo would make the first sixteen characters likelier.
    /// </summary>
    private static int NextIndex(RandomNumberGenerator? rng)
    {
        var limit = 256 - 256 % Alphabet.Length;
        Span<byte> buffer = stackalloc byte[1];

        while (true)
        {
            if (rng is null)
            {
                RandomNumberGenerator.Fill(buffer);
            }
            else
            {
                rng.GetBytes(buffer);
            }

            if (buffer[0] < limit)
            {
                return buffer[0] % Alphabet.Length;
            }
        }
    }
}
```

- [ ] **Step 6: Write the vocabulary**

Create `src/MUI.Catalog/Claiming/ClaimVocabulary.cs`:

```csharp
namespace MUI.Catalog;

/// <summary>
/// The wire vocabulary of a claim: the three places an operator may put the token, named once.
/// </summary>
/// <remarks>
/// <para>
/// Read by <c>MUI.Discovery.ClaimTokenBeacon</c> (which finds an unknown token on a probe), by
/// <c>ClaimVerifier</c> (which looks for a known one), and by the claim page's own instructions — so
/// the page cannot tell an operator one variable name while the crawler looks for another. That is
/// the entire reason these constants are in <c>MUI.Catalog</c>, which every side can see, rather
/// than beside either reader.
/// </para>
/// <para>
/// <b>Two of these disagree with the delivered design and the disagreement is deliberate.</b> The
/// design handoff shows <c>CONTACT_TOKEN</c> as the MSSP variable and a bare token on the connect
/// screen. <c>CONTACT_TOKEN</c> reads as a qualifier on MSSP's official <c>CONTACT</c> and is not
/// obviously ours, so <c>MUINDEX CLAIM</c> is canonical and <c>CONTACT_TOKEN</c> is accepted —
/// operators follow screenshots, and failing a claim that was typed exactly as shown is the support
/// mail the diagnostic exists to prevent. The labelled connect-screen form is what the page renders,
/// because a bare token is indistinguishable from noise to the admin who finds it six months later;
/// a bare well-formed token still verifies, via <see cref="ClaimTokenFormat.FindIn"/>.
/// </para>
/// </remarks>
public static class ClaimVocabulary
{
    /// <summary>The MSSP variable the instructions name and the crawler prefers.</summary>
    public const string MsspVariable = "MUINDEX CLAIM";

    /// <summary>The labelled connect-screen form, e.g. <c>MUINDEX-CLAIM: muidx-a2b3-c4d5-e6f7</c>.</summary>
    public const string ConnectScreenPrefix = "MUINDEX-CLAIM:";

    /// <summary>The DNS label the TXT record sits under: <c>_muindex.&lt;host&gt;</c>.</summary>
    public const string DnsLabel = "_muindex";

    /// <summary>
    /// Every MSSP variable a token is honoured in, canonical first. Order is precedence: the first
    /// one carrying a value wins.
    /// </summary>
    public static IReadOnlyList<string> AcceptedMsspVariables { get; } = [MsspVariable, "CONTACT_TOKEN"];

    /// <summary>
    /// The MSSP variable an operator sets to make us stop, with no claim and no correspondence
    /// (spec §11, and the design handoff's crawler-transparency panel).
    /// </summary>
    public const string OptOutMsspVariable = "CRAWL_OPT_OUT";

    /// <summary>The TXT value that means the same thing at <c>_muindex.&lt;host&gt;</c>.</summary>
    public const string OptOutValue = "optout";

    /// <summary>The fully-qualified name a claim or opt-out TXT record lives at.</summary>
    public static string DnsNameFor(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        return $"{DnsLabel}.{host.Trim().TrimEnd('.').ToLowerInvariant()}";
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
```
Expected: PASS — 16 new tests, plus everything Plans 02 and earlier left green.

- [ ] **Step 8: Commit**

```bash
git add src/MUI.Catalog/Claiming tests/MUI.Catalog.Tests/Claiming
git commit -m "feat(catalog): the claim token, its four states, and an alphabet a human can retype (spec 8)"
```

---

### Task 2: `IClaimRepository`, the in-memory fake, and `ClaimTokenIssuer` (spec §8)

One live token per game, regenerable, and regenerating invalidates the previous one — otherwise an
operator who regenerated because they lost the first email would find two strings both working, and
a revoked token that still verifies is a revoked token that still votes at weight 10.0 later.

**Files:**
- Create: `src/MUI.Storage/Claiming/IClaimRepository.cs`
- Create: `src/MUI.Discovery/Ownership/ClaimTokenIssuer.cs`
- Create: `tests/MUI.Discovery.Tests/Support/InMemoryClaimRepository.cs`
- Create: `tests/MUI.Discovery.Tests/Ownership/ClaimTokenIssuerTests.cs`

**Interfaces:**
- Consumes: `ClaimToken`, `ClaimTokenState`, `ClaimChannel`, `ClaimAttempt`, `ClaimTokenFormat` (Task 1);
  `ManualTimeProvider` (Plan 02 Task 14 declares it in `tests/MUI.Discovery.Tests/Support`; Plan 03
  extends the same file with `CreateTimer`. It is deliberately **not** called `FakeTimeProvider`,
  which would collide with `Microsoft.Extensions.Time.Testing.FakeTimeProvider`).
- Produces:
  - `interface MUI.Storage.IClaimRepository` with
    `Task<ClaimToken?> ByIdAsync(Guid id, CancellationToken ct)`,
    `Task<ClaimToken?> LiveForGameAsync(Guid gameId, DateTimeOffset now, CancellationToken ct)`,
    `Task<ClaimToken?> VerifiedForGameAsync(Guid gameId, CancellationToken ct)`,
    `Task<IReadOnlyList<ClaimToken>> PendingAsync(DateTimeOffset now, CancellationToken ct)`,
    `Task<ClaimToken?> ByValueAsync(string value, CancellationToken ct)`,
    `Task InsertAsync(ClaimToken token, CancellationToken ct)`,
    `Task SetStateAsync(Guid id, ClaimTokenState state, ClaimChannel? verifiedVia, DateTimeOffset? verifiedAt, CancellationToken ct)`,
    `Task RecordProbeAsync(Guid id, CancellationToken ct)`,
    `Task<long> AppendAttemptAsync(ClaimAttempt attempt, CancellationToken ct)`,
    `Task<IReadOnlyList<ClaimAttempt>> AttemptsAsync(Guid tokenId, CancellationToken ct)`
  - `sealed class MUI.Discovery.Ownership.ClaimTokenIssuer(IClaimRepository claims, TimeProvider time)`
    with `Task<ClaimToken> IssueAsync(Guid gameId, CancellationToken ct)`,
    `Task<ClaimToken> RegenerateAsync(Guid gameId, CancellationToken ct)`,
    `Task<int> ExpireLapsedAsync(CancellationToken ct)`,
    `Task RevokeAsync(Guid gameId, CancellationToken ct)`
  - `MUI.Discovery.Tests.Support.InMemoryClaimRepository : IClaimRepository` with public `List<ClaimToken> Tokens`
    and `List<ClaimAttempt> Attempts`

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/Ownership/ClaimTokenIssuerTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery.Ownership;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests.Ownership;

/// <summary>
/// Spec §8: the site issues a token. One per game, valid fourteen days, regenerable — and
/// regenerating invalidates the previous one, because two strings that both work is a claim flow
/// nobody can reason about and a beacon that votes twice.
/// </summary>
public class ClaimTokenIssuerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed record Rig(ClaimTokenIssuer Issuer, InMemoryClaimRepository Claims, ManualTimeProvider Time);

    private static Rig Subject()
    {
        var claims = new InMemoryClaimRepository();
        var time = new ManualTimeProvider(Now);

        return new Rig(new ClaimTokenIssuer(claims, time), claims, time);
    }

    [Test]
    public async Task AnIssuedTokenIsPendingForFourteenDays()
    {
        var rig = Subject();
        var game = Guid.CreateVersion7();

        var token = await rig.Issuer.IssueAsync(game, None);

        await Assert.That(token.GameId).IsEqualTo(game);
        await Assert.That(token.State).IsEqualTo(ClaimTokenState.Pending);
        await Assert.That(token.IssuedAt).IsEqualTo(Now);
        await Assert.That(token.ExpiresAt).IsEqualTo(Now + ClaimToken.Validity);
        await Assert.That(token.ProbesSinceIssue).IsEqualTo(0);
        await Assert.That(ClaimTokenFormat.IsWellFormed(token.Value)).IsTrue();
    }

    [Test]
    public async Task AskingTwiceReturnsTheSameTokenRatherThanMintingASecond()
    {
        // The claim page is reloaded, bookmarked and shared. Reloading it must not silently
        // invalidate the string the operator already pasted into mush.cnf.
        var rig = Subject();
        var game = Guid.CreateVersion7();

        var first = await rig.Issuer.IssueAsync(game, None);
        var second = await rig.Issuer.IssueAsync(game, None);

        await Assert.That(second.Id).IsEqualTo(first.Id);
        await Assert.That(second.Value).IsEqualTo(first.Value);
        await Assert.That(rig.Claims.Tokens.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RegeneratingRevokesThePreviousOne()
    {
        var rig = Subject();
        var game = Guid.CreateVersion7();
        var first = await rig.Issuer.IssueAsync(game, None);

        var second = await rig.Issuer.RegenerateAsync(game, None);

        await Assert.That(second.Value).IsNotEqualTo(first.Value);
        await Assert.That(second.State).IsEqualTo(ClaimTokenState.Pending);
        await Assert.That(rig.Claims.Tokens.Single(t => t.Id == first.Id).State)
            .IsEqualTo(ClaimTokenState.Revoked);
        await Assert.That(await rig.Claims.LiveForGameAsync(game, Now, None))
            .IsEqualTo(second);
    }

    [Test]
    public async Task AnExpiredTokenIsReplacedRatherThanReturned()
    {
        var rig = Subject();
        var game = Guid.CreateVersion7();
        var first = await rig.Issuer.IssueAsync(game, None);

        rig.Time.Advance(ClaimToken.Validity + TimeSpan.FromDays(1));
        var second = await rig.Issuer.IssueAsync(game, None);

        await Assert.That(second.Value).IsNotEqualTo(first.Value);
        await Assert.That(second.ExpiresAt).IsEqualTo(rig.Time.GetUtcNow() + ClaimToken.Validity);
    }

    [Test]
    public async Task AVerifiedGameIsNotIssuedAFreshTokenByAccident()
    {
        // The verified token is the permanent beacon (§7.3). Handing out a new one on a page reload
        // would rotate the identity signal for nothing. Transfer regenerates explicitly (Task 11).
        var rig = Subject();
        var game = Guid.CreateVersion7();
        var token = await rig.Issuer.IssueAsync(game, None);
        await rig.Claims.SetStateAsync(token.Id, ClaimTokenState.Verified, ClaimChannel.Mssp, Now, None);

        var again = await rig.Issuer.IssueAsync(game, None);

        await Assert.That(again.Id).IsEqualTo(token.Id);
        await Assert.That(again.State).IsEqualTo(ClaimTokenState.Verified);
        await Assert.That(rig.Claims.Tokens.Count).IsEqualTo(1);
    }

    [Test]
    public async Task LapsedTokensAreExpiredInABatchAndVerifiedOnesAreNotTouched()
    {
        var rig = Subject();
        var lapsing = await rig.Issuer.IssueAsync(Guid.CreateVersion7(), None);
        var claimed = await rig.Issuer.IssueAsync(Guid.CreateVersion7(), None);
        await rig.Claims.SetStateAsync(claimed.Id, ClaimTokenState.Verified, ClaimChannel.DnsTxt, Now, None);

        rig.Time.Advance(ClaimToken.Validity + TimeSpan.FromHours(1));
        var expired = await rig.Issuer.ExpireLapsedAsync(None);

        await Assert.That(expired).IsEqualTo(1);
        await Assert.That(rig.Claims.Tokens.Single(t => t.Id == lapsing.Id).State)
            .IsEqualTo(ClaimTokenState.Expired);
        await Assert.That(rig.Claims.Tokens.Single(t => t.Id == claimed.Id).State)
            .IsEqualTo(ClaimTokenState.Verified);
    }

    [Test]
    public async Task RevokingClearsEveryTokenTheGameHasIncludingAVerifiedOne()
    {
        // Used by staff and by transfer. A verified token that survives a revoke keeps voting at
        // IdentityWeights.ClaimToken for ever (Task 10).
        var rig = Subject();
        var game = Guid.CreateVersion7();
        var token = await rig.Issuer.IssueAsync(game, None);
        await rig.Claims.SetStateAsync(token.Id, ClaimTokenState.Verified, ClaimChannel.Mssp, Now, None);

        await rig.Issuer.RevokeAsync(game, None);

        await Assert.That(rig.Claims.Tokens.Single().State).IsEqualTo(ClaimTokenState.Revoked);
        await Assert.That(await rig.Claims.VerifiedForGameAsync(game, None)).IsNull();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'ClaimTokenIssuer' could not be found`.

- [ ] **Step 3: Write the repository interface**

Create `src/MUI.Storage/Claiming/IClaimRepository.cs`:

```csharp
using MUI.Catalog;

namespace MUI.Storage;

/// <summary>
/// Claim tokens and the attempts made to verify them (spec §8).
/// </summary>
/// <remarks>
/// A game has at most one token that is not <see cref="ClaimTokenState.Revoked"/> or
/// <see cref="ClaimTokenState.Expired"/> — enforced by a partial unique index rather than by
/// convention, because two live tokens for one game is a state no reader can render.
/// </remarks>
public interface IClaimRepository
{
    Task<ClaimToken?> ByIdAsync(Guid id, CancellationToken ct);

    /// <summary>The pending, unexpired token for a game, if it has one.</summary>
    Task<ClaimToken?> LiveForGameAsync(Guid gameId, DateTimeOffset now, CancellationToken ct);

    /// <summary>The verified token for a game — the §7.3 beacon — if it has one.</summary>
    Task<ClaimToken?> VerifiedForGameAsync(Guid gameId, CancellationToken ct);

    /// <summary>Every pending token still inside its window, for the DNS poller and the expirer.</summary>
    Task<IReadOnlyList<ClaimToken>> PendingAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>Whoever a token belongs to, looked up by the value seen on the wire.</summary>
    Task<ClaimToken?> ByValueAsync(string value, CancellationToken ct);

    Task InsertAsync(ClaimToken token, CancellationToken ct);

    Task SetStateAsync(
        Guid id, ClaimTokenState state, ClaimChannel? verifiedVia, DateTimeOffset? verifiedAt, CancellationToken ct);

    /// <summary>Increments <see cref="ClaimToken.ProbesSinceIssue"/> — one look, whatever it found.</summary>
    Task RecordProbeAsync(Guid id, CancellationToken ct);

    Task<long> AppendAttemptAsync(ClaimAttempt attempt, CancellationToken ct);

    Task<IReadOnlyList<ClaimAttempt>> AttemptsAsync(Guid tokenId, CancellationToken ct);
}
```

- [ ] **Step 4: Write the issuer**

Create `src/MUI.Discovery/Ownership/ClaimTokenIssuer.cs`:

```csharp
using MUI.Catalog;
using MUI.Storage;

namespace MUI.Discovery.Ownership;

/// <summary>
/// Issues, regenerates, expires and revokes claim tokens (spec §8).
/// </summary>
/// <remarks>
/// <b>Issuing is idempotent and regenerating is not.</b> The claim page is reloaded, bookmarked and
/// shared between the two people who administer a game, so <see cref="IssueAsync"/> returning a
/// fresh string each time would silently invalidate whatever one of them had already pasted into
/// <c>mush.cnf</c>. Rotating the token is therefore an explicit gesture, and it revokes its
/// predecessor in the same breath — a superseded token that still verifies is a token that still
/// votes at <c>IdentityWeights.ClaimToken</c> afterwards (Task 10).
/// </remarks>
public sealed class ClaimTokenIssuer(IClaimRepository claims, TimeProvider time)
{
    /// <summary>The game's current token: the live one, the verified one, or a new one.</summary>
    public async Task<ClaimToken> IssueAsync(Guid gameId, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        if (await claims.VerifiedForGameAsync(gameId, ct) is { } verified)
        {
            return verified;
        }

        if (await claims.LiveForGameAsync(gameId, now, ct) is { } live)
        {
            return live;
        }

        return await MintAsync(gameId, now, ct);
    }

    /// <summary>A new token, revoking whatever the game had. Also the first half of a transfer.</summary>
    public async Task<ClaimToken> RegenerateAsync(Guid gameId, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        await RevokeAsync(gameId, ct);

        return await MintAsync(gameId, now, ct);
    }

    /// <summary>Moves every lapsed pending token to <see cref="ClaimTokenState.Expired"/>.</summary>
    public async Task<int> ExpireLapsedAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var expired = 0;

        foreach (var token in await claims.PendingAsync(DateTimeOffset.MaxValue, ct))
        {
            if (token.IsExpired(now))
            {
                await claims.SetStateAsync(token.Id, ClaimTokenState.Expired, null, null, ct);
                expired++;
            }
        }

        return expired;
    }

    /// <summary>Revokes every token a game holds, verified ones included.</summary>
    public async Task RevokeAsync(Guid gameId, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        if (await claims.LiveForGameAsync(gameId, now, ct) is { } live)
        {
            await claims.SetStateAsync(live.Id, ClaimTokenState.Revoked, null, null, ct);
        }

        if (await claims.VerifiedForGameAsync(gameId, ct) is { } verified)
        {
            await claims.SetStateAsync(verified.Id, ClaimTokenState.Revoked, null, null, ct);
        }
    }

    private async Task<ClaimToken> MintAsync(Guid gameId, DateTimeOffset now, CancellationToken ct)
    {
        var token = new ClaimToken(
            Guid.CreateVersion7(),
            gameId,
            ClaimTokenFormat.New(),
            ClaimTokenState.Pending,
            now,
            now + ClaimToken.Validity,
            VerifiedVia: null,
            VerifiedAt: null,
            ProbesSinceIssue: 0);

        await claims.InsertAsync(token, ct);

        return token;
    }
}
```

- [ ] **Step 5: Write the in-memory fake**

Create `tests/MUI.Discovery.Tests/Support/InMemoryClaimRepository.cs`:

```csharp
using MUI.Catalog;
using MUI.Storage;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// The claim store every behavioural test in this plan runs against. Public lists, so a test asserts
/// on what was written rather than on what a method returned.
/// </summary>
public sealed class InMemoryClaimRepository : IClaimRepository
{
    public List<ClaimToken> Tokens { get; } = [];

    public List<ClaimAttempt> Attempts { get; } = [];

    public Task<ClaimToken?> ByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Tokens.SingleOrDefault(token => token.Id == id));

    public Task<ClaimToken?> LiveForGameAsync(Guid gameId, DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult(Tokens.SingleOrDefault(token => token.GameId == gameId && token.IsLive(now)));

    public Task<ClaimToken?> VerifiedForGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult(Tokens.SingleOrDefault(
            token => token.GameId == gameId && token.State is ClaimTokenState.Verified));

    public Task<IReadOnlyList<ClaimToken>> PendingAsync(DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ClaimToken>>(
            Tokens.Where(token => token.State is ClaimTokenState.Pending && token.ExpiresAt > now
                                  || token.State is ClaimTokenState.Pending && now == DateTimeOffset.MaxValue)
                .ToList());

    public Task<ClaimToken?> ByValueAsync(string value, CancellationToken ct) =>
        Task.FromResult(Tokens.SingleOrDefault(
            token => string.Equals(token.Value, value, StringComparison.OrdinalIgnoreCase)));

    public Task InsertAsync(ClaimToken token, CancellationToken ct)
    {
        Tokens.Add(token);

        return Task.CompletedTask;
    }

    public Task SetStateAsync(
        Guid id, ClaimTokenState state, ClaimChannel? verifiedVia, DateTimeOffset? verifiedAt, CancellationToken ct)
    {
        Replace(id, token => token with { State = state, VerifiedVia = verifiedVia, VerifiedAt = verifiedAt });

        return Task.CompletedTask;
    }

    public Task RecordProbeAsync(Guid id, CancellationToken ct)
    {
        Replace(id, token => token with { ProbesSinceIssue = token.ProbesSinceIssue + 1 });

        return Task.CompletedTask;
    }

    public Task<long> AppendAttemptAsync(ClaimAttempt attempt, CancellationToken ct)
    {
        var id = Attempts.Count + 1L;
        Attempts.Add(attempt with { Id = id });

        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<ClaimAttempt>> AttemptsAsync(Guid tokenId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ClaimAttempt>>(
            Attempts.Where(attempt => attempt.TokenId == tokenId).OrderBy(attempt => attempt.At).ToList());

    private void Replace(Guid id, Func<ClaimToken, ClaimToken> change)
    {
        var index = Tokens.FindIndex(token => token.Id == id);
        if (index >= 0)
        {
            Tokens[index] = change(Tokens[index]);
        }
    }
}
```

Note the `PendingAsync` shape: `ExpireLapsedAsync` passes `DateTimeOffset.MaxValue` because it wants
every pending token including the lapsed ones, and the DNS poller passes `now` because it wants only
the ones still worth looking for. One method, two callers, and the boundary spelled out rather than
implied.

- [ ] **Step 6: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
```
Expected: PASS — 7 new tests.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Storage/Claiming src/MUI.Discovery/Ownership \
        tests/MUI.Discovery.Tests/Support/InMemoryClaimRepository.cs \
        tests/MUI.Discovery.Tests/Ownership
git commit -m "feat: issue claim tokens — one per game, fourteen days, regenerating revokes (spec 8)"
```

---

### Task 3: `claim_token`, `claim_attempt`, and `NpgsqlClaimRepository` (spec §8)

**Files:**
- Create: `src/MUI.Storage/Migrations/0020_claim_token.sql`
- Create: `src/MUI.Storage/Migrations/0021_claim_attempt.sql`
- Create: `src/MUI.Storage/Claiming/NpgsqlClaimRepository.cs`
- Create: `tests/MUI.Storage.Tests/Claiming/ClaimRepositoryTests.cs`

**Interfaces:**
- Consumes: `MigrationRunner`, `PostgresFixture.MigratedAsync`, `TestDatabase`, `GameSeed.InsertAsync`,
  `SqlEnums` (Plan 02 Tasks 1, 6, 8); `IClaimRepository`, `ClaimToken`, `ClaimAttempt` (Tasks 1–2).
- Produces:
  - `sealed class MUI.Storage.NpgsqlClaimRepository(NpgsqlDataSource source) : IClaimRepository`
  - Tables `claim_token` and `claim_attempt`, with the partial unique index that makes "one live
    token per game" a schema fact.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Storage.Tests/Claiming/ClaimRepositoryTests.cs`:

```csharp
using Dapper;

using MUI.Catalog;
using MUI.Storage.Tests.Support;

namespace MUI.Storage.Tests.Claiming;

/// <summary>
/// Spec §8. The invariant worth putting in the schema is "one live token per game": two of them is
/// a state the claim page cannot render and the beacon cannot resolve, so it fails at the database
/// rather than becoming something a reader has to cope with.
/// </summary>
public class ClaimRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private static ClaimToken Pending(Guid gameId) =>
        new(Guid.CreateVersion7(), gameId, ClaimTokenFormat.New(), ClaimTokenState.Pending,
            Now, Now + ClaimToken.Validity, null, null, 0);

    [Test]
    public async Task APendingTokenRoundTrips()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await GameSeed.InsertAsync(db.DataSource);
        var claims = new NpgsqlClaimRepository(db.DataSource);
        var token = Pending(game);

        await claims.InsertAsync(token, None);

        await Assert.That(await claims.ByIdAsync(token.Id, None)).IsEqualTo(token);
        await Assert.That(await claims.LiveForGameAsync(game, Now, None)).IsEqualTo(token);
        await Assert.That(await claims.ByValueAsync(token.Value, None)).IsEqualTo(token);
    }

    [Test]
    public async Task AGameCannotHaveTwoLiveTokens()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await GameSeed.InsertAsync(db.DataSource);
        var claims = new NpgsqlClaimRepository(db.DataSource);
        await claims.InsertAsync(Pending(game), None);

        await Assert.That(async () => await claims.InsertAsync(Pending(game), None))
            .Throws<Npgsql.PostgresException>();
    }

    [Test]
    public async Task AGameMayHaveAFreshTokenOnceTheOldOneIsRevoked()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await GameSeed.InsertAsync(db.DataSource);
        var claims = new NpgsqlClaimRepository(db.DataSource);
        var first = Pending(game);
        await claims.InsertAsync(first, None);

        await claims.SetStateAsync(first.Id, ClaimTokenState.Revoked, null, null, None);
        var second = Pending(game);
        await claims.InsertAsync(second, None);

        await Assert.That((await claims.LiveForGameAsync(game, Now, None))!.Id).IsEqualTo(second.Id);
    }

    [Test]
    public async Task TwoGamesCannotShareATokenValue()
    {
        // The value is what a probe presents, and Task 10 makes it decisive. Two games behind one
        // string is an identity collision we would then have to arbitrate.
        await using var db = await PostgresFixture.MigratedAsync();
        var claims = new NpgsqlClaimRepository(db.DataSource);
        var first = Pending(await GameSeed.InsertAsync(db.DataSource));
        await claims.InsertAsync(first, None);

        await Assert.That(async () => await claims.InsertAsync(
            first with { Id = Guid.CreateVersion7(), GameId = await GameSeed.InsertAsync(db.DataSource) }, None))
            .Throws<Npgsql.PostgresException>();
    }

    [Test]
    public async Task VerifyingRecordsTheChannelAndTheInstant()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await GameSeed.InsertAsync(db.DataSource);
        var claims = new NpgsqlClaimRepository(db.DataSource);
        var token = Pending(game);
        await claims.InsertAsync(token, None);

        await claims.SetStateAsync(token.Id, ClaimTokenState.Verified, ClaimChannel.DnsTxt, Now.AddHours(2), None);

        var verified = await claims.VerifiedForGameAsync(game, None);

        await Assert.That(verified!.VerifiedVia).IsEqualTo(ClaimChannel.DnsTxt);
        await Assert.That(verified.VerifiedAt).IsEqualTo(Now.AddHours(2));
        await Assert.That(await claims.LiveForGameAsync(game, Now, None)).IsNull();
    }

    [Test]
    public async Task ARecordedProbeIncrementsTheCounterTheDiagnosticReads()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var claims = new NpgsqlClaimRepository(db.DataSource);
        var token = Pending(await GameSeed.InsertAsync(db.DataSource));
        await claims.InsertAsync(token, None);

        await claims.RecordProbeAsync(token.Id, None);
        await claims.RecordProbeAsync(token.Id, None);
        await claims.RecordProbeAsync(token.Id, None);

        await Assert.That((await claims.ByIdAsync(token.Id, None))!.ProbesSinceIssue).IsEqualTo(3);
    }

    [Test]
    public async Task AnAttemptKeepsTheFieldListItSaw()
    {
        // The list is the diagnostic. Storing it as text[] rather than JSON because it is a list of
        // short identifiers and Postgres can then be asked "which claims never saw MSSP at all".
        await using var db = await PostgresFixture.MigratedAsync();
        var claims = new NpgsqlClaimRepository(db.DataSource);
        var token = Pending(await GameSeed.InsertAsync(db.DataSource));
        await claims.InsertAsync(token, None);

        await claims.AppendAttemptAsync(
            new ClaimAttempt(0, token.Id, Now, ClaimChannel.Mssp, false, ["CODEBASE", "CONTACT", "NAME"]), None);
        await claims.AppendAttemptAsync(
            new ClaimAttempt(0, token.Id, Now.AddHours(6), ClaimChannel.ConnectScreen, false, []), None);

        var attempts = await claims.AttemptsAsync(token.Id, None);

        await Assert.That(attempts.Count).IsEqualTo(2);
        await Assert.That(attempts[0].MsspFieldsSeen).IsEquivalentTo(new[] { "CODEBASE", "CONTACT", "NAME" });
        await Assert.That(attempts[1].Channel).IsEqualTo(ClaimChannel.ConnectScreen);
        await Assert.That(attempts[1].MsspFieldsSeen).IsEmpty();
    }

    [Test]
    public async Task PendingListsOnlyTokensStillWorthLookingFor()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var claims = new NpgsqlClaimRepository(db.DataSource);
        var live = Pending(await GameSeed.InsertAsync(db.DataSource));
        var lapsed = Pending(await GameSeed.InsertAsync(db.DataSource)) with
        {
            ExpiresAt = Now.AddDays(-1),
        };
        await claims.InsertAsync(live, None);
        await claims.InsertAsync(lapsed, None);

        await Assert.That((await claims.PendingAsync(Now, None)).Select(t => t.Id))
            .IsEquivalentTo(new[] { live.Id });
        await Assert.That((await claims.PendingAsync(DateTimeOffset.MaxValue, None)).Count).IsEqualTo(2);
    }

    [Test]
    public async Task AnAttemptCannotOutliveItsToken()
    {
        // Nothing is deleted in this system except a cascade nobody can observe: a token row is only
        // ever removed with the game it belongs to, and its attempts go with it.
        await using var db = await PostgresFixture.MigratedAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();

        var constraint = await connection.QuerySingleAsync<string>(
            """
            SELECT confdeltype::text FROM pg_constraint
            WHERE conrelid = 'claim_attempt'::regclass AND contype = 'f'
            """);

        await Assert.That(constraint).IsEqualTo("c");
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'NpgsqlClaimRepository' could not be found`.

- [ ] **Step 3: Write `0020_claim_token.sql`**

Create `src/MUI.Storage/Migrations/0020_claim_token.sql`:

```sql
-- Spec §8. A site-issued token an owner publishes through the game itself, proving server or DNS
-- access. After verification it is not discarded: §7.3 makes it the permanent identity beacon, so a
-- verified row lives as long as the game does.
CREATE TABLE claim_token (
    id                  uuid PRIMARY KEY,
    game_id             uuid        NOT NULL REFERENCES game (id) ON DELETE CASCADE,
    value               text        NOT NULL,
    state               text        NOT NULL,
    issued_at           timestamptz NOT NULL,
    expires_at          timestamptz NOT NULL,
    verified_via        text        NULL,
    verified_at         timestamptz NULL,
    probes_since_issue  integer     NOT NULL DEFAULT 0,

    CONSTRAINT claim_token_state_declared
        CHECK (state IN ('pending', 'verified', 'expired', 'revoked')),
    CONSTRAINT claim_token_channel_declared
        CHECK (verified_via IS NULL OR verified_via IN ('mssp', 'connect_screen', 'dns_txt')),

    -- A verified token knows how and when it was proved, or it is not verified. The audit log, the
    -- dashboard and the beacon all read those two columns and none of them can render a null.
    CONSTRAINT claim_token_verification_is_complete
        CHECK ((state = 'verified') = (verified_via IS NOT NULL AND verified_at IS NOT NULL)),

    CONSTRAINT claim_token_window_is_forwards CHECK (expires_at > issued_at),
    CONSTRAINT claim_token_probes_not_negative CHECK (probes_since_issue >= 0)
);

-- The value is what a probe presents and what Task 10 makes decisive at weight 10.0. Two games
-- behind one string is an identity collision with no correct arbitration.
CREATE UNIQUE INDEX claim_token_value_is_unique ON claim_token (value);

-- One live token per game, as a schema fact rather than a convention. 'expired' and 'revoked' rows
-- stay for the audit trail and are excluded here; 'verified' is included because a game with a
-- proved claim must not simultaneously be running a second claim through a different channel.
CREATE UNIQUE INDEX claim_token_one_live_per_game
    ON claim_token (game_id)
    WHERE state IN ('pending', 'verified');

CREATE INDEX claim_token_pending_by_expiry
    ON claim_token (expires_at)
    WHERE state = 'pending';
```

- [ ] **Step 4: Write `0021_claim_attempt.sql`**

Create `src/MUI.Storage/Migrations/0021_claim_attempt.sql`:

```sql
-- Spec §8's diagnostic, in table form. Every look, through every channel, with the MSSP variables
-- the server actually reported — which is the whole content of "we did not see the token, and here
-- is what we did see". An attempt row is why an operator who set the token on the wrong variable can
-- work that out without writing to us.
CREATE TABLE claim_attempt (
    id                bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    token_id          uuid        NOT NULL REFERENCES claim_token (id) ON DELETE CASCADE,
    at                timestamptz NOT NULL,
    channel           text        NOT NULL,
    found             boolean     NOT NULL,

    -- Short identifiers, so text[] rather than jsonb: it keeps "which claims never saw MSSP at all"
    -- a one-line query instead of a JSON traversal.
    mssp_fields_seen  text[]      NOT NULL DEFAULT '{}',

    CONSTRAINT claim_attempt_channel_declared
        CHECK (channel IN ('mssp', 'connect_screen', 'dns_txt'))
);

CREATE INDEX claim_attempt_by_token ON claim_attempt (token_id, at DESC);
```

- [ ] **Step 5: Write the repository**

Create `src/MUI.Storage/Claiming/NpgsqlClaimRepository.cs`:

```csharp
using Dapper;

using MUI.Catalog;

using Npgsql;

namespace MUI.Storage;

/// <summary>PostgreSQL-backed claim tokens and attempts (spec §8).</summary>
public sealed class NpgsqlClaimRepository(NpgsqlDataSource source) : IClaimRepository
{
    private const string Columns =
        "id, game_id, value, state, issued_at, expires_at, verified_via, verified_at, probes_since_issue";

    public async Task<ClaimToken?> ByIdAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        return Map(await connection.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition($"SELECT {Columns} FROM claim_token WHERE id = @id", new { id }, cancellationToken: ct)));
    }

    public async Task<ClaimToken?> LiveForGameAsync(Guid gameId, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        return Map(await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM claim_token WHERE game_id = @gameId AND state = 'pending' AND expires_at > @now",
            new { gameId, now }, cancellationToken: ct)));
    }

    public async Task<ClaimToken?> VerifiedForGameAsync(Guid gameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        return Map(await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM claim_token WHERE game_id = @gameId AND state = 'verified'",
            new { gameId }, cancellationToken: ct)));
    }

    public async Task<IReadOnlyList<ClaimToken>> PendingAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // DateTimeOffset.MaxValue is the expirer asking for the lapsed ones too; every other caller
        // passes the clock and gets only what is still worth looking for.
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM claim_token WHERE state = 'pending' AND (@now = @max OR expires_at > @now) ORDER BY issued_at",
            new { now, max = DateTimeOffset.MaxValue }, cancellationToken: ct));

        return rows.Select(row => Map(row)!).ToList();
    }

    public async Task<ClaimToken?> ByValueAsync(string value, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        return Map(await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM claim_token WHERE value = @value", new { value }, cancellationToken: ct)));
    }

    public async Task InsertAsync(ClaimToken token, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO claim_token (id, game_id, value, state, issued_at, expires_at,
                                     verified_via, verified_at, probes_since_issue)
            VALUES (@Id, @GameId, @Value, @State, @IssuedAt, @ExpiresAt, @VerifiedVia, @VerifiedAt, @ProbesSinceIssue)
            """,
            new
            {
                token.Id,
                token.GameId,
                token.Value,
                State = SqlEnums.ToDb(token.State),
                token.IssuedAt,
                token.ExpiresAt,
                VerifiedVia = token.VerifiedVia is { } via ? SqlEnums.ToDb(via) : null,
                token.VerifiedAt,
                token.ProbesSinceIssue,
            },
            cancellationToken: ct));
    }

    public async Task SetStateAsync(
        Guid id, ClaimTokenState state, ClaimChannel? verifiedVia, DateTimeOffset? verifiedAt, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE claim_token
               SET state = @state, verified_via = @verifiedVia, verified_at = @verifiedAt
             WHERE id = @id
            """,
            new
            {
                id,
                state = SqlEnums.ToDb(state),
                verifiedVia = verifiedVia is { } via ? SqlEnums.ToDb(via) : null,
                verifiedAt,
            },
            cancellationToken: ct));
    }

    public async Task RecordProbeAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE claim_token SET probes_since_issue = probes_since_issue + 1 WHERE id = @id",
            new { id }, cancellationToken: ct));
    }

    public async Task<long> AppendAttemptAsync(ClaimAttempt attempt, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO claim_attempt (token_id, at, channel, found, mssp_fields_seen)
            VALUES (@TokenId, @At, @channel, @Found, @fields)
            RETURNING id
            """,
            new
            {
                attempt.TokenId,
                attempt.At,
                channel = SqlEnums.ToDb(attempt.Channel),
                attempt.Found,
                fields = attempt.MsspFieldsSeen.ToArray(),
            },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ClaimAttempt>> AttemptsAsync(Guid tokenId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<AttemptRow>(new CommandDefinition(
            """
            SELECT id, token_id, at, channel, found, mssp_fields_seen
              FROM claim_attempt WHERE token_id = @tokenId ORDER BY at, id
            """,
            new { tokenId }, cancellationToken: ct));

        return rows.Select(row => new ClaimAttempt(
            row.Id, row.Token_Id, row.At, SqlEnums.ToClaimChannel(row.Channel), row.Found,
            row.Mssp_Fields_Seen ?? [])).ToList();
    }

    private static ClaimToken? Map(Row? row) =>
        row is null
            ? null
            : new ClaimToken(
                row.Id, row.Game_Id, row.Value, SqlEnums.ToClaimTokenState(row.State),
                row.Issued_At, row.Expires_At,
                row.Verified_Via is null ? null : SqlEnums.ToClaimChannel(row.Verified_Via),
                row.Verified_At, row.Probes_Since_Issue);

    private sealed record Row(
        Guid Id, Guid Game_Id, string Value, string State, DateTimeOffset Issued_At, DateTimeOffset Expires_At,
        string? Verified_Via, DateTimeOffset? Verified_At, int Probes_Since_Issue);

    private sealed record AttemptRow(
        long Id, Guid Token_Id, DateTimeOffset At, string Channel, bool Found, string[]? Mssp_Fields_Seen);
}
```

- [ ] **Step 6: Teach `SqlEnums` the two new enums**

`SqlEnums` (Plan 02 Task 6) already snake-cases an enum name to its stored spelling and back. Add the
two readers this plan needs, in `src/MUI.Storage/SqlEnums.cs`, beside the existing ones:

```csharp
    public static ClaimTokenState ToClaimTokenState(string value) => Parse<ClaimTokenState>(value);

    public static ClaimChannel ToClaimChannel(string value) => Parse<ClaimChannel>(value);
```

`ToDb` is already generic over `Enum` and needs no change: `ClaimChannel.ConnectScreen` snake-cases to
`connect_screen` and `ClaimChannel.DnsTxt` to `dns_txt`, which is exactly what the two `CHECK`
constraints above declare.

- [ ] **Step 7: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests </dev/null
```
Expected: PASS — 9 new tests. (Requires Docker for Testcontainers.)

- [ ] **Step 8: Commit**

```bash
git add src/MUI.Storage/Migrations/0020_claim_token.sql \
        src/MUI.Storage/Migrations/0021_claim_attempt.sql \
        src/MUI.Storage/Claiming/NpgsqlClaimRepository.cs \
        src/MUI.Storage/SqlEnums.cs \
        tests/MUI.Storage.Tests/Claiming/ClaimRepositoryTests.cs
git commit -m "feat(storage): claim_token and claim_attempt, with one live token per game in the schema"
```

---

### Task 4: `ClaimVerifier` — the two channels a probe can already see (spec §8, §6.2)

§8: "all three are verified by the crawler that already exists". For two of the three that is
literally true — the MSSP variable and the connect-screen line are both sitting in a `ProbeResult`
that has already been captured, so this task touches no socket and no resolver.

**Files:**
- Create: `src/MUI.Discovery/Ownership/ClaimVerifier.cs`
- Modify: `src/MUI.Discovery/Identity.cs` (`ClaimTokenBeacon` reads `ClaimVocabulary` rather than
  declaring the wire literals itself)
- Create: `tests/MUI.Discovery.Tests/Ownership/ClaimVerifierTests.cs`
- Create: `tests/MUI.Discovery.Tests/Ownership/ClaimVocabularyAgreementTests.cs`

**Interfaces:**
- Consumes: `ProbeResult`, `MsspData` (`MUI.Crawl.Mssp`), `MUI.Crawl.Who.AnsiText.Strip` (Plan 01);
  `ClaimToken`, `ClaimChannel`, `ClaimVocabulary`, `ClaimTokenFormat` (Task 1); `IClaimRepository`,
  `InMemoryClaimRepository` (Task 2).
- Produces:
  - `sealed record MUI.Discovery.Ownership.ClaimEvidence(bool Found, ClaimChannel Channel, IReadOnlyList<string> MsspFieldsSeen)`
  - `sealed class MUI.Discovery.Ownership.ClaimVerifier(IClaimRepository claims, IDnsTxtResolver dns, TimeProvider time)`
    with `ClaimEvidence ReadMssp(ClaimToken token, ProbeResult result)`,
    `ClaimEvidence ReadConnectScreen(ClaimToken token, ProbeResult result)`
    (the DNS reader arrives in Task 6; the `IDnsTxtResolver` parameter is introduced in Task 5 and is
    unused until then — Task 5 is the very next task and splitting the constructor twice would churn
    every call site for nothing)
  - `MUI.Discovery.ClaimTokenBeacon` — `MsspVariable`, `ConnectScreenPrefix`, `Read(ProbeResult) → string?`

**Note for the implementer:** Task 5 introduces `IDnsTxtResolver`. To keep this task's deliverable
independently testable, declare the one-line interface here as part of Step 4 — the full record type
and the real resolver are Task 5's. It is nine lines and it stops this task's constructor changing
shape a task later.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/Ownership/ClaimVerifierTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Discovery.Ownership;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests.Ownership;

/// <summary>
/// Spec §8's first two channels. Both are read off a captured ProbeResult, which is the point:
/// verifying a claim needs no probe of its own, because the crawler was going to visit anyway.
/// </summary>
public class ClaimVerifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private const string Token = "muidx-a2b3-c4d5-e6f7";

    private static ClaimToken Pending() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Token, ClaimTokenState.Pending,
            Now, Now + ClaimToken.Validity, null, null, 0);

    private static ClaimVerifier Subject() =>
        new(new InMemoryClaimRepository(), new FakeDnsTxtResolver(), new ManualTimeProvider(Now));

    [Test]
    public async Task TheTokenIsFoundInTheCanonicalMsspVariable()
    {
        var evidence = Subject().ReadMssp(Pending(), ProbeResults.Answered(
            mssp: ProbeResults.Mssp(
                ("NAME", ["Corvid"]), (ClaimVocabulary.MsspVariable, [Token]))));

        await Assert.That(evidence.Found).IsTrue();
        await Assert.That(evidence.Channel).IsEqualTo(ClaimChannel.Mssp);
    }

    [Test]
    public async Task TheTokenIsAlsoFoundInTheVariableTheDeliveredDesignShowed()
    {
        // An operator who followed the screenshot must not be told their claim failed.
        var evidence = Subject().ReadMssp(Pending(), ProbeResults.Answered(
            mssp: ProbeResults.Mssp(("CONTACT_TOKEN", [Token]))));

        await Assert.That(evidence.Found).IsTrue();
    }

    [Test]
    public async Task WhitespaceAroundTheValueDoesNotDefeatIt()
    {
        // mush.cnf lines get trailing spaces. A claim that fails on one is a support mail.
        var evidence = Subject().ReadMssp(Pending(), ProbeResults.Answered(
            mssp: ProbeResults.Mssp((ClaimVocabulary.MsspVariable, ["  " + Token + " "]))));

        await Assert.That(evidence.Found).IsTrue();
    }

    [Test]
    public async Task CaseDoesNotDefeatItEither()
    {
        var evidence = Subject().ReadMssp(Pending(), ProbeResults.Answered(
            mssp: ProbeResults.Mssp((ClaimVocabulary.MsspVariable, [Token.ToUpperInvariant()]))));

        await Assert.That(evidence.Found).IsTrue();
    }

    [Test]
    public async Task SomebodyElsesTokenIsNotOurs()
    {
        var evidence = Subject().ReadMssp(Pending(), ProbeResults.Answered(
            mssp: ProbeResults.Mssp((ClaimVocabulary.MsspVariable, ["muidx-9999-8888-7777"]))));

        await Assert.That(evidence.Found).IsFalse();
    }

    [Test]
    public async Task ANotFoundReadingNamesEveryMsspVariableTheServerDidReport()
    {
        // This is the diagnostic's entire raw material (§8): an operator who put the token in the
        // wrong variable can only work that out from the list they actually published.
        var evidence = Subject().ReadMssp(Pending(), ProbeResults.Answered(
            mssp: ProbeResults.Mssp(
                ("NAME", ["Corvid"]), ("PORT", ["4201"]), ("CODEBASE", ["PennMUSH"]),
                ("CONTACT", ["admin@example.org"]), ("FAMILY", ["TinyMUD"]))));

        await Assert.That(evidence.Found).IsFalse();
        await Assert.That(evidence.MsspFieldsSeen)
            .IsEquivalentTo(new[] { "CODEBASE", "CONTACT", "FAMILY", "NAME", "PORT" });
    }

    [Test]
    public async Task AServerWithNoMsspAtAllReportsAnEmptyListRatherThanNothing()
    {
        // "We saw no MSSP variables" and "we did not look" render very differently on the claim page.
        var evidence = Subject().ReadMssp(Pending(), ProbeResults.Answered(banner: "Login:"));

        await Assert.That(evidence.Found).IsFalse();
        await Assert.That(evidence.MsspFieldsSeen).IsEmpty();
    }

    [Test]
    public async Task TheLabelledFormIsFoundOnTheConnectScreen()
    {
        var evidence = Subject().ReadConnectScreen(Pending(), ProbeResults.Answered(
            banner: $"Welcome to Corvid.\n{ClaimVocabulary.ConnectScreenPrefix} {Token}\nType 'connect'."));

        await Assert.That(evidence.Found).IsTrue();
        await Assert.That(evidence.Channel).IsEqualTo(ClaimChannel.ConnectScreen);
    }

    [Test]
    public async Task ABareTokenAnywhereInTheBannerIsFoundToo()
    {
        // The delivered design tells operators "anywhere in the banner, including the bottom where
        // players will not notice", and that has to actually work.
        var evidence = Subject().ReadConnectScreen(Pending(), ProbeResults.Answered(
            banner: $"+========================+\n|  Corvid                |\n+========================+\n{Token}\n"));

        await Assert.That(evidence.Found).IsTrue();
    }

    [Test]
    public async Task ColourDoesNotHideTheToken()
    {
        // An operator pastes the line into a coloured banner, so the SGR runs straight through the
        // middle of it. This is why the banner is ANSI-stripped before matching.
        var evidence = Subject().ReadConnectScreen(Pending(), ProbeResults.Answered(
            banner: $"\e[1;36mWelcome to Corvid.\e[0m\n\e[2m{ClaimVocabulary.ConnectScreenPrefix} \e[0m\e[2m{Token}\e[0m\n"));

        await Assert.That(evidence.Found).IsTrue();
    }

    [Test]
    public async Task AnEmptyBannerIsNotAMatch()
    {
        await Assert.That(Subject().ReadConnectScreen(Pending(), ProbeResults.Answered(banner: null)).Found)
            .IsFalse();
        await Assert.That(Subject().ReadConnectScreen(Pending(), ProbeResults.Answered(banner: "")).Found)
            .IsFalse();
    }

    [Test]
    public async Task AConnectScreenReadingCarriesTheMsspListAsWell()
    {
        // Every attempt records what MSSP said, whichever channel it was looking at, because the
        // diagnostic is one report over all of them rather than one per channel.
        var evidence = Subject().ReadConnectScreen(Pending(), ProbeResults.Answered(
            banner: "Login:", mssp: ProbeResults.Mssp(("NAME", ["Corvid"]))));

        await Assert.That(evidence.MsspFieldsSeen).IsEquivalentTo(new[] { "NAME" });
    }
}
```

- [ ] **Step 2: Write the failing agreement test**

This is the test that stops the two halves drifting apart. Create
`tests/MUI.Discovery.Tests/Ownership/ClaimVocabularyAgreementTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Discovery.Ownership;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests.Ownership;

/// <summary>
/// Plan 3 reads an <em>unknown</em> beacon off a probe; this plan verifies a <em>known</em> token.
/// They are different jobs and they must not be different vocabularies: a game that verifies through
/// a channel Plan 3 cannot read is a claim that works and a beacon that never fires — the failure
/// mode where everything looks wired up and nothing does anything (§7.3).
/// </summary>
public class ClaimVocabularyAgreementTests
{
    private const string Token = "muidx-a2b3-c4d5-e6f7";

    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static ClaimToken Pending() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Token, ClaimTokenState.Pending,
            Now, Now + ClaimToken.Validity, null, null, 0);

    private static ClaimVerifier Subject() =>
        new(new InMemoryClaimRepository(), new FakeDnsTxtResolver(), new ManualTimeProvider(Now));

    [Test]
    public async Task TheBeaconReadsTheConstantsThisPlanDeclares()
    {
        await Assert.That(ClaimTokenBeacon.MsspVariable).IsEqualTo(ClaimVocabulary.MsspVariable);
        await Assert.That(ClaimTokenBeacon.ConnectScreenPrefix).IsEqualTo(ClaimVocabulary.ConnectScreenPrefix);
    }

    [Test]
    public async Task EveryChannelTheVerifierAcceptsIsAChannelTheBeaconCanRead()
    {
        var probes = new[]
        {
            ProbeResults.Answered(mssp: ProbeResults.Mssp((ClaimVocabulary.MsspVariable, [Token]))),
            ProbeResults.Answered(mssp: ProbeResults.Mssp(("CONTACT_TOKEN", [Token]))),
            ProbeResults.Answered(banner: $"{ClaimVocabulary.ConnectScreenPrefix} {Token}"),
            ProbeResults.Answered(banner: $"Corvid\n   {Token}\n"),
        };

        foreach (var probe in probes)
        {
            var verified = Subject().ReadMssp(Pending(), probe).Found
                           || Subject().ReadConnectScreen(Pending(), probe).Found;

            await Assert.That(verified).IsTrue();
            await Assert.That(ClaimTokenBeacon.Read(probe)).IsEqualTo(Token);
        }
    }

    [Test]
    public async Task TheBeaconReadsNothingOutOfAnOrdinaryProbe()
    {
        await Assert.That(ClaimTokenBeacon.Read(ProbeResults.Answered(
            banner: "Welcome to Corvid. Type 'connect <name> <password>'.",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]))))).IsNull();
    }
}
```

- [ ] **Step 3: Run both to verify they fail**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'ClaimVerifier' could not be found`.
(`ClaimTokenBeacon.Read` already exists; Plan 03 declares it. What is missing here is this plan's
verifier and the agreement between the two.)

- [ ] **Step 4: Declare the resolver seam Task 5 fills in**

Create `src/MUI.Discovery/Ownership/Dns/IDnsTxtResolver.cs`:

```csharp
namespace MUI.Discovery.Ownership;

/// <summary>
/// Reads TXT records. Spec §8's third channel is the one the existing crawler genuinely cannot do —
/// nothing in a telnet session can see a DNS record — so this subsystem owns a resolver of its own.
/// </summary>
/// <remarks>The record type and the real implementation land in Task 5; this is the seam.</remarks>
public interface IDnsTxtResolver
{
    Task<DnsTxtLookup> LookupAsync(string name, CancellationToken ct);
}
```

- [ ] **Step 5: Write the verifier**

Create `src/MUI.Discovery/Ownership/ClaimVerifier.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Crawl.Who;
using MUI.Storage;

namespace MUI.Discovery.Ownership;

/// <summary>
/// What one look through one channel found, and what MSSP said while we were there.
/// </summary>
/// <param name="MsspFieldsSeen">
/// Every variable the server reported, sorted, whatever channel this reading was of. The claim
/// diagnostic is one report across all three channels rather than one per channel, so every
/// reading carries the list.
/// </param>
public sealed record ClaimEvidence(bool Found, ClaimChannel Channel, IReadOnlyList<string> MsspFieldsSeen);

/// <summary>
/// Looks for a known token in the three places spec §8 permits.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two of the three cost nothing.</b> The MSSP variable and the connect-screen line are already in
/// the <c>ProbeResult</c> the crawler captured on its ordinary visit, so verifying them is a read over
/// data we hold — no socket, no schedule change, and a full test suite with no network in it.
/// </para>
/// <para>
/// <b>Matching is deliberately forgiving about presentation and exact about value.</b> Trailing
/// whitespace from a <c>mush.cnf</c> line, a different case, and SGR sequences wrapped round a pasted
/// banner line are all transcription accidents; a different token is a different game.
/// </para>
/// </remarks>
public sealed class ClaimVerifier(IClaimRepository claims, IDnsTxtResolver dns, TimeProvider time)
{
    /// <summary>Spec §8's first channel: an MSSP variable, the easiest because we already read MSSP.</summary>
    public ClaimEvidence ReadMssp(ClaimToken token, ProbeResult result)
    {
        var seen = FieldsSeen(result);

        foreach (var variable in ClaimVocabulary.AcceptedMsspVariables)
        {
            if (Matches(token.Value, result.Mssp.Default(variable)))
            {
                return new ClaimEvidence(true, ClaimChannel.Mssp, seen);
            }
        }

        return new ClaimEvidence(false, ClaimChannel.Mssp, seen);
    }

    /// <summary>
    /// Spec §8's second channel: a line on the connect screen, which works on a codebase with no MSSP
    /// at all. Both the labelled form and a bare well-formed token are honoured — the page shows the
    /// label, and the delivered design's "anywhere in the banner" instruction still verifies.
    /// </summary>
    public ClaimEvidence ReadConnectScreen(ClaimToken token, ProbeResult result)
    {
        var seen = FieldsSeen(result);

        if (result.Banner is not { Length: > 0 } banner)
        {
            return new ClaimEvidence(false, ClaimChannel.ConnectScreen, seen);
        }

        // The connect screen is stored *with* its ANSI (spec §6.2) because it is a display asset.
        // This is the parsing side of the same bytes: an operator pastes the line into a coloured
        // banner and the SGR ends up inside it.
        var plain = AnsiText.Strip(banner);
        var found = plain.Contains(token.Value, StringComparison.OrdinalIgnoreCase)
                    || Matches(token.Value, ClaimTokenFormat.FindIn(plain));

        return new ClaimEvidence(found, ClaimChannel.ConnectScreen, seen);
    }

    private static IReadOnlyList<string> FieldsSeen(ProbeResult result) =>
        result.Mssp.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();

    private static bool Matches(string expected, string? candidate) =>
        candidate is not null && string.Equals(candidate.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 6: Point Plan 03's beacon at the shared vocabulary**

`src/MUI.Discovery/Identity.cs` already declares `ClaimTokenBeacon`; this step only makes it read
`ClaimVocabulary` rather than its own literals. The whole type is reproduced here so the two halves
can be diffed against each other — if Plan 03's copy has drifted from this, **Plan 03 is the
authority on everything but the vocabulary reference**:

```csharp
/// <summary>
/// Reads a claim-token beacon off a probe (spec §7.3, §8).
/// </summary>
/// <remarks>
/// <para>
/// <b>This reads a beacon; it does not model a token.</b> The issued record is
/// <c>MUI.Catalog.ClaimToken</c> (Plan 6), which knows who it was issued to, when it expires and
/// through which channel it was proved. This type answers one narrower question: what token, if any,
/// is the host on the other end of this probe emitting? It does not know whose it is.
/// </para>
/// <para>
/// The wire vocabulary is <c>MUI.Catalog.ClaimVocabulary</c> and is deliberately not re-declared
/// here. The claim page's instructions render from the same constants, so an operator cannot be told
/// one variable name while this looks for another.
/// </para>
/// <para>
/// Two of §8's three channels are visible to a probe. The third — a DNS TXT record — is not, and is
/// the claiming subsystem's own (Plan 6, Task 5).
/// </para>
/// </remarks>
public static class ClaimTokenBeacon
{
    /// <summary>The MSSP variable the site asks owners to set.</summary>
    public const string MsspVariable = ClaimVocabulary.MsspVariable;

    /// <summary>The labelled connect-screen form, e.g. <c>MUINDEX-CLAIM: muidx-a2b3-c4d5-e6f7</c>.</summary>
    public const string ConnectScreenPrefix = ClaimVocabulary.ConnectScreenPrefix;

    /// <summary>The token this probe carries, from any channel a probe can see, or null.</summary>
    public static string? Read(ProbeResult result)
    {
        foreach (var variable in ClaimVocabulary.AcceptedMsspVariables)
        {
            if (result.Mssp.Default(variable) is { } declared && !string.IsNullOrWhiteSpace(declared))
            {
                return declared.Trim();
            }
        }

        if (result.Banner is not { Length: > 0 } banner)
        {
            return null;
        }

        var plain = AnsiText.Strip(banner);
        var start = plain.IndexOf(ConnectScreenPrefix, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            var rest = plain[(start + ConnectScreenPrefix.Length)..];
            var labelled = new string(rest.TrimStart().TakeWhile(ch => !char.IsWhiteSpace(ch)).ToArray());
            if (labelled.Length > 0)
            {
                return labelled;
            }
        }

        // A bare, well-formed token, which is what the delivered claim flow asks an operator to paste.
        // Findable without a label only because the rendering is self-identifying.
        return ClaimTokenFormat.FindIn(plain);
    }
}
```

Add `using MUI.Catalog;` and `using MUI.Crawl.Who;` to the file's usings. **No call site moves.** The
beacon has exactly one caller in Plan 03 — `ClaimTokenBeacon.Read(result)` at the top of
`IdentityMatcher.ResolveAsync`, in `src/MUI.Discovery/IdentityMatcher.cs` — and it is already spelled
that way; `IdentityCorpusTests` asserts the constants and is where Plan 03's claim-token cases live.
The change here is confined to where the two constants come from.

- [ ] **Step 7: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
```
Expected: PASS — 12 verifier tests, 3 agreement tests, and every Plan 03 identity test still green.

- [ ] **Step 8: Commit**

```bash
git add src/MUI.Discovery/Ownership/ClaimVerifier.cs \
        src/MUI.Discovery/Ownership/Dns/IDnsTxtResolver.cs \
        src/MUI.Discovery/Identity.cs \
        tests/MUI.Discovery.Tests/Ownership
git commit -m "feat: verify a claim from the MSSP variable and the connect screen, off a captured probe"
```

---

### Task 5: `DnsTxtResolver` — the one channel the existing crawler cannot do (spec §8, §12)

**Say this plainly, because it is the single most load-bearing sentence in this plan.** Spec §8 claims
"all three are verified by the crawler that already exists". That is true of the MSSP variable and the
connect-screen line, and it is **not true of the DNS TXT record**: nothing in a telnet session can see
a DNS record, `TelnetNegotiationCore` has no notion of one, and `ProbeResult` carries no field that
could ever hold one. The third channel needs a resolver of its own, and this task builds it.

It is worth the cost. The DNS channel is the one an operator can use when they do not run the game
daemon themselves — a hosted game, a volunteer with web access and no shell — and the design handoff
names exactly that case.

**Files:**
- Modify: `Directory.Packages.props` (add `DnsClient`)
- Modify: `src/MUI.Discovery/MUI.Discovery.csproj`
- Modify: `src/MUI.Discovery/Ownership/Dns/IDnsTxtResolver.cs` (the record types)
- Create: `src/MUI.Discovery/Ownership/Dns/DnsTxtResolver.cs`
- Create: `tests/MUI.Discovery.Tests/Support/FakeDnsTxtResolver.cs`
- Create: `tests/MUI.Discovery.Tests/Support/LoopbackDnsServer.cs`
- Create: `tests/MUI.Discovery.Tests/Ownership/DnsTxtResolverTests.cs`

**Interfaces:**
- Consumes: `ClaimVocabulary.DnsNameFor` (Task 1).
- Produces:
  - `enum MUI.Discovery.Ownership.DnsTxtStatus { Answered, NoRecord, Unavailable }`
  - `sealed record MUI.Discovery.Ownership.DnsTxtLookup(DnsTxtStatus Status, IReadOnlyList<string> Values)`
    with `static readonly DnsTxtLookup NoRecord`, `static readonly DnsTxtLookup Unavailable`,
    `static DnsTxtLookup Of(params string[] values)`
  - `sealed class MUI.Discovery.Ownership.DnsTxtResolver(DnsTxtResolverOptions options, ILogger<DnsTxtResolver>? logger = null) : IDnsTxtResolver`
  - `sealed record MUI.Discovery.Ownership.DnsTxtResolverOptions` with `Timeout`, `NameServers`
  - `MUI.Discovery.Tests.Support.FakeDnsTxtResolver : IDnsTxtResolver` with
    `void Answer(string name, params string[] values)`, `void Unavailable(string name)`,
    `List<string> Queried`
  - `MUI.Discovery.Tests.Support.LoopbackDnsServer : IAsyncDisposable` with `int Port`,
    `void Answer(string name, params string[] values)`, `static Task<LoopbackDnsServer> StartAsync()`

- [ ] **Step 1: Add the package**

In `Directory.Packages.props`, beside the existing entries:

```xml
    <PackageVersion Include="DnsClient" Version="1.8.0" />
```

In `src/MUI.Discovery/MUI.Discovery.csproj`, inside the existing `<ItemGroup>`:

```xml
    <PackageReference Include="DnsClient" />
```

`System.Net.Dns` resolves names to addresses and cannot query a TXT record at all, so this is not a
dependency taken for convenience. `DnsClient` is MIT, pure managed, and — the part that matters —
reads the platform's configured name servers, which on Linux means `/etc/resolv.conf` and on Windows
means the adapter configuration. Re-deriving that per platform is exactly the sort of thing that works
on the developer's laptop and not in the container.

- [ ] **Step 2: Write the failing test**

Create `tests/MUI.Discovery.Tests/Ownership/DnsTxtResolverTests.cs`:

```csharp
using System.Diagnostics;
using System.Net;

using MUI.Catalog;
using MUI.Discovery.Ownership;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests.Ownership;

/// <summary>
/// The real resolver, against a DNS server this repository starts on 127.0.0.1. There is no live DNS
/// in this suite and there must never be: a test that depends on somebody else's zone file is a test
/// that fails on a plane.
/// </summary>
/// <remarks>
/// Spec §12 applies here exactly as it does to a probe — hard-bounded by timeout and
/// <see cref="CancellationToken"/> — because the crawler runs in-process with the web tier and a
/// wedged lookup must not be able to starve request threads.
/// </remarks>
public class DnsTxtResolverTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static DnsTxtResolver Against(int port, TimeSpan? timeout = null) =>
        new(new DnsTxtResolverOptions
        {
            NameServers = [new IPEndPoint(IPAddress.Loopback, port)],
            Timeout = timeout ?? TimeSpan.FromSeconds(2),
        });

    [Test]
    public async Task ATxtRecordComesBack()
    {
        await using var server = await LoopbackDnsServer.StartAsync();
        server.Answer("_muindex.tidewater.example", "muidx-a2b3-c4d5-e6f7");

        var lookup = await Against(server.Port).LookupAsync("_muindex.tidewater.example", None);

        await Assert.That(lookup.Status).IsEqualTo(DnsTxtStatus.Answered);
        await Assert.That(lookup.Values).IsEquivalentTo(new[] { "muidx-a2b3-c4d5-e6f7" });
    }

    [Test]
    public async Task SeveralTxtRecordsAllComeBack()
    {
        // An operator may already have SPF-style records under the same label, or may be running a
        // claim and an opt-out at once. Returning only the first would be a coin toss.
        await using var server = await LoopbackDnsServer.StartAsync();
        server.Answer("_muindex.tidewater.example", "v=spf1 -all", "muidx-a2b3-c4d5-e6f7");

        var lookup = await Against(server.Port).LookupAsync("_muindex.tidewater.example", None);

        await Assert.That(lookup.Values.Count).IsEqualTo(2);
        await Assert.That(lookup.Values).Contains("muidx-a2b3-c4d5-e6f7");
    }

    [Test]
    public async Task ANameWithNoRecordIsNoRecordAndNotAFailure()
    {
        // "The operator has not added it yet" and "we could not ask" are different facts and the
        // claim page says different things about them.
        await using var server = await LoopbackDnsServer.StartAsync();

        var lookup = await Against(server.Port).LookupAsync("_muindex.nothing.example", None);

        await Assert.That(lookup.Status).IsEqualTo(DnsTxtStatus.NoRecord);
        await Assert.That(lookup.Values).IsEmpty();
    }

    [Test]
    public async Task ASilentResolverIsUnavailableWithinTheTimeout()
    {
        // Spec §12. The bound is real, not aspirational, so it is measured. A Stopwatch and not the
        // ambient clock: this is elapsed wall time against a real socket, which is the one thing
        // TimeProvider cannot fake and the one case Global Constraints exempts.
        await using var server = await LoopbackDnsServer.StartAsync();
        server.GoSilent();

        var elapsed = Stopwatch.StartNew();
        var lookup = await Against(server.Port, TimeSpan.FromMilliseconds(400))
            .LookupAsync("_muindex.tidewater.example", None);

        await Assert.That(lookup.Status).IsEqualTo(DnsTxtStatus.Unavailable);
        await Assert.That(elapsed.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task CancellationIsHonouredRatherThanWaitedOut()
    {
        await using var server = await LoopbackDnsServer.StartAsync();
        server.GoSilent();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var lookup = await Against(server.Port, TimeSpan.FromSeconds(30))
            .LookupAsync("_muindex.tidewater.example", cancelled.Token);

        await Assert.That(lookup.Status).IsEqualTo(DnsTxtStatus.Unavailable);
    }

    [Test]
    public async Task TheNameQueriedIsTheOneTheVocabularyBuilds()
    {
        await using var server = await LoopbackDnsServer.StartAsync();
        server.Answer(ClaimVocabulary.DnsNameFor("tidewater.example"), "muidx-a2b3-c4d5-e6f7");

        var lookup = await Against(server.Port)
            .LookupAsync(ClaimVocabulary.DnsNameFor("TIDEWATER.example."), None);

        await Assert.That(lookup.Status).IsEqualTo(DnsTxtStatus.Answered);
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'DnsTxtResolver' could not be found`.

- [ ] **Step 4: Write the lookup types**

Replace `src/MUI.Discovery/Ownership/Dns/IDnsTxtResolver.cs` with:

```csharp
namespace MUI.Discovery.Ownership;

/// <summary>What a TXT lookup came back with.</summary>
/// <remarks>
/// The three cases are kept apart for the same reason spec §5.4 keeps three renderings of an hour
/// apart: "the operator has not added the record yet" and "we could not reach a resolver" are
/// different facts, and collapsing them would tell a waiting owner their DNS is wrong when ours is.
/// </remarks>
public enum DnsTxtStatus
{
    Answered,
    NoRecord,
    Unavailable,
}

/// <summary>Every TXT string at one name, in the order the resolver gave them.</summary>
public sealed record DnsTxtLookup(DnsTxtStatus Status, IReadOnlyList<string> Values)
{
    public static readonly DnsTxtLookup NoRecord = new(DnsTxtStatus.NoRecord, []);

    public static readonly DnsTxtLookup Unavailable = new(DnsTxtStatus.Unavailable, []);

    public static DnsTxtLookup Of(params string[] values) =>
        values.Length == 0 ? NoRecord : new DnsTxtLookup(DnsTxtStatus.Answered, values);
}

/// <summary>
/// Reads TXT records. Spec §8's third channel is the one the existing crawler genuinely cannot do —
/// nothing in a telnet session can see a DNS record, and <c>ProbeResult</c> has no field that could
/// hold one — so this subsystem owns a resolver of its own.
/// </summary>
public interface IDnsTxtResolver
{
    Task<DnsTxtLookup> LookupAsync(string name, CancellationToken ct);
}
```

- [ ] **Step 5: Write the resolver**

Create `src/MUI.Discovery/Ownership/Dns/DnsTxtResolver.cs`:

```csharp
using System.Net;

using DnsClient;

using Microsoft.Extensions.Logging;

namespace MUI.Discovery.Ownership;

/// <summary>How the resolver behaves. Every field exists to keep a lookup bounded (spec §12).</summary>
public sealed record DnsTxtResolverOptions
{
    /// <summary>
    /// The whole budget for one lookup. Short, because this runs in-process with the web tier and a
    /// claim that verifies four seconds later than it might have is not a problem anyone has.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Empty means the platform's configured resolvers. Tests set this to a loopback server, which is
    /// how this suite has a real resolver test and no live DNS.
    /// </summary>
    public IReadOnlyList<IPEndPoint> NameServers { get; init; } = [];
}

/// <summary>
/// The real TXT resolver behind spec §8's third proof channel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Retries are off and the timeout is the real bound.</b> DnsClient defaults to retrying and to a
/// TCP fallback, which multiplies the wall-clock cost of one unreachable resolver by a factor nobody
/// reading <see cref="DnsTxtResolverOptions.Timeout"/> would expect. §12 wants a hard bound, so it is
/// one query, one budget.
/// </para>
/// <para>
/// <b>Nothing here throws.</b> A resolver being unreachable is an ordinary Tuesday, and it is not the
/// owner's fault; it must not fail a claim, and it must not take a crawl cycle down. Every failure
/// becomes <see cref="DnsTxtLookup.Unavailable"/> and a log line.
/// </para>
/// </remarks>
public sealed class DnsTxtResolver(DnsTxtResolverOptions options, ILogger<DnsTxtResolver>? logger = null)
    : IDnsTxtResolver
{
    private readonly LookupClient _client = Build(options);

    public async Task<DnsTxtLookup> LookupAsync(string name, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        try
        {
            var response = await _client.QueryAsync(name, QueryType.TXT, cancellationToken: ct)
                .ConfigureAwait(false);

            if (response.HasError)
            {
                // NXDOMAIN is a real answer — the name is not there — and is not an outage.
                return response.Header.ResponseCode is DnsHeaderResponseCode.NotExistentDomain
                    ? DnsTxtLookup.NoRecord
                    : DnsTxtLookup.Unavailable;
            }

            var values = response.Answers.TxtRecords()
                .SelectMany(record => record.Text)
                .Select(text => text.Trim())
                .Where(text => text.Length > 0)
                .ToList();

            return values.Count == 0 ? DnsTxtLookup.NoRecord : new DnsTxtLookup(DnsTxtStatus.Answered, values);
        }
        catch (Exception error)
        {
            logger?.LogDebug(error, "TXT lookup for {Name} did not complete.", name);

            return DnsTxtLookup.Unavailable;
        }
    }

    private static LookupClient Build(DnsTxtResolverOptions options)
    {
        var settings = options.NameServers.Count == 0
            ? new LookupClientOptions()
            : new LookupClientOptions([.. options.NameServers]);

        settings.Timeout = options.Timeout;
        settings.Retries = 0;
        settings.UseTcpFallback = false;
        settings.UseCache = false;
        settings.ThrowDnsErrors = false;

        return new LookupClient(settings);
    }
}
```

- [ ] **Step 6: Write the fake every behavioural test uses**

Create `tests/MUI.Discovery.Tests/Support/FakeDnsTxtResolver.cs`:

```csharp
using MUI.Discovery.Ownership;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// The only resolver any behavioural test in this plan sees. A name nobody configured answers
/// <see cref="DnsTxtLookup.NoRecord"/>, which is what an operator who has not added the record yet
/// looks like — the default a test should have to opt out of, not into.
/// </summary>
public sealed class FakeDnsTxtResolver : IDnsTxtResolver
{
    private readonly Dictionary<string, DnsTxtLookup> _answers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every name asked for, in order, so a test can assert what was and was not queried.</summary>
    public List<string> Queried { get; } = [];

    public void Answer(string name, params string[] values) => _answers[name] = DnsTxtLookup.Of(values);

    public void Unavailable(string name) => _answers[name] = DnsTxtLookup.Unavailable;

    public Task<DnsTxtLookup> LookupAsync(string name, CancellationToken ct)
    {
        Queried.Add(name);

        return Task.FromResult(_answers.GetValueOrDefault(name, DnsTxtLookup.NoRecord));
    }
}
```

- [ ] **Step 7: Write the loopback DNS server**

Create `tests/MUI.Discovery.Tests/Support/LoopbackDnsServer.cs`:

```csharp
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// A DNS server that answers TXT queries and nothing else, on 127.0.0.1.
/// </summary>
/// <remarks>
/// The same idea as Plan 1's <c>ScriptedMuServer</c>: the real client, over a real socket, against a
/// server we control. It is sixty lines because a TXT query and its answer are a fixed header, one
/// question echoed back, and one answer record — and because writing them out is the only way to have
/// a resolver test that is neither a mock nor a dependency on somebody else's zone file.
/// </remarks>
public sealed class LoopbackDnsServer : IAsyncDisposable
{
    private const ushort TypeTxt = 16;
    private const ushort ClassInternet = 1;

    private readonly UdpClient _socket;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Dictionary<string, string[]> _answers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task _loop;
    private volatile bool _silent;

    private LoopbackDnsServer(UdpClient socket)
    {
        _socket = socket;
        Port = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
        _loop = Task.Run(ServeAsync);
    }

    public int Port { get; }

    public static Task<LoopbackDnsServer> StartAsync() =>
        Task.FromResult(new LoopbackDnsServer(new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))));

    public void Answer(string name, params string[] values) => _answers[name.TrimEnd('.')] = values;

    /// <summary>Stop replying at all — the black hole a §12 timeout has to survive.</summary>
    public void GoSilent() => _silent = true;

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _socket.Dispose();

        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
            // Expected: cancelling the receive is how this loop ends.
        }

        _stopping.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            UdpReceiveResult request;
            try
            {
                request = await _socket.ReceiveAsync(_stopping.Token);
            }
            catch (Exception) when (_stopping.IsCancellationRequested || true)
            {
                return;
            }

            if (_silent)
            {
                continue;
            }

            if (Reply(request.Buffer) is { } reply)
            {
                await _socket.SendAsync(reply, reply.Length, request.RemoteEndPoint);
            }
        }
    }

    private byte[]? Reply(byte[] query)
    {
        if (query.Length < 17)
        {
            return null;
        }

        var (name, questionEnd) = ReadName(query, 12);
        if (questionEnd + 4 > query.Length)
        {
            return null;
        }

        var values = _answers.GetValueOrDefault(name, []);
        var reply = new List<byte>(query.Length + 64);

        // Header: the query's ID, then QR=1 AA=1 RD=1 RA=1, then one question and N answers.
        reply.AddRange(query[..2]);
        reply.AddRange([0x85, 0x80]);
        reply.AddRange(Be(1));
        reply.AddRange(Be((ushort)values.Length));
        reply.AddRange(Be(0));
        reply.AddRange(Be(0));

        // NXDOMAIN when the name is unknown, so "not added yet" is distinguishable from "no TXT".
        if (values.Length == 0)
        {
            reply[3] = 0x83;
        }

        reply.AddRange(query[12..(questionEnd + 4)]);

        foreach (var value in values)
        {
            var text = Encoding.UTF8.GetBytes(value);

            reply.AddRange([0xC0, 0x0C]);                 // pointer back to the question's name
            reply.AddRange(Be(TypeTxt));
            reply.AddRange(Be(ClassInternet));
            reply.AddRange([0, 0, 0, 60]);                // TTL
            reply.AddRange(Be((ushort)(text.Length + 1))); // RDLENGTH: the length byte plus the text
            reply.Add((byte)text.Length);
            reply.AddRange(text);
        }

        return [.. reply];
    }

    private static (string Name, int End) ReadName(byte[] message, int offset)
    {
        var labels = new List<string>();

        while (offset < message.Length && message[offset] != 0)
        {
            var length = message[offset];
            labels.Add(Encoding.UTF8.GetString(message, offset + 1, length));
            offset += length + 1;
        }

        return (string.Join('.', labels), offset + 1);
    }

    private static byte[] Be(ushort value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);

        return buffer;
    }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
```
Expected: PASS — 6 new tests, all against `127.0.0.1`.

- [ ] **Step 9: Commit**

```bash
git add Directory.Packages.props src/MUI.Discovery/MUI.Discovery.csproj \
        src/MUI.Discovery/Ownership/Dns tests/MUI.Discovery.Tests/Support/FakeDnsTxtResolver.cs \
        tests/MUI.Discovery.Tests/Support/LoopbackDnsServer.cs \
        tests/MUI.Discovery.Tests/Ownership/DnsTxtResolverTests.cs
git commit -m "feat: a bounded DNS TXT resolver — the one claim channel a telnet probe cannot see"
```

---

### Task 6: The DNS channel, and the one deliberate asymmetry (spec §8, §11)

Verification rides the crawl schedule (Task 8) because probing costs the game's server a TCP
connection, and §11 says we do not spend more of those because somebody is waiting. **A DNS query
costs the game's server nothing at all** — it goes to a public resolver, not to the game — so the DNS
channel may be, and is, checked off-schedule.

That asymmetry is a decision, not an oversight, and it is exactly the asymmetry the design handoff
already describes: "Slowest to propagate; we re-check hourly for 14 days."

**Files:**
- Modify: `src/MUI.Discovery/Ownership/ClaimVerifier.cs` (add `ReadDnsAsync`)
- Create: `src/MUI.Discovery/Ownership/Dns/DnsClaimPoller.cs`
- Create: `tests/MUI.Discovery.Tests/Ownership/DnsClaimChannelTests.cs`

**Interfaces:**
- Consumes: `IDnsTxtResolver`, `DnsTxtLookup`, `DnsTxtStatus` (Task 5); `ClaimVerifier`,
  `ClaimEvidence` (Task 4); `IClaimRepository`, `InMemoryClaimRepository` (Task 2); `IEndpointRepository`,
  `InMemoryEndpointRepository` (Plan 02).
- Produces:
  - `Task<ClaimEvidence> ClaimVerifier.ReadDnsAsync(ClaimToken token, string host, CancellationToken ct)`
  - `sealed class MUI.Discovery.Ownership.DnsClaimPoller(IClaimRepository claims, IEndpointRepository endpoints, ClaimVerifier verifier, TimeProvider time, ILogger<DnsClaimPoller>? logger = null)`
    with `static readonly TimeSpan Interval`, `Task<int> PollAsync(CancellationToken ct)`

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/Ownership/DnsClaimChannelTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery.Ownership;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests.Ownership;

/// <summary>
/// Spec §8's third channel, and the one place this subsystem is deliberately allowed off the crawl
/// schedule (§11). A TXT lookup goes to a public resolver; it does not touch the game's port, so the
/// politeness argument that bounds probing simply does not apply to it.
/// </summary>
public class DnsClaimChannelTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private const string Token = "muidx-a2b3-c4d5-e6f7";
    private const string Host = "tidewater.example";

    private sealed record Rig(
        DnsClaimPoller Poller,
        ClaimVerifier Verifier,
        InMemoryClaimRepository Claims,
        InMemoryEndpointRepository Endpoints,
        FakeDnsTxtResolver Dns,
        ManualTimeProvider Time);

    private static Rig Subject()
    {
        var claims = new InMemoryClaimRepository();
        var endpoints = new InMemoryEndpointRepository();
        var dns = new FakeDnsTxtResolver();
        var time = new ManualTimeProvider(Now);
        var verifier = new ClaimVerifier(claims, dns, time);

        return new Rig(new DnsClaimPoller(claims, endpoints, verifier, time), verifier, claims, endpoints, dns, time);
    }

    private static async Task<ClaimToken> PendingAsync(Rig rig, Guid game)
    {
        var token = new ClaimToken(Guid.CreateVersion7(), game, Token, ClaimTokenState.Pending,
            Now, Now + ClaimToken.Validity, null, null, 0);
        await rig.Claims.InsertAsync(token, None);
        await rig.Endpoints.UpsertAsync(
            new GameEndpoint(game, Host, 4201, EndpointKind.Telnet, Now, Now, EndpointState.Active), None);

        return token;
    }

    [Test]
    public async Task ATokenInATxtRecordUnderTheGamesHostIsProof()
    {
        var rig = Subject();
        var token = await PendingAsync(rig, Guid.CreateVersion7());
        rig.Dns.Answer(ClaimVocabulary.DnsNameFor(Host), Token);

        var evidence = await rig.Verifier.ReadDnsAsync(token, Host, None);

        await Assert.That(evidence.Found).IsTrue();
        await Assert.That(evidence.Channel).IsEqualTo(ClaimChannel.DnsTxt);
        await Assert.That(rig.Dns.Queried).IsEquivalentTo(new[] { "_muindex.tidewater.example" });
    }

    [Test]
    public async Task ATokenAmongOtherTxtStringsIsStillProof()
    {
        var rig = Subject();
        var token = await PendingAsync(rig, Guid.CreateVersion7());
        rig.Dns.Answer(ClaimVocabulary.DnsNameFor(Host), "v=spf1 -all", Token, "google-site-verification=x");

        await Assert.That((await rig.Verifier.ReadDnsAsync(token, Host, None)).Found).IsTrue();
    }

    [Test]
    public async Task AMissingRecordIsNotProofAndIsNotAnError()
    {
        var rig = Subject();
        var token = await PendingAsync(rig, Guid.CreateVersion7());

        var evidence = await rig.Verifier.ReadDnsAsync(token, Host, None);

        await Assert.That(evidence.Found).IsFalse();
    }

    [Test]
    public async Task AResolverOutageDoesNotBurnAProbeAgainstTheDiagnostic()
    {
        // "We could not ask" must not count towards "we looked three times and did not see it" —
        // that would tell an operator their DNS is wrong when ours is.
        var rig = Subject();
        var token = await PendingAsync(rig, Guid.CreateVersion7());
        rig.Dns.Unavailable(ClaimVocabulary.DnsNameFor(Host));

        await rig.Poller.PollAsync(None);

        await Assert.That(rig.Claims.Attempts).IsEmpty();
        await Assert.That((await rig.Claims.ByIdAsync(token.Id, None))!.ProbesSinceIssue).IsEqualTo(0);
    }

    [Test]
    public async Task ThePollerVerifiesEveryPendingTokenWhoseRecordIsThere()
    {
        var rig = Subject();
        var game = Guid.CreateVersion7();
        var token = await PendingAsync(rig, game);
        rig.Dns.Answer(ClaimVocabulary.DnsNameFor(Host), Token);

        var verified = await rig.Poller.PollAsync(None);

        await Assert.That(verified).IsEqualTo(1);

        var stored = await rig.Claims.ByIdAsync(token.Id, None);
        await Assert.That(stored!.State).IsEqualTo(ClaimTokenState.Verified);
        await Assert.That(stored.VerifiedVia).IsEqualTo(ClaimChannel.DnsTxt);
        await Assert.That(stored.VerifiedAt).IsEqualTo(Now);
    }

    [Test]
    public async Task ThePollerLooksEveryHourAndTheDesignSaysSo()
    {
        // "Slowest to propagate; we re-check hourly for 14 days" — the delivered claim screen, and
        // the number the owner is looking at while they wait.
        await Assert.That(DnsClaimPoller.Interval).IsEqualTo(TimeSpan.FromHours(1));
    }

    [Test]
    public async Task ThePollerIgnoresATokenThatIsNoLongerPending()
    {
        var rig = Subject();
        var game = Guid.CreateVersion7();
        var token = await PendingAsync(rig, game);
        await rig.Claims.SetStateAsync(token.Id, ClaimTokenState.Revoked, null, null, None);
        rig.Dns.Answer(ClaimVocabulary.DnsNameFor(Host), Token);

        await Assert.That(await rig.Poller.PollAsync(None)).IsEqualTo(0);
        await Assert.That(rig.Dns.Queried).IsEmpty();
    }

    [Test]
    public async Task ThePollerStopsLookingOnceTheWindowHasPassed()
    {
        // Fourteen days of hourly queries is 336 lookups; an unbounded poller would run for ever
        // against a name nobody is ever going to create.
        var rig = Subject();
        await PendingAsync(rig, Guid.CreateVersion7());
        rig.Dns.Answer(ClaimVocabulary.DnsNameFor(Host), Token);

        rig.Time.Advance(ClaimToken.Validity + TimeSpan.FromHours(1));

        await Assert.That(await rig.Poller.PollAsync(None)).IsEqualTo(0);
        await Assert.That(rig.Dns.Queried).IsEmpty();
    }

    [Test]
    public async Task EveryEndpointHostIsTriedBecauseAGameMayHaveMoved()
    {
        // §5.5: endpoints are plural and historical. An operator adds the record under whichever
        // hostname they think of as theirs, which is not necessarily the one we probed last.
        var rig = Subject();
        var game = Guid.CreateVersion7();
        await PendingAsync(rig, game);
        await rig.Endpoints.UpsertAsync(
            new GameEndpoint(game, "tidewater.org", 4201, EndpointKind.Telnet, Now, Now, EndpointState.Active), None);
        rig.Dns.Answer(ClaimVocabulary.DnsNameFor("tidewater.org"), Token);

        await Assert.That(await rig.Poller.PollAsync(None)).IsEqualTo(1);
        await Assert.That(rig.Dns.Queried).Contains("_muindex.tidewater.org");
    }

    [Test]
    public async Task AGameWithNoEndpointIsSkippedRatherThanGuessedAt()
    {
        var rig = Subject();
        var token = new ClaimToken(Guid.CreateVersion7(), Guid.CreateVersion7(), Token,
            ClaimTokenState.Pending, Now, Now + ClaimToken.Validity, null, null, 0);
        await rig.Claims.InsertAsync(token, None);

        await Assert.That(await rig.Poller.PollAsync(None)).IsEqualTo(0);
        await Assert.That(rig.Dns.Queried).IsEmpty();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'DnsClaimPoller' could not be found`.

- [ ] **Step 3: Add the DNS reader to the verifier**

In `src/MUI.Discovery/Ownership/ClaimVerifier.cs`, add to `ClaimVerifier`:

```csharp
    /// <summary>
    /// Spec §8's third channel: a TXT record at <c>_muindex.&lt;host&gt;</c>, for operators who do not
    /// run the game daemon themselves.
    /// </summary>
    /// <remarks>
    /// The only channel with no MSSP list to report, because no probe was involved — the field list is
    /// empty rather than absent, which is what the diagnostic renders as "we did not read your MSSP on
    /// this check".
    /// </remarks>
    public async Task<ClaimEvidence> ReadDnsAsync(ClaimToken token, string host, CancellationToken ct)
    {
        var lookup = await dns.LookupAsync(ClaimVocabulary.DnsNameFor(host), ct);

        var found = lookup.Status is DnsTxtStatus.Answered
                    && lookup.Values.Any(value => Matches(token.Value, value));

        return new ClaimEvidence(found, ClaimChannel.DnsTxt, []);
    }

    /// <summary>Whether a lookup told us anything at all — an outage is not evidence of absence.</summary>
    public async Task<DnsTxtStatus> ProbeDnsAsync(string host, CancellationToken ct) =>
        (await dns.LookupAsync(ClaimVocabulary.DnsNameFor(host), ct)).Status;
```

- [ ] **Step 4: Write the poller**

Create `src/MUI.Discovery/Ownership/Dns/DnsClaimPoller.cs`:

```csharp
using Microsoft.Extensions.Logging;

using MUI.Catalog;
using MUI.Storage;

namespace MUI.Discovery.Ownership;

/// <summary>
/// Checks the DNS channel on its own clock — the one place this subsystem is off the crawl schedule.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a deliberate asymmetry and not a loophole.</b> Verification of the other two channels
/// rides the crawl schedule because each look costs the game's server a TCP connection, and §11 says
/// we do not spend more of those because somebody is waiting. A TXT lookup goes to a public resolver
/// and costs the game's server nothing, so the argument that bounds probing does not reach it. Every
/// word of §11's politeness contract is still honoured where it applies.
/// </para>
/// <para>
/// <b>An outage is not an attempt.</b> A resolver we could not reach records nothing and increments
/// nothing, because "we looked three times and did not see it" must never be a statement about our
/// own infrastructure — that is precisely the diagnostic that sends an operator hunting a
/// non-existent typo.
/// </para>
/// </remarks>
public sealed class DnsClaimPoller(
    IClaimRepository claims,
    IEndpointRepository endpoints,
    ClaimVerifier verifier,
    TimeProvider time,
    ILogger<DnsClaimPoller>? logger = null)
{
    /// <summary>
    /// Hourly, which is what the claim screen promises an owner in so many words, and roughly the
    /// granularity at which a TTL-bound record becomes visible anyway.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>How many pending claims this pass proved.</summary>
    public async Task<int> PollAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var verified = 0;

        foreach (var token in await claims.PendingAsync(now, ct))
        {
            ct.ThrowIfCancellationRequested();

            foreach (var host in await HostsAsync(token.GameId, ct))
            {
                var evidence = await verifier.ReadDnsAsync(token, host, ct);
                if (!evidence.Found)
                {
                    continue;
                }

                await claims.AppendAttemptAsync(
                    new ClaimAttempt(0, token.Id, now, ClaimChannel.DnsTxt, true, []), ct);
                await claims.SetStateAsync(token.Id, ClaimTokenState.Verified, ClaimChannel.DnsTxt, now, ct);

                logger?.LogInformation(
                    "Claim {Token} for game {Game} verified by DNS TXT at {Host}.", token.Id, token.GameId, host);
                verified++;
                break;
            }
        }

        return verified;
    }

    /// <summary>
    /// Every host the game has ever been seen at (§5.5). An operator adds the record under whichever
    /// hostname they think of as theirs, which need not be the one we probed most recently.
    /// </summary>
    private async Task<IReadOnlyList<string>> HostsAsync(Guid gameId, CancellationToken ct) =>
        (await endpoints.ForGameAsync(gameId, ct))
        .Select(endpoint => endpoint.Host)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
```

Note what this method does **not** do: it records no attempt and increments no counter on a miss. That
is Task 7's job, driven from the crawl loop, where a miss is a real observation of the game's own
server. A DNS name that is simply not there yet is the expected state for most of fourteen days and
saying so 336 times would drown the diagnostic it feeds.

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
```
Expected: PASS — 10 new tests.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Discovery/Ownership/ClaimVerifier.cs \
        src/MUI.Discovery/Ownership/Dns/DnsClaimPoller.cs \
        tests/MUI.Discovery.Tests/Ownership/DnsClaimChannelTests.cs
git commit -m "feat: the DNS claim channel, checked hourly because it costs the game's server nothing"
```

---

### Task 7: `ClaimDiagnostic` — "we did not see it, and here is what we did see" (spec §8)

An operator sets the token on the wrong MSSP variable, or edits `mushcnf.dst` and does not restart.
Both look identical from the outside: nothing happens. **This is the difference between a claim flow
that works and one that generates support mail**, and the fix is to show them the variable list their
own server published.

**Files:**
- Create: `src/MUI.Catalog/Claiming/ClaimDiagnostic.cs`
- Create: `tests/MUI.Catalog.Tests/Claiming/ClaimDiagnosticTests.cs`

**Interfaces:**
- Consumes: `ClaimToken`, `ClaimAttempt`, `ClaimChannel`, `ClaimVocabulary` (Task 1).
- Produces:
  - `enum MUI.Catalog.ClaimDiagnosisKind { TooEarly, NoMsspAtAll, TokenNotInAnyVariable, LooksLikeATypo, Found }`
  - `sealed record MUI.Catalog.ClaimDiagnostic(ClaimDiagnosisKind Kind, int ProbesSinceIssue, IReadOnlyList<string> MsspFieldsSeen, IReadOnlyList<ClaimChannel> ChannelsChecked, string ExpectedVariable, string? NearestVariable)`
  - `static class MUI.Catalog.ClaimDiagnostics` with `const int ProbesBeforeDiagnostic = 3`,
    `ClaimDiagnostic? For(ClaimToken token, IReadOnlyList<ClaimAttempt> attempts)`

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Catalog.Tests/Claiming/ClaimDiagnosticTests.cs`:

```csharp
namespace MUI.Catalog.Tests.Claiming;

/// <summary>
/// Spec §8's diagnostic, and the delivered design's words for it: "A diagnostic, never a failure.
/// The likeliest cause is that the game has not restarted since you edited the config; the second is
/// a typo, so we show you the raw field list to compare against."
/// </summary>
public class ClaimDiagnosticTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static ClaimToken Token(int probes) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "muidx-a2b3-c4d5-e6f7",
            ClaimTokenState.Pending, Now, Now + ClaimToken.Validity, null, null, probes);

    private static ClaimAttempt Miss(ClaimChannel channel, params string[] fields) =>
        new(0, Guid.Empty, Now, channel, false, fields);

    [Test]
    public async Task NothingIsSaidBeforeTheThirdProbe()
    {
        // Two visits is roughly twelve hours. Telling somebody their config is wrong before their
        // game has plausibly restarted is worse than saying nothing.
        await Assert.That(ClaimDiagnostics.ProbesBeforeDiagnostic).IsEqualTo(3);
        await Assert.That(ClaimDiagnostics.For(Token(0), [])).IsNull();
        await Assert.That(ClaimDiagnostics.For(Token(2), [Miss(ClaimChannel.Mssp, "NAME")])).IsNull();
    }

    [Test]
    public async Task AfterThreeProbesTheFieldListIsReported()
    {
        var diagnostic = ClaimDiagnostics.For(
            Token(3), [Miss(ClaimChannel.Mssp, "CODEBASE", "CONTACT", "FAMILY", "NAME", "PORT")]);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Kind).IsEqualTo(ClaimDiagnosisKind.TokenNotInAnyVariable);
        await Assert.That(diagnostic.MsspFieldsSeen)
            .IsEquivalentTo(new[] { "CODEBASE", "CONTACT", "FAMILY", "NAME", "PORT" });
        await Assert.That(diagnostic.ExpectedVariable).IsEqualTo(ClaimVocabulary.MsspVariable);
    }

    [Test]
    public async Task AServerWithNoMsspGetsADifferentAnswer()
    {
        // "Your MSSP is empty" and "your MSSP is fine but the token is not in it" are different
        // problems with different fixes, and RhostMUSH and TinyMUSH have no MSSP at all (§3.1).
        var diagnostic = ClaimDiagnostics.For(Token(4), [Miss(ClaimChannel.Mssp), Miss(ClaimChannel.ConnectScreen)]);

        await Assert.That(diagnostic!.Kind).IsEqualTo(ClaimDiagnosisKind.NoMsspAtAll);
    }

    [Test]
    public async Task AVariableThatLooksLikeAMistypedOneIsNamed()
    {
        // The second-likeliest cause, and the one an operator cannot see for themselves: they set
        // MUINDEX_CLAIM, or MUINDEX-CLAIM, and it is sitting right there in the list.
        var diagnostic = ClaimDiagnostics.For(
            Token(3), [Miss(ClaimChannel.Mssp, "MUINDEX_CLAIM", "NAME", "PORT")]);

        await Assert.That(diagnostic!.Kind).IsEqualTo(ClaimDiagnosisKind.LooksLikeATypo);
        await Assert.That(diagnostic.NearestVariable).IsEqualTo("MUINDEX_CLAIM");
    }

    [Test]
    public async Task TheLatestVisitIsTheOneReported()
    {
        // An operator who has just fixed their config wants to know what we saw this morning, not
        // what we saw a week ago.
        var diagnostic = ClaimDiagnostics.For(Token(5),
        [
            new ClaimAttempt(1, Guid.Empty, Now.AddDays(-2), ClaimChannel.Mssp, false, ["NAME"]),
            new ClaimAttempt(2, Guid.Empty, Now, ClaimChannel.Mssp, false, ["CONTACT", "NAME", "WEBSITE"]),
        ]);

        await Assert.That(diagnostic!.MsspFieldsSeen).IsEquivalentTo(new[] { "CONTACT", "NAME", "WEBSITE" });
    }

    [Test]
    public async Task EveryChannelWeLookedAtIsNamed()
    {
        // So the page can say which three places we checked, rather than leaving an operator to
        // wonder whether we ever looked at their DNS.
        var diagnostic = ClaimDiagnostics.For(Token(3),
        [
            Miss(ClaimChannel.Mssp, "NAME"),
            Miss(ClaimChannel.ConnectScreen, "NAME"),
            Miss(ClaimChannel.DnsTxt),
        ]);

        await Assert.That(diagnostic!.ChannelsChecked)
            .IsEquivalentTo(new[] { ClaimChannel.Mssp, ClaimChannel.ConnectScreen, ClaimChannel.DnsTxt });
    }

    [Test]
    public async Task AFoundTokenIsNotADiagnosisOfAnything()
    {
        var found = new ClaimAttempt(1, Guid.Empty, Now, ClaimChannel.Mssp, true, ["MUINDEX CLAIM", "NAME"]);

        var diagnostic = ClaimDiagnostics.For(
            Token(3) with { State = ClaimTokenState.Verified, VerifiedVia = ClaimChannel.Mssp, VerifiedAt = Now },
            [found]);

        await Assert.That(diagnostic!.Kind).IsEqualTo(ClaimDiagnosisKind.Found);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'ClaimDiagnostics' could not be found`.

- [ ] **Step 3: Write the diagnostic**

Create `src/MUI.Catalog/Claiming/ClaimDiagnostic.cs`:

```csharp
namespace MUI.Catalog;

/// <summary>What we think is going on, in the order of how likely each is.</summary>
public enum ClaimDiagnosisKind
{
    /// <summary>Fewer than three visits. Nothing is wrong yet and saying so would be noise.</summary>
    TooEarly,

    /// <summary>The server reported no MSSP variables at all — a different problem with a different fix.</summary>
    NoMsspAtAll,

    /// <summary>MSSP is fine and the token is not in it. Usually a game that has not restarted.</summary>
    TokenNotInAnyVariable,

    /// <summary>There is a variable in the list that looks like a mistyped version of ours.</summary>
    LooksLikeATypo,

    /// <summary>We saw it.</summary>
    Found,
}

/// <summary>
/// The §8 report: not that we failed, but what we saw.
/// </summary>
/// <param name="NearestVariable">
/// A variable the server published that differs from ours only in punctuation or case — the mistake
/// an operator cannot spot for themselves, because from their side they typed the right words.
/// </param>
public sealed record ClaimDiagnostic(
    ClaimDiagnosisKind Kind,
    int ProbesSinceIssue,
    IReadOnlyList<string> MsspFieldsSeen,
    IReadOnlyList<ClaimChannel> ChannelsChecked,
    string ExpectedVariable,
    string? NearestVariable);

/// <summary>
/// Turns a token's attempt history into something an operator can act on (spec §8).
/// </summary>
/// <remarks>
/// <b>This is the difference between a claim flow that works and one that generates support mail.</b>
/// A claim that silently does nothing gives an operator no way to tell "the game has not restarted"
/// from "I put it on the wrong variable" from "your crawler is broken", and the only party who can
/// tell them apart is us — we are holding the variable list their server published.
/// </remarks>
public static class ClaimDiagnostics
{
    /// <summary>
    /// Three visits is roughly a day at the base interval. Fewer is not evidence of anything: most
    /// games have not restarted yet, and telling somebody their configuration is wrong before it has
    /// plausibly taken effect is worse than saying nothing.
    /// </summary>
    public const int ProbesBeforeDiagnostic = 3;

    public static ClaimDiagnostic? For(ClaimToken token, IReadOnlyList<ClaimAttempt> attempts)
    {
        if (token.State is ClaimTokenState.Verified)
        {
            return new ClaimDiagnostic(
                ClaimDiagnosisKind.Found, token.ProbesSinceIssue, Latest(attempts),
                Channels(attempts), ClaimVocabulary.MsspVariable, null);
        }

        if (token.ProbesSinceIssue < ProbesBeforeDiagnostic)
        {
            return null;
        }

        var seen = Latest(attempts);
        var nearest = seen.FirstOrDefault(Resembles);

        var kind = seen.Count == 0 ? ClaimDiagnosisKind.NoMsspAtAll
            : nearest is not null ? ClaimDiagnosisKind.LooksLikeATypo
            : ClaimDiagnosisKind.TokenNotInAnyVariable;

        return new ClaimDiagnostic(
            kind, token.ProbesSinceIssue, seen, Channels(attempts), ClaimVocabulary.MsspVariable, nearest);
    }

    /// <summary>
    /// The most recent visit's field list. An operator who has just fixed their configuration wants to
    /// know what we saw this morning, not the union of everything we have ever seen.
    /// </summary>
    private static IReadOnlyList<string> Latest(IReadOnlyList<ClaimAttempt> attempts) =>
        attempts.Where(attempt => attempt.MsspFieldsSeen.Count > 0)
            .OrderByDescending(attempt => attempt.At)
            .Select(attempt => attempt.MsspFieldsSeen)
            .FirstOrDefault() ?? [];

    private static IReadOnlyList<ClaimChannel> Channels(IReadOnlyList<ClaimAttempt> attempts) =>
        attempts.Select(attempt => attempt.Channel).Distinct().ToList();

    /// <summary>
    /// Whether a published variable is ours with the punctuation wrong. Comparing on letters and
    /// digits alone catches <c>MUINDEX_CLAIM</c>, <c>MUINDEX-CLAIM</c> and <c>muindexclaim</c>, which
    /// are the three spellings an operator reaches for when a space in a variable name looks wrong.
    /// </summary>
    private static bool Resembles(string variable)
    {
        static string Letters(string value) =>
            new([.. value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

        return !string.Equals(variable, ClaimVocabulary.MsspVariable, StringComparison.OrdinalIgnoreCase)
               && Letters(variable) == Letters(ClaimVocabulary.MsspVariable);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
```
Expected: PASS — 7 new tests.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Catalog/Claiming/ClaimDiagnostic.cs tests/MUI.Catalog.Tests/Claiming/ClaimDiagnosticTests.cs
git commit -m "feat(catalog): the after-three-probes diagnostic — what we saw, not that we failed (spec 8)"
```

---

### Task 8: `ClaimCycle` — verification rides the schedule and cannot hurry it (spec §8, §11)

§11: `CRAWL DELAY` is honoured as a floor, and the crawler self-identifies so an admin can find out
who we are. **A pending claim is not an exemption from any of that.** The design handoff says it to
the owner in as many words: "We check on the schedule your game already sets with `CRAWL DELAY` — we
will not hammer your port to be helpful."

This task makes that structural rather than a matter of care. `ClaimCycle` is called from
`CrawlerService.ApplyAsync` **between** the ingestor and the rescheduler, and it is handed nothing it
could reschedule with.

**Files:**
- Create: `src/MUI.Discovery/Ownership/ClaimCycle.cs`
- Modify: `src/MUI.Discovery/CrawlerService.cs`
- Create: `tests/MUI.Discovery.Tests/Support/ClaimWorld.cs`
- Create: `tests/MUI.Discovery.Tests/Ownership/ClaimSchedulingTests.cs`

**Interfaces:**
- Consumes: `ClaimVerifier`, `ClaimEvidence` (Tasks 4, 6); `IClaimRepository` (Task 2);
  `ProbeResult`, `ProbeOutcome` (Plan 01); `CrawlerService`, `ProbeSchedule`, `ICrawlTargetRepository`,
  `CrawlTarget`, `InMemoryCrawlTargetRepository`, `FakeProbe` (Plan 03).
- Produces:
  - `enum MUI.Discovery.Ownership.ClaimCycleResult { NoPendingClaim, LookedAndDidNotSeeIt, Verified }`
  - `sealed class MUI.Discovery.Ownership.ClaimCycle(IClaimRepository claims, ClaimVerifier verifier, TimeProvider time, ILogger<ClaimCycle>? logger = null)`
    with `Task<ClaimCycleResult> OnProbeAsync(Guid gameId, ProbeResult result, CancellationToken ct)`
  - `MUI.Discovery.Tests.Support.ClaimWorld` — the rig every later task in this plan reuses, with
    `Service`, `Targets`, `Games`, `Fields`, `Endpoints`, `Claims`, `Probe`, `Dns`, `Time`, `Cycle`,
    and `Task<Guid> GameAsync(string name, TimeSpan? crawlDelay = null)`

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/Ownership/ClaimSchedulingTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Discovery.Ownership;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests.Ownership;

/// <summary>
/// Spec §11's politeness contract, and the sentence the delivered claim screen shows an owner: "We
/// check on the schedule your game already sets with CRAWL DELAY — we will not hammer your port to
/// be helpful."
/// </summary>
public class ClaimSchedulingTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private const string Token = "muidx-a2b3-c4d5-e6f7";

    [Test]
    public async Task AGameWithCrawlDelaySixAndAPendingClaimIsNotProbedSoonerThanSixHours()
    {
        // The test the whole task exists for. Six hours between visits, claim or no claim.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid", crawlDelay: TimeSpan.FromHours(6));
        await world.IssueAsync(game);
        world.Probe.Answering("corvid.example", 4201, () => ProbeResults.Answered(
            host: "corvid.example",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), ("CRAWL DELAY", ["6"]))));

        var before = world.Time.GetUtcNow();
        await world.Service.RunCycleAsync(None);

        var target = world.Targets.All.Single();

        await Assert.That(target.NextProbeAt - before).IsGreaterThanOrEqualTo(TimeSpan.FromHours(6));
        await Assert.That(target.NextProbeAt).IsEqualTo(before + TimeSpan.FromHours(6));
        await Assert.That(world.Probe.Visited.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ThePendingClaimChangesTheScheduleByExactlyNothing()
    {
        // Run the same cycle twice, once with a claim outstanding and once without, and compare the
        // instant the target is next due. Anything that made claiming "urgent" shows up here.
        var withClaim = new ClaimWorld();
        var claimed = await withClaim.GameAsync("Corvid", crawlDelay: TimeSpan.FromHours(6));
        await withClaim.IssueAsync(claimed);
        withClaim.Probe.Answering("corvid.example", 4201, () => ProbeResults.Answered(
            host: "corvid.example", mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), ("CRAWL DELAY", ["6"]))));

        var without = new ClaimWorld();
        await without.GameAsync("Corvid", crawlDelay: TimeSpan.FromHours(6));
        without.Probe.Answering("corvid.example", 4201, () => ProbeResults.Answered(
            host: "corvid.example", mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), ("CRAWL DELAY", ["6"]))));

        await withClaim.Service.RunCycleAsync(None);
        await without.Service.RunCycleAsync(None);

        await Assert.That(withClaim.Targets.All.Single().NextProbeAt)
            .IsEqualTo(without.Targets.All.Single().NextProbeAt);
    }

    [Test]
    public async Task NothingInTheClaimSubsystemCanReachTheCrawlSchedule()
    {
        // Structural, and deliberately so. The behavioural tests above prove today's code polite;
        // this one makes the impolite version fail to compile against its own constructor.
        var parameters = new[] { typeof(ClaimCycle), typeof(ClaimVerifier), typeof(ClaimTokenIssuer) }
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToList();

        await Assert.That(parameters).DoesNotContain(typeof(ICrawlTargetRepository));
        await Assert.That(parameters.Any(type => type.Name.Contains("CrawlTarget", StringComparison.Ordinal)))
            .IsFalse();
        await Assert.That(parameters.Any(type => type == typeof(ProbeSchedule))).IsFalse();
    }

    [Test]
    public async Task AProbeThatCarriesTheTokenVerifiesTheClaimOnTheSameVisit()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        world.Probe.Answering("corvid.example", 4201, () => ProbeResults.Answered(
            host: "corvid.example",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), (ClaimVocabulary.MsspVariable, [token.Value]))));

        await world.Service.RunCycleAsync(None);

        var stored = await world.Claims.ByIdAsync(token.Id, None);

        await Assert.That(stored!.State).IsEqualTo(ClaimTokenState.Verified);
        await Assert.That(stored.VerifiedVia).IsEqualTo(ClaimChannel.Mssp);
    }

    [Test]
    public async Task EveryVisitCountsAsALookWhetherOrNotItFoundAnything()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        world.Probe.Answering("corvid.example", 4201, () => ProbeResults.Answered(
            host: "corvid.example", mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), ("PORT", ["4201"]))));

        for (var visit = 0; visit < 3; visit++)
        {
            await world.Service.RunCycleAsync(None);
            world.Time.Advance(world.Targets.All.Single().NextProbeAt - world.Time.GetUtcNow());
        }

        var stored = await world.Claims.ByIdAsync(token.Id, None);

        await Assert.That(stored!.ProbesSinceIssue).IsEqualTo(3);
        await Assert.That(world.Claims.Attempts.Count).IsEqualTo(3);
        await Assert.That(ClaimDiagnostics.For(stored, await world.Claims.AttemptsAsync(token.Id, None))!.Kind)
            .IsEqualTo(ClaimDiagnosisKind.TokenNotInAnyVariable);
    }

    [Test]
    public async Task AFailedProbeIsNotALookAtAll()
    {
        // We did not get in, so we did not fail to see the token. Counting an unreachable game's
        // outage towards "three probes and no token" would blame an operator for our own timeout.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);

        await world.Service.RunCycleAsync(None);

        await Assert.That((await world.Claims.ByIdAsync(token.Id, None))!.ProbesSinceIssue).IsEqualTo(0);
        await Assert.That(world.Claims.Attempts).IsEmpty();
    }

    [Test]
    public async Task AGameWithNoPendingClaimCostsNothingOnTheHotPath()
    {
        // Every probe of every game goes through this, and almost none of them has a claim.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        world.Probe.Answering("corvid.example", 4201, () => ProbeResults.Answered(
            host: "corvid.example", mssp: ProbeResults.Mssp(("NAME", ["Corvid"]))));

        await world.Service.RunCycleAsync(None);

        await Assert.That(await world.Cycle.OnProbeAsync(game, ProbeResults.Answered(), None))
            .IsEqualTo(ClaimCycleResult.NoPendingClaim);
        await Assert.That(world.Claims.Attempts).IsEmpty();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'ClaimCycle' could not be found`.

- [ ] **Step 3: Write the cycle**

Create `src/MUI.Discovery/Ownership/ClaimCycle.cs`:

```csharp
using Microsoft.Extensions.Logging;

using MUI.Catalog;
using MUI.Crawl;
using MUI.Storage;

namespace MUI.Discovery.Ownership;

/// <summary>What one visit did for the game's outstanding claim, if it had one.</summary>
public enum ClaimCycleResult
{
    NoPendingClaim,
    LookedAndDidNotSeeIt,
    Verified,
}

/// <summary>
/// The claim subsystem's entry point on the crawl loop (spec §8, §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a passenger, not a driver.</b> It is called from <c>CrawlerService.ApplyAsync</c>
/// between the ingestor and the rescheduler, and it is handed a clock, a store and a verifier — no
/// crawl-target repository, no schedule, nothing it could use to bring the next visit forward. §11
/// says <c>CRAWL DELAY</c> is honoured as a floor and offers no exemption for somebody waiting on a
/// screen, and the delivered claim page promises an owner in so many words that we will not hammer
/// their port to be helpful. <c>ClaimSchedulingTests</c> pins both the behaviour and the constructor.
/// </para>
/// <para>
/// <b>A failed probe is not a look.</b> We did not get in, so we did not fail to see the token, and
/// counting an outage of ours towards "three visits and no token" would send an operator hunting a
/// typo that is not there.
/// </para>
/// </remarks>
public sealed class ClaimCycle(
    IClaimRepository claims,
    ClaimVerifier verifier,
    TimeProvider time,
    ILogger<ClaimCycle>? logger = null)
{
    public async Task<ClaimCycleResult> OnProbeAsync(Guid gameId, ProbeResult result, CancellationToken ct)
    {
        if (result.Outcome is not ProbeOutcome.Answered)
        {
            return ClaimCycleResult.NoPendingClaim;
        }

        var now = time.GetUtcNow();
        if (await claims.LiveForGameAsync(gameId, now, ct) is not { } token)
        {
            return ClaimCycleResult.NoPendingClaim;
        }

        await claims.RecordProbeAsync(token.Id, ct);

        var mssp = verifier.ReadMssp(token, result);
        var evidence = mssp.Found ? mssp : verifier.ReadConnectScreen(token, result);

        await claims.AppendAttemptAsync(
            new ClaimAttempt(0, token.Id, now, evidence.Channel, evidence.Found, evidence.MsspFieldsSeen), ct);

        if (!evidence.Found)
        {
            return ClaimCycleResult.LookedAndDidNotSeeIt;
        }

        await claims.SetStateAsync(token.Id, ClaimTokenState.Verified, evidence.Channel, now, ct);
        logger?.LogInformation(
            "Claim {Token} for game {Game} verified via {Channel}.", token.Id, gameId, evidence.Channel);

        return ClaimCycleResult.Verified;
    }
}
```

- [ ] **Step 4: Wire it into the crawl loop**

In `src/MUI.Discovery/CrawlerService.cs`, add `ClaimCycle claims` to the primary constructor after
`ReferralGraphWriter referrals`:

```csharp
public sealed class CrawlerService(
    IProbe probe,
    ICrawlTargetRepository targets,
    ProbeIngestor ingestor,
    IdentityMatcher identity,
    IGameRepository games,
    MergeApplier merges,
    IDuplicateReviewRepository reviews,
    ReferralGraphWriter referrals,
    ClaimCycle claims,
    AdvisoryLock advisoryLock,
    DiscoveryOptions options,
    TimeProvider time,
    ILogger<CrawlerService> logger) : BackgroundService
```

and in `ApplyAsync`, immediately after the existing ingest call:

```csharp
        if (gameId is { } known)
        {
            await ingestor.IngestAsync(known, result, cancellationToken);

            // Spec §8. Verification is a passenger on this visit: it reads the result we already
            // have and returns. RescheduleAsync below is unaware of it, which is what §11's
            // politeness contract requires — a pending claim is not a reason to visit sooner.
            await claims.OnProbeAsync(known, result, cancellationToken);
        }
```

Add `using MUI.Discovery.Ownership;` to the file's usings. **Do not move `RescheduleAsync`, and do not
give it a claim-aware branch** — the position of these two calls is the whole design.

- [ ] **Step 5: Write the rig every later task reuses**

Create `tests/MUI.Discovery.Tests/Support/ClaimWorld.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;

using MUI.Catalog;
using MUI.Discovery.Ownership;
using MUI.Discovery.Writers;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// A whole crawler, in memory, with a claim subsystem attached. No socket, no container, no resolver
/// that leaves the process — every dependency is one of this suite's fakes.
/// </summary>
/// <remarks>
/// Deliberately a real <c>CrawlerService</c> rather than a stand-in: the properties this plan cares
/// about most are where a call sits in that loop and what it is not given, and a rig that reimplemented
/// the loop would assert those against itself.
/// </remarks>
public sealed class ClaimWorld
{
    private static readonly DateTimeOffset Start = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    public ClaimWorld()
    {
        Time = new ManualTimeProvider(Start);
        Targets = new InMemoryCrawlTargetRepository(Time);
        Games = new InMemoryGameRepository();
        Fields = new InMemoryGameFieldRepository();
        Endpoints = new InMemoryEndpointRepository();
        Presence = new InMemoryPresenceRepository();
        Availability = new InMemoryAvailabilityRepository();
        Claims = new InMemoryClaimRepository();
        Dns = new FakeDnsTxtResolver();
        Probe = new FakeProbe(Time);

        Verifier = new ClaimVerifier(Claims, Dns, Time);
        Issuer = new ClaimTokenIssuer(Claims, Time);
        Cycle = new ClaimCycle(Claims, Verifier, Time);

        var ingestor = new ProbeIngestor(
            new FieldReconciler(Fields, Time),
            new PresenceWriter(Presence),
            new AvailabilityWriter(Availability));

        var options = new DiscoveryOptions();

        // Plan 03's matcher takes six arguments: the field repository twice, because the double is
        // both IGameFieldRepository (forward, by game id) and IGameFieldIndex (the reverse lookup
        // candidates are gathered on), then DiscoveryOptions for the two thresholds, then the
        // optional ClaimBeaconPolicy this plan adds in Task 10.
        Service = new CrawlerService(
            Probe, Targets, ingestor,
            new IdentityMatcher(Games, Endpoints, Fields, Fields, options),
            Games,
            new MergeApplier(Games, Endpoints, Fields, new InMemoryMergeLog(), Time),
            new InMemoryDuplicateReviewRepository(),
            new ReferralGraphWriter(new InMemoryReferralRepository(), Targets, options, Time),
            Cycle,
            new AdvisoryLock(null!),
            options,
            Time,
            NullLogger<CrawlerService>.Instance);
    }

    public ManualTimeProvider Time { get; }

    public InMemoryCrawlTargetRepository Targets { get; }

    public InMemoryGameRepository Games { get; }

    public InMemoryGameFieldRepository Fields { get; }

    public InMemoryEndpointRepository Endpoints { get; }

    public InMemoryPresenceRepository Presence { get; }

    public InMemoryAvailabilityRepository Availability { get; }

    public InMemoryClaimRepository Claims { get; }

    public FakeDnsTxtResolver Dns { get; }

    public FakeProbe Probe { get; }

    public ClaimVerifier Verifier { get; }

    public ClaimTokenIssuer Issuer { get; }

    public ClaimCycle Cycle { get; }

    public CrawlerService Service { get; }

    /// <summary>A listed game at <c>&lt;slug&gt;.example:4201</c>, with a due crawl target attached.</summary>
    public async Task<Guid> GameAsync(string name, TimeSpan? crawlDelay = null)
    {
        var ct = CancellationToken.None;
        var id = Guid.CreateVersion7();
        var slug = name.ToLowerInvariant();
        var host = $"{slug}.example";

        await Games.InsertAsync(
            new Game(id, slug, name, LifecycleState.Active, IsClaimed: false, Start, Start, null), ct);
        await Endpoints.UpsertAsync(
            new GameEndpoint(id, host, 4201, EndpointKind.Telnet, Start, Start, EndpointState.Active), ct);

        var targetId = await Targets.AddAsync(new CrawlTarget
        {
            Id = Guid.CreateVersion7(),
            GameId = id,
            Host = host,
            Port = 4201,
            NextProbeAt = Time.GetUtcNow(),
            FirstSeenAt = Start,
            CrawlDelay = crawlDelay,
        }, ct);
        await Targets.AttachGameAsync(targetId, id, ct);

        return id;
    }

    public Task<ClaimToken> IssueAsync(Guid gameId) => Issuer.IssueAsync(gameId, CancellationToken.None);
}
```

**Note for the implementer:** `AdvisoryLock(null!)` is safe here because `RunCycleAsync` is called
directly and never touches the lock — the lock is acquired in `ExecuteAsync`, which these tests do not
run. If that ever changes, this line will `NullReferenceException` loudly rather than silently
crawling twice, which is the right failure.

- [ ] **Step 6: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
```
Expected: PASS — 7 new tests, and every Plan 03 `CrawlLoopTests` case still green (they will need the
new constructor argument; pass `new ClaimCycle(new InMemoryClaimRepository(), …)` in their `World`).

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Discovery/Ownership/ClaimCycle.cs src/MUI.Discovery/CrawlerService.cs \
        tests/MUI.Discovery.Tests/Support/ClaimWorld.cs \
        tests/MUI.Discovery.Tests/Ownership/ClaimSchedulingTests.cs \
        tests/MUI.Discovery.Tests/CrawlLoopTests.cs
git commit -m "feat: verification rides the crawl schedule and is structurally unable to hurry it (spec 11)"
```

---

### Task 9: A verified claim flips `is_claimed`, and the archive ceiling follows (spec §7.5, §8)

§7.5: "A claimed game always receives the ceiling. Someone with server access has demonstrably staked
a claim, which is worth a year regardless of how long we have been watching. This is also one more
concrete reason to claim (§8)."

**The formula already exists and is already pinned.** `ArchivePolicy.GraceFor(..., isClaimed: true)`
returns `Ceiling` outright, `ArchivePolicyTests` proves it, and Plan 02's `ArchiveSweeper` already
feeds `Game.IsClaimed` into it. The only thing missing is anything that ever sets that flag. **Do not
reimplement, re-derive or re-clamp the formula here.**

**Files:**
- Modify: `src/MUI.Storage/IGameRepository.cs`
- Modify: `src/MUI.Storage/NpgsqlGameRepository.cs`
- Modify: `tests/MUI.Discovery.Tests/Support/InMemoryRepositories.cs`
- Modify: `src/MUI.Discovery/Ownership/ClaimCycle.cs`
- Modify: `src/MUI.Discovery/Ownership/Dns/DnsClaimPoller.cs`
- Create: `src/MUI.Discovery/Ownership/ClaimGrant.cs`
- Create: `tests/MUI.Discovery.Tests/Ownership/ClaimedGameArchivingTests.cs`

**Interfaces:**
- Consumes: `ArchivePolicy.GraceFor`, `ArchivePolicy.Ceiling` (existing); `ArchiveSweeper` (Plan 02
  Task 18); `IGameRepository`, `Game`, `LifecycleState` (Plan 02); `ClaimCycle`, `DnsClaimPoller`
  (Tasks 6, 8).
- Produces:
  - `Task IGameRepository.SetClaimedAsync(Guid id, bool isClaimed, CancellationToken ct)`
  - `sealed class MUI.Discovery.Ownership.ClaimGrant(IGameRepository games, IGameFieldRepository fields, IClaimRepository claims, IOwnerRepository owners, TimeProvider time)`
    with `Task ApplyAsync(ClaimToken token, ClaimChannel channel, CancellationToken ct)`
    and `Task WithdrawAsync(Guid gameId, CancellationToken ct)`

**Note for the implementer:** `ClaimGrant` is introduced here with only its `IGameRepository` half
wired. Task 10 adds the `game_field` mirror and Task 11 adds the owner and audit rows to the *same*
method. It is one type because "what happens when a claim is proved" is one fact, and splitting it
across three call sites is how one of them gets forgotten. The `IOwnerRepository` parameter is
declared in Task 11; until then pass Task 11's interface with the in-memory fake, which Task 11's
Step 4 replaces with the real one.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/Ownership/ClaimedGameArchivingTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery.Ownership;
using MUI.Discovery.Tests.Support;
using MUI.Discovery.Writers;

namespace MUI.Discovery.Tests.Ownership;

/// <summary>
/// Spec §7.5's ceiling, reached the way a real game reaches it: by proving a claim and then going
/// dark. The formula is ArchivePolicy's and is pinned by ArchivePolicyTests; what is under test here
/// is that verifying a claim is what makes ArchiveSweeper see a claimed game at all.
/// </summary>
public class ClaimedGameArchivingTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private const string Token = "muidx-a2b3-c4d5-e6f7";

    [Test]
    public async Task VerifyingAClaimMarksTheGameClaimed()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        world.Probe.Answering("corvid.example", 4201, () => ProbeResults.Answered(
            host: "corvid.example",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), (ClaimVocabulary.MsspVariable, [token.Value]))));

        await world.Service.RunCycleAsync(None);

        await Assert.That((await world.Games.ByIdAsync(game, None))!.IsClaimed).IsTrue();
    }

    [Test]
    public async Task AClaimProvedThroughDnsMarksItJustTheSame()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        world.Dns.Answer(ClaimVocabulary.DnsNameFor("corvid.example"), token.Value);

        await world.Poller.PollAsync(None);

        await Assert.That((await world.Games.ByIdAsync(game, None))!.IsClaimed).IsTrue();
    }

    [Test]
    public async Task AnUnclaimedYoungGameIsArchivedAndTheSameGameClaimedIsNot()
    {
        // 300 days dark on a game we have only ever seen up for a day. Unclaimed that is far past
        // the 60-day floor; claimed it is inside the 365-day ceiling. One flag, opposite outcomes.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        await world.GoDarkAsync(game, reachableDays: 1, darkDays: 300);
        var sweeper = new ArchiveSweeper(world.Games, world.Availability, world.Time);

        await Assert.That(await sweeper.SweepAsync(None)).IsEqualTo(1);
        await Assert.That((await world.Games.ByIdAsync(game, None))!.State).IsEqualTo(LifecycleState.Archived);

        var claimedWorld = new ClaimWorld();
        var claimedGame = await claimedWorld.GameAsync("Corvid");
        await claimedWorld.GoDarkAsync(claimedGame, reachableDays: 1, darkDays: 300);
        await claimedWorld.Games.SetClaimedAsync(claimedGame, true, None);
        var claimedSweeper = new ArchiveSweeper(claimedWorld.Games, claimedWorld.Availability, claimedWorld.Time);

        await Assert.That(await claimedSweeper.SweepAsync(None)).IsEqualTo(0);
        await Assert.That((await claimedWorld.Games.ByIdAsync(claimedGame, None))!.State)
            .IsEqualTo(LifecycleState.Dark);
    }

    [Test]
    public async Task TheGraceAClaimBuysIsExactlyTheCeilingAndNotAFormulaOfItsOwn()
    {
        // Guards against somebody re-deriving §7.5 here. The number comes from ArchivePolicy or it
        // comes from nowhere.
        await Assert.That(ArchivePolicy.GraceFor(TimeSpan.FromDays(1), isClaimed: true))
            .IsEqualTo(ArchivePolicy.Ceiling);
        await Assert.That(ArchivePolicy.GraceFor(TimeSpan.FromDays(4000), isClaimed: true))
            .IsEqualTo(ArchivePolicy.Ceiling);
    }

    [Test]
    public async Task AClaimedGameIsStillProbedAndStillComesBackByItself()
    {
        // §7.4 is untouched by claiming: the ceiling is about the archive, not about the schedule.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        await world.Games.SetClaimedAsync(game, true, None);

        await world.Service.RunCycleAsync(None);

        await Assert.That(world.Targets.All.Single().NextProbeAt)
            .IsEqualTo(world.Time.GetUtcNow() + ProbeSchedule.BaseInterval);
    }

    [Test]
    public async Task WithdrawingAClaimUnmarksTheGame()
    {
        // Staff withdrawal and transfer both go through here. A game that keeps the ceiling after
        // its claim is gone is a game that never archives, for a reason nobody can see.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None);

        await world.Grant.WithdrawAsync(game, None);

        await Assert.That((await world.Games.ByIdAsync(game, None))!.IsClaimed).IsFalse();
    }
}
```

Add to `ClaimWorld` the two members these tests use:

```csharp
    public DnsClaimPoller Poller { get; }

    public ClaimGrant Grant { get; }
```

built in the constructor as
`Poller = new DnsClaimPoller(Claims, Endpoints, Verifier, Time);` and
`Grant = new ClaimGrant(Games, Fields, Claims, Owners, Time);`, plus:

```csharp
    /// <summary>Gives a game a reachable stretch and then an open outage, the shape §7.5 measures.</summary>
    public async Task GoDarkAsync(Guid gameId, double reachableDays, double darkDays)
    {
        var ct = CancellationToken.None;
        var now = Time.GetUtcNow();
        var start = now.AddDays(-(reachableDays + darkDays));
        var wentDark = now.AddDays(-darkDays);

        var up = await Availability.OpenAsync(gameId, AvailabilityState.Reachable, FailureCause.None, start, ct);
        await Availability.CloseAsync(up, wentDark, ct);
        await Availability.OpenAsync(gameId, AvailabilityState.Unreachable, FailureCause.Timeout, wentDark, ct);

        var game = (await Games.ByIdAsync(gameId, ct))!;
        await Games.InsertAsync(game with { State = LifecycleState.Dark, LastReachableAt = wentDark }, ct);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS1061: 'IGameRepository' does not contain a definition for 'SetClaimedAsync'`.

- [ ] **Step 3: Teach the game repository to record a claim**

In `src/MUI.Storage/IGameRepository.cs`, add:

```csharp
    /// <summary>
    /// Records that an owner has proved control (spec §8), which is the flag <c>ArchiveSweeper</c>
    /// feeds to <see cref="MUI.Catalog.ArchivePolicy.GraceFor"/> to grant the §7.5 ceiling.
    /// </summary>
    Task SetClaimedAsync(Guid id, bool isClaimed, CancellationToken ct);
```

In `src/MUI.Storage/NpgsqlGameRepository.cs`:

```csharp
    public async Task SetClaimedAsync(Guid id, bool isClaimed, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE game SET is_claimed = @isClaimed WHERE id = @id",
            new { id, isClaimed }, cancellationToken: ct));
    }
```

In `tests/MUI.Discovery.Tests/Support/InMemoryRepositories.cs`, on `InMemoryGameRepository`:

```csharp
    public Task SetClaimedAsync(Guid id, bool isClaimed, CancellationToken ct)
    {
        var index = All.FindIndex(game => game.Id == id);
        if (index >= 0)
        {
            All[index] = All[index] with { IsClaimed = isClaimed };
        }

        return Task.CompletedTask;
    }
```

- [ ] **Step 4: Write `ClaimGrant`**

Create `src/MUI.Discovery/Ownership/ClaimGrant.cs`:

```csharp
using MUI.Catalog;
using MUI.Storage;

namespace MUI.Discovery.Ownership;

/// <summary>
/// Everything that happens when a claim is proved, in one place (spec §7.5, §7.3, §8).
/// </summary>
/// <remarks>
/// <para>
/// Three channels prove a claim and each of them would otherwise have to remember the same list:
/// mark the game claimed, mirror the token into <c>game_field</c> so the identity beacon fires, and
/// write the audit entry. One type, because "what happens when a claim is proved" is one fact and
/// splitting it across three call sites is how one of them gets forgotten.
/// </para>
/// <para>
/// <b><see cref="WithdrawAsync"/> is the exact inverse and is not optional.</b> A game that keeps
/// <c>is_claimed</c> after its claim is gone never archives, for a reason nobody looking at it can
/// see; a <c>claim_token</c> field row that outlives its token keeps voting at weight 10.0 for ever
/// (Task 10).
/// </para>
/// </remarks>
public sealed class ClaimGrant(
    IGameRepository games,
    IGameFieldRepository fields,
    IClaimRepository claims,
    IOwnerRepository owners,
    TimeProvider time)
{
    public async Task ApplyAsync(ClaimToken token, ClaimChannel channel, CancellationToken ct)
    {
        await games.SetClaimedAsync(token.GameId, true, ct);
    }

    public async Task WithdrawAsync(Guid gameId, CancellationToken ct)
    {
        await games.SetClaimedAsync(gameId, false, ct);
    }
}
```

Task 10 fills in the `game_field` mirror and Task 11 the audit entry; the unused parameters are
declared now so neither task changes this type's shape at every call site.

- [ ] **Step 5: Call it from both verification paths**

In `ClaimCycle.OnProbeAsync`, replace the `SetStateAsync` call on the success path with:

```csharp
        await claims.SetStateAsync(token.Id, ClaimTokenState.Verified, evidence.Channel, now, ct);
        await grant.ApplyAsync(token, evidence.Channel, ct);
```

adding `ClaimGrant grant` to the primary constructor after `ClaimVerifier verifier`. Do the same in
`DnsClaimPoller.PollAsync`, adding `ClaimGrant grant` after `ClaimVerifier verifier`:

```csharp
                await claims.SetStateAsync(token.Id, ClaimTokenState.Verified, ClaimChannel.DnsTxt, now, ct);
                await grant.ApplyAsync(token, ClaimChannel.DnsTxt, ct);
```

The `ClaimSchedulingTests` structural pin still holds: `ClaimGrant` takes repositories for games,
fields, claims and owners, and no crawl target or schedule among them.

- [ ] **Step 6: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
```
Expected: PASS — 6 new tests, and Plan 02's `ArchiveSweeperTests` unchanged and still green.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Storage/IGameRepository.cs src/MUI.Storage/NpgsqlGameRepository.cs \
        src/MUI.Discovery/Ownership tests/MUI.Discovery.Tests
git commit -m "feat: a proved claim marks the game claimed, which grants the 7.5 archive ceiling"
```

---

### Task 10: The token persists as the identity beacon, and the clone guard it needs (spec §7.3)

§7.3 lists the claim token as "Decisive when present — a claimed game is never duplicated", and Plan
03 weights it at `IdentityWeights.ClaimToken = 10.0`, ten times the auto-merge threshold. Two things
have to be true for that to mean anything, and neither is free.

**First: the matcher reads `game_field["claim_token"]` and nothing else.** It never sees this plan's
`claim_token` table. A token held only in `IClaimRepository` leaves the signal permanently dead and
§7.3 silently not holding — the failure mode where every piece looks correctly wired and nothing
fires. So a verified token is mirrored into `game_field` through `IGameFieldRepository`, like any
other field, and a revoked one is deleted from it, or a withdrawn claim keeps voting for ever.

**Second: the token is public.** It is published in MSSP, on a connect screen, or in DNS — every
channel §8 offers is readable by anyone, deliberately, because that is what makes proof cost no email
round-trip. So a stranger can read a claimed game's token and put it in their own MSSP. Secrecy
cannot fix a credential whose whole job is to be published; a guard on decisiveness can. A real host
move and a clone differ in exactly one observable way: **when a game moves, its old endpoints stop
answering; when it is cloned, they keep answering.**

**Files:**
- Create: `src/MUI.Discovery/Ownership/ClaimBeaconPolicy.cs`
- Modify: `src/MUI.Discovery/Ownership/ClaimGrant.cs`
- Modify: `src/MUI.Discovery/IdentityMatcher.cs` (`IdentityMatcher` consults the policy — the matcher
  is its own file; `Identity.cs` holds the vocabulary and score types)
- Create: `tests/MUI.Discovery.Tests/Ownership/ClaimBeaconTests.cs`

**Interfaces:**
- Consumes: `IdentityFields.ClaimToken`, `IdentityWeights.ClaimToken`, `IdentityMatcher`,
  `IdentityVerdict`, `ClaimTokenBeacon` (Plan 03); `IGameFieldRepository`, `GameField`,
  `FieldSource`, `FieldConfidence`, `IEndpointRepository`, `GameEndpoint`, `EndpointState`
  (Plan 02); `ClaimGrant` (Task 9).
- Produces:
  - `enum MUI.Discovery.Ownership.BeaconVerdict { Absent, Decisive, Contested }`
  - `sealed class MUI.Discovery.Ownership.ClaimBeaconPolicy(IEndpointRepository endpoints)`
    with `Task<BeaconVerdict> WeighAsync(Guid candidateGameId, string? presentedToken, string? storedToken, string probedHost, CancellationToken ct)`
    — **no clock parameter.** The body reads no time, and a `now` it ignores is an invitation for the
    caller to reach for the ambient clock (which is exactly what the first draft of this task did).
    When the policy does need time — a token is valid for fourteen days, so it plausibly will —
    it takes `TimeProvider time` as a constructor parameter like every other type in this plan, and
    `ManualTimeProvider` drives it. See *Global Constraints*.
  - `Task IGameFieldRepository.DeleteAsync(Guid gameId, string field, CancellationToken ct)`

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/Ownership/ClaimBeaconTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery.Ownership;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests.Ownership;

/// <summary>
/// Spec §7.3's decisive signal, and the guard it needs to survive being public.
/// </summary>
/// <remarks>
/// The claim token is published where anyone can read it — that is the point of §8, not an oversight
/// — and it is weighted at ten times the auto-merge threshold. Without a guard, reading a game's MSSP
/// is enough to be merged into it.
/// </remarks>
public class ClaimBeaconTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private const string Token = "muidx-a2b3-c4d5-e6f7";

    [Test]
    public async Task AVerifiedClaimWritesTheBeaconFieldTheMatcherReads()
    {
        // Without this row the 10.0 signal never fires and §7.3 silently does not hold.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);

        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None);

        var field = (await world.Fields.ForGameAsync(game, None))
            .Single(f => f.Field == IdentityFields.ClaimToken);

        await Assert.That(field.Value).IsEqualTo(token.Value);
        await Assert.That(field.Source).IsEqualTo(FieldSource.Owner);
    }

    [Test]
    public async Task AClaimedGameSeenAgainScoresAtTheClaimTokenWeight()
    {
        // The end-to-end pin: verify a claim, then run the matcher over a later probe carrying the
        // same beacon. This is what catches the two halves drifting apart.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None);

        var matcher = new IdentityMatcher(
            world.Games, world.Endpoints, world.Fields, world.Fields, new DiscoveryOptions());
        var verdict = await matcher.ResolveAsync(ProbeResults.Answered(
            host: "corvid.example",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), (ClaimVocabulary.MsspVariable, [token.Value]))), None);

        var merge = verdict as IdentityVerdict.Merge;

        await Assert.That(merge).IsNotNull();
        await Assert.That(merge!.GameId).IsEqualTo(game);
        await Assert.That(merge.Score.Signals
                .Single(signal => signal.Name == nameof(IdentityWeights.ClaimToken)).Matched)
            .IsTrue();
        await Assert.That(merge.Score.Score).IsGreaterThanOrEqualTo(IdentityWeights.ClaimToken);
    }

    [Test]
    public async Task AGameThatMovedIsFollowedByItsToken()
    {
        // The argument we make to owners on the success screen: "never lost on a host move".
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None);
        await world.MarkEndpointAsync(game, "corvid.example", EndpointState.Gone);

        var policy = new ClaimBeaconPolicy(world.Endpoints);
        var verdict = await policy.WeighAsync(game, token.Value, token.Value, "newhome.example", None);

        await Assert.That(verdict).IsEqualTo(BeaconVerdict.Decisive);
    }

    [Test]
    public async Task ACloneCannotStealAGameByRepublishingItsToken()
    {
        // The original is still answering at its own address, so this is not a move. §7.3 already
        // says the right answer under uncertainty: both pages stay live, linked reciprocally.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None);

        var policy = new ClaimBeaconPolicy(world.Endpoints);
        var verdict = await policy.WeighAsync(game, token.Value, token.Value, "impostor.example", None);

        await Assert.That(verdict).IsEqualTo(BeaconVerdict.Contested);
    }

    [Test]
    public async Task TheGamesOwnAddressIsAlwaysDecisive()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None);

        var policy = new ClaimBeaconPolicy(world.Endpoints);

        await Assert.That(await policy.WeighAsync(game, token.Value, token.Value, "corvid.example", None))
            .IsEqualTo(BeaconVerdict.Decisive);
    }

    [Test]
    public async Task ADifferentTokenIsNoSignalRatherThanANegativeOne()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var policy = new ClaimBeaconPolicy(world.Endpoints);

        await Assert.That(await policy.WeighAsync(game, "muidx-9999-8888-7777", Token, "elsewhere.example", None))
            .IsEqualTo(BeaconVerdict.Absent);
        await Assert.That(await policy.WeighAsync(game, null, Token, "elsewhere.example", None))
            .IsEqualTo(BeaconVerdict.Absent);
        await Assert.That(await policy.WeighAsync(game, Token, null, "elsewhere.example", None))
            .IsEqualTo(BeaconVerdict.Absent);
    }

    [Test]
    public async Task AContestedBeaconDoesNotAutoMergeAndDoesNotHideEitherGame()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None);

        var matcher = new IdentityMatcher(
            world.Games, world.Endpoints, world.Fields, world.Fields, new DiscoveryOptions(),
            new ClaimBeaconPolicy(world.Endpoints));
        var verdict = await matcher.ResolveAsync(ProbeResults.Answered(
            host: "impostor.example",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid Reborn"]), (ClaimVocabulary.MsspVariable, [token.Value]))), None);

        await Assert.That(verdict is IdentityVerdict.Merge).IsFalse();
    }

    [Test]
    public async Task WithdrawingAClaimDeletesTheBeaconRow()
    {
        // A revoked token that stays in game_field keeps voting at weight 10.0 for ever, which is
        // both wrong and invisible.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None);

        await world.Grant.WithdrawAsync(game, None);

        await Assert.That((await world.Fields.ForGameAsync(game, None))
            .Any(field => field.Field == IdentityFields.ClaimToken)).IsFalse();
    }

    [Test]
    public async Task RegeneratingReplacesTheBeaconRatherThanAddingASecond()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var first = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(first, ClaimChannel.Mssp, None);

        var second = await world.Issuer.RegenerateAsync(game, None);
        await world.Grant.ApplyAsync(second, ClaimChannel.DnsTxt, None);

        var rows = (await world.Fields.ForGameAsync(game, None))
            .Where(field => field.Field == IdentityFields.ClaimToken).ToList();

        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].Value).IsEqualTo(second.Value);
    }
}
```

Add to `ClaimWorld`:

```csharp
    /// <summary>Moves one of a game's endpoints to a new state — how a host move looks in §5.5.</summary>
    public async Task MarkEndpointAsync(Guid gameId, string host, EndpointState state)
    {
        var ct = CancellationToken.None;
        var endpoint = (await Endpoints.ForGameAsync(gameId, ct))
            .Single(e => string.Equals(e.Host, host, StringComparison.OrdinalIgnoreCase));

        await Endpoints.UpsertAsync(endpoint with { State = state }, ct);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'ClaimBeaconPolicy' could not be found`.

- [ ] **Step 3: Mirror the token into `game_field` on grant, and delete it on withdrawal**

Replace the two methods in `src/MUI.Discovery/Ownership/ClaimGrant.cs`:

```csharp
    public async Task ApplyAsync(ClaimToken token, ClaimChannel channel, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        await games.SetClaimedAsync(token.GameId, true, ct);

        // §7.3's beacon. IdentityMatcher compares candidates against game_field["claim_token"] and
        // never against the claim_token table, so a token that lives only in this subsystem's own
        // store leaves the 10.0 signal permanently dead — with every piece looking wired up.
        // FieldSource.Owner because it is the owner asserting identity, through a channel we then
        // observed; the field competes with nothing, so the §5.1 ladder never has to arbitrate it.
        await fields.UpsertAsync(
            new GameField(token.GameId, IdentityFields.ClaimToken, token.Value,
                FieldSource.Owner, FieldConfidence.Observed, now, now), ct);
    }

    public async Task WithdrawAsync(Guid gameId, CancellationToken ct)
    {
        await games.SetClaimedAsync(gameId, false, ct);

        // Not optional. A beacon row that outlives its token votes at weight 10.0 for ever, and
        // nothing on any page would show why.
        await fields.DeleteAsync(gameId, IdentityFields.ClaimToken, ct);
    }
```

Add `Task DeleteAsync(Guid gameId, string field, CancellationToken ct);` to
`MUI.Storage.IGameFieldRepository`, implemented in `NpgsqlGameFieldRepository` as
`DELETE FROM game_field WHERE game_id = @gameId AND field = @field` and in
`InMemoryGameFieldRepository` as a `RemoveAll`.

Also change `ClaimTokenIssuer.RevokeAsync` to be reached through `ClaimGrant.WithdrawAsync` from the
dashboard and staff paths — the issuer keeps revoking the row, and the grant is what clears the
catalogue state. Task 11's transfer calls both, in that order.

- [ ] **Step 4: Write the policy**

Create `src/MUI.Discovery/Ownership/ClaimBeaconPolicy.cs`:

```csharp
using MUI.Catalog;
using MUI.Storage;

namespace MUI.Discovery.Ownership;

/// <summary>How much a presented claim token is worth on this probe.</summary>
public enum BeaconVerdict
{
    /// <summary>No token, or not this game's. Contributes nothing, positive or negative.</summary>
    Absent,

    /// <summary>This game's token, from somewhere it could legitimately be. Worth the full weight.</summary>
    Decisive,

    /// <summary>
    /// This game's token, from a host it has never been seen at, while its own addresses are still
    /// answering. Worth nothing on its own: §7.3's answer under uncertainty is a review pair with both
    /// pages live, because a wrongly hidden game is worse than a visible duplicate.
    /// </summary>
    Contested,
}

/// <summary>
/// Whether a claim-token beacon is decisive (spec §7.3, §8).
/// </summary>
/// <remarks>
/// <para>
/// <b>The token is public and cannot be made otherwise.</b> Every channel §8 offers — an MSSP
/// variable, a connect-screen line, a DNS TXT record — is readable by anyone who asks, deliberately,
/// because that is what makes proving control cost no mail round-trip. So anyone can read a claimed
/// game's token and republish it as their own, and at <c>IdentityWeights.ClaimToken = 10.0</c> that
/// would be enough to be auto-merged into somebody else's game.
/// </para>
/// <para>
/// <b>A move and a clone differ in one observable way.</b> When a game moves, the addresses it used to
/// answer on stop answering. When a game is cloned, they keep answering. So the token is decisive
/// from the game's own addresses always, and from a strange address only when the game's known ones
/// have gone quiet — which is exactly the case the beacon exists for, and exactly not the case a
/// clone presents.
/// </para>
/// <para>
/// <b>This type reads no clock, and takes no <c>now</c>.</b> The question it answers — is this host one
/// this game is known at, and are its other addresses still answering — is answered entirely out of
/// <see cref="IEndpointRepository"/>. A <c>now</c> parameter the body ignores is worse than no
/// parameter: every caller then has to produce one, and the shortest way to produce one is the
/// ambient static clock, which is a real clock inside a type the rest of this design keeps
/// deterministic. If a later rule does need time — the token is valid for fourteen days, so one
/// plausibly will — this class takes <c>TimeProvider time</c> as a constructor parameter and the tests
/// drive it with <c>ManualTimeProvider</c>.
/// </para>
/// </remarks>
public sealed class ClaimBeaconPolicy(IEndpointRepository endpoints)
{
    public async Task<BeaconVerdict> WeighAsync(
        Guid candidateGameId,
        string? presentedToken,
        string? storedToken,
        string probedHost,
        CancellationToken ct)
    {
        if (presentedToken is null || storedToken is null
            || !string.Equals(presentedToken.Trim(), storedToken.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return BeaconVerdict.Absent;
        }

        var known = await endpoints.ForGameAsync(candidateGameId, ct);

        if (known.Any(endpoint =>
                string.Equals(endpoint.Host, probedHost, StringComparison.OrdinalIgnoreCase)))
        {
            return BeaconVerdict.Decisive;
        }

        // A strange host presenting this game's token. If nothing of the game's own is still active,
        // this is what a host move looks like and the beacon is doing its job.
        return known.Any(endpoint => endpoint.State is EndpointState.Active)
            ? BeaconVerdict.Contested
            : BeaconVerdict.Decisive;
    }
}
```

- [ ] **Step 5: Have the matcher consult it**

In `src/MUI.Discovery/IdentityMatcher.cs`, add an optional last parameter to `IdentityMatcher` —
`ClaimBeaconPolicy? beacons = null`, after `DiscoveryOptions options` — and in **`ScoreAsync`**,
which is Plan 03's one scoring method and builds the whole signal list, replace the claim-token
signal line with:

```csharp
            new(nameof(IdentityWeights.ClaimToken), IdentityWeights.ClaimToken,
                await IsBeaconDecisiveAsync(gameId, stored, token, result.Host, ct)),
```

adding:

```csharp
    /// <summary>
    /// A claim token counts only where <see cref="ClaimBeaconPolicy"/> says it is this game's and
    /// from somewhere it could legitimately be. With no policy supplied the old behaviour stands —
    /// a bare value comparison — which is what the pre-claiming tests assert.
    /// </summary>
    private async Task<bool> IsBeaconDecisiveAsync(
        Guid gameId,
        IReadOnlyDictionary<string, string> stored,
        string? presented,
        string probedHost,
        CancellationToken ct)
    {
        if (beacons is null)
        {
            return Same(stored, IdentityFields.ClaimToken, presented);
        }

        stored.TryGetValue(IdentityFields.ClaimToken, out var storedToken);

        return await beacons.WeighAsync(gameId, presented, storedToken, probedHost, ct)
            is BeaconVerdict.Decisive;
    }
```

Three things about that signature, each of which the first draft of this task got wrong by writing
against a remembered Plan 03 rather than the real one:

- **`stored` is an `IReadOnlyDictionary<string, string>`**, not `IReadOnlyList<GameField>`.
  `ScoreAsync` builds it once with
  `(await fields.ForGameAsync(gameId, ct)).ToDictionary(f => f.Field, f => f.Value, StringComparer.OrdinalIgnoreCase)`,
  and `Same` reads it by `TryGetValue`. A list-shaped helper does not compile against it.
- **The game id is `ScoreAsync`'s own parameter and is passed straight through.** Do not recover it
  from the stored fields (`stored.FirstOrDefault()?.GameId`): a candidate with no `game_field` rows
  yet — an endpoint match on a brand-new game — yields `Guid.Empty`, and the policy is then asked
  about a game that does not exist. The dictionary has no game id in it at all.
- **No clock is passed.** `WeighAsync` takes none; see *Global Constraints*.

- [ ] **Step 6: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
```
Expected: PASS — 9 new tests, and every Plan 03 identity test still green (they construct
`IdentityMatcher` without a policy and take the bare-comparison arm).

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Discovery/Ownership/ClaimBeaconPolicy.cs src/MUI.Discovery/Ownership/ClaimGrant.cs \
        src/MUI.Discovery/IdentityMatcher.cs src/MUI.Storage/IGameFieldRepository.cs \
        src/MUI.Storage/NpgsqlGameFieldRepository.cs tests/MUI.Discovery.Tests
git commit -m "feat: the verified token becomes the 7.3 identity beacon, guarded against republication"
```

---

### Task 11: Multi-owner, transfer, and the audit log (spec §8)

§8's last sentence about the dashboard: "Multi-owner, transfer, and an audit log." The design handoff
§09 fills in what each means. Two owners, "both listed publicly as 'claimed by the game's staff' and
never by name". And transfer: "Transfer needs a fresh token published through the game — the same
proof as the first claim, because staff turnover is the normal case, not an exception."

**Files:**
- Create: `src/MUI.Catalog/Ownership/Owner.cs`
- Create: `src/MUI.Storage/Migrations/0022_owner.sql`
- Create: `src/MUI.Storage/Migrations/0023_owner_audit.sql`
- Create: `src/MUI.Storage/Ownership/IOwnerRepository.cs`
- Create: `src/MUI.Storage/Ownership/NpgsqlOwnerRepository.cs`
- Create: `src/MUI.Discovery/Ownership/OwnershipService.cs`
- Create: `tests/MUI.Discovery.Tests/Support/InMemoryOwnerRepository.cs`
- Create: `tests/MUI.Discovery.Tests/Ownership/OwnershipTests.cs`
- Create: `tests/MUI.Storage.Tests/Claiming/OwnerRepositoryTests.cs`

**Interfaces:**
- Consumes: `ClaimTokenIssuer`, `ClaimGrant` (Tasks 2, 9, 10); `GameSeed`, `PostgresFixture` (Plan 02).
- Produces:
  - `sealed record MUI.Catalog.Owner(Guid Id, Guid GameId, string Handle, DateTimeOffset AddedAt, DateTimeOffset? RemovedAt)`
    with `bool IsCurrent`
  - `enum MUI.Catalog.OwnerAuditActor { Owner, System, Staff }`
  - `sealed record MUI.Catalog.OwnerAuditEntry(long Id, Guid GameId, DateTimeOffset At, OwnerAuditActor Actor, string? OwnerHandle, string Action, string? Detail)`
  - `static class MUI.Catalog.OwnerActions` with `Claimed`, `OwnerAdded`, `OwnerRemoved`,
    `TransferStarted`, `TransferCompleted`, `FieldEdited`, `FieldConfirmed`, `ConnectScreenHidden`,
    `ConnectScreenShown`, `WhoPreferenceChanged`, `OptedOut`, `OptedIn`, `TokenRegenerated`,
    `ClaimWithdrawn`, `Observed`
  - `interface MUI.Storage.IOwnerRepository` with `ForGameAsync`, `AddAsync`, `RemoveAsync`,
    `IsOwnerAsync`, `AppendAuditAsync`, `AuditAsync`
  - `sealed class MUI.Storage.NpgsqlOwnerRepository(NpgsqlDataSource source) : IOwnerRepository`
  - `sealed class MUI.Discovery.Ownership.OwnershipService(IOwnerRepository owners, ClaimTokenIssuer issuer, ClaimGrant grant, TimeProvider time)`
    with `AddOwnerAsync`, `RemoveOwnerAsync`, `BeginTransferAsync`, `RecordAsync`

- [ ] **Step 1: Write the failing behavioural test**

Create `tests/MUI.Discovery.Tests/Ownership/OwnershipTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery.Ownership;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests.Ownership;

/// <summary>
/// Spec §8's "multi-owner, transfer, and an audit log", with the delivered design's rules: owners are
/// listed publicly as the game's staff and never by name, and a transfer needs a fresh token proved
/// through the game — the same proof as the first claim, because staff turnover is the normal case.
/// </summary>
public class OwnershipTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task ProvingAClaimMakesTheClaimantAnOwnerAndWritesTheAuditEntry()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);

        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None, claimant: "rowan@example.org");

        await Assert.That((await world.Owners.ForGameAsync(game, None)).Select(o => o.Handle))
            .IsEquivalentTo(new[] { "rowan@example.org" });

        var audit = await world.Owners.AuditAsync(game, 10, None);

        await Assert.That(audit.Single().Action).IsEqualTo(OwnerActions.Claimed);
        await Assert.That(audit.Single().Detail).IsEqualTo("mssp");
        await Assert.That(audit.Single().Actor).IsEqualTo(OwnerAuditActor.Owner);
    }

    [Test]
    public async Task AGameMayHaveTwoOwners()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None, claimant: "rowan@example.org");

        await world.Ownership.AddOwnerAsync(game, "rowan@example.org", "wren@example.org", None);

        await Assert.That((await world.Owners.ForGameAsync(game, None)).Count).IsEqualTo(2);
        await Assert.That(await world.Owners.IsOwnerAsync(game, "wren@example.org", None)).IsTrue();
    }

    [Test]
    public async Task OnlyAnOwnerMayAddAnOwner()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None, claimant: "rowan@example.org");

        await Assert.That(async () =>
                await world.Ownership.AddOwnerAsync(game, "stranger@example.org", "wren@example.org", None))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task ARemovedOwnerLeavesTheListAndStaysInTheLog()
    {
        // Nothing is ever deleted (rule 3). The audit log is the one place a departed owner remains,
        // because "who changed the description in 2024" is a question the log exists to answer.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None, claimant: "rowan@example.org");
        await world.Ownership.AddOwnerAsync(game, "rowan@example.org", "wren@example.org", None);

        await world.Ownership.RemoveOwnerAsync(game, "rowan@example.org", "wren@example.org", None);

        await Assert.That((await world.Owners.ForGameAsync(game, None)).Select(o => o.Handle))
            .IsEquivalentTo(new[] { "rowan@example.org" });
        await Assert.That((await world.Owners.AuditAsync(game, 10, None)).Select(entry => entry.Action))
            .Contains(OwnerActions.OwnerRemoved);
    }

    [Test]
    public async Task ATransferIssuesAFreshTokenAndUnclaimsTheGameUntilItIsProved()
    {
        // The design's rule, and the reason for it: the new staff have server access or they do not,
        // and the old staff's say-so is not evidence either way.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var first = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(first, ClaimChannel.Mssp, None, claimant: "rowan@example.org");

        var fresh = await world.Ownership.BeginTransferAsync(game, "rowan@example.org", None);

        await Assert.That(fresh.Value).IsNotEqualTo(first.Value);
        await Assert.That(fresh.State).IsEqualTo(ClaimTokenState.Pending);
        await Assert.That((await world.Games.ByIdAsync(game, None))!.IsClaimed).IsFalse();
        await Assert.That((await world.Fields.ForGameAsync(game, None))
            .Any(field => field.Field == IdentityFields.ClaimToken)).IsFalse();
        await Assert.That((await world.Owners.AuditAsync(game, 10, None)).Select(entry => entry.Action))
            .Contains(OwnerActions.TransferStarted);
    }

    [Test]
    public async Task ProvingTheTransferTokenClaimsTheGameForWhoeverProvedIt()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var first = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(first, ClaimChannel.Mssp, None, claimant: "rowan@example.org");
        var fresh = await world.Ownership.BeginTransferAsync(game, "rowan@example.org", None);

        await world.Grant.ApplyAsync(fresh, ClaimChannel.DnsTxt, None, claimant: "wren@example.org");

        await Assert.That((await world.Games.ByIdAsync(game, None))!.IsClaimed).IsTrue();
        await Assert.That(await world.Owners.IsOwnerAsync(game, "wren@example.org", None)).IsTrue();
        await Assert.That((await world.Fields.ForGameAsync(game, None))
            .Single(field => field.Field == IdentityFields.ClaimToken).Value).IsEqualTo(fresh.Value);
    }

    [Test]
    public async Task TheSystemGetsAuditEntriesOfItsOwn()
    {
        // The delivered audit log has a "system · TLS observed, no owner action" row, and that is
        // the right shape: the log is the game's history, not only the owners' keystrokes.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");

        await world.Ownership.RecordAsync(
            game, OwnerAuditActor.System, null, OwnerActions.Observed, "TLS observed on port 4202", None);

        var entry = (await world.Owners.AuditAsync(game, 10, None)).Single();

        await Assert.That(entry.Actor).IsEqualTo(OwnerAuditActor.System);
        await Assert.That(entry.OwnerHandle).IsNull();
    }

    [Test]
    public async Task TheAuditLogComesBackNewestFirstAndBounded()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");

        for (var i = 0; i < 20; i++)
        {
            await world.Ownership.RecordAsync(
                game, OwnerAuditActor.System, null, OwnerActions.Observed, $"entry {i}", None);
            world.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var audit = await world.Owners.AuditAsync(game, 5, None);

        await Assert.That(audit.Count).IsEqualTo(5);
        await Assert.That(audit[0].Detail).IsEqualTo("entry 19");
    }
}
```

Add to `ClaimWorld`: `public InMemoryOwnerRepository Owners { get; }` and
`public OwnershipService Ownership { get; }`, built as `Owners = new InMemoryOwnerRepository();` before
`Grant`, and `Ownership = new OwnershipService(Owners, Issuer, Grant, Time);` after it.

- [ ] **Step 2: Write the failing storage test**

Create `tests/MUI.Storage.Tests/Claiming/OwnerRepositoryTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Storage.Tests.Support;

namespace MUI.Storage.Tests.Claiming;

/// <summary>Spec §8's owner list and audit log, round-tripped (see also §7.4 — nothing is deleted).</summary>
public class OwnerRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task AnOwnerRoundTrips()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await GameSeed.InsertAsync(db.DataSource);
        var owners = new NpgsqlOwnerRepository(db.DataSource);

        await owners.AddAsync(new Owner(Guid.CreateVersion7(), game, "rowan@example.org", Now, null), None);

        await Assert.That((await owners.ForGameAsync(game, None)).Single().Handle).IsEqualTo("rowan@example.org");
        await Assert.That(await owners.IsOwnerAsync(game, "rowan@example.org", None)).IsTrue();
        await Assert.That(await owners.IsOwnerAsync(game, "someone@example.org", None)).IsFalse();
    }

    [Test]
    public async Task OneHandleCannotBeAddedToOneGameTwice()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await GameSeed.InsertAsync(db.DataSource);
        var owners = new NpgsqlOwnerRepository(db.DataSource);
        await owners.AddAsync(new Owner(Guid.CreateVersion7(), game, "rowan@example.org", Now, null), None);

        await Assert.That(async () => await owners.AddAsync(
                new Owner(Guid.CreateVersion7(), game, "rowan@example.org", Now, null), None))
            .Throws<Npgsql.PostgresException>();
    }

    [Test]
    public async Task ARemovedOwnerMayBeAddedBackLater()
    {
        // Staff come back. The partial unique index is on current owners only, so the historical row
        // stays and a fresh one may be written beside it.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await GameSeed.InsertAsync(db.DataSource);
        var owners = new NpgsqlOwnerRepository(db.DataSource);
        var first = new Owner(Guid.CreateVersion7(), game, "rowan@example.org", Now, null);
        await owners.AddAsync(first, None);

        await owners.RemoveAsync(first.Id, Now.AddDays(1), None);
        await owners.AddAsync(
            new Owner(Guid.CreateVersion7(), game, "rowan@example.org", Now.AddDays(2), null), None);

        await Assert.That((await owners.ForGameAsync(game, None)).Count).IsEqualTo(1);
    }

    [Test]
    public async Task AnAuditEntryRoundTripsAndComesBackNewestFirst()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await GameSeed.InsertAsync(db.DataSource);
        var owners = new NpgsqlOwnerRepository(db.DataSource);

        await owners.AppendAuditAsync(new OwnerAuditEntry(
            0, game, Now, OwnerAuditActor.Owner, "rowan@example.org", OwnerActions.Claimed, "mssp"), None);
        await owners.AppendAuditAsync(new OwnerAuditEntry(
            0, game, Now.AddHours(1), OwnerAuditActor.System, null, OwnerActions.Observed, "TLS observed"), None);

        var audit = await owners.AuditAsync(game, 10, None);

        await Assert.That(audit[0].Action).IsEqualTo(OwnerActions.Observed);
        await Assert.That(audit[0].OwnerHandle).IsNull();
        await Assert.That(audit[1].OwnerHandle).IsEqualTo("rowan@example.org");
    }

    [Test]
    public async Task AnActorNobodyDeclaredIsRefusedByTheSchema()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();
        var game = await GameSeed.InsertAsync(db.DataSource);

        await Assert.That(async () => await connection.ExecuteAsync(
            """
            INSERT INTO owner_audit (game_id, at, actor, action) VALUES (@game, now(), 'robot', 'claimed')
            """, new { game })).Throws<Npgsql.PostgresException>();
    }
}
```

- [ ] **Step 3: Run both to verify they fail**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'Owner' could not be found`.

- [ ] **Step 4: Write the records**

Create `src/MUI.Catalog/Ownership/Owner.cs`:

```csharp
namespace MUI.Catalog;

/// <summary>
/// Somebody who has proved control of a game (spec §8).
/// </summary>
/// <remarks>
/// <b>An owner is never named publicly.</b> The game page says "claimed by the game's staff" and
/// stops there. The handle exists so the dashboard can address the person and the audit log can
/// attribute an edit; it is not a display name and there is no owner profile — spec §2 rules out
/// player profiles and a social graph, and an owner list is the same thing wearing a different hat.
/// </remarks>
public sealed record Owner(
    Guid Id,
    Guid GameId,
    string Handle,
    DateTimeOffset AddedAt,
    DateTimeOffset? RemovedAt)
{
    public bool IsCurrent => RemovedAt is null;
}

/// <summary>Who did a thing. The system is an actor because the log is the game's history, not the owners'.</summary>
public enum OwnerAuditActor
{
    Owner,
    System,
    Staff,
}

/// <summary>One line of a game's audit log (spec §8).</summary>
public sealed record OwnerAuditEntry(
    long Id,
    Guid GameId,
    DateTimeOffset At,
    OwnerAuditActor Actor,
    string? OwnerHandle,
    string Action,
    string? Detail);

/// <summary>
/// The vocabulary of the audit log. Constants rather than an enum: the log is append-only and
/// permanent, so a value written in 2026 must still read back in 2036 after the enum has been
/// reordered twice.
/// </summary>
public static class OwnerActions
{
    public const string Claimed = "claimed";
    public const string ClaimWithdrawn = "claim_withdrawn";
    public const string TokenRegenerated = "token_regenerated";
    public const string OwnerAdded = "owner_added";
    public const string OwnerRemoved = "owner_removed";
    public const string TransferStarted = "transfer_started";
    public const string TransferCompleted = "transfer_completed";
    public const string FieldEdited = "field_edited";
    public const string FieldConfirmed = "field_confirmed";
    public const string ConnectScreenHidden = "connect_screen_hidden";
    public const string ConnectScreenShown = "connect_screen_shown";
    public const string WhoPreferenceChanged = "who_preference_changed";
    public const string OptedOut = "opted_out";
    public const string OptedIn = "opted_in";

    /// <summary>Something we measured, with no owner action — the log's "TLS observed" row.</summary>
    public const string Observed = "observed";
}
```

- [ ] **Step 5: Write the migrations**

Create `src/MUI.Storage/Migrations/0022_owner.sql`:

```sql
-- Spec §8's multi-owner. A handle, not a profile: the public page says "claimed by the game's staff"
-- and never a name, and §2 rules out player profiles and a social graph — an owner directory would
-- be that, wearing a different hat.
CREATE TABLE game_owner (
    id          uuid PRIMARY KEY,
    game_id     uuid        NOT NULL REFERENCES game (id) ON DELETE CASCADE,
    handle      text        NOT NULL,
    added_at    timestamptz NOT NULL,
    removed_at  timestamptz NULL,

    CONSTRAINT game_owner_handle_not_blank CHECK (length(btrim(handle)) > 0),
    CONSTRAINT game_owner_removal_follows_addition CHECK (removed_at IS NULL OR removed_at >= added_at)
);

-- One handle owns one game once, at a time. Historical rows stay — staff come back, and "who
-- changed this in 2024" is a question the audit log exists to answer.
CREATE UNIQUE INDEX game_owner_one_current_per_handle
    ON game_owner (game_id, lower(handle))
    WHERE removed_at IS NULL;

CREATE INDEX game_owner_by_handle ON game_owner (lower(handle)) WHERE removed_at IS NULL;
```

Create `src/MUI.Storage/Migrations/0023_owner_audit.sql`:

```sql
-- Spec §8's audit log. Append-only and permanent, which is why `action` is free text against a
-- vocabulary in code rather than an enum in the schema: a value written today must still read back
-- after the C# enum has been reordered twice.
CREATE TABLE owner_audit (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    game_id       uuid        NOT NULL REFERENCES game (id) ON DELETE CASCADE,
    at            timestamptz NOT NULL,
    actor         text        NOT NULL,
    owner_handle  text        NULL,
    action        text        NOT NULL,
    detail        text        NULL,

    CONSTRAINT owner_audit_actor_declared CHECK (actor IN ('owner', 'system', 'staff')),

    -- An owner action has an owner; a system observation does not. Both render, and neither renders
    -- if the two can disagree.
    CONSTRAINT owner_audit_owner_actions_have_an_owner
        CHECK ((actor = 'owner') = (owner_handle IS NOT NULL))
);

CREATE INDEX owner_audit_by_game ON owner_audit (game_id, at DESC, id DESC);
```

- [ ] **Step 6: Write the repository and the service**

Create `src/MUI.Storage/Ownership/IOwnerRepository.cs`:

```csharp
using MUI.Catalog;

namespace MUI.Storage;

/// <summary>A game's current owners and its permanent audit log (spec §8).</summary>
public interface IOwnerRepository
{
    /// <summary>Current owners only. Departed ones survive in the audit log, which is where they belong.</summary>
    Task<IReadOnlyList<Owner>> ForGameAsync(Guid gameId, CancellationToken ct);

    Task AddAsync(Owner owner, CancellationToken ct);

    Task RemoveAsync(Guid ownerId, DateTimeOffset at, CancellationToken ct);

    Task<bool> IsOwnerAsync(Guid gameId, string handle, CancellationToken ct);

    Task AppendAuditAsync(OwnerAuditEntry entry, CancellationToken ct);

    /// <summary>Newest first, bounded — the dashboard renders a page of it, never all of it.</summary>
    Task<IReadOnlyList<OwnerAuditEntry>> AuditAsync(Guid gameId, int limit, CancellationToken ct);
}
```

`NpgsqlOwnerRepository` is six Dapper statements against the two tables above, in the same shape as
`NpgsqlClaimRepository` (Task 3): `SELECT … WHERE game_id = @gameId AND removed_at IS NULL`;
`INSERT INTO game_owner …`; `UPDATE game_owner SET removed_at = @at WHERE id = @ownerId`;
`SELECT EXISTS(… AND lower(handle) = lower(@handle) AND removed_at IS NULL)`;
`INSERT INTO owner_audit (game_id, at, actor, owner_handle, action, detail) VALUES (…)` with
`SqlEnums.ToDb(entry.Actor)`; and
`SELECT … FROM owner_audit WHERE game_id = @gameId ORDER BY at DESC, id DESC LIMIT @limit`.
Add `public static OwnerAuditActor ToOwnerAuditActor(string value) => Parse<OwnerAuditActor>(value);`
to `SqlEnums`. `InMemoryOwnerRepository` is the same six operations over two public `List<T>`s, in the
shape of `InMemoryClaimRepository` (Task 2 Step 5).

Create `src/MUI.Discovery/Ownership/OwnershipService.cs`:

```csharp
using MUI.Catalog;
using MUI.Storage;

namespace MUI.Discovery.Ownership;

/// <summary>
/// Adding and removing owners, transferring a game, and writing the audit log (spec §8).
/// </summary>
/// <remarks>
/// <b>A transfer is a fresh claim and nothing less.</b> The design's rule, and the reason for it:
/// staff turnover is the normal case rather than an exception, and the incoming staff either have
/// server access or they do not — the outgoing staff's say-so is not evidence either way. So a
/// transfer revokes the claim, clears the beacon, issues a new token and waits, exactly as the first
/// claim did.
/// </remarks>
public sealed class OwnershipService(
    IOwnerRepository owners,
    ClaimTokenIssuer issuer,
    ClaimGrant grant,
    TimeProvider time)
{
    public async Task AddOwnerAsync(Guid gameId, string actingHandle, string newHandle, CancellationToken ct)
    {
        await EnsureOwnerAsync(gameId, actingHandle, ct);

        await owners.AddAsync(new Owner(Guid.CreateVersion7(), gameId, newHandle, time.GetUtcNow(), null), ct);
        await RecordAsync(gameId, OwnerAuditActor.Owner, actingHandle, OwnerActions.OwnerAdded, newHandle, ct);
    }

    public async Task RemoveOwnerAsync(Guid gameId, string handle, string actingHandle, CancellationToken ct)
    {
        await EnsureOwnerAsync(gameId, actingHandle, ct);

        var owner = (await owners.ForGameAsync(gameId, ct))
            .SingleOrDefault(o => string.Equals(o.Handle, handle, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{handle} does not own this game.");

        await owners.RemoveAsync(owner.Id, time.GetUtcNow(), ct);
        await RecordAsync(gameId, OwnerAuditActor.Owner, actingHandle, OwnerActions.OwnerRemoved, handle, ct);
    }

    /// <summary>Withdraws the claim and issues a fresh token for the incoming staff to publish.</summary>
    public async Task<ClaimToken> BeginTransferAsync(Guid gameId, string actingHandle, CancellationToken ct)
    {
        await EnsureOwnerAsync(gameId, actingHandle, ct);

        await grant.WithdrawAsync(gameId, ct);
        var token = await issuer.RegenerateAsync(gameId, ct);

        await RecordAsync(gameId, OwnerAuditActor.Owner, actingHandle, OwnerActions.TransferStarted, null, ct);

        return token;
    }

    public Task RecordAsync(
        Guid gameId, OwnerAuditActor actor, string? handle, string action, string? detail, CancellationToken ct) =>
        owners.AppendAuditAsync(
            new OwnerAuditEntry(0, gameId, time.GetUtcNow(), actor, handle, action, detail), ct);

    private async Task EnsureOwnerAsync(Guid gameId, string handle, CancellationToken ct)
    {
        if (!await owners.IsOwnerAsync(gameId, handle, ct))
        {
            throw new UnauthorizedAccessException($"{handle} does not own this game.");
        }
    }
}
```

- [ ] **Step 7: Have `ClaimGrant` record the owner and the audit entry**

Change `ClaimGrant.ApplyAsync` to take the claimant and finish the job:

```csharp
    public async Task ApplyAsync(
        ClaimToken token, ClaimChannel channel, CancellationToken ct, string? claimant = null)
    {
        var now = time.GetUtcNow();

        await games.SetClaimedAsync(token.GameId, true, ct);
        await fields.UpsertAsync(
            new GameField(token.GameId, IdentityFields.ClaimToken, token.Value,
                FieldSource.Owner, FieldConfidence.Observed, now, now), ct);

        if (claimant is not null && !await owners.IsOwnerAsync(token.GameId, claimant, ct))
        {
            await owners.AddAsync(new Owner(Guid.CreateVersion7(), token.GameId, claimant, now, null), ct);
        }

        await owners.AppendAuditAsync(new OwnerAuditEntry(
            0, token.GameId, now,
            claimant is null ? OwnerAuditActor.System : OwnerAuditActor.Owner,
            claimant, OwnerActions.Claimed, channel.ToString().ToLowerInvariant()), ct);
    }
```

and `WithdrawAsync` to log its own:

```csharp
        await owners.AppendAuditAsync(new OwnerAuditEntry(
            0, gameId, time.GetUtcNow(), OwnerAuditActor.System, null, OwnerActions.ClaimWithdrawn, null), ct);
```

`ClaimCycle` and `DnsClaimPoller` call `ApplyAsync` without a claimant — nobody is on a screen at that
moment — and the web endpoint in Task 16 passes the signed-in handle when it polls for the result.

- [ ] **Step 8: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests </dev/null
```
Expected: PASS — 8 behavioural tests and 5 storage tests.

- [ ] **Step 9: Commit**

```bash
git add src/MUI.Catalog/Ownership src/MUI.Storage/Migrations/0022_owner.sql \
        src/MUI.Storage/Migrations/0023_owner_audit.sql src/MUI.Storage/Ownership \
        src/MUI.Discovery/Ownership tests/MUI.Discovery.Tests tests/MUI.Storage.Tests
git commit -m "feat: multi-owner, transfer through a fresh proved token, and a permanent audit log"
```

---

### Task 12: `OwnerFieldWriter` — enrichment through the §5.1 ladder, not around it (spec §3.2, §5.1, §8)

§3.2 names exactly four things as genuinely absent from MSSP and therefore owner-supplied: fandom/IP
("`SUBGENRE` cannot say 'Marvel' or 'Exalted'"), character application process, RP enforcement level,
and consent/content tooling. Plan 02's `FieldRegistry` already declares all four with
`ownerEnrichable: true`.

**An owner write is an ordinary field write.** It goes through `SourcePrecedence` like anything else,
which means an owner does not outrank a measured handshake capability — the game either offered GMCP
or it did not, and the person who runs it saying otherwise does not change what we observed. The
design handoff §09 shows exactly that case as a *finding*, not an override: "Your GMCP field says 1,
but your server has never offered GMCP in 214 handshakes."

The dashboard's other verb, from §09: **confirming a stale value without changing it.** "Editing it,
or confirming it here, clears the mark for a year." That is `last_confirmed_at`, and it must not
append a `FieldChange`, because nothing changed.

**Files:**
- Create: `src/MUI.Discovery/Ownership/OwnerFieldWriter.cs`
- Create: `tests/MUI.Discovery.Tests/Ownership/OwnerFieldWriterTests.cs`

**Interfaces:**
- Consumes: `FieldRegistry`, `FieldDefinition`, `SourcePrecedence`, `GameField`, `FieldChange`,
  `FieldSource`, `FieldConfidence`, `CapabilityFields` (Plan 02); `IGameFieldRepository`,
  `InMemoryGameFieldRepository` (Plan 02); `OwnershipService`, `OwnerActions` (Task 11).
- Produces:
  - `enum MUI.Discovery.Ownership.OwnerWriteResult { Written, Confirmed, NotEnrichable, Refused, Unchanged }`
  - `sealed class MUI.Discovery.Ownership.OwnerFieldWriter(IGameFieldRepository fields, IOwnerRepository owners, OwnershipService ownership, TimeProvider time)`
    with `Task<OwnerWriteResult> WriteAsync(Guid gameId, string handle, string field, string value, CancellationToken ct)`
    and `Task<OwnerWriteResult> ConfirmAsync(Guid gameId, string handle, string field, CancellationToken ct)`
  - `static class MUI.Catalog.OwnerEnrichmentFields` with `Fandom`, `ApplicationProcess`,
    `RpEnforcement`, `ConsentTools`, `All`

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/Ownership/OwnerFieldWriterTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery.Ownership;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests.Ownership;

/// <summary>
/// Spec §8's enrichment fields, written through §5.1's ladder like anything else. The owner is one
/// source among several and outranks the measured ones nowhere — which is the whole product, applied
/// to the one person with the most reason to want an exception.
/// </summary>
public class OwnerFieldWriterTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static async Task<(ClaimWorld World, Guid Game)> ClaimedAsync()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None, claimant: "rowan@example.org");

        return (world, game);
    }

    [Test]
    public async Task AnOwnerWritesTheFourFieldsMsspCannotExpress()
    {
        // §3.2 names exactly these: SUBGENRE cannot say "Marvel" or "Exalted", and nothing in the
        // taxonomy expresses how a character application works or what consent tooling exists.
        var (world, game) = await ClaimedAsync();

        foreach (var field in OwnerEnrichmentFields.All)
        {
            await Assert.That(await world.OwnerFields.WriteAsync(game, "rowan@example.org", field, "a value", None))
                .IsEqualTo(OwnerWriteResult.Written);
        }

        var stored = await world.Fields.ForGameAsync(game, None);

        await Assert.That(stored.Count(f => OwnerEnrichmentFields.All.Contains(f.Field))).IsEqualTo(4);
        await Assert.That(stored.Where(f => OwnerEnrichmentFields.All.Contains(f.Field))
            .All(f => f.Source is FieldSource.Owner)).IsTrue();
    }

    [Test]
    public async Task EveryEnrichmentFieldIsOneTheRegistryAlreadyDeclaredEnrichable()
    {
        // Two lists of the same four strings is how they drift. This one holds them together.
        foreach (var field in OwnerEnrichmentFields.All)
        {
            await Assert.That(FieldRegistry.For(field).OwnerEnrichable).IsTrue();
        }

        await Assert.That(FieldRegistry.All.Where(d => d.OwnerEnrichable).Select(d => d.Name))
            .IsEquivalentTo(OwnerEnrichmentFields.All.ToList());
    }

    [Test]
    public async Task AnOwnerDoesNotOutrankAMeasuredHandshakeCapability()
    {
        // The delivered dashboard shows this case as a finding rather than an override: "Your GMCP
        // field says 1, but your server has never offered GMCP in 214 handshakes."
        var (world, game) = await ClaimedAsync();
        var measured = CapabilityFields.Measured("GMCP");
        await world.Fields.UpsertAsync(new GameField(game, measured, "false",
            FieldSource.Handshake, FieldConfidence.Observed, world.Time.GetUtcNow(), world.Time.GetUtcNow()), None);

        var result = await world.OwnerFields.WriteAsync(game, "rowan@example.org", measured, "true", None);

        await Assert.That(result).IsEqualTo(OwnerWriteResult.NotEnrichable);
        await Assert.That((await world.Fields.ForGameAsync(game, None))
            .Single(f => f.Field == measured).Value).IsEqualTo("false");
    }

    [Test]
    public async Task AnOwnerCannotOverwriteAFieldTheGameItselfReports()
    {
        // CODEBASE is auto-filled by the server. §5.1 gives the owner enrichment-only fields, and
        // SourcePrecedence already says so — this writer asks it rather than deciding for itself.
        var (world, game) = await ClaimedAsync();
        await world.Fields.UpsertAsync(new GameField(game, "CODEBASE", "PennMUSH 1.8.8p2",
            FieldSource.Mssp, FieldConfidence.Reported, world.Time.GetUtcNow(), world.Time.GetUtcNow()), None);

        var result = await world.OwnerFields.WriteAsync(game, "rowan@example.org", "CODEBASE", "Evennia", None);

        await Assert.That(result).IsEqualTo(OwnerWriteResult.NotEnrichable);
        await Assert.That((await world.Fields.ForGameAsync(game, None))
            .Single(f => f.Field == "CODEBASE").Value).IsEqualTo("PennMUSH 1.8.8p2");
    }

    [Test]
    public async Task AWriteAppendsAChangeAndTheAuditEntry()
    {
        var (world, game) = await ClaimedAsync();
        await world.OwnerFields.WriteAsync(game, "rowan@example.org", OwnerEnrichmentFields.Fandom, "original setting", None);

        await world.OwnerFields.WriteAsync(game, "rowan@example.org", OwnerEnrichmentFields.Fandom, "Exalted", None);

        var change = (await world.Fields.ChangesAsync(game, 10, None))
            .Single(c => c.Field == OwnerEnrichmentFields.Fandom && c.NewValue == "Exalted");

        await Assert.That(change.OldValue).IsEqualTo("original setting");
        await Assert.That(change.Source).IsEqualTo(FieldSource.Owner);
        await Assert.That((await world.Owners.AuditAsync(game, 10, None)).Select(e => e.Action))
            .Contains(OwnerActions.FieldEdited);
    }

    [Test]
    public async Task WritingTheSameValueAgainIsNotAChange()
    {
        // §5.1: a probe confirms or changes, never both, and the change feed is "events that
        // actually happened". A dashboard that saves as you type must not fill it with non-events.
        var (world, game) = await ClaimedAsync();
        await world.OwnerFields.WriteAsync(game, "rowan@example.org", OwnerEnrichmentFields.Fandom, "Exalted", None);
        world.Time.Advance(TimeSpan.FromDays(1));

        var result = await world.OwnerFields.WriteAsync(game, "rowan@example.org", OwnerEnrichmentFields.Fandom, "Exalted", None);

        await Assert.That(result).IsEqualTo(OwnerWriteResult.Unchanged);
        await Assert.That((await world.Fields.ChangesAsync(game, 10, None))
            .Count(c => c.Field == OwnerEnrichmentFields.Fandom)).IsEqualTo(1);
        await Assert.That((await world.Fields.ForGameAsync(game, None))
            .Single(f => f.Field == OwnerEnrichmentFields.Fandom).LastConfirmedAt)
            .IsEqualTo(world.Time.GetUtcNow());
    }

    [Test]
    public async Task ConfirmingAStaleValueClearsTheMarkWithoutInventingAChange()
    {
        // The delivered dashboard's "still accurate — confirm" button: "Editing it, or confirming it
        // here, clears the mark for a year."
        var (world, game) = await ClaimedAsync();
        var written = world.Time.GetUtcNow();
        await world.Fields.UpsertAsync(new GameField(game, "GENRE", "Fantasy",
            FieldSource.Mssp, FieldConfidence.Reported, written, written), None);
        world.Time.Advance(TimeSpan.FromDays(400));

        await Assert.That((await world.Fields.ForGameAsync(game, None))
            .Single(f => f.Field == "GENRE").IsStale(world.Time.GetUtcNow())).IsTrue();

        var result = await world.OwnerFields.ConfirmAsync(game, "rowan@example.org", "GENRE", None);

        await Assert.That(result).IsEqualTo(OwnerWriteResult.Confirmed);
        await Assert.That((await world.Fields.ForGameAsync(game, None))
            .Single(f => f.Field == "GENRE").IsStale(world.Time.GetUtcNow())).IsFalse();
        await Assert.That(await world.Fields.ChangesAsync(game, 10, None)).IsEmpty();
        await Assert.That((await world.Owners.AuditAsync(game, 10, None)).Select(e => e.Action))
            .Contains(OwnerActions.FieldConfirmed);
    }

    [Test]
    public async Task SomebodyWhoDoesNotOwnTheGameWritesNothing()
    {
        var (world, game) = await ClaimedAsync();

        await Assert.That(async () => await world.OwnerFields.WriteAsync(
                game, "stranger@example.org", OwnerEnrichmentFields.Fandom, "mine now", None))
            .Throws<UnauthorizedAccessException>();
        await Assert.That(await world.Fields.ForGameAsync(game, None)).IsEmpty();
    }

    [Test]
    public async Task AnEmptyValueClearsTheFieldRatherThanStoringABlank()
    {
        var (world, game) = await ClaimedAsync();
        await world.OwnerFields.WriteAsync(game, "rowan@example.org", OwnerEnrichmentFields.Fandom, "Exalted", None);

        var result = await world.OwnerFields.WriteAsync(game, "rowan@example.org", OwnerEnrichmentFields.Fandom, "  ", None);

        await Assert.That(result).IsEqualTo(OwnerWriteResult.Written);
        await Assert.That((await world.Fields.ForGameAsync(game, None))
            .Any(f => f.Field == OwnerEnrichmentFields.Fandom)).IsFalse();
    }
}
```

Add to `ClaimWorld`: `public OwnerFieldWriter OwnerFields { get; }`, built as
`OwnerFields = new OwnerFieldWriter(Fields, Owners, Ownership, Time);`.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'OwnerFieldWriter' could not be found`.

- [ ] **Step 3: Name the four fields once**

Create `src/MUI.Catalog/Ownership/OwnerPreferences.cs` (the preferences record arrives in Task 13; the
field names go here now because the writer needs them):

```csharp
namespace MUI.Catalog;

/// <summary>
/// The four things spec §3.2 names as genuinely absent from MSSP and therefore owner-supplied.
/// </summary>
/// <remarks>
/// <c>SUBGENRE</c> cannot say "Marvel" or "Exalted"; nothing in the taxonomy expresses how a character
/// application works, how RP is enforced, or what consent tooling exists. Peak activity hours are
/// deliberately not here — they are derived from the crawl, not asked for.
/// These names are the same strings <c>FieldRegistry</c> registers with <c>OwnerEnrichable: true</c>,
/// and <c>OwnerFieldWriterTests</c> holds the two lists together.
/// </remarks>
public static class OwnerEnrichmentFields
{
    public const string Fandom = "FANDOM";
    public const string ApplicationProcess = "APPLICATION PROCESS";
    public const string RpEnforcement = "RP ENFORCEMENT";
    public const string ConsentTools = "CONSENT TOOLS";

    public static IReadOnlyList<string> All { get; } =
        [Fandom, ApplicationProcess, RpEnforcement, ConsentTools];
}
```

- [ ] **Step 4: Write the writer**

Create `src/MUI.Discovery/Ownership/OwnerFieldWriter.cs`:

```csharp
using MUI.Catalog;
using MUI.Storage;

namespace MUI.Discovery.Ownership;

/// <summary>What an owner's edit did.</summary>
public enum OwnerWriteResult
{
    Written,

    /// <summary>The value was already right; only its age moved. No change row, because nothing changed.</summary>
    Confirmed,

    /// <summary>Not a field an owner may set — a measured capability, or something the game reports itself.</summary>
    NotEnrichable,

    /// <summary>An incumbent value from a source that outranks the owner for this field.</summary>
    Refused,

    /// <summary>Same value, same source. Confirmed rather than rewritten.</summary>
    Unchanged,
}

/// <summary>
/// The owner dashboard's field-editing surface (spec §5.1, §8).
/// </summary>
/// <remarks>
/// <para>
/// <b>An owner write is an ordinary field write.</b> It goes through <c>SourcePrecedence</c> like any
/// other source, which is why an owner cannot assert a capability their server does not offer: the
/// handshake is an observation and the owner's word is not. That is the whole product, applied to the
/// one person with the strongest reason to want an exception — and the dashboard is designed for it,
/// showing the disagreement as a finding rather than letting it be overwritten.
/// </para>
/// <para>
/// <b>Confirming is not writing.</b> §5.1 says a probe either confirms or changes, never both, and the
/// change feed is "a table of events that actually happened". The dashboard's "still accurate —
/// confirm" button moves <c>last_confirmed_at</c> and appends nothing.
/// </para>
/// </remarks>
public sealed class OwnerFieldWriter(
    IGameFieldRepository fields,
    IOwnerRepository owners,
    OwnershipService ownership,
    TimeProvider time)
{
    public async Task<OwnerWriteResult> WriteAsync(
        Guid gameId, string handle, string field, string value, CancellationToken ct)
    {
        await EnsureOwnerAsync(gameId, handle, ct);

        if (!FieldRegistry.For(field).OwnerEnrichable)
        {
            return OwnerWriteResult.NotEnrichable;
        }

        var now = time.GetUtcNow();
        var existing = (await fields.ForGameAsync(gameId, ct)).SingleOrDefault(f => f.Field == field);

        if (existing is not null && !SourcePrecedence.Wins(FieldSource.Owner, existing.Source, field))
        {
            return OwnerWriteResult.Refused;
        }

        var trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            await fields.DeleteAsync(gameId, field, ct);
            await ownership.RecordAsync(
                gameId, OwnerAuditActor.Owner, handle, OwnerActions.FieldEdited, $"{field} cleared", ct);

            return OwnerWriteResult.Written;
        }

        if (existing is { } incumbent && incumbent.Value == trimmed && incumbent.Source is FieldSource.Owner)
        {
            await fields.ConfirmAsync(gameId, field, now, ct);

            return OwnerWriteResult.Unchanged;
        }

        await fields.UpsertAsync(new GameField(
            gameId, field, trimmed, FieldSource.Owner, FieldConfidence.Reported,
            existing?.FirstSeenAt ?? now, now), ct);

        await fields.AppendChangeAsync(
            new FieldChange(0, gameId, field, existing?.Value, trimmed, FieldSource.Owner, now), ct);
        await ownership.RecordAsync(gameId, OwnerAuditActor.Owner, handle, OwnerActions.FieldEdited, field, ct);

        return OwnerWriteResult.Written;
    }

    /// <summary>
    /// "Still accurate — confirm." Clears the staleness mark for another window without pretending
    /// something changed.
    /// </summary>
    public async Task<OwnerWriteResult> ConfirmAsync(Guid gameId, string handle, string field, CancellationToken ct)
    {
        await EnsureOwnerAsync(gameId, handle, ct);

        await fields.ConfirmAsync(gameId, field, time.GetUtcNow(), ct);
        await ownership.RecordAsync(gameId, OwnerAuditActor.Owner, handle, OwnerActions.FieldConfirmed, field, ct);

        return OwnerWriteResult.Confirmed;
    }

    private async Task EnsureOwnerAsync(Guid gameId, string handle, CancellationToken ct)
    {
        if (!await owners.IsOwnerAsync(gameId, handle, ct))
        {
            throw new UnauthorizedAccessException($"{handle} does not own this game.");
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
```
Expected: PASS — 9 new tests.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Catalog/Ownership/OwnerPreferences.cs \
        src/MUI.Discovery/Ownership/OwnerFieldWriter.cs tests/MUI.Discovery.Tests
git commit -m "feat: owner enrichment writes through the 5.1 ladder, and confirming is not changing"
```

---

### Task 13: Connect-screen suppression and the WHO-format override (spec §6.2, §6.3, §8)

Two settings, one shape: an owner tells us what to publish about them, and the answer reaches the
*probe* rather than being applied afterwards.

§6.2: "Connect screen: stored and displayed, ANSI intact, **suppressible on owner request**." §11
adds the terms: "no questions asked". §6.3: "A claimed owner may override the format from the
dashboard, or simply assert 'use MSSP `PLAYERS`'."

**The override has to reach the probe, and that is not obvious.** `ProbeResult` carries a
`WhoReading`, not the transcript it was read from, so no downstream writer can re-parse WHO with a
different pattern. And politeness argues the same way: if an owner has told us not to use their WHO,
we should stop sending it, not send it and discard the answer. `WhoConfidence.NotAttempted` exists
for exactly this — its own documentation says "An owner override said to use MSSP PLAYERS" — so
Plan 02's `PresenceWriter` already falls back to MSSP `PLAYERS` with no change at all.

**Files:**
- Modify: `src/MUI.Catalog/Ownership/OwnerPreferences.cs`
- Create: `src/MUI.Storage/Migrations/0024_owner_preferences.sql`
- Create: `src/MUI.Storage/Ownership/IOwnerPreferencesRepository.cs`
- Create: `src/MUI.Storage/Ownership/NpgsqlOwnerPreferencesRepository.cs`
- Create: `src/MUI.Discovery/Ownership/OwnerPreferenceService.cs`
- Modify: `src/MUI.Crawl/ProbeResult.cs` (`ProbeTarget` gains two fields)
- Modify: `src/MUI.Crawl/ProbeSession.cs`, `src/MUI.Crawl/Who/WhoParser.cs`
- Modify: `src/MUI.Discovery/CrawlerService.cs`
- Create: `tests/MUI.Discovery.Tests/Support/InMemoryOwnerPreferencesRepository.cs`
- Create: `tests/MUI.Discovery.Tests/Ownership/OwnerPreferenceTests.cs`

**Interfaces:**
- Consumes: `WhoReading`, `WhoConfidence`, `WhoParser`, `ProbeTarget` (Plan 01); `PresenceWriter`,
  `UnmeasurableReasons` (Plan 02); `OwnershipService` (Task 11).
- Produces:
  - `enum MUI.Catalog.WhoPreference { Auto, UseMsspPlayers, SummaryPattern }`
  - `sealed record MUI.Catalog.OwnerPreferences(Guid GameId, bool PublishConnectScreen, WhoPreference Who, string? WhoSummaryPattern, DateTimeOffset? OptedOutAt)`
    with `static OwnerPreferences Default(Guid gameId)`, `bool IsOptedOut`
  - `static class MUI.Catalog.WhoPatterns` with `bool IsUsable(string? pattern, out string? reason)`
  - `interface MUI.Storage.IOwnerPreferencesRepository` with `ForGameAsync`, `UpsertAsync`, `AllOptedOutAsync`
  - `sealed class MUI.Discovery.Ownership.OwnerPreferenceService(IOwnerPreferencesRepository preferences, IOwnerRepository owners, OwnershipService ownership, TimeProvider time)`
    with `SetConnectScreenAsync`, `SetWhoAsync`, `ForGameAsync`, `ProbeTarget Apply(ProbeTarget target, OwnerPreferences preferences)`
  - `ProbeTarget.SendWho`, `ProbeTarget.WhoSummaryPattern`; `WhoParser.Parse(string, string?)`

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/Ownership/OwnerPreferenceTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Crawl.Who;
using MUI.Discovery.Ownership;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests.Ownership;

/// <summary>
/// Spec §6.2's suppression and §6.3's WHO override — the two settings on the delivered dashboard's
/// "what we publish about you" panel that change what the crawler does rather than what a page shows.
/// </summary>
public class OwnerPreferenceTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static async Task<(ClaimWorld World, Guid Game)> ClaimedAsync()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None, claimant: "rowan@example.org");

        return (world, game);
    }

    [Test]
    public async Task ByDefaultWePublishTheConnectScreenAndReadWhoOurselves()
    {
        // §6.2 and the auto-listing policy: a server sends its connect screen unauthenticated to
        // every anonymous connection, so displaying it is the default and hiding it is the request.
        var preferences = OwnerPreferences.Default(Guid.CreateVersion7());

        await Assert.That(preferences.PublishConnectScreen).IsTrue();
        await Assert.That(preferences.Who).IsEqualTo(WhoPreference.Auto);
        await Assert.That(preferences.IsOptedOut).IsFalse();
    }

    [Test]
    public async Task SuppressionIsHonouredWithNoQuestionsAndIsRecorded()
    {
        var (world, game) = await ClaimedAsync();

        await world.Preferences.SetConnectScreenAsync(game, "rowan@example.org", publish: false, None);

        await Assert.That((await world.Preferences.ForGameAsync(game, None)).PublishConnectScreen).IsFalse();
        await Assert.That((await world.Owners.AuditAsync(game, 10, None)).Select(e => e.Action))
            .Contains(OwnerActions.ConnectScreenHidden);
    }

    [Test]
    public async Task ASuppressedConnectScreenIsNotStoredAsAField()
    {
        // §6.2 stores the screen as a field (`connect_screen`, Plan 2's registry). Suppression has to
        // stop the write, not only the render: "we stop republishing it" means we stop holding it.
        var (world, game) = await ClaimedAsync();
        await world.Preferences.SetConnectScreenAsync(game, "rowan@example.org", publish: false, None);
        world.Probe.Answering("corvid.example", 4201, () => ProbeResults.Answered(
            host: "corvid.example", banner: "Welcome to Corvid.",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]))));

        await world.Service.RunCycleAsync(None);

        await Assert.That((await world.Fields.ForGameAsync(game, None))
            .Any(field => field.Field == "connect_screen")).IsFalse();
    }

    [Test]
    public async Task AnOwnerWhoSaysUseMsspPlayersStopsUsSendingWhoAtAll()
    {
        // Politeness and correctness point the same way: if they have told us not to use their WHO,
        // asking for it anyway and discarding the answer is a command spent for nothing.
        var target = new ProbeTarget { Host = "corvid.example", Port = 4201 };
        var preferences = OwnerPreferences.Default(Guid.CreateVersion7()) with { Who = WhoPreference.UseMsspPlayers };

        var applied = OwnerPreferenceService.Apply(target, preferences);

        await Assert.That(applied.SendWho).IsFalse();
        await Assert.That(applied.WhoSummaryPattern).IsNull();
    }

    [Test]
    public async Task NotAskingLeavesTheReadingAtNotAttemptedAndTheCountComesFromMssp()
    {
        // The seam already exists: WhoConfidence.NotAttempted's own documentation says "An owner
        // override said to use MSSP PLAYERS", and Plan 2's PresenceWriter already falls back.
        var (world, game) = await ClaimedAsync();
        await world.Preferences.SetWhoAsync(game, "rowan@example.org", WhoPreference.UseMsspPlayers, null, None);
        world.Probe.Answering("corvid.example", 4201, () => ProbeResults.Answered(
            host: "corvid.example",
            who: WhoReading.NotAttempted,
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), ("PLAYERS", ["17"]))));

        await world.Service.RunCycleAsync(None);

        var sample = world.Presence.All.Single();

        await Assert.That(sample.Count).IsEqualTo(17);
        await Assert.That(sample.Source).IsEqualTo(PresenceSource.Mssp);
        await Assert.That(sample.UnmeasurableReason).IsNull();
        await Assert.That(world.Probe.LastTarget!.SendWho).IsFalse();
    }

    [Test]
    public async Task AFormatOverrideReachesTheParser()
    {
        // The override cannot be applied downstream: ProbeResult carries a WhoReading, not the
        // transcript it was read from, so there is nothing left to re-parse.
        const string transcript = "Name        On For  Idle\nRowan       01:12   0s\n>> 3 wizards about <<";

        await Assert.That(WhoParser.Parse(transcript).Confidence).IsEqualTo(WhoConfidence.Unknown);

        var reading = WhoParser.Parse(transcript, @">>\s*(?<count>\d+)\s+wizards about");

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.Count);
        await Assert.That(reading.Count).IsEqualTo(3);
    }

    [Test]
    public async Task AnOverridePatternThatCannotWorkIsRefusedAtTheDashboardRatherThanOnTheWire()
    {
        // An owner-supplied regex runs against text a stranger's server sent us. It is validated once,
        // when it is saved, and it runs with a match timeout — a pattern that hangs the crawl loop is
        // a §12 failure, not a typo.
        await Assert.That(WhoPatterns.IsUsable(@"(?<count>\d+) players", out _)).IsTrue();
        await Assert.That(WhoPatterns.IsUsable(@"\d+ players", out var noGroup)).IsFalse();
        await Assert.That(noGroup).Contains("count");
        await Assert.That(WhoPatterns.IsUsable(@"([a-z]+", out var broken)).IsFalse();
        await Assert.That(broken).IsNotNull();

        var (world, game) = await ClaimedAsync();

        await Assert.That(async () => await world.Preferences.SetWhoAsync(
                game, "rowan@example.org", WhoPreference.SummaryPattern, @"\d+ players", None))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AnOverridePatternIsCarriedToTheProbe()
    {
        var (world, game) = await ClaimedAsync();
        await world.Preferences.SetWhoAsync(
            game, "rowan@example.org", WhoPreference.SummaryPattern, @"(?<count>\d+) souls", None);
        world.Probe.Answering("corvid.example", 4201, () => ProbeResults.Answered(
            host: "corvid.example", mssp: ProbeResults.Mssp(("NAME", ["Corvid"]))));

        await world.Service.RunCycleAsync(None);

        await Assert.That(world.Probe.LastTarget!.SendWho).IsTrue();
        await Assert.That(world.Probe.LastTarget.WhoSummaryPattern).IsEqualTo(@"(?<count>\d+) souls");
    }

    [Test]
    public async Task AGameWithNoPreferencesGetsTheDefaultsAndNoExtraQuery()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");

        var preferences = await world.Preferences.ForGameAsync(game, None);

        await Assert.That(preferences.PublishConnectScreen).IsTrue();
        await Assert.That(preferences.Who).IsEqualTo(WhoPreference.Auto);
    }

    [Test]
    public async Task SomebodyWhoDoesNotOwnTheGameChangesNothing()
    {
        var (world, game) = await ClaimedAsync();

        await Assert.That(async () =>
                await world.Preferences.SetConnectScreenAsync(game, "stranger@example.org", false, None))
            .Throws<UnauthorizedAccessException>();
        await Assert.That((await world.Preferences.ForGameAsync(game, None)).PublishConnectScreen).IsTrue();
    }
}
```

Add to `ClaimWorld`: `public InMemoryOwnerPreferencesRepository PreferenceStore { get; }` and
`public OwnerPreferenceService Preferences { get; }`, and give `FakeProbe` a
`public ProbeTarget? LastTarget { get; private set; }` set in `ProbeAsync`.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'OwnerPreferenceService' could not be found`.

- [ ] **Step 3: Write the preference record and the pattern guard**

Append to `src/MUI.Catalog/Ownership/OwnerPreferences.cs`:

```csharp
using System.Text.RegularExpressions;

/// <summary>How a game's player count is read (spec §6.3).</summary>
public enum WhoPreference
{
    /// <summary>Our structural parser, which reports <c>unknown</c> rather than fabricating a zero.</summary>
    Auto,

    /// <summary>
    /// The owner's assertion that MSSP <c>PLAYERS</c> is the better number. We then stop sending WHO
    /// at all, which leaves <c>WhoConfidence.NotAttempted</c> and lets the presence writer fall back.
    /// </summary>
    UseMsspPlayers,

    /// <summary>An owner-supplied summary-line pattern, for a <c>DOING</c> header past our parser.</summary>
    SummaryPattern,
}

/// <summary>What an owner has told us to publish about their game (spec §6.2, §6.3, §11).</summary>
public sealed record OwnerPreferences(
    Guid GameId,
    bool PublishConnectScreen,
    WhoPreference Who,
    string? WhoSummaryPattern,
    DateTimeOffset? OptedOutAt)
{
    /// <summary>
    /// Publishing the connect screen and reading WHO ourselves are the defaults, because a server
    /// sends both to every anonymous connection unasked. Turning them off is the request (§6.2, §11).
    /// </summary>
    public static OwnerPreferences Default(Guid gameId) => new(gameId, true, WhoPreference.Auto, null, null);

    public bool IsOptedOut => OptedOutAt is not null;
}

/// <summary>
/// Whether an owner-supplied WHO summary pattern can be used (spec §6.3, §12).
/// </summary>
/// <remarks>
/// The pattern is written by one stranger and run against text sent by another, inside the crawl
/// loop that shares a process with the web tier. It is validated once, when it is saved, so a
/// mistake is a form error rather than a wedged cycle — and it is compiled with a match timeout
/// wherever it runs, because validation cannot prove a pattern terminates quickly on every input.
/// </remarks>
public static class WhoPatterns
{
    /// <summary>The named group the count is read from. Anything else is a pattern that cannot answer.</summary>
    public const string CountGroup = "count";

    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    public static bool IsUsable(string? pattern, out string? reason)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            reason = "A pattern is required.";

            return false;
        }

        try
        {
            var compiled = new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout);

            if (!compiled.GetGroupNames().Contains(CountGroup))
            {
                reason = $"The pattern must contain a named group '{CountGroup}', e.g. (?<{CountGroup}>\\d+).";

                return false;
            }
        }
        catch (ArgumentException error)
        {
            reason = error.Message;

            return false;
        }

        reason = null;

        return true;
    }
}
```

- [ ] **Step 4: Write the migration, the repository and the service**

Create `src/MUI.Storage/Migrations/0024_owner_preferences.sql`:

```sql
-- Spec §6.2, §6.3 and §11: what an owner has told us to publish about their game. One row per game,
-- written only when a preference differs from the default — an absent row is the default, which is
-- what almost every game will have for ever.
CREATE TABLE owner_preferences (
    game_id                uuid PRIMARY KEY REFERENCES game (id) ON DELETE CASCADE,
    publish_connect_screen boolean     NOT NULL DEFAULT true,
    who_preference         text        NOT NULL DEFAULT 'auto',
    who_summary_pattern    text        NULL,
    opted_out_at           timestamptz NULL,

    CONSTRAINT owner_preferences_who_declared
        CHECK (who_preference IN ('auto', 'use_mssp_players', 'summary_pattern')),

    -- A pattern preference has a pattern, or it is not one. The probe reads both columns and cannot
    -- render the disagreement.
    CONSTRAINT owner_preferences_pattern_is_present_when_used
        CHECK ((who_preference = 'summary_pattern') = (who_summary_pattern IS NOT NULL))
);

-- Spec §11's opt-out for a host we have no game for yet — a referred address that answered, or one
-- an operator asks us about before we have listed them. Keyed on the address, because that is all
-- we have.
CREATE TABLE crawl_opt_out (
    host        text        NOT NULL,
    port        integer     NOT NULL,
    requested_at timestamptz NOT NULL,
    source      text        NOT NULL,

    PRIMARY KEY (host, port),
    CONSTRAINT crawl_opt_out_source_declared CHECK (source IN ('mssp', 'dns_txt', 'request')),
    CONSTRAINT crawl_opt_out_port_is_a_port CHECK (port BETWEEN 1 AND 65535)
);

-- Denormalised onto game so the listing, the API and the sweeper can all see it in one join-free
-- read. owner_preferences.opted_out_at stays the record of *when and by whom*; this is the flag.
ALTER TABLE game ADD COLUMN opted_out_at timestamptz NULL;

CREATE INDEX game_opted_out ON game (opted_out_at) WHERE opted_out_at IS NOT NULL;
```

`IOwnerPreferencesRepository` is three methods —
`Task<OwnerPreferences?> ForGameAsync(Guid gameId, CancellationToken ct)`,
`Task UpsertAsync(OwnerPreferences preferences, CancellationToken ct)`,
`Task<IReadOnlyList<Guid>> AllOptedOutAsync(CancellationToken ct)` — and
`NpgsqlOwnerPreferencesRepository` implements them with `SELECT`, `INSERT … ON CONFLICT (game_id) DO
UPDATE`, and `SELECT game_id … WHERE opted_out_at IS NOT NULL`, in the shape of Task 3's repository.
`InMemoryOwnerPreferencesRepository` is a `Dictionary<Guid, OwnerPreferences>` behind the same three.

Create `src/MUI.Discovery/Ownership/OwnerPreferenceService.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Storage;

namespace MUI.Discovery.Ownership;

/// <summary>
/// The dashboard's "what we publish about you" panel, and how it reaches the crawler
/// (spec §6.2, §6.3, §8).
/// </summary>
/// <remarks>
/// <b>The WHO override cannot be applied downstream.</b> <c>ProbeResult</c> carries a
/// <c>WhoReading</c> and not the transcript it was read from, so by the time a writer sees it there is
/// nothing left to re-parse — the preference has to be on the <c>ProbeTarget</c>. That also happens to
/// be the polite arrangement: an owner who has told us to use MSSP <c>PLAYERS</c> should stop
/// receiving a <c>WHO</c> command, not receive one whose answer we throw away.
/// </remarks>
public sealed class OwnerPreferenceService(
    IOwnerPreferencesRepository preferences,
    IOwnerRepository owners,
    OwnershipService ownership,
    TimeProvider time)
{
    /// <summary>An absent row is the default, which is what almost every game has for ever.</summary>
    public async Task<OwnerPreferences> ForGameAsync(Guid gameId, CancellationToken ct) =>
        await preferences.ForGameAsync(gameId, ct) ?? OwnerPreferences.Default(gameId);

    public async Task SetConnectScreenAsync(Guid gameId, string handle, bool publish, CancellationToken ct)
    {
        await EnsureOwnerAsync(gameId, handle, ct);

        var current = await ForGameAsync(gameId, ct);
        await preferences.UpsertAsync(current with { PublishConnectScreen = publish }, ct);

        // "No questions asked" (§11) means exactly that: no reason field, and no note on the page
        // beyond "not republished".
        await ownership.RecordAsync(gameId, OwnerAuditActor.Owner, handle,
            publish ? OwnerActions.ConnectScreenShown : OwnerActions.ConnectScreenHidden, null, ct);
    }

    public async Task SetWhoAsync(
        Guid gameId, string handle, WhoPreference who, string? pattern, CancellationToken ct)
    {
        await EnsureOwnerAsync(gameId, handle, ct);

        if (who is WhoPreference.SummaryPattern && !WhoPatterns.IsUsable(pattern, out var reason))
        {
            throw new ArgumentException(reason, nameof(pattern));
        }

        var current = await ForGameAsync(gameId, ct);
        await preferences.UpsertAsync(current with
        {
            Who = who,
            WhoSummaryPattern = who is WhoPreference.SummaryPattern ? pattern : null,
        }, ct);

        await ownership.RecordAsync(
            gameId, OwnerAuditActor.Owner, handle, OwnerActions.WhoPreferenceChanged, who.ToString(), ct);
    }

    /// <summary>Folds a game's preferences into the target the crawler is about to probe.</summary>
    public static ProbeTarget Apply(ProbeTarget target, OwnerPreferences preferences) =>
        preferences.Who switch
        {
            WhoPreference.UseMsspPlayers => target with { SendWho = false, WhoSummaryPattern = null },
            WhoPreference.SummaryPattern => target with { WhoSummaryPattern = preferences.WhoSummaryPattern },
            _ => target,
        };

    private async Task EnsureOwnerAsync(Guid gameId, string handle, CancellationToken ct)
    {
        if (!await owners.IsOwnerAsync(gameId, handle, ct))
        {
            throw new UnauthorizedAccessException($"{handle} does not own this game.");
        }
    }
}
```

- [ ] **Step 5: Carry the two fields through Plan 01's probe**

In `src/MUI.Crawl/ProbeResult.cs`, add to `ProbeTarget`:

```csharp
    /// <summary>
    /// Whether to send <c>WHO</c> at all. False when the game's owner has asserted that MSSP
    /// <c>PLAYERS</c> is the better number (spec §6.3) — we then do not spend the command, and the
    /// reading stays <see cref="WhoConfidence.NotAttempted"/>, which is what that state is for.
    /// </summary>
    public bool SendWho { get; init; } = true;

    /// <summary>
    /// An owner-supplied summary-line pattern with a named <c>count</c> group, for a <c>DOING</c>
    /// header our structural parser cannot read (spec §6.3). Validated at the dashboard; applied with
    /// a match timeout here.
    /// </summary>
    public string? WhoSummaryPattern { get; init; }
```

In `ProbeSession`, gate the WHO exchange on `options.SendWho && target.SendWho` and pass
`target.WhoSummaryPattern` into `WhoParser.Parse`. In `WhoParser`:

```csharp
    /// <param name="summaryPattern">
    /// An owner's override (spec §6.3), tried before the structural read. A pattern that does not
    /// match falls through rather than failing: an owner who mistyped it gets our parser's answer,
    /// not a blank.
    /// </param>
    public static WhoReading Parse(string transcript, string? summaryPattern = null)
    {
        if (summaryPattern is not null
            && new Regex(summaryPattern, RegexOptions.CultureInvariant, WhoPatterns.MatchTimeout)
                .Match(AnsiText.Strip(transcript)) is { Success: true } match
            && int.TryParse(match.Groups[WhoPatterns.CountGroup].Value, out var overridden))
        {
            return new WhoReading(WhoConfidence.Count, overridden);
        }

        // …the existing structural read, unchanged…
    }
```

`WhoPatterns` lives in `MUI.Catalog`, which `MUI.Crawl` does not reference — so re-declare
`MatchTimeout` and `CountGroup` as `MUI.Crawl.Who.WhoParser`'s own two constants, and add a test in
`tests/MUI.Discovery.Tests` asserting they equal `WhoPatterns`'. `MUI.Discovery` sees both and is the
only place the equality can be checked; the architecture rule is worth two constants.

- [ ] **Step 6: Apply preferences in the crawl loop**

In `CrawlerService`, add `OwnerPreferenceService owners` to the constructor and, in `ProbeOneAsync`,
build the target through it:

```csharp
            var target = ToProbeTarget(crawlTarget);
            if (crawlTarget.GameId is { } owned)
            {
                target = OwnerPreferenceService.Apply(target, await owners.ForGameAsync(owned, cancellationToken));
            }
```

and in `ApplyAsync`, skip the connect-screen field write when the owner has suppressed it — pass the
preference into `ProbeIngestor.IngestAsync` as a new optional
`bool publishConnectScreen = true` parameter, which `FieldReconciler` reads to skip the
`connect_screen` field. That one flag is the whole of §6.2's suppression on the write side.

- [ ] **Step 7: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
```
Expected: PASS — 10 new tests, and Plan 01's WHO suite unchanged (the new parameter defaults to null).

- [ ] **Step 8: Commit**

```bash
git add src/MUI.Catalog/Ownership src/MUI.Storage src/MUI.Discovery src/MUI.Crawl tests
git commit -m "feat: connect-screen suppression and the WHO-format override, applied at the probe (6.2, 6.3)"
```

---

### Task 14: Opt-out — honoured within one cycle, recorded, and available without claiming (spec §11)

§11: "Documented opt-out — MSSP field, DNS TXT, or request — honoured within one cycle and recorded."
Three channels, and **two of them require no claim at all** — which matters, because the operator most
likely to want us gone is the one least likely to want an account with us first.

The design handoff's crawler-transparency panel is the copy: "MSSP `CRAWL_OPT_OUT 1`, or DNS TXT
`_muindex … "optout"`, or email us. Honoured within one cycle, no reply required, no reason asked."
And §09's dashboard toggle: "The page becomes a stub with the name and the date you opted out — the
URL keeps working, because other sites link to it."

**Files:**
- Create: `src/MUI.Storage/Ownership/ICrawlOptOutRepository.cs`
- Create: `src/MUI.Storage/Ownership/NpgsqlCrawlOptOutRepository.cs`
- Create: `src/MUI.Discovery/Ownership/OptOutService.cs`
- Modify: `src/MUI.Discovery/Storage/NpgsqlCrawlTargetRepository.cs` (`DueAsync` excludes opt-outs)
- Modify: `src/MUI.Discovery/CrawlerService.cs`
- Create: `tests/MUI.Discovery.Tests/Support/InMemoryCrawlOptOutRepository.cs`
- Create: `tests/MUI.Discovery.Tests/Ownership/OptOutTests.cs`

**Interfaces:**
- Consumes: `ClaimVocabulary.OptOutMsspVariable`, `.OptOutValue`, `.DnsNameFor` (Task 1);
  `IDnsTxtResolver` (Task 5); `OwnerPreferenceService`, `IOwnerPreferencesRepository` (Task 13);
  `OwnershipService` (Task 11); `ICrawlTargetRepository` (Plan 03).
- Produces:
  - `enum MUI.Catalog.OptOutSource { Mssp, DnsTxt, Request }`
  - `sealed record MUI.Catalog.CrawlOptOut(string Host, int Port, DateTimeOffset RequestedAt, OptOutSource Source)`
  - `interface MUI.Storage.ICrawlOptOutRepository` with `IsOptedOutAsync`, `AddAsync`, `AllAsync`
  - `sealed class MUI.Discovery.Ownership.OptOutService(ICrawlOptOutRepository optOuts, IOwnerPreferencesRepository preferences, IGameRepository games, IOwnerRepository owners, OwnershipService ownership, TimeProvider time)`
    with `Task<bool> ObserveAsync(CrawlTarget target, ProbeResult result, CancellationToken ct)`,
    `Task OptOutAsync(Guid gameId, string handle, CancellationToken ct)`,
    `Task OptInAsync(Guid gameId, string handle, CancellationToken ct)`

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/Ownership/OptOutTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery.Ownership;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests.Ownership;

/// <summary>
/// Spec §11's opt-out. Three channels, two of which need no account with us — the operator most
/// likely to want us gone is the one least likely to want to sign up first.
/// </summary>
public class OptOutTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task AnMsspFlagStopsTheNextCycleAndNotTheOneAfter()
    {
        // "Honoured within one cycle" measured the only way it can be: the cycle that saw the flag
        // finishes, and the next one does not visit.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        world.Probe.Answering("corvid.example", 4201, () => ProbeResults.Answered(
            host: "corvid.example",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), (ClaimVocabulary.OptOutMsspVariable, ["1"]))));

        await world.Service.RunCycleAsync(None);
        await Assert.That(world.Probe.Visited.Count).IsEqualTo(1);

        world.Time.Advance(TimeSpan.FromDays(30));
        await world.Service.RunCycleAsync(None);

        await Assert.That(world.Probe.Visited.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AnOptOutIsRecordedWithItsSourceAndItsDate()
    {
        // "Recorded" is half of §11's sentence, and it is the half that lets us answer "when did you
        // stop?" without the operator having to remember.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        world.Probe.Answering("corvid.example", 4201, () => ProbeResults.Answered(
            host: "corvid.example",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), (ClaimVocabulary.OptOutMsspVariable, ["1"]))));

        var at = world.Time.GetUtcNow();
        await world.Service.RunCycleAsync(None);

        await Assert.That((await world.Games.ByIdAsync(game, None))!.OptedOutAt).IsEqualTo(at);
        await Assert.That((await world.Owners.AuditAsync(game, 10, None)).Single(e => e.Action == OwnerActions.OptedOut).Detail)
            .IsEqualTo("mssp");
    }

    [Test]
    public async Task ADnsTxtOptOutWorksForAHostWeHaveNoGameFor()
    {
        // A referred address that answered but never named itself is a crawl target and not a game
        // (§7.2). Its operator can still make us stop, and must be able to.
        var world = new ClaimWorld();
        await world.TargetOnlyAsync("mystery.example", 4201);
        world.Dns.Answer(ClaimVocabulary.DnsNameFor("mystery.example"), ClaimVocabulary.OptOutValue);
        world.Probe.Answering("mystery.example", 4201, () => ProbeResults.Answered(host: "mystery.example"));

        await world.Service.RunCycleAsync(None);
        world.Time.Advance(TimeSpan.FromDays(30));
        await world.Service.RunCycleAsync(None);

        await Assert.That(world.Probe.Visited.Count).IsEqualTo(1);
        await Assert.That(await world.OptOuts.IsOptedOutAsync("mystery.example", 4201, None)).IsTrue();
    }

    [Test]
    public async Task TheDashboardToggleOptsOutAndKeepsTheUrlWorking()
    {
        // §7.4's rule 3 with a different cause: nothing is deleted. The page becomes a stub with the
        // name and the date, because other sites link to it.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None, claimant: "rowan@example.org");

        await world.OptOut.OptOutAsync(game, "rowan@example.org", None);

        var stored = (await world.Games.ByIdAsync(game, None))!;

        await Assert.That(stored.OptedOutAt).IsEqualTo(world.Time.GetUtcNow());
        await Assert.That(stored.Slug).IsEqualTo("corvid");
        await Assert.That(stored.Name).IsEqualTo("Corvid");
        await Assert.That((await world.Preferences.ForGameAsync(game, None)).IsOptedOut).IsTrue();
    }

    [Test]
    public async Task NoReasonIsAskedForAndNoneIsStored()
    {
        // §11: "no questions asked". There is no reason parameter, deliberately, and this is the test
        // that stops one being added.
        var method = typeof(OptOutService).GetMethod(nameof(OptOutService.OptOutAsync))!;

        await Assert.That(method.GetParameters().Select(p => p.Name))
            .IsEquivalentTo(new[] { "gameId", "handle", "ct" });
    }

    [Test]
    public async Task OptingBackInResumesTheCrawl()
    {
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        var token = await world.IssueAsync(game);
        await world.Grant.ApplyAsync(token, ClaimChannel.Mssp, None, claimant: "rowan@example.org");
        await world.OptOut.OptOutAsync(game, "rowan@example.org", None);
        world.Probe.Answering("corvid.example", 4201, () => ProbeResults.Answered(
            host: "corvid.example", mssp: ProbeResults.Mssp(("NAME", ["Corvid"]))));

        await world.OptOut.OptInAsync(game, "rowan@example.org", None);
        await world.Service.RunCycleAsync(None);

        await Assert.That(world.Probe.Visited.Count).IsEqualTo(1);
        await Assert.That((await world.Games.ByIdAsync(game, None))!.OptedOutAt).IsNull();
        await Assert.That((await world.Owners.AuditAsync(game, 10, None)).Select(e => e.Action))
            .Contains(OwnerActions.OptedIn);
    }

    [Test]
    public async Task AnOptedOutGameIsNotArchivedForGoingQuiet()
    {
        // It did not go dark; we stopped looking. Archiving it would record our own silence as the
        // game's death, which is the one thing §5.7's whole vocabulary argument is against.
        var world = new ClaimWorld();
        var game = await world.GameAsync("Corvid");
        await world.GoDarkAsync(game, reachableDays: 1, darkDays: 300);
        await world.Games.SetOptedOutAsync(game, world.Time.GetUtcNow().AddDays(-299), None);

        var swept = await new ArchiveSweeper(world.Games, world.Availability, world.Time).SweepAsync(None);

        await Assert.That(swept).IsEqualTo(0);
    }

    [Test]
    public async Task AFlagOfZeroIsNotAnOptOut()
    {
        // MSSP flags are "1"/"0" and a game that sets CRAWL_OPT_OUT 0 is saying the opposite.
        var world = new ClaimWorld();
        await world.GameAsync("Corvid");
        world.Probe.Answering("corvid.example", 4201, () => ProbeResults.Answered(
            host: "corvid.example",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), (ClaimVocabulary.OptOutMsspVariable, ["0"]))));

        await world.Service.RunCycleAsync(None);
        world.Time.Advance(TimeSpan.FromDays(30));
        await world.Service.RunCycleAsync(None);

        await Assert.That(world.Probe.Visited.Count).IsEqualTo(2);
    }
}
```

`Game` gains `DateTimeOffset? OptedOutAt` as a tenth positional member, `IGameRepository` gains
`Task SetOptedOutAsync(Guid id, DateTimeOffset? at, CancellationToken ct)`, and `ClaimWorld` gains
`OptOuts`, `OptOut` and `Task<Guid> TargetOnlyAsync(string host, int port)` (a `CrawlTarget` with a
null `GameId`, which is §7.2's un-promoted candidate).

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'OptOutService' could not be found`.

- [ ] **Step 3: Write the service**

Create `src/MUI.Discovery/Ownership/OptOutService.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Storage;

namespace MUI.Discovery.Ownership;

/// <summary>
/// Spec §11's opt-out: MSSP field, DNS TXT, or request — honoured within one cycle and recorded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two of the three channels need no account with us,</b> and that is the point. The operator most
/// likely to want us gone is the one least likely to want to sign up first, and an opt-out that
/// required a claim would be an opt-out only for people who had already opted in.
/// </para>
/// <para>
/// <b>An opted-out game is not an archived one.</b> It did not go dark; we stopped looking. Archiving
/// it would record our own silence as the game's death, which is precisely what §5.7's vocabulary
/// argument exists to prevent — so the sweeper skips it and the page becomes a stub with the name and
/// the date, URL intact, because other sites link to it.
/// </para>
/// </remarks>
public sealed class OptOutService(
    ICrawlOptOutRepository optOuts,
    IOwnerPreferencesRepository preferences,
    IGameRepository games,
    IOwnerRepository owners,
    OwnershipService ownership,
    IDnsTxtResolver dns,
    TimeProvider time)
{
    /// <summary>
    /// Reads both wire channels off a visit we were making anyway. Returns whether this host has just
    /// asked us to stop.
    /// </summary>
    public async Task<bool> ObserveAsync(CrawlTarget target, ProbeResult result, CancellationToken ct)
    {
        var now = time.GetUtcNow();

        // MSSP: a flag, so "0" is a game saying the opposite and must not be read as consent to stop.
        if (result.Mssp.Flag(ClaimVocabulary.OptOutMsspVariable) is true)
        {
            await ApplyAsync(target, OptOutSource.Mssp, now, ct);

            return true;
        }

        var lookup = await dns.LookupAsync(ClaimVocabulary.DnsNameFor(target.Host), ct);
        if (lookup.Status is DnsTxtStatus.Answered
            && lookup.Values.Any(value =>
                string.Equals(value.Trim(), ClaimVocabulary.OptOutValue, StringComparison.OrdinalIgnoreCase)))
        {
            await ApplyAsync(target, OptOutSource.DnsTxt, now, ct);

            return true;
        }

        return false;
    }

    /// <summary>The dashboard toggle. No reason parameter — §11 says no questions asked.</summary>
    public async Task OptOutAsync(Guid gameId, string handle, CancellationToken ct)
    {
        await EnsureOwnerAsync(gameId, handle, ct);

        var now = time.GetUtcNow();
        await games.SetOptedOutAsync(gameId, now, ct);
        await preferences.UpsertAsync(
            (await preferences.ForGameAsync(gameId, ct) ?? OwnerPreferences.Default(gameId)) with
            {
                OptedOutAt = now,
            }, ct);

        await ownership.RecordAsync(gameId, OwnerAuditActor.Owner, handle, OwnerActions.OptedOut, "request", ct);
    }

    public async Task OptInAsync(Guid gameId, string handle, CancellationToken ct)
    {
        await EnsureOwnerAsync(gameId, handle, ct);

        await games.SetOptedOutAsync(gameId, null, ct);
        await preferences.UpsertAsync(
            (await preferences.ForGameAsync(gameId, ct) ?? OwnerPreferences.Default(gameId)) with
            {
                OptedOutAt = null,
            }, ct);

        await ownership.RecordAsync(gameId, OwnerAuditActor.Owner, handle, OwnerActions.OptedIn, null, ct);
    }

    private async Task ApplyAsync(CrawlTarget target, OptOutSource source, DateTimeOffset now, CancellationToken ct)
    {
        // Always by address, because a host with no game (§7.2's un-promoted candidate) has no other
        // key — and because the address is what the crawler selects on.
        await optOuts.AddAsync(new CrawlOptOut(target.Host, target.Port, now, source), ct);

        if (target.GameId is not { } gameId)
        {
            return;
        }

        await games.SetOptedOutAsync(gameId, now, ct);
        await preferences.UpsertAsync(
            (await preferences.ForGameAsync(gameId, ct) ?? OwnerPreferences.Default(gameId)) with
            {
                OptedOutAt = now,
            }, ct);

        await ownership.RecordAsync(
            gameId, OwnerAuditActor.System, null, OwnerActions.OptedOut,
            source.ToString().ToLowerInvariant(), ct);
    }

    private async Task EnsureOwnerAsync(Guid gameId, string handle, CancellationToken ct)
    {
        if (!await owners.IsOwnerAsync(gameId, handle, ct))
        {
            throw new UnauthorizedAccessException($"{handle} does not own this game.");
        }
    }
}
```

- [ ] **Step 4: Stop the crawler visiting an opted-out host**

In `CrawlerService.ApplyAsync`, after the ingest and claim calls:

```csharp
            await optOut.ObserveAsync(target, result, cancellationToken);
```

and in `NpgsqlCrawlTargetRepository.DueAsync`, add to the `WHERE`:

```sql
              AND NOT EXISTS (SELECT 1 FROM crawl_opt_out o
                              WHERE o.host = t.host AND o.port = t.port)
              AND NOT EXISTS (SELECT 1 FROM game g
                              WHERE g.id = t.game_id AND g.opted_out_at IS NOT NULL)
```

with the same two exclusions in `InMemoryCrawlTargetRepository.DueAsync`. This is the "within one cycle"
guarantee, and it is a selection rule rather than a deletion — §7.4's *nothing is ever deleted* still
holds, the target stays on the books, and opting back in resumes it with no re-discovery.

Also exclude opted-out games from `ArchiveSweeper.SweepAsync` (`Game.OptedOutAt is null`), for the
reason in the service's remarks.

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests </dev/null
```
Expected: PASS — 8 new tests.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Catalog src/MUI.Storage src/MUI.Discovery tests
git commit -m "feat: opt-out via MSSP, DNS or the dashboard — honoured in one cycle, recorded (spec 11)"
```

---

### Task 15: `MsspLinter` — a continuous scorecard, and deliberately not a grade (spec §8, §3.1)

§8: "the MSSP linter scorecard — continuous rather than one-shot, flagging missing fields, wrong types
and non-standard values." The design handoff §09 adds the constraint that shapes the whole type: **"no
score out of ten, no letter grade, no progress ring. A grade turns a volunteer's hobby into homework,
and the fields that are missing are mostly ones no player reads. Findings are ordered by what changes
the public page, each with the exact edit and the reason a player would care."**

`MSSPVariables.IsOfficial` / `.IsKnown` / `.Official` from TelnetNegotiationCore and Plan 02's
`FieldRegistry` already give most of the analysis.

**Files:**
- Create: `src/MUI.Catalog/Ownership/MsspScorecard.cs`
- Create: `src/MUI.Discovery/Ownership/MsspLinter.cs`
- Create: `tests/MUI.Discovery.Tests/Ownership/MsspLinterTests.cs`

**Interfaces:**
- Consumes: `MsspData` (`MUI.Crawl.Mssp`), `ProbeResult`; `MSSPVariables` (`TelnetNegotiationCore.Models`);
  `FieldRegistry`, `FieldDefinition`, `FieldValueKind`, `CapabilityFields`, `GameField` (Plan 02).
- Produces:
  - `enum MUI.Catalog.MsspFindingKind { Contradiction, Missing, Stale, NonStandardValue, WrongType, Observed }`
  - `sealed record MUI.Catalog.MsspFinding(MsspFindingKind Kind, string Variable, string Headline, string Detail, bool ChangesThePublicPage)`
  - `sealed record MUI.Catalog.MsspScorecard(int OfficialSet, int OfficialTotal, IReadOnlyList<MsspFinding> Findings)`
  - `sealed class MUI.Discovery.Ownership.MsspLinter` with
    `MsspScorecard Inspect(ProbeResult result, IReadOnlyList<GameField> stored, DateTimeOffset now)`

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/Ownership/MsspLinterTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery.Ownership;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests.Ownership;

/// <summary>
/// Spec §8's linter, with the delivered design's constraint: no grade, and findings ordered by what
/// changes the public page.
/// </summary>
public class MsspLinterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static MsspScorecard Inspect(ProbeResult result, params GameField[] stored) =>
        new MsspLinter().Inspect(result, stored, Now);

    [Test]
    public async Task ThereIsNoGradeAnywhereOnTheScorecard()
    {
        // The design's argument, kept as a test because a score is the first thing anyone adds:
        // "A grade turns a volunteer's hobby into homework."
        var names = typeof(MsspScorecard).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();

        await Assert.That(names.Any(name =>
            name.Contains("score", StringComparison.Ordinal)
            || name.Contains("grade", StringComparison.Ordinal)
            || name.Contains("percent", StringComparison.Ordinal)
            || name.Contains("rating", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task ItCountsWhatIsSetOutOfTheOfficialSet()
    {
        // "You have 12 of the 26 MSSP fields set" — a count, which is a fact, rather than a mark.
        var card = Inspect(ProbeResults.Answered(mssp: ProbeResults.Mssp(
            ("NAME", ["Corvid"]), ("PLAYERS", ["14"]), ("UPTIME", ["1750000000"]), ("CODEBASE", ["PennMUSH"]))));

        await Assert.That(card.OfficialSet).IsEqualTo(4);
        await Assert.That(card.OfficialTotal).IsGreaterThan(20);
    }

    [Test]
    public async Task ADeclaredCapabilityWeNeverMeasuredIsTheFirstFinding()
    {
        // The delivered dashboard's leading example, and the product's whole thesis: "Your GMCP field
        // says 1, but your server has never offered GMCP in 214 handshakes."
        var card = Inspect(
            ProbeResults.Answered(mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), ("GMCP", ["1"]))),
            new GameField(Guid.Empty, CapabilityFields.Measured("GMCP"), "false",
                FieldSource.Handshake, FieldConfidence.Observed, Now.AddYears(-1), Now));

        var finding = card.Findings[0];

        await Assert.That(finding.Kind).IsEqualTo(MsspFindingKind.Contradiction);
        await Assert.That(finding.Variable).IsEqualTo("GMCP");
        await Assert.That(finding.ChangesThePublicPage).IsTrue();
    }

    [Test]
    public async Task FindingsAreOrderedByWhetherTheyChangeThePublicPage()
    {
        var card = Inspect(
            ProbeResults.Answered(mssp: ProbeResults.Mssp(
                ("NAME", ["Corvid"]), ("GMCP", ["1"]), ("LANGUAGE", ["english"]))),
            new GameField(Guid.Empty, CapabilityFields.Measured("GMCP"), "false",
                FieldSource.Handshake, FieldConfidence.Observed, Now.AddYears(-1), Now));

        await Assert.That(card.Findings.Select(f => f.ChangesThePublicPage))
            .IsEquivalentTo(card.Findings.Select(f => f.ChangesThePublicPage).OrderByDescending(x => x).ToList());
    }

    [Test]
    public async Task ANonStandardValueIsNamedAndSoIsTheStandardOne()
    {
        // "LANGUAGE is set to english; the standard value is English. We already normalise it, so
        // nothing is broken. Worth fixing only if you care what other crawlers see."
        var card = Inspect(ProbeResults.Answered(mssp: ProbeResults.Mssp(
            ("NAME", ["Corvid"]), ("LANGUAGE", ["english"]))));

        var finding = card.Findings.Single(f => f.Variable == "LANGUAGE");

        await Assert.That(finding.Kind).IsEqualTo(MsspFindingKind.NonStandardValue);
        await Assert.That(finding.Detail).Contains("English");
        await Assert.That(finding.ChangesThePublicPage).IsFalse();
    }

    [Test]
    public async Task AValueOfTheWrongTypeIsFlagged()
    {
        var card = Inspect(ProbeResults.Answered(mssp: ProbeResults.Mssp(
            ("NAME", ["Corvid"]), ("PORT", ["not a number"]), ("WEBSITE", ["corvid dot example"]))));

        await Assert.That(card.Findings.Where(f => f.Kind is MsspFindingKind.WrongType)
            .Select(f => f.Variable)).IsEquivalentTo(new[] { "PORT", "WEBSITE" });
    }

    [Test]
    public async Task AStaleHandTypedFieldIsMarkedOldAndNotWrong()
    {
        // "Nothing has changed in DESCRIPTION since 2019. Not a problem — plenty of games are stable.
        // We mark it as old on your page because we cannot tell the difference between stable and
        // forgotten."
        var card = Inspect(
            ProbeResults.Answered(mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), ("GENRE", ["Fantasy"]))),
            new GameField(Guid.Empty, "GENRE", "Fantasy", FieldSource.Mssp, FieldConfidence.Reported,
                Now.AddYears(-7), Now.AddYears(-7)));

        var finding = card.Findings.Single(f => f.Variable == "GENRE");

        await Assert.That(finding.Kind).IsEqualTo(MsspFindingKind.Stale);
        await Assert.That(finding.ChangesThePublicPage).IsTrue();
    }

    [Test]
    public async Task SomethingWeMeasuredAndTheOwnerDidNothingForIsReportedToo()
    {
        // "You added TLS on port 4202 in March, and we picked it up the same hour." A scorecard that
        // only ever lists faults reads as a nagging tool and gets closed.
        var card = Inspect(
            ProbeResults.Answered(mssp: ProbeResults.Mssp(("NAME", ["Corvid"])), tlsObserved: true),
            new GameField(Guid.Empty, CapabilityFields.Measured("TLS"), "true",
                FieldSource.Handshake, FieldConfidence.Observed, Now.AddMonths(-4), Now));

        await Assert.That(card.Findings.Any(f => f.Kind is MsspFindingKind.Observed)).IsTrue();
    }

    [Test]
    public async Task AGameWithGoodMsspGetsAShortListAndNotAnEmptyRitual()
    {
        var card = Inspect(ProbeResults.Answered(mssp: ProbeResults.Mssp(
            ("NAME", ["Corvid"]), ("PLAYERS", ["14"]), ("PORT", ["4201"]), ("CODEBASE", ["PennMUSH"]),
            ("CONTACT", ["admin@corvid.example"]), ("WEBSITE", ["https://corvid.example"]),
            ("LANGUAGE", ["English"]), ("FAMILY", ["TinyMUD"]))));

        await Assert.That(card.Findings.Any(f => f.ChangesThePublicPage)).IsFalse();
    }

    [Test]
    public async Task ItReReadsEveryProbeRatherThanBeingRunOnce()
    {
        // "continuous · re-read every probe" — which is a property of the type: it holds no state, so
        // there is nothing to go stale between runs.
        await Assert.That(typeof(MsspLinter).GetFields(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public))
            .IsEmpty();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'MsspLinter' could not be found`.

- [ ] **Step 3: Write the scorecard shape**

Create `src/MUI.Catalog/Ownership/MsspScorecard.cs`:

```csharp
namespace MUI.Catalog;

/// <summary>What kind of thing the linter noticed (spec §8).</summary>
public enum MsspFindingKind
{
    /// <summary>Declared in MSSP, not observed in the handshake. The product's whole thesis, on one row.</summary>
    Contradiction,

    /// <summary>An official variable this game does not set.</summary>
    Missing,

    /// <summary>Set once and never since. Not wrong — we simply cannot tell stable from forgotten.</summary>
    Stale,

    /// <summary>A value outside the published vocabulary. We normalise it; other crawlers may not.</summary>
    NonStandardValue,

    /// <summary>A value that is not the kind of thing the variable holds.</summary>
    WrongType,

    /// <summary>Something we measured that the owner did not have to do anything for.</summary>
    Observed,
}

/// <summary>
/// One thing the linter noticed, with the reason a player would care.
/// </summary>
/// <param name="ChangesThePublicPage">
/// Whether fixing this changes what a visitor sees. The scorecard is sorted on it, because a list
/// ordered by our tidiness is a list nobody reads twice.
/// </param>
public sealed record MsspFinding(
    MsspFindingKind Kind,
    string Variable,
    string Headline,
    string Detail,
    bool ChangesThePublicPage);

/// <summary>
/// The owner dashboard's continuous MSSP scorecard (spec §8).
/// </summary>
/// <remarks>
/// <b>There is no score, no grade, no percentage and no progress ring, and there must not be.</b> A
/// grade turns a volunteer's hobby into homework, and most of the fields a game does not set are ones
/// no player reads. What is here instead is a count of what is set — a fact — and a list of findings
/// ordered by whether acting on one changes the public page.
/// </remarks>
public sealed record MsspScorecard(int OfficialSet, int OfficialTotal, IReadOnlyList<MsspFinding> Findings);
```

- [ ] **Step 4: Write the linter**

Create `src/MUI.Discovery/Ownership/MsspLinter.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;

using TelnetNegotiationCore.Models;

namespace MUI.Discovery.Ownership;

/// <summary>
/// Re-reads a game's MSSP on every probe and reports what an owner might want to change (spec §8, §3.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Stateless on purpose.</b> "Continuous rather than one-shot" is a property of this type having
/// nothing to remember: every call is a fresh read of the probe we just took, so there is no cached
/// verdict to go stale and no run to schedule.
/// </para>
/// <para>
/// Most of the analysis is already written elsewhere. <c>MSSPVariables.IsOfficial</c> and
/// <c>.Official</c> come from TelnetNegotiationCore; the type of each field and its staleness window
/// come from Plan 2's <c>FieldRegistry</c>; the measured side of a capability is a <c>GameField</c>
/// somebody else wrote. This assembles them.
/// </para>
/// </remarks>
public sealed class MsspLinter
{
    private static readonly IReadOnlyDictionary<string, string[]> StandardValues =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["LANGUAGE"] = ["English", "French", "German", "Spanish", "Portuguese", "Dutch", "Russian", "Chinese"],
            ["STATUS"] = ["Alpha", "Closed Beta", "Open Beta", "Live"],
            ["FAMILY"] = ["AberMUD", "CoffeeMUD", "DikuMUD", "LPMud", "MOO", "Mordor", "TinyMUD", "Custom"],
        };

    public MsspScorecard Inspect(ProbeResult result, IReadOnlyList<GameField> stored, DateTimeOffset now)
    {
        var official = MSSPVariables.Official;
        var set = result.Mssp.Keys.Count(MSSPVariables.IsOfficial);
        var findings = new List<MsspFinding>();

        findings.AddRange(Contradictions(result, stored));
        findings.AddRange(Stale(result, stored, now));
        findings.AddRange(WrongTypes(result));
        findings.AddRange(NonStandard(result));
        findings.AddRange(GoodNews(result, stored));

        return new MsspScorecard(
            set,
            official.Count,
            // "Ordered by what changes the public page." A list sorted by our tidiness is one nobody
            // reads twice.
            [.. findings.OrderByDescending(finding => finding.ChangesThePublicPage)
                .ThenBy(finding => finding.Kind)]);
    }

    /// <summary>Declared in MSSP, not offered in the handshake — §3.1's second gap, on one row.</summary>
    private static IEnumerable<MsspFinding> Contradictions(ProbeResult result, IReadOnlyList<GameField> stored)
    {
        foreach (var capability in CapabilityFields.Names)
        {
            if (result.Mssp.Flag(capability) is not true)
            {
                continue;
            }

            var measured = stored.FirstOrDefault(field => field.Field == CapabilityFields.Measured(capability));
            if (measured is null || measured.Value != "false")
            {
                continue;
            }

            yield return new MsspFinding(
                MsspFindingKind.Contradiction, capability,
                $"Your {capability} field says 1, but your server has never offered {capability} in a handshake.",
                $"Your page shows this as a disagreement, which looks worse than it is. Either set {capability} "
                + "to 0, or enable it on the server.",
                ChangesThePublicPage: true);
        }
    }

    private static IEnumerable<MsspFinding> Stale(
        ProbeResult result, IReadOnlyList<GameField> stored, DateTimeOffset now)
    {
        foreach (var field in stored.Where(field => field.IsStale(now) && result.Mssp.ContainsKey(field.Field)))
        {
            yield return new MsspFinding(
                MsspFindingKind.Stale, field.Field,
                $"Nothing has changed in {field.Field} since {field.LastConfirmedAt:yyyy}.",
                "Not a problem — plenty of games are stable. We mark it as old on your page because we cannot "
                + "tell the difference between stable and forgotten. Editing it, or confirming it, clears the mark.",
                ChangesThePublicPage: true);
        }
    }

    private static IEnumerable<MsspFinding> WrongTypes(ProbeResult result)
    {
        foreach (var variable in result.Mssp.Keys)
        {
            var value = result.Mssp.Default(variable);
            if (value is null)
            {
                continue;
            }

            var definition = FieldRegistry.For(variable);
            var wrong = definition.Kind switch
            {
                FieldValueKind.Integer => !int.TryParse(value, out _),
                FieldValueKind.Boolean => value is not ("0" or "1"),
                FieldValueKind.Url => !Uri.TryCreate(value, UriKind.Absolute, out _),
                FieldValueKind.Email => !value.Contains('@', StringComparison.Ordinal),
                _ => false,
            };

            if (wrong)
            {
                yield return new MsspFinding(
                    MsspFindingKind.WrongType, variable,
                    $"{variable} is set to “{value}”, which is not {Article(definition.Kind)}.",
                    "We ignore the value rather than publishing something wrong, so this field is simply absent "
                    + "from your page until it is fixed.",
                    ChangesThePublicPage: true);
            }
        }
    }

    private static IEnumerable<MsspFinding> NonStandard(ProbeResult result)
    {
        foreach (var (variable, permitted) in StandardValues)
        {
            var value = result.Mssp.Default(variable);
            if (value is null || permitted.Contains(value, StringComparer.Ordinal))
            {
                continue;
            }

            var nearest = permitted.FirstOrDefault(
                candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));

            yield return new MsspFinding(
                MsspFindingKind.NonStandardValue, variable,
                $"{variable} is set to “{value}”; the standard value is “{nearest ?? permitted[0]}”.",
                "We already normalise it, so nothing is broken. Worth fixing only if you care what other "
                + "crawlers see.",
                ChangesThePublicPage: false);
        }
    }

    /// <summary>
    /// A scorecard that only ever lists faults reads as a nagging tool and gets closed. This is the
    /// row the delivered design opens with a filled bullet rather than an outline.
    /// </summary>
    private static IEnumerable<MsspFinding> GoodNews(ProbeResult result, IReadOnlyList<GameField> stored)
    {
        if (result.TlsObserved
            && stored.Any(field => field.Field == CapabilityFields.Measured("TLS") && field.Value == "true"))
        {
            yield return new MsspFinding(
                MsspFindingKind.Observed, "TLS",
                "We measured TLS on this game and your page says so.",
                "Nothing to do — this is an observation, not a claim, which is why it carries more weight than "
                + "an MSSP field could.",
                ChangesThePublicPage: false);
        }
    }

    private static string Article(FieldValueKind kind) => kind switch
    {
        FieldValueKind.Integer => "a number",
        FieldValueKind.Boolean => "0 or 1",
        FieldValueKind.Url => "a URL",
        FieldValueKind.Email => "an email address",
        _ => "the expected kind of value",
    };
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
```
Expected: PASS — 10 new tests.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Catalog/Ownership/MsspScorecard.cs src/MUI.Discovery/Ownership/MsspLinter.cs \
        tests/MUI.Discovery.Tests/Ownership/MsspLinterTests.cs
git commit -m "feat: a continuous MSSP scorecard with findings and deliberately no grade (spec 8)"
```

---

### Task 16: The claim pages and the dashboard's write surface (spec §8)

Plan 05 owns every read surface and is read-only by design; this task adds the writes, in `MUI.Web`,
under the two routes the delivered design names: `/g/{slug}/claim` and `/dashboard/{slug}`.

**Files:**
- Create: `src/MUI.Web/Ownership/IOwnerPrincipal.cs`
- Create: `src/MUI.Web/Claiming/ClaimEndpoints.cs`
- Create: `src/MUI.Web/Ownership/DashboardEndpoints.cs`
- Create: `src/MUI.Web/Claiming/ClaimInstructions.cs`
- Modify: `src/MUI.Web/Program.cs`
- Create: `tests/MUI.Web.Tests/Claiming/ClaimEndpointTests.cs`

**Interfaces:**
- Consumes: `ClaimTokenIssuer`, `ClaimGrant`, `ClaimDiagnostics`, `OwnershipService`,
  `OwnerFieldWriter`, `OwnerPreferenceService`, `OptOutService`, `MsspLinter` (Tasks 2–15);
  `WebApplicationFactory<Program>` (Plan 05 Task 1).
- Produces:
  - `interface MUI.Web.IOwnerPrincipal` with `string? Handle { get; }`
  - `sealed record MUI.Web.Claiming.ClaimInstructionView(string Token, string MsspLine, string ConnectScreenLine, string DnsName, string DnsValue, int ValidDays)`
  - `static class MUI.Web.Claiming.ClaimInstructions` with
    `ClaimInstructionView For(string token, string host)`
  - `static class MUI.Web.Claiming.ClaimEndpoints` with `void Map(IEndpointRouteBuilder routes)`
  - `static class MUI.Web.Ownership.DashboardEndpoints` with `void Map(IEndpointRouteBuilder routes)`

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Web.Tests/Claiming/ClaimEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;

using MUI.Catalog;
using MUI.Web.Claiming;

namespace MUI.Web.Tests.Claiming;

/// <summary>
/// Spec §8's two surfaces. The one test that matters most is the cheapest: the instructions must name
/// the variable the crawler actually looks for, because a page that says otherwise fails every claim
/// it produces and blames the operator.
/// </summary>
public class ClaimEndpointTests
{
    [Test]
    public async Task TheInstructionsNameTheVariableTheCrawlerActuallyLooksFor()
    {
        var view = ClaimInstructions.For("muidx-a2b3-c4d5-e6f7", "tidewater.example");

        await Assert.That(view.MsspLine).Contains(ClaimVocabulary.MsspVariable);
        await Assert.That(view.MsspLine).Contains("muidx-a2b3-c4d5-e6f7");
        await Assert.That(view.ConnectScreenLine).Contains(ClaimVocabulary.ConnectScreenPrefix);
        await Assert.That(view.DnsName).IsEqualTo(ClaimVocabulary.DnsNameFor("tidewater.example"));
        await Assert.That(view.DnsValue).IsEqualTo("muidx-a2b3-c4d5-e6f7");
        await Assert.That(view.ValidDays).IsEqualTo((int)ClaimToken.Validity.TotalDays);
    }

    [Test]
    public async Task TheMsspLineIsInTheFormAPennmushOperatorPastes()
    {
        // `mssp <field>/<value>` in mushcnf.dst — the syntax the delivered design shows, because it
        // is the one that can be copied without editing.
        var view = ClaimInstructions.For("muidx-a2b3-c4d5-e6f7", "tidewater.example");

        await Assert.That(view.MsspLine).IsEqualTo("mssp MUINDEX CLAIM/muidx-a2b3-c4d5-e6f7");
    }

    [Test]
    public async Task StartingAClaimReturnsATokenAndReturningReturnsTheSameOne()
    {
        await using var app = new MuiWebFactory();
        var client = app.CreateClient();
        await app.SeedGameAsync("corvid");

        var first = await (await client.PostAsync("/g/corvid/claim", null))
            .Content.ReadFromJsonAsync<ClaimInstructionView>();
        var second = await (await client.GetAsync("/g/corvid/claim"))
            .Content.ReadFromJsonAsync<ClaimInstructionView>();

        await Assert.That(second!.Token).IsEqualTo(first!.Token);
    }

    [Test]
    public async Task RegeneratingReturnsADifferentToken()
    {
        await using var app = new MuiWebFactory();
        var client = app.CreateClient();
        await app.SeedGameAsync("corvid");
        var first = await (await client.PostAsync("/g/corvid/claim", null))
            .Content.ReadFromJsonAsync<ClaimInstructionView>();

        var second = await (await client.PostAsync("/g/corvid/claim/regenerate", null))
            .Content.ReadFromJsonAsync<ClaimInstructionView>();

        await Assert.That(second!.Token).IsNotEqualTo(first!.Token);
    }

    [Test]
    public async Task TheWaitingPageSaysNothingUntilThreeProbesHavePassed()
    {
        await using var app = new MuiWebFactory();
        var client = app.CreateClient();
        await app.SeedGameAsync("corvid");
        await client.PostAsync("/g/corvid/claim", null);

        var status = await client.GetFromJsonAsync<ClaimStatusView>("/g/corvid/claim/status");

        await Assert.That(status!.Diagnostic).IsNull();
        await Assert.That(status.State).IsEqualTo("pending");
    }

    [Test]
    public async Task AGameThatDoesNotExistIsFourOhFourAndNotAFreshToken()
    {
        await using var app = new MuiWebFactory();

        await Assert.That((await app.CreateClient().PostAsync("/g/nothing-here/claim", null)).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task EveryDashboardWriteNeedsAnOwnerAndTheGameMustBeClaimed()
    {
        await using var app = new MuiWebFactory();
        var client = app.CreateClient();
        await app.SeedGameAsync("corvid");

        var routes = new (string Method, string Path)[]
        {
            ("POST", "/dashboard/corvid/fields"),
            ("POST", "/dashboard/corvid/fields/confirm"),
            ("POST", "/dashboard/corvid/connect-screen"),
            ("POST", "/dashboard/corvid/who"),
            ("POST", "/dashboard/corvid/opt-out"),
            ("POST", "/dashboard/corvid/owners"),
            ("POST", "/dashboard/corvid/transfer"),
        };

        foreach (var (method, path) in routes)
        {
            var response = await client.SendAsync(
                new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
    }

    [Test]
    public async Task NoWriteEndpointLivesUnderTheReadApiPrefix()
    {
        // Plan 5's surface is read-only and its parity test greps read payloads. A write under
        // /api/v1 would be inside the wrong contract.
        await using var app = new MuiWebFactory();
        var writes = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()
                ?.HttpMethods.Any(method => method is "POST" or "PUT" or "PATCH" or "DELETE") == true)
            .Select(endpoint => endpoint.RoutePattern.RawText!)
            .ToList();

        await Assert.That(writes).IsNotEmpty();
        await Assert.That(writes.Any(pattern => pattern.StartsWith("/api/v1", StringComparison.Ordinal)))
            .IsFalse();
    }
}
```

`MuiWebFactory` is Plan 05 Task 1's `WebApplicationFactory<Program>` harness with two additions this
task makes: `Task SeedGameAsync(string slug)` and an `IOwnerPrincipal` registered as a test double
whose `Handle` is null by default and settable per client.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'ClaimInstructions' could not be found`.

- [ ] **Step 3: Write the instructions view**

Create `src/MUI.Web/Claiming/ClaimInstructions.cs`:

```csharp
using MUI.Catalog;

namespace MUI.Web.Claiming;

/// <summary>The three lines an operator copies, in the form each one is pasted.</summary>
public sealed record ClaimInstructionView(
    string Token,
    string MsspLine,
    string ConnectScreenLine,
    string DnsName,
    string DnsValue,
    int ValidDays);

/// <summary>Where a claim is currently standing, for the waiting page.</summary>
public sealed record ClaimStatusView(string State, int ProbesSinceIssue, ClaimDiagnostic? Diagnostic);

/// <summary>
/// Renders spec §8's three channels as copyable text.
/// </summary>
/// <remarks>
/// <b>Every string here comes from <c>ClaimVocabulary</c>.</b> A page that names one MSSP variable
/// while the crawler looks for another fails every claim it produces and blames the operator for it,
/// and there is no test anywhere else that could catch it —
/// <c>TheInstructionsNameTheVariableTheCrawlerActuallyLooksFor</c> is the cheap one that does.
/// </remarks>
public static class ClaimInstructions
{
    public static ClaimInstructionView For(string token, string host) =>
        new(
            token,
            // PennMUSH's own syntax, from mushcnf.dst: `mssp <field>/<value>`. Copyable without editing.
            $"mssp {ClaimVocabulary.MsspVariable}/{token}",
            $"{ClaimVocabulary.ConnectScreenPrefix} {token}",
            ClaimVocabulary.DnsNameFor(host),
            token,
            (int)ClaimToken.Validity.TotalDays);
}
```

- [ ] **Step 4: Write the principal seam and the endpoints**

Create `src/MUI.Web/Ownership/IOwnerPrincipal.cs`:

```csharp
namespace MUI.Web;

/// <summary>
/// Who is signed in, if anyone.
/// </summary>
/// <remarks>
/// <b>The one seam a real authentication provider plugs into.</b> Password hashing, session cookies
/// and the login form are a deployment concern with their own review and are deliberately not in this
/// plan; every dashboard write goes through this interface, so adding a provider is one registration
/// and touches no endpoint.
/// </remarks>
public interface IOwnerPrincipal
{
    string? Handle { get; }
}
```

Create `src/MUI.Web/Claiming/ClaimEndpoints.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery.Ownership;
using MUI.Storage;

namespace MUI.Web.Claiming;

/// <summary>Spec §8's claim flow: three steps, one screen each, and the third is a wait.</summary>
public static class ClaimEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        // Deliberately not under /api/v1: that prefix is Plan 5's read contract, and nothing that
        // writes belongs inside it.
        var group = routes.MapGroup("/g/{slug}/claim");

        group.MapPost("/", async (
            string slug, IGameRepository games, IEndpointRepository endpoints,
            ClaimTokenIssuer issuer, CancellationToken ct) =>
        {
            if (await games.BySlugAsync(slug, ct) is not { } game)
            {
                return Results.NotFound();
            }

            var token = await issuer.IssueAsync(game.Id, ct);

            return Results.Ok(ClaimInstructions.For(token.Value, await HostAsync(endpoints, game.Id, ct)));
        });

        group.MapGet("/", async (
            string slug, IGameRepository games, IEndpointRepository endpoints,
            ClaimTokenIssuer issuer, CancellationToken ct) =>
        {
            if (await games.BySlugAsync(slug, ct) is not { } game)
            {
                return Results.NotFound();
            }

            var token = await issuer.IssueAsync(game.Id, ct);

            return Results.Ok(ClaimInstructions.For(token.Value, await HostAsync(endpoints, game.Id, ct)));
        });

        group.MapPost("/regenerate", async (
            string slug, IGameRepository games, IEndpointRepository endpoints,
            ClaimTokenIssuer issuer, CancellationToken ct) =>
        {
            if (await games.BySlugAsync(slug, ct) is not { } game)
            {
                return Results.NotFound();
            }

            var token = await issuer.RegenerateAsync(game.Id, ct);

            return Results.Ok(ClaimInstructions.For(token.Value, await HostAsync(endpoints, game.Id, ct)));
        });

        group.MapGet("/status", async (
            string slug, IGameRepository games, IClaimRepository claims, TimeProvider time, CancellationToken ct) =>
        {
            if (await games.BySlugAsync(slug, ct) is not { } game)
            {
                return Results.NotFound();
            }

            var token = await claims.LiveForGameAsync(game.Id, time.GetUtcNow(), ct)
                        ?? await claims.VerifiedForGameAsync(game.Id, ct);

            if (token is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new ClaimStatusView(
                token.State.ToString().ToLowerInvariant(),
                token.ProbesSinceIssue,
                ClaimDiagnostics.For(token, await claims.AttemptsAsync(token.Id, ct))));
        });
    }

    private static async Task<string> HostAsync(IEndpointRepository endpoints, Guid gameId, CancellationToken ct) =>
        (await endpoints.ForGameAsync(gameId, ct))
        .OrderByDescending(endpoint => endpoint.LastSeenAt)
        .Select(endpoint => endpoint.Host)
        .FirstOrDefault() ?? "your-host.example";
}
```

`DashboardEndpoints.Map` follows the same shape over `/dashboard/{slug}`, with one guard at the top of
each handler:

```csharp
        var handle = principal.Handle;
        if (handle is null)
        {
            return Results.Unauthorized();
        }
```

and one call each into `OwnerFieldWriter.WriteAsync` / `.ConfirmAsync`,
`OwnerPreferenceService.SetConnectScreenAsync` / `.SetWhoAsync`, `OptOutService.OptOutAsync` /
`.OptInAsync`, `OwnershipService.AddOwnerAsync` / `.RemoveOwnerAsync` / `.BeginTransferAsync`.
`UnauthorizedAccessException` maps to `403` and `ArgumentException` — the WHO pattern that will not
compile — to `400` with the reason, through one `IExceptionHandler` registered beside the group.

Register both in `Program.cs` (`ClaimEndpoints.Map(app); DashboardEndpoints.Map(app);`) along with the
DI graph this plan added: `IClaimRepository` → `NpgsqlClaimRepository`, `IOwnerRepository` →
`NpgsqlOwnerRepository`, `IOwnerPreferencesRepository` → `NpgsqlOwnerPreferencesRepository`,
`ICrawlOptOutRepository` → `NpgsqlCrawlOptOutRepository`, `IDnsTxtResolver` → `DnsTxtResolver`, and
the eight `MUI.Discovery.Ownership` services as scoped registrations. Add `DnsClaimPoller` to the
hosted crawler's own loop beside `CrawlerService`, on `DnsClaimPoller.Interval`.

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Web.Tests </dev/null
```
Expected: PASS — 8 new tests, and Plan 05's parity and no-"uptime" tests still green.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Web tests/MUI.Web.Tests/Claiming
git commit -m "feat(web): the claim pages and the owner dashboard's write surface (spec 8)"
```

---

## Self-review

Run with fresh eyes against the spec, the contract addendum and the design handoff.

### 1. Spec coverage

| Requirement | Where | Task |
|---|---|---|
| §8 the site issues a token | `ClaimTokenIssuer` | 2 |
| §8 fourteen days, one game, regenerable, regeneration invalidates | `ClaimToken.Validity`, `IssueAsync`/`RegenerateAsync`, partial unique index | 1, 2, 3 |
| §8 cryptographically random, retypable alphabet | `ClaimTokenFormat` | 1 |
| §8 channel — MSSP field | `ClaimVerifier.ReadMssp` | 4 |
| §8 channel — connect-screen line, ANSI-stripped | `ClaimVerifier.ReadConnectScreen` | 4 |
| §8 channel — DNS TXT at `_muindex.<host>` | `IDnsTxtResolver`, `DnsTxtResolver`, `ReadDnsAsync` | 5, 6 |
| §8 "verified by the crawler that already exists" | `ClaimCycle` on the crawl loop — **and the DNS exception, stated** | 6, 8 |
| §8 "none requires the site to send mail" | No SMTP anywhere; stated in *Scope* | — |
| §8 diagnostic naming the MSSP fields we did see | `ClaimAttempt`, `ClaimDiagnostics` | 3, 7 |
| §8 owner dashboard — enrichment fields | `OwnerFieldWriter`, `OwnerEnrichmentFields` | 12 |
| §8 owner dashboard — connect-screen suppression | `OwnerPreferenceService.SetConnectScreenAsync` | 13 |
| §8 owner dashboard — WHO-format override | `WhoPreference`, `ProbeTarget.SendWho`/`.WhoSummaryPattern` | 13 |
| §8 owner dashboard — opt-out | `OptOutService` | 14 |
| §8 MSSP linter scorecard, continuous | `MsspLinter` | 15 |
| §8 multi-owner, transfer, audit log | `OwnershipService`, `game_owner`, `owner_audit` | 11 |
| §8 badge and JSON endpoint | **Out of scope — Plan 05 owns read surfaces.** Stated in *Scope* | — |
| §7.3 the token is the decisive identity signal | `game_field["claim_token"]` mirror, `IdentityWeights.ClaimToken` | 10 |
| §7.3 a claimed game is never duplicated | The mirror plus `ClaimBeaconPolicy` | 10 |
| §7.5 a claimed game always receives the ceiling | `SetClaimedAsync` → existing `ArchivePolicy.GraceFor` | 9 |
| §6.2 connect screen suppressible on owner request | `PublishConnectScreen`, skipped at the field write | 13 |
| §6.3 WHO override, or "use MSSP `PLAYERS`" | `WhoPreference`, `WhoParser.Parse(transcript, pattern)` | 13 |
| §11 `CRAWL DELAY` honoured, no harder polling for a waiter | `ClaimCycle`'s position and its constructor | 8 |
| §11 opt-out honoured within one cycle and recorded | `DueAsync` exclusion, `owner_audit`, `crawl_opt_out` | 14 |
| §5.1 owner writes go through the precedence ladder | `SourcePrecedence.Wins` in `OwnerFieldWriter` | 12 |
| §12 bounded, cancellable I/O | `DnsTxtResolverOptions`, `WhoPatterns.MatchTimeout` | 5, 13 |

Every §8 sentence is claimed by a task or explicitly excluded. **No gaps found.**

### 2. Placeholder scan

No "TBD", no "add error handling", no "similar to Task N", no "write tests for the above". Three
places delegate to a pattern rather than repeating a hundred lines — `NpgsqlOwnerRepository` (Task 11
Step 6), `IOwnerPreferencesRepository` (Task 13 Step 4) and `DashboardEndpoints` (Task 16 Step 4) —
and each names the exact SQL statements or exact method calls required, plus the file whose shape they
follow. That is a reference to written code, not a placeholder; each is checkable by a reviewer.

### 3. Type consistency

- `ClaimToken` (record, `MUI.Catalog`) versus `ClaimTokenBeacon` (static class, `MUI.Discovery`) —
  distinct in every task, with the split stated in the reconciliation section.
- `ClaimVocabulary` is the single declaration of `MsspVariable`, `ConnectScreenPrefix` and `DnsLabel`;
  Task 4 pins Plan 03's beacon against it and Task 16 pins the instructions page against it.
- `ClaimGrant.ApplyAsync` gains its `claimant` parameter in Task 11 as an **optional trailing**
  argument, so Tasks 9 and 10's call sites compile unchanged.
- `ClaimVerifier`'s constructor takes `(IClaimRepository, IDnsTxtResolver, TimeProvider)` from Task 4
  onwards and never changes shape.
- `ClaimBeaconPolicy.WeighAsync` takes **no `now`**, and no call site produces one. It is the one
  place in this plan where an ambient-clock read could hide inside an otherwise deterministic type,
  which is why *Global Constraints* states the rule rather than leaving it to review.
- `IdentityMatcher`'s constructor is Plan 03's — `(IGameRepository, IEndpointRepository,
  IGameFieldRepository, IGameFieldIndex, DiscoveryOptions, ClaimBeaconPolicy? = null)` — and the
  field double is passed twice because it implements both field interfaces. Every construction in
  this plan spells all of it out; a four-argument call is a stale snippet, not a shorter overload.
- `ClaimCycle` and `DnsClaimPoller` each gain `ClaimGrant grant` in Task 9, in the position named.
- `OwnerEnrichmentFields.All` and `FieldRegistry`'s four `ownerEnrichable: true` entries are held
  equal by a test in Task 12, so the two lists cannot drift.
- `WhoPatterns.CountGroup`/`.MatchTimeout` are re-declared in `MUI.Crawl.Who.WhoParser` because
  `MUI.Crawl` may not reference `MUI.Catalog`, with an equality test in `MUI.Discovery.Tests` — the
  one project that sees both.
- `Game` gains `OptedOutAt` in Task 14; `IGameRepository` gains `SetClaimedAsync` (Task 9) and
  `SetOptedOutAsync` (Task 14); `IGameFieldRepository` gains `DeleteAsync` (Task 10). All three are
  listed as Plan 02 modifications in the reconciliation table.

### 4. Three things this plan surfaces rather than resolves

Recorded here so a coordinator sees them, because all three are decisions above this plan's pay grade.

1. **§8 says all three channels are verified by the existing crawler; one is not, and cannot be.** A
   DNS TXT record is invisible to a telnet session. This plan builds the resolver, states the
   exception plainly, and takes the one deliberate liberty that follows — the DNS channel is checked
   hourly and off-schedule, because it costs the game's server nothing.
2. **The token is simultaneously public and decisive, and §7.3 does not address the tension.** Every
   channel §8 offers publishes it; `IdentityWeights.ClaimToken` is ten times the auto-merge threshold.
   `ClaimBeaconPolicy` (Task 10) closes it by distinguishing a host move from a clone, which is
   observable: a moved game's old endpoints stop answering and a cloned game's do not. If a
   coordinator prefers a different resolution — a second private half of the token, say, or capping
   the beacon below the merge threshold — Task 10 is the only task that changes.
3. **This plan named two shared test doubles differently from Plans 02, 03 and 04. Both are now
   settled and applied.** `tests/MUI.Discovery.Tests/Support` is **one assembly**, so a second
   declaration of a double is `CS0101` rather than a merge conflict — this was never a style
   preference. Neither was silently adapted while drafting, because guessing wrong creates exactly
   that duplicate; both were escalated and ruled on:
   - **`Fake*Repository` → `InMemory*Repository`, applied.** Plans 02, 03 and 04 all wrote
     `InMemory*` and this plan alone wrote `Fake*` — three against one, and the ruling went the
     obvious way. Every repository double here is now `InMemoryClaimRepository`,
     `InMemoryOwnerRepository`, `InMemoryGameRepository` and so on, and `InMemoryMergeLog` matches
     Plan 03. **Service fakes keep the `Fake` prefix** — `FakeDnsTxtResolver` and `FakeProbe` stub a
     collaborator rather than reimplement a store in memory, and the two ideas are worth telling
     apart by name.
   - **`ProbeFixtures` → `ProbeResults`, applied.** Plan 02 declares `ProbeFixtures` and Plan 03
     declares `ProbeResults`, in the same namespace, with *different* signatures — and this plan's
     snippets were calling `ProbeFixtures` while passing `ProbeResults`' arguments: a `host:` that
     `ProbeFixtures.Answered` has no parameter for, and `Mssp` pairs whose value is a `string[]`
     where `ProbeFixtures.Mssp` takes a bare `string`. Every one of those call sites failed to
     compile as written. They now name `ProbeResults`, which is the builder they were always written
     against.
     **The underlying duplication is recorded, not resolved:** two fixture builders for one
     `ProbeResult` in one assembly is the drift this project keeps designing against, and they should
     become one type. Folding them together is cross-plan churn whose right shape is far easier to
     judge with the code in hand than from a plan, so it belongs to implementation rather than to
     another round of plan edits.

   `ManualTimeProvider` was the third of these and is **resolved**, not surfaced: Plan 02 states in the
   type's own doc comment why it is not called `FakeTimeProvider` (it would collide with
   `Microsoft.Extensions.Time.Testing.FakeTimeProvider`), which settles it, and this plan now uses that
   name throughout.
