# Discovery, Scheduling and Identity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the MUIndex crawler run unattended — MUIndex's own scheduler, per spec §7: a
Postgres-backed permanent crawl-target registry, selected by `next_probe_at`, that never retires
anything; an exponential probe schedule against a permanent weekly ceiling; a referral graph that is
provenance rather than scheduling; per-host serialisation; a scored identity matcher with reversible
merges; and a `CrawlerService` gated on a Postgres advisory lock so N web replicas run exactly one crawler.

**Architecture:** Everything lands in `src/MUI.Discovery`, which is the only project allowed to see both
`MUI.Crawl` (a `ProbeResult`) and `MUI.Catalog`/`MUI.Storage` (catalogue state). Scheduling is a pure
function (`ProbeSchedule`); rate limiting answers a question rather than sleeping, so it is assertable
against an injected `TimeProvider`; concurrency is a semaphore in the loop, deliberately not in the
limiter. Persistence is plain SQL migrations in `src/MUI.Storage/Migrations` applied by Plan 2's
`MigrationRunner`, with the Dapper repositories living in `MUI.Discovery.Storage` because the interfaces
they implement live in `MUI.Discovery` and `MUI.Storage` may not reference it.

**Tech Stack:** .NET 10, C# 14, TUnit on Microsoft.Testing.Platform, Npgsql 10 + Dapper 2 against
PostgreSQL 17, `Testcontainers.PostgreSql` for integration tests, `MUI.Crawl.Mssp` — MUIndex's own MSSP
domain types, written by Plan 01 over `TelnetNegotiationCore` 2.6.5 — for host modelling and scope
classification, `Microsoft.Extensions.Hosting.Abstractions` for `BackgroundService`.

**Depends on: Plan 01 for `IProbe`/`ProbeResult` and the `MUI.Crawl.Mssp` domain types, Plan 02 for
`MUI.Storage`, the repositories, the `MigrationRunner` and `ProbeIngestor`. Both are in this repository:
there is no external prerequisite, and nothing here waits on a package. This plan is what makes the
crawler run unattended.**

**Spec:** `docs/specs/2026-07-30-mu-directory-design.md`, §7.1–§7.4, §7.7, §11, §12, §13.

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
- **Vocabulary is "reachable", never "uptime"** — schema, API, code and copy alike (spec §5.7).
- **Branch from `main`, open a PR, never commit directly to `main`.**
- **Any new test project goes into `MUIndex.slnx` AND `.github/workflows/ci.yml`**, which runs each
  suite as its own explicit step.
- **The MSSP domain is MUIndex's own, in namespace `MUI.Crawl.Mssp`** — `MsspData`, `MsspHost`,
  `MsspHostScope`, `MsspVariables`. There is no shared package and no external dependency here:
  MUIndex implements its own crawler end to end, and Plan 01 writes these types in `MUI.Crawl`
  against its own tests, projecting `TelnetNegotiationCore` 2.6.5's own MSSP collection rather than
  re-parsing option 70. `MUI.Discovery` consumes them with `using MUI.Crawl.Mssp;` and never
  re-declares one.
- **Persistence is PostgreSQL 17 with Npgsql + Dapper and plain numbered `.sql` migration files
  applied by a small idempotent runner. No EF Core**, ever. Integration tests use
  `Testcontainers.PostgreSql`.

---

## The one thing this plan must not get wrong

**Nothing is ever retired.** The scheduler this plan builds is MUIndex's own, answering §7.1 and §7.4
on their own terms: a **Postgres-backed permanent registry, selected by `next_probe_at`, that never
retires a host**. Spec §7.4 is explicit — a game dark for two years is still probed weekly, forever,
*including after it has been archived*, because that is precisely what no incumbent managed (§3:
MudStats came back in Sept 2024, TMC in Jul 2023, and no directory noticed automatically — including
their own). There is no `retired` column in `crawl_target`, no `RetireAfterFailures` option, and no
code path that removes a row. `ProbeSchedule` lengthens the interval and clamps it; it never returns
"never".

SharpMUTerm's in-memory `CrawlFrontier` was read while this was designed and answers a *different*
question: it holds one bounded run's hosts in a dictionary, keyed on a normalised `MsspHost`, and
retires a host after `RetireAfterFailures` consecutive failures because a run has to end. A registry
that grows monotonically forever and outlives every process that touches it has no such moment.
Prior art may be read; **nothing is shared, inherited or ported.** Every type below is MUIndex's own,
in MUIndex's namespaces, with MUIndex's own tests —
`CrawlTargetSchemaTests.ThereIsNoRetiredColumnAndThereNeverWillBe` is the one that holds the
difference in place.

### The wording trap in §7.4, stated once so nobody clamps the wrong way

§7.4 calls the backoff limit a **floor**. It means a floor under *frequency* — "still probed weekly"
— which is a **ceiling on the interval**. Read as a floor under the *interval* it would mean the
opposite: never probe more often than weekly. The constant is therefore named
`ProbeSchedule.LongestInterval`, and both readings are written into its XML doc comment. Every
comparison against it is `interval > LongestInterval ? LongestInterval : interval`.

### Where §7.4 and §11 genuinely disagree, and how this plan resolves it

§7.4 wants a hard weekly ceiling on the interval. §11 says `CRAWL DELAY` is "honoured as a floor" —
and a server may ask for more than a week. These cannot both win. **This plan lets politeness win:**
the exponential backoff is clamped to `LongestInterval` *first*, and the server's `CRAWL DELAY` is
applied as a floor *afterwards*, so a server that asks for thirty days gets thirty days. The
rationale is that §7.4's point is that we never *give up*, not that we override an explicit request
from the operator whose machine we are dialling. `ProbeScheduleTests.AServerAskingForLongerThanTheCeilingGetsIt`
pins it, and the XML doc comment records the tension.

---

## Names this plan adds beyond `CONTRACT.md`

Every name below is new. Where it *changes* a signature the contract fixes, the change and its reason
are stated; the default remains "copy the contract verbatim".

| Name | Namespace | Why |
|---|---|---|
| `ActivityBand` | `MUI.Discovery` | §7.7 says the interval is "tightened for games with recent activity". The contract's `ProbeSchedule.Next(int, TimeSpan?)` cannot express it. |
| `CrawlRateLimiter` | `MUI.Discovery` | The contract has `DiscoveryOptions.GlobalInterval`/`PerHostInterval` but no type that consumes them. Written here, keyed on the host rather than on (host, port). |
| `IGameFieldIndex`, `NpgsqlGameFieldIndex` | `MUI.Discovery`, `MUI.Discovery.Storage` | Identity needs a **reverse** lookup ("which game has `name` = *Corvid*"). Plan 2's `IGameFieldRepository` only reads forward, by game id. |
| `IdentityFields`, `ClaimTokenBeacon`, `IdentitySignals` | `MUI.Discovery` | The field-name vocabulary the matcher compares on, the reader that pulls a claim-token *beacon* off a probe, and signal→JSON. `ClaimTokenBeacon` is deliberately not `ClaimToken` — Plan 06 owns `MUI.Catalog.ClaimToken`, the issued record, and both are in scope here. It **reads** the wire spellings from Plan 06's `MUI.Catalog.ClaimVocabulary` and declares none of its own. |
| `MergeApplier` | `MUI.Discovery` | `IdentityMatcher` only *decides*. Something has to attach the endpoint and write the `FieldChange`. |
| `DuplicateReview`, `IDuplicateReviewRepository`, `NpgsqlDuplicateReviewRepository` | `MUI.Discovery`, `.Storage` | §7.3's "suspected-duplicate pair … both pages stay live and link to each other reciprocally" has no contract type. |
| `GameListingGate`, `Slug` | `MUI.Discovery` | §7.2's "must independently answer MSSP with its own `NAME`/`HOSTNAME` before it is listed", and a slug for a newly created game. |
| `CrawlCycle` | `MUI.Discovery` | What one pass of the loop did, so the loop is assertable without a background thread. |
| `IHostResolver`, `SystemHostResolver`, `HostScopeGuard`, `HostScopeDecision`, `HostScopeRuling` | `MUI.Discovery` | **§7.2's gate, closed on the resolved address.** `MsspHost.IsCrawlable` only ever sees a string, so it cannot be the whole gate — see Task 9 and the note below. Nothing in `CONTRACT.md` resolves a name. |
| `NpgsqlCrawlTargetRepository`, `NpgsqlReferralRepository`, `NpgsqlMergeLog` | `MUI.Discovery.Storage` | The interfaces live in `MUI.Discovery` and `MUI.Storage` may not reference it, so the implementations cannot live in `MUI.Storage`. |
| `DiscoveryServiceCollectionExtensions.AddMuiCrawler` | `MUI.Discovery` | One composition entry point, so `MUI.Web` gains one line. |
| `ReferralVerdict.FanOutExceeded` | `MUI.Discovery` | **Enum member added.** §7.2 caps fan-out per source; without a verdict the overflow would be silently dropped and a run could not explain itself. |

**Signature changes to `CONTRACT.md` types, with reasons:**

1. `ProbeSchedule.Next` / `NextProbeAt` gain a trailing **optional** parameter
   `ActivityBand activity = ActivityBand.Unknown`. The contract's two-argument call shape still
   compiles unchanged. Reason: §7.7's "tightened for games with recent activity".
2. `IdentityMatcher`'s constructor gains `IGameFieldIndex index` and `DiscoveryOptions options`:
   `IdentityMatcher(IGameRepository games, IEndpointRepository endpoints, IGameFieldRepository fields, IGameFieldIndex index, DiscoveryOptions options)`.
   Reasons: candidate discovery is a reverse field lookup (above), and §15.5 says the auto-merge
   threshold "needs calibration against real data, so ship conservative and tune" — a `const double`
   cannot be tuned, so `DiscoveryOptions` carries the thresholds and defaults them to the
   `IdentityWeights` constants, which stay the single source of the default.
3. `CrawlerService`'s constructor gains `IGameRepository games`, `MergeApplier merges` and
   `IDuplicateReviewRepository reviews`, inserted after `identity`. Reason: the loop has to be able
   to *create* a game, *attach* an endpoint and *open* a review pair, and the contract list has no
   collaborator that can do any of the three.
4. `DiscoveryOptions` gains `BatchSize`, `PollInterval`, `LeaseRetryInterval`, `ProbeTimeout`,
   `AutoMergeThreshold`, `ReviewThreshold` and a `Validate()` method. Purely additive.
5. `CrawlerService`'s constructor further gains `HostScopeGuard scopes`, after `referrals`. Reason:
   Task 9's resolved-address gate has to run between taking a target off the registry and opening a
   socket to it, and no contracted collaborator can do that.
6. `IdentityMatcher`'s constructor further gains a trailing optional
   `ClaimBeaconPolicy? beacons = null` (Plan 06's type). Optional and trailing so `null` is the
   pre-claiming bare comparison and no existing test changes. Reason: **the claim token is
   simultaneously public and decisive**, so on the bare comparison anyone can republish a claimed
   game's token and be auto-merged into it — see item 8 below and the type's remarks.
7. `CrawlerService`'s constructor further gains a trailing optional `ClaimCycle? claims = null` (Plan
   06's type), **called in `ApplyAsync` between `ingestor.IngestAsync` and `RescheduleAsync`**. The
   call *position* is the requirement, not the parameter: verification rides a visit the crawler was
   making anyway and cannot reach the schedule from there, which is what stops a pending claim buying
   an extra dial. Optional only so Plan 03 builds before Plan 06 lands.
8. `CrawlTarget` gains `bool IsOperatorSeed { get; init; }`, defaulting to **false**. Reason: Task 9's
   gate refuses a host that resolves into non-global space, and an operator who types `127.0.0.1`
   against their own test server has said what they mean. The default is the *guarded* value on
   purpose — a target built without thinking about it, which is every referral and every import, is
   checked. Contract callers that never set it are unaffected and get the safe behaviour.

## Names this plan **requires** from Plan 01 that `CONTRACT.md` does not fix

Task 9's guard classifies a **resolved `IPAddress`**, and `MsspHost` already implements exactly that
classification for the literal-address case. Writing a second copy of the loopback / RFC 1918 / RFC
6598 / `fc00::/7` / link-local / multicast range checks in `MUI.Discovery` would be two sets of rules
that must agree for ever, and the day they disagree is the day one of them is wrong about
`169.254.169.254`. **Plan 01 must therefore expose its classifier as a public static over `IPAddress`:**

```csharp
namespace MUI.Crawl.Mssp;

public sealed record MsspHost
{
    /// The scope of an already-resolved address. Public because the crawl loop must classify what DNS
    /// returned, not just what a REFERRAL string said — see Plan 03 Task 9.
    public static MsspHostScope ScopeOf(System.Net.IPAddress address);
}
```

It is a refactor rather than new behaviour: `MsspHost.Create` already computes this internally when the
host string parses as an address, so the body moves and the existing behaviour is unchanged. If Plan 01
lands without it, add it there — do not re-implement the ranges here.

## Names this plan **requires** from Plan 02 that `CONTRACT.md` does not fix

`CONTRACT.md` names Plan 2's *interfaces* but not its Npgsql *implementations*, and this plan's test
fixtures have to construct one to seed a `game` row for foreign keys. **Plan 2 must name them
`MUI.Storage.NpgsqlGameRepository`, `NpgsqlGameFieldRepository`, `NpgsqlEndpointRepository`,
`NpgsqlPresenceRepository`, `NpgsqlAvailabilityRepository`, each with a single-argument constructor
taking `NpgsqlDataSource`.** This plan also assumes Plan 2's `game_field` table has columns
`game_id`, `field`, `value` and that `MUI.Storage.csproj` embeds `Migrations/*.sql` by glob.

It further consumes **`MUI.Catalog.HostName.Normalize`** (Plan 02 Task 10). `crawl_target.host` adopts
the same canonical form Plan 02 gave `game_endpoint.host`, with the same CHECK constraint and the same
ordinal comparison on both sides — see Task 4 and Task 5. Plan 04 raised this and correctly stopped at
this plan's boundary rather than tightening only its own fake; adopting it here is what makes the rule
one rule. `HostNormalisationAgreementTests` (Task 5) pins `HostName.Normalize` against
`MsspHost.Create(…).Host`, because those are two implementations of one rule that no compiler can
check against each other. **That file is Plan 02's** (Task 10) — Task 5 adds a method to the existing
class rather than declaring a second one, because `tests/MUI.Discovery.Tests` is a single assembly.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/MUI.Discovery/DiscoveryOptions.cs` | Every knob a crawl run has, plus `Validate()` and `ActivityBand`. |
| `src/MUI.Discovery/ProbeSchedule.cs` | Pure scheduling arithmetic. No I/O, no clock. |
| `src/MUI.Discovery/CrawlTarget.cs` | The registry record and `ICrawlTargetRepository`. |
| `src/MUI.Discovery/ReferralEdge.cs` | **Exists. Never redefined.** |
| `src/MUI.Discovery/ReferralGraphWriter.cs` | `ReferralVerdict`, `ReferralIntake`, `IReferralRepository`, the writer. |
| `src/MUI.Discovery/BannerFingerprint.cs` | ANSI-stripped, whitespace-collapsed SHA-256. |
| `src/MUI.Discovery/HostGate.cs` | Per-host serialisation. |
| `src/MUI.Discovery/CrawlRateLimiter.cs` | Two time floors; answers questions, never sleeps for you. |
| `src/MUI.Discovery/HostScopeGuard.cs` | `IHostResolver`, `SystemHostResolver`, `HostScopeDecision`, `HostScopeRuling`, the guard. §7.2 on the resolved address. |
| `src/MUI.Discovery/Identity.cs` | `IdentitySignal`, `IdentityScore`, `IdentityWeights`, `IdentityVerdict`, `IdentityFields`, `ClaimTokenBeacon`, `IdentitySignals`, `IGameFieldIndex`. |
| `src/MUI.Discovery/IdentityMatcher.cs` | The scored fingerprint. |
| `src/MUI.Discovery/MergeLog.cs` | `MergeRecord`, `IMergeLog`. |
| `src/MUI.Discovery/DuplicateReview.cs` | `DuplicateReview`, `IDuplicateReviewRepository`. |
| `src/MUI.Discovery/MergeApplier.cs` | Endpoint attach + `FieldChange`; game-to-game merge. |
| `src/MUI.Discovery/AdvisoryLock.cs` | `pg_try_advisory_lock` on a dedicated connection. |
| `src/MUI.Discovery/CrawlerService.cs` | `CrawlCycle`, `GameListingGate`, `Slug`, the loop. |
| `src/MUI.Discovery/DiscoveryServiceCollectionExtensions.cs` | One `AddMuiCrawler()`. |
| `src/MUI.Discovery/Storage/*.cs` | Dapper implementations of this plan's three-plus repositories. |
| `src/MUI.Storage/Migrations/0010…0013_*.sql` | `crawl_target`, `referral_edge`, `merge_log`, `duplicate_review`. Numbered from 0010 to leave Plan 2 room below. |
| `tests/MUI.Discovery.Tests/Support/*.cs` | **Created here:** `PostgresFixture`, `FakeProbe`, `FakeHostResolver`, `ProbeIngestors`, and the doubles for this plan's own interfaces — `InMemoryCrawlTargetRepository`, `InMemoryReferralRepository`, `InMemoryMergeLog`, `InMemoryDuplicateReviewRepository`. **Extended, not created:** `ManualTimeProvider` (Plan 02 Task 14 declares it; this plan adds `CreateTimer`) and `InMemoryRepositories.cs`'s `InMemoryGameFieldRepository` (Plan 02 Task 13; this plan adds `IGameFieldIndex`). **Consumed unchanged:** `InMemoryGameRepository`, `InMemoryEndpointRepository`, `InMemoryPresenceRepository`, `InMemoryAvailabilityRepository`. This is one test assembly — a second declaration of any of these is CS0101, not a merge conflict. |

---

### Task 1: Wire `MUI.Discovery` for Postgres, hosting and Testcontainers

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/MUI.Discovery/MUI.Discovery.csproj`
- Modify: `tests/MUI.Discovery.Tests/MUI.Discovery.Tests.csproj`
- Modify: `.github/workflows/ci.yml:47-49`
- **Modify (do not create):** `tests/MUI.Discovery.Tests/Support/ManualTimeProvider.cs` — **Plan 02
  Task 14 creates this file**, and `tests/MUI.Discovery.Tests` is one assembly, so a second declaration
  is a duplicate-type compile error rather than a merge inconvenience. Plan 02 lands first; this plan
  extends what is there.
- Create: `tests/MUI.Discovery.Tests/Support/PostgresFixture.cs`
- Test: `tests/MUI.Discovery.Tests/Support/PostgresFixtureTests.cs`

**Interfaces:**
- Consumes: `MUI.Storage.MigrationRunner(NpgsqlDataSource, ILogger?)` and its
  `Task<IReadOnlyList<string>> ApplyAsync(CancellationToken)` (Plan 2); and Plan 02's
  `MUI.Discovery.Tests.Support.ManualTimeProvider : TimeProvider`, whose constructor
  `ManualTimeProvider(DateTimeOffset? start = null)` and `void Advance(TimeSpan by)` this plan keeps
  unchanged in signature.
- Produces: on `ManualTimeProvider`, a working **`CreateTimer`** — the one thing Plan 02's version does
  not have, and the thing this plan cannot do without, because `Task.Delay(ts, timeProvider, ct)` and
  `new CancellationTokenSource(ts, timeProvider)` are how the rate limiter, the probe bound and the
  crawl loop are driven by hand.
  `MUI.Discovery.Tests.Support.PostgresFixture` with
  `static Task<NpgsqlDataSource> SourceAsync()`, `static Task ResetAsync(NpgsqlDataSource)` and
  `static Task<Guid> InsertGameAsync(NpgsqlDataSource, string name, Guid? id = null)`.

- [ ] **Step 1: Add the package versions**

In `Directory.Packages.props`, inside the first `<ItemGroup>` (after the
`Microsoft.Extensions.Logging.Abstractions` line), add — **skipping any line already present, because
Plans 1 and 2 add some of these:**

```xml
    <!-- BackgroundService for the in-process crawler (spec §4.11). -->
    <PackageVersion Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.2" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.2" />
    <PackageVersion Include="Npgsql" Version="10.0.0" />
    <PackageVersion Include="Dapper" Version="2.1.66" />
```

In the test `<ItemGroup>` (the one holding `TUnit`), add — again skipping what is already there:

```xml
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.7.0" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.2" />
```

- [ ] **Step 2: Reference them from the two projects**

Replace the `<ItemGroup>` in `src/MUI.Discovery/MUI.Discovery.csproj` with:

```xml
  <ItemGroup>
    <ProjectReference Include="..\MUI.Catalog\MUI.Catalog.csproj" />
    <ProjectReference Include="..\MUI.Crawl\MUI.Crawl.csproj" />
    <ProjectReference Include="..\MUI.Storage\MUI.Storage.csproj" />
  </ItemGroup>

  <!--
    No MSSP package reference, deliberately. The MSSP domain types live in MUI.Crawl.Mssp, which the
    ProjectReference above already brings in; MUIndex implements its own crawler end to end.
  -->
  <ItemGroup>
    <PackageReference Include="Npgsql" />
    <PackageReference Include="Dapper" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
```

Add to `tests/MUI.Discovery.Tests/MUI.Discovery.Tests.csproj`, inside the `TUnit` `<ItemGroup>`:

```xml
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Npgsql" />
    <PackageReference Include="Dapper" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
```

- [ ] **Step 3: Give Plan 02's manual clock working timers**

**Do not create this file — Plan 02 Task 14 already did, and `tests/MUI.Discovery.Tests` is one
assembly.** Open `tests/MUI.Discovery.Tests/Support/ManualTimeProvider.cs` and extend the class that is
there. Plan 02's version moves `GetUtcNow()` by hand, which is all Plan 02's writers need; this plan
additionally needs `CreateTimer`, because `Task.Delay(ts, timeProvider, ct)` and
`new CancellationTokenSource(ts, timeProvider)` are what `CrawlRateLimiter.WaitForTurnAsync`,
`CrawlerService.ProbeBoundedAsync` and `RestAsync` are built on. Without it those calls fall back to
real time and three tasks in this plan become sleep-and-hope.

Add the field, the override, the remover and the nested timer; **replace** `Advance`, whose current
body only assigns `_now`. For a test with no timers the replacement behaves identically, which is why
Plan 02's suite is unaffected.

```csharp
    // ---- Added by Plan 03. Plan 02's writers need only a clock; the crawler needs timers, because
    // Task.Delay(ts, timeProvider, ct) and CancellationTokenSource(ts, timeProvider) are how the rate
    // limiter, the probe bound and the loop's rest are driven without sleeping.
    private readonly List<ManualTimer> _timers = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>
    /// Moves the clock, firing every timer that falls due on the way, in due order.
    /// </summary>
    /// <remarks>
    /// Replaces Plan 02's body, which assigned <c>_now</c> and returned. With no timers registered the
    /// two are indistinguishable; with timers, this is the difference between a test that drives the
    /// crawl loop and a test that waits for it.
    /// </remarks>
    public void Advance(TimeSpan by)
    {
        var end = GetUtcNow() + by;
        while (true)
        {
            ManualTimer? due = null;
            lock (_gate)
            {
                foreach (var timer in _timers)
                {
                    if (timer.DueAt is { } at && at <= end && (due?.DueAt is not { } best || at < best))
                    {
                        due = timer;
                    }
                }

                if (due is null)
                {
                    _now = end;
                    return;
                }

                _now = due.DueAt!.Value;
            }

            // Fired outside the lock: a Task.Delay continuation may run inline and ask the clock what
            // time it is.
            due.Fire();
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;
            return true;
        }

        public void Fire()
        {
            DueAt = _period == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + _period;
            callback(state);
        }

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
```

Three things this assumes about Plan 02's file, each cheap to reconcile if it landed differently:

- **the backing field is `_now`** and the lock guarding it is `_gate`. If they are named otherwise, use
  Plan 02's names — do not add a second field.
- **`_gate` is a `Lock`** (or a plain object). Either works with `lock`.
- **`TimestampFrequency` and `GetTimestamp()` are already overridden.** If not, add
  `public override long TimestampFrequency => TimeSpan.TicksPerSecond;` and a `GetTimestamp()`
  returning `_now.UtcTicks` under the lock; `Task.Delay` does not need them, but `Stopwatch`-shaped
  code does and getting a real timestamp from a fake clock is a confusing failure.

- [ ] **Step 4: Write the Postgres fixture**

Create `tests/MUI.Discovery.Tests/Support/PostgresFixture.cs`:

```csharp
using Dapper;
using MUI.Catalog;
using MUI.Storage;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Exceptions;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// One Postgres container for the whole suite, migrated once. Tests that touch it are
/// <c>[NotInParallel]</c> and call <see cref="ResetAsync"/> first, because they share one schema.
/// </summary>
/// <remarks>
/// Skipping is driven by an environment variable rather than by sniffing for a daemon: a silent skip
/// when Docker is merely broken would turn every storage test into a no-op nobody noticed. CI sets
/// <c>MUI_SKIP_POSTGRES_TESTS=1</c> on the Windows runner only, where Linux containers are not
/// available; everywhere else a missing daemon is a hard failure, which is the correct signal.
/// </remarks>
public static class PostgresFixture
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgreSqlContainer? _container;
    private static NpgsqlDataSource? _source;

    public static bool Skipped =>
        Environment.GetEnvironmentVariable("MUI_SKIP_POSTGRES_TESTS") == "1";

    public static async Task<NpgsqlDataSource> SourceAsync()
    {
        if (Skipped)
        {
            throw new SkipTestException("MUI_SKIP_POSTGRES_TESTS=1 — no Linux container runtime here.");
        }

        await Gate.WaitAsync();
        try
        {
            if (_source is not null)
            {
                return _source;
            }

            _container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
            await _container.StartAsync();

            var source = NpgsqlDataSource.Create(_container.GetConnectionString());
            await new MigrationRunner(source).ApplyAsync(CancellationToken.None);
            _source = source;
            return _source;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Empties every table but the migration ledger, whatever Plan 2 happened to name them.</summary>
    public static async Task ResetAsync(NpgsqlDataSource source) =>
        await source.CreateCommand("""
            DO $$
            DECLARE t text;
            BEGIN
                FOR t IN
                    SELECT tablename FROM pg_tables
                     WHERE schemaname = 'public' AND tablename <> 'mui_migration'
                LOOP
                    EXECUTE format('TRUNCATE TABLE %I RESTART IDENTITY CASCADE', t);
                END LOOP;
            END $$;
            """).ExecuteNonQueryAsync();

    /// <summary>A minimal game row, for the foreign keys this plan's tables carry.</summary>
    public static async Task<Guid> InsertGameAsync(NpgsqlDataSource source, string name, Guid? id = null)
    {
        var gameId = id ?? Guid.CreateVersion7();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await new NpgsqlGameRepository(source).InsertAsync(
            new Game(gameId, $"{name.ToLowerInvariant()}-{gameId:N}"[..24], name,
                LifecycleState.Active, IsClaimed: false, now, now, null),
            CancellationToken.None);
        return gameId;
    }

    /// <summary>Convenience for the assertions that read a column no repository exposes.</summary>
    public static async Task<T?> ScalarAsync<T>(NpgsqlDataSource source, string sql, object? parameters = null)
    {
        await using var connection = await source.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters);
    }
}
```

- [ ] **Step 5: Write the failing fixture test**

Create `tests/MUI.Discovery.Tests/Support/PostgresFixtureTests.cs`:

```csharp
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

[NotInParallel]
public class PostgresFixtureTests
{
    [Test]
    public async Task TheMigrationsApplyAndAGameRowCanBeSeeded()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);

        var gameId = await PostgresFixture.InsertGameAsync(source, "Corvid");

        var found = await PostgresFixture.ScalarAsync<int>(
            source, "SELECT count(*) FROM game WHERE id = @gameId", new { gameId });

        await Assert.That(found).IsEqualTo(1);
    }
}
```

- [ ] **Step 6: Run it and watch it fail**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `MUI.Storage.NpgsqlGameRepository` does not exist until Plan 2 has landed. If it
fails for that reason, Plan 2 is not merged and this plan cannot start; stop and land Plan 2 first.
With Plan 2 in place the build succeeds and the test run is the real check.

- [ ] **Step 7: Teach CI which runner has no Docker**

In `.github/workflows/ci.yml`, replace the `Test — Discovery` step with:

```yaml
      # The Discovery suite carries the Postgres-backed tests. Testcontainers needs a Linux container
      # runtime, which the Windows runner does not have; the pure unit tests still run there.
      - name: Test — Discovery
        shell: bash
        env:
          MUI_SKIP_POSTGRES_TESTS: ${{ runner.os == 'Windows' && '1' || '0' }}
        run: dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests/MUI.Discovery.Tests.csproj
```

- [ ] **Step 8: Run the suite**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS, including `TheMigrationsApplyAndAGameRowCanBeSeeded`.

- [ ] **Step 9: Commit**

```bash
git add Directory.Packages.props src/MUI.Discovery/MUI.Discovery.csproj \
        tests/MUI.Discovery.Tests/MUI.Discovery.Tests.csproj \
        tests/MUI.Discovery.Tests/Support .github/workflows/ci.yml
git commit -m "build: wire MUI.Discovery for Postgres, hosting and Testcontainers"
```

---

### Task 2: `DiscoveryOptions` and `ActivityBand`

**Files:**
- Create: `src/MUI.Discovery/DiscoveryOptions.cs`
- Test: `tests/MUI.Discovery.Tests/DiscoveryOptionsTests.cs`

**Interfaces:**
- Produces: `MUI.Discovery.DiscoveryOptions` — a record with `MaxDepth`, `MaxFanOutPerSource`,
  `FollowReferrals`, `MaxConcurrency`, `GlobalInterval`, `PerHostInterval` (all from `CONTRACT.md`)
  plus `BatchSize`, `PollInterval`, `LeaseRetryInterval`, `ProbeTimeout`, `AutoMergeThreshold`,
  `ReviewThreshold`, and `void Validate()`. `MUI.Discovery.ActivityBand` — `Unknown`, `Quiet`, `Busy`.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/DiscoveryOptionsTests.cs`:

```csharp
using MUI.Discovery;

namespace MUI.Discovery.Tests;

/// <summary>
/// These numbers reach other people's game servers. Lowering one should be a deliberate act with a
/// test to change (spec §11).
/// </summary>
public class DiscoveryOptionsTests
{
    [Test]
    public async Task TheDefaultsAreConservative()
    {
        var defaults = new DiscoveryOptions();

        await Assert.That(defaults.MaxDepth).IsEqualTo(4);
        await Assert.That(defaults.MaxFanOutPerSource).IsEqualTo(50);
        await Assert.That(defaults.FollowReferrals).IsTrue();
        await Assert.That(defaults.MaxConcurrency).IsEqualTo(8);
        await Assert.That(defaults.GlobalInterval).IsEqualTo(TimeSpan.FromMilliseconds(250));
        await Assert.That(defaults.PerHostInterval).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(defaults.BatchSize).IsEqualTo(200);
        await Assert.That(defaults.PollInterval).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(defaults.LeaseRetryInterval).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(defaults.ProbeTimeout).IsEqualTo(TimeSpan.FromSeconds(60));
    }

    [Test]
    public async Task TheThresholdsDefaultToTheWeightsSoThereIsOneSourceOfTheDefault()
    {
        // Spec §15.5: the auto-merge threshold needs calibration against real data. It ships as a
        // configurable option so it can be tuned without a redeploy of the constants, and it defaults
        // to the conservative constant.
        var defaults = new DiscoveryOptions();

        await Assert.That(defaults.AutoMergeThreshold).IsEqualTo(IdentityWeights.AutoMergeThreshold);
        await Assert.That(defaults.ReviewThreshold).IsEqualTo(IdentityWeights.ReviewThreshold);
    }

    [Test]
    public async Task AReviewThresholdAboveTheMergeThresholdIsRefused()
    {
        var options = new DiscoveryOptions { AutoMergeThreshold = 0.4, ReviewThreshold = 0.9 };

        await Assert.That(options.Validate).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0, 8)]
    [Arguments(8, 0)]
    public async Task ZeroConcurrencyOrZeroBatchIsRefused(int concurrency, int batch)
    {
        var options = new DiscoveryOptions { MaxConcurrency = concurrency, BatchSize = batch };

        await Assert.That(options.Validate).Throws<ArgumentException>();
    }

    [Test]
    public async Task ANegativeDepthIsRefusedButZeroIsNot()
    {
        await Assert.That(new DiscoveryOptions { MaxDepth = -1 }.Validate).Throws<ArgumentException>();
        await Assert.That(new DiscoveryOptions { MaxDepth = 0 }.Validate).ThrowsNothing();
    }

    [Test]
    public async Task ANonPositiveProbeTimeoutIsRefusedBecauseAWedgedProbeMustBeBounded()
    {
        // Spec §12: bounding is a correctness requirement, not hygiene — the crawler shares a process
        // with the web tier.
        var options = new DiscoveryOptions { ProbeTimeout = TimeSpan.Zero };

        await Assert.That(options.Validate).Throws<ArgumentException>();
    }
}
```

- [ ] **Step 2: Run it and verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `DiscoveryOptions` and `IdentityWeights` do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/MUI.Discovery/DiscoveryOptions.cs`:

```csharp
namespace MUI.Discovery;

/// <summary>
/// How busy a game looked the last time we got in. §7.7 tightens the interval "for games with recent
/// activity"; this is that input, derived from the probe rather than stored, because it is a fact
/// about the observation we are rescheduling from.
/// </summary>
public enum ActivityBand
{
    /// <summary>No usable reading — a failed probe, or a WHO the parser could not read. Parsers never fabricate.</summary>
    Unknown,

    /// <summary>We got in and nobody was there. A measured zero is a real fact, not an absence (spec §5.4).</summary>
    Quiet,

    /// <summary>Somebody was connected.</summary>
    Busy,
}

/// <summary>
/// Everything a crawl run is allowed to do, with defaults chosen to be conservative rather than fast.
/// </summary>
/// <remarks>
/// MSSP's <c>REFERRAL</c> is a documented invitation to crawl, but an invitation is not a licence to be
/// expensive. Every default here errs toward the operator on the other end.
/// </remarks>
public sealed record DiscoveryOptions
{
    /// <summary>How many referral hops from an originally seeded game a target may be discovered at (spec §7.2).</summary>
    public int MaxDepth { get; init; } = 4;

    /// <summary>The most referrals one game's <c>REFERRAL</c> list may contribute in one probe (spec §7.2).</summary>
    public int MaxFanOutPerSource { get; init; } = 50;

    /// <summary>Off makes this a status checker for a known list; on — the default — makes it a crawler.</summary>
    public bool FollowReferrals { get; init; } = true;

    /// <summary>
    /// How many probes may be in flight at once. Enforced by a semaphore in the crawl loop and
    /// deliberately not by <see cref="CrawlRateLimiter"/>: it is a fact about connections in flight
    /// rather than about time, and folding the two together would make neither testable.
    /// </summary>
    public int MaxConcurrency { get; init; } = 8;

    /// <summary>The minimum gap between the starts of any two connections, anywhere.</summary>
    public TimeSpan GlobalInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The minimum gap between two connections to the same host — the floor under a retry.</summary>
    public TimeSpan PerHostInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How many due targets one cycle claims.</summary>
    public int BatchSize { get; init; } = 200;

    /// <summary>How long the loop rests between cycles when it holds the lease.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a replica that lost the advisory lock waits before asking again (spec §12).</summary>
    public TimeSpan LeaseRetryInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The crawl loop's own hard bound on one probe, applied on top of whatever the probe promises.
    /// The crawler shares a process with the web tier, so a wedged probe must not be able to starve
    /// request threads (spec §12) — and the loop does not get to trust a collaborator for that.
    /// </summary>
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>At or above this score the probe is merged into the candidate game (spec §7.3).</summary>
    public double AutoMergeThreshold { get; init; } = IdentityWeights.AutoMergeThreshold;

    /// <summary>At or above this score a suspected-duplicate pair is opened instead (spec §7.3).</summary>
    public double ReviewThreshold { get; init; } = IdentityWeights.ReviewThreshold;

    /// <summary>
    /// Throws when a setting could only have arrived from a typo or a hand-edited file, rather than
    /// starting a run that would be wrong in a way nobody notices until it is on the network.
    /// </summary>
    public void Validate()
    {
        if (MaxConcurrency < 1)
        {
            throw new ArgumentException("MaxConcurrency must be at least 1.");
        }

        if (BatchSize < 1)
        {
            throw new ArgumentException("BatchSize must be at least 1.");
        }

        if (MaxDepth < 0)
        {
            throw new ArgumentException("MaxDepth cannot be negative.");
        }

        if (MaxFanOutPerSource < 0)
        {
            throw new ArgumentException("MaxFanOutPerSource cannot be negative.");
        }

        if (GlobalInterval < TimeSpan.Zero || PerHostInterval < TimeSpan.Zero)
        {
            throw new ArgumentException("Rate-limit intervals cannot be negative.");
        }

        if (ProbeTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("ProbeTimeout must be positive: an unbounded probe can starve the web tier.");
        }

        if (PollInterval <= TimeSpan.Zero || LeaseRetryInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("PollInterval and LeaseRetryInterval must be positive.");
        }

        if (ReviewThreshold > AutoMergeThreshold)
        {
            throw new ArgumentException("ReviewThreshold cannot exceed AutoMergeThreshold: nothing would ever be reviewed.");
        }
    }
}
```

- [ ] **Step 4: Add the weights this file leans on**

`DiscoveryOptions` defaults from `IdentityWeights`, which Task 10 fills out. Create the minimum now,
in `src/MUI.Discovery/Identity.cs`:

```csharp
namespace MUI.Discovery;

/// <summary>
/// The weighted signals of spec §7.3, and the two thresholds they are compared against.
/// </summary>
/// <remarks>
/// Spec §15.5 records that the auto-merge threshold needs calibration against real data, so these are
/// the conservative shipping defaults and <see cref="DiscoveryOptions"/> is what a deployment tunes.
/// </remarks>
public static class IdentityWeights
{
    /// <summary>A previously-seen (host, port). Strongest: direct continuity, and on its own enough to merge.</summary>
    public const double Endpoint = 1.00;

    /// <summary>MSSP <c>NAME</c> together with <c>CREATED</c>. Both, because a name alone collides.</summary>
    public const double MsspNameAndCreated = 0.60;

    /// <summary>The connect screen's fingerprint. Survives a host move; changes on redesign.</summary>
    public const double BannerHash = 0.50;

    /// <summary><c>WEBSITE</c> or <c>CONTACT</c>. Stable, and rarely coincidental.</summary>
    public const double WebsiteOrContact = 0.40;

    /// <summary><c>CODEBASE</c> and its version. Weak alone; useful as corroboration.</summary>
    public const double CodebaseAndVersion = 0.15;

    /// <summary>
    /// The site-issued claim token (spec §7.3, §8). Decisive — <em>subject to</em>
    /// <c>ClaimBeaconPolicy</c>.
    /// </summary>
    /// <remarks>
    /// <b>Read this before treating the number on its own as the guarantee.</b> Four times
    /// <see cref="AutoMergeThreshold"/> means one matching token merges two games with nothing else
    /// agreeing — and every §8 channel <em>publishes</em> the token, because proving control without an
    /// email round-trip is the entire point. On a bare value comparison this weight is therefore a merge
    /// primitive that any passer-by can trigger: read a claimed game's MSSP, republish the token from
    /// your own host, be absorbed into their listing. Secrecy cannot fix a credential whose job is to be
    /// public. What makes it safe is <c>ClaimBeaconPolicy</c> (Plan 06), which
    /// <see cref="IdentityMatcher"/> consults: the token counts on the game's own known endpoints, and
    /// from a strange host only once the game's own addresses have stopped answering. §7.3's "a claimed
    /// game is never duplicated" holds under that policy and not without it.
    /// </remarks>
    public const double ClaimToken = 10.0;

    /// <summary>At or above this, merge. Equal to <see cref="Endpoint"/>: a known endpoint <em>is</em> the game.</summary>
    public const double AutoMergeThreshold = 1.00;

    /// <summary>At or above this, open a review pair. Below it, a new game.</summary>
    public const double ReviewThreshold = 0.45;
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Discovery/DiscoveryOptions.cs src/MUI.Discovery/Identity.cs \
        tests/MUI.Discovery.Tests/DiscoveryOptionsTests.cs
git commit -m "feat: DiscoveryOptions, ActivityBand and the identity weights"
```

---

### Task 3: `ProbeSchedule` — exponential backoff against a permanent ceiling

**Files:**
- Create: `src/MUI.Discovery/ProbeSchedule.cs`
- Test: `tests/MUI.Discovery.Tests/ProbeScheduleTests.cs`

**Interfaces:**
- Consumes: `MUI.Discovery.ActivityBand` (Task 2).
- Produces: `MUI.Discovery.ProbeSchedule` — `static readonly TimeSpan BaseInterval` (6 h),
  `BusyInterval` (2 h), `LongestInterval` (7 d); `const int MaxDoublings`;
  `static TimeSpan Next(int consecutiveFailures, TimeSpan? crawlDelay, ActivityBand activity = ActivityBand.Unknown)`;
  `static DateTimeOffset NextProbeAt(DateTimeOffset now, int consecutiveFailures, TimeSpan? crawlDelay, ActivityBand activity = ActivityBand.Unknown)`.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/ProbeScheduleTests.cs`:

```csharp
using MUI.Discovery;

namespace MUI.Discovery.Tests;

/// <summary>
/// The schedule is pure arithmetic and is tested as such. The one behaviour worth naming twice: it has
/// no "never" (spec §7.4).
/// </summary>
public class ProbeScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AHealthyGameIsProbedAtTheBaseInterval()
    {
        await Assert.That(ProbeSchedule.Next(0, null)).IsEqualTo(TimeSpan.FromHours(6));
    }

    [Test]
    public async Task AGameWithSomebodyOnItIsProbedSooner()
    {
        // §7.7: "tightened for games with recent activity".
        await Assert.That(ProbeSchedule.Next(0, null, ActivityBand.Busy)).IsEqualTo(TimeSpan.FromHours(2));
        await Assert.That(ProbeSchedule.Next(0, null, ActivityBand.Quiet)).IsEqualTo(TimeSpan.FromHours(6));
    }

    [Test]
    [Arguments(1, 6)]
    [Arguments(2, 12)]
    [Arguments(3, 24)]
    [Arguments(4, 48)]
    [Arguments(5, 96)]
    public async Task EachFailureDoublesTheInterval(int failures, int expectedHours)
    {
        await Assert.That(ProbeSchedule.Next(failures, null))
            .IsEqualTo(TimeSpan.FromHours(expectedHours));
    }

    [Test]
    public async Task ActivityDoesNotTightenAFailingGameBecauseThereWasNoActivityToRead()
    {
        await Assert.That(ProbeSchedule.Next(3, null, ActivityBand.Busy)).IsEqualTo(TimeSpan.FromHours(24));
    }

    [Test]
    public async Task TheIntervalIsClampedAtOneWeekAndStaysThereForEver()
    {
        // The whole product differentiator. A game dark for two years is still probed weekly, and after
        // a decade of failures it is still probed weekly — there is no retirement and no "never".
        await Assert.That(ProbeSchedule.Next(6, null)).IsEqualTo(ProbeSchedule.LongestInterval);
        await Assert.That(ProbeSchedule.Next(50, null)).IsEqualTo(ProbeSchedule.LongestInterval);
        await Assert.That(ProbeSchedule.Next(100_000, null)).IsEqualTo(ProbeSchedule.LongestInterval);
        await Assert.That(ProbeSchedule.LongestInterval).IsEqualTo(TimeSpan.FromDays(7));
    }

    [Test]
    public async Task AServerAskingForALongerGapGetsIt()
    {
        // "CRAWL DELAY — preferred minimum number of hours between crawls" (spec §11), honoured as a
        // floor under the interval.
        await Assert.That(ProbeSchedule.Next(0, TimeSpan.FromHours(24))).IsEqualTo(TimeSpan.FromHours(24));
    }

    [Test]
    public async Task AServerAskingForAShorterGapDoesNotGetVisitedMoreOften()
    {
        // It is a minimum a server asks for, not a permission it grants.
        await Assert.That(ProbeSchedule.Next(0, TimeSpan.FromMinutes(5))).IsEqualTo(TimeSpan.FromHours(6));
    }

    [Test]
    public async Task AServerAskingForLongerThanTheCeilingGetsIt()
    {
        // Where §7.4 (a weekly ceiling on the interval) and §11 (CRAWL DELAY is a floor) disagree,
        // politeness wins: the backoff is clamped first and the server's request applied afterwards.
        // §7.4's point is that we never give up, not that we override the operator whose machine we
        // are dialling.
        await Assert.That(ProbeSchedule.Next(9, TimeSpan.FromDays(30))).IsEqualTo(TimeSpan.FromDays(30));
    }

    [Test]
    public async Task NoPreferenceMeansOurOwnInterval()
    {
        // MsspData.CrawlDelay already resolves the MSSP spec's -1 ("use the crawler's default") to null.
        await Assert.That(ProbeSchedule.Next(0, crawlDelay: null)).IsEqualTo(ProbeSchedule.BaseInterval);
    }

    [Test]
    public async Task NextProbeAtIsJustNowPlusTheInterval()
    {
        await Assert.That(ProbeSchedule.NextProbeAt(Now, 2, null))
            .IsEqualTo(Now + TimeSpan.FromHours(12));
    }

    [Test]
    public async Task ANegativeFailureCountIsAProgrammingError()
    {
        await Assert.That(() => ProbeSchedule.Next(-1, null)).Throws<ArgumentOutOfRangeException>();
    }
}
```

- [ ] **Step 2: Run it and verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `ProbeSchedule` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/MUI.Discovery/ProbeSchedule.cs`:

```csharp
namespace MUI.Discovery;

/// <summary>
/// When a target is next due. Pure arithmetic: no clock, no storage, no I/O.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no retirement here, and there must never be.</b> This schedule serves a permanent
/// registry that grows monotonically and outlives every process that touches it, so failure has only
/// one effect: the interval lengthens, and it is clamped. Spec §7.4 is explicit that a game dark for
/// two years is still probed weekly, forever, including after it has been archived — which is exactly
/// what no incumbent managed (spec §3). A bounded single-run frontier can afford to give a host up
/// because its run ends; this one has no such moment, so it has no such verdict.
/// </para>
/// <para>
/// <b>The wording trap.</b> §7.4 calls the clamp a "floor". It means a floor under <em>frequency</em>
/// — "still probed weekly" — which is a <em>ceiling</em> on the interval. Read the other way round it
/// would mean "never probe more often than weekly", which is the opposite behaviour. The constant is
/// therefore called <see cref="LongestInterval"/> and every comparison against it takes the smaller
/// value.
/// </para>
/// <para>
/// <b>Where §7.4 and §11 disagree.</b> A server may ask, through <c>CRAWL DELAY</c>, for a gap longer
/// than a week. §11 says that request is honoured as a floor; §7.4 wants a weekly ceiling. Politeness
/// wins: the backoff is clamped to <see cref="LongestInterval"/> first and the request applied
/// afterwards, so a server asking for thirty days gets thirty days.
/// </para>
/// </remarks>
public static class ProbeSchedule
{
    /// <summary>The gap after a successful probe of a game with nobody on it.</summary>
    public static readonly TimeSpan BaseInterval = TimeSpan.FromHours(6);

    /// <summary>The gap after a successful probe that found players — §7.7's "tightened for recent activity".</summary>
    public static readonly TimeSpan BusyInterval = TimeSpan.FromHours(2);

    /// <summary>
    /// The longest gap the backoff may produce: §7.4's permanent weekly probe, expressed as a ceiling
    /// on the interval rather than a floor under it. See the type's remarks.
    /// </summary>
    public static readonly TimeSpan LongestInterval = TimeSpan.FromDays(7);

    /// <summary>Caps the exponent so a long-dead target cannot overflow the multiplication.</summary>
    public const int MaxDoublings = 20;

    /// <summary>How long from a probe until that target is due again.</summary>
    public static TimeSpan Next(
        int consecutiveFailures,
        TimeSpan? crawlDelay,
        ActivityBand activity = ActivityBand.Unknown)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailures);

        var interval = consecutiveFailures == 0
            ? activity is ActivityBand.Busy ? BusyInterval : BaseInterval
            : BaseInterval * Math.Pow(2, Math.Min(consecutiveFailures - 1, MaxDoublings));

        if (interval > LongestInterval)
        {
            interval = LongestInterval;
        }

        return crawlDelay is { } requested && requested > interval ? requested : interval;
    }

    /// <summary>The instant that gap lands on.</summary>
    public static DateTimeOffset NextProbeAt(
        DateTimeOffset now,
        int consecutiveFailures,
        TimeSpan? crawlDelay,
        ActivityBand activity = ActivityBand.Unknown) =>
        now + Next(consecutiveFailures, crawlDelay, activity);
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS, all twelve.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Discovery/ProbeSchedule.cs tests/MUI.Discovery.Tests/ProbeScheduleTests.cs
git commit -m "feat: ProbeSchedule — exponential backoff against a permanent weekly ceiling"
```

---

### Task 4: `CrawlTarget` and the `crawl_target` migration

**Files:**
- Create: `src/MUI.Discovery/CrawlTarget.cs`
- Create: `src/MUI.Storage/Migrations/0010_crawl_target.sql`
- Modify: `src/MUI.Storage/MUI.Storage.csproj` (only if the migration glob is absent)
- Test: `tests/MUI.Discovery.Tests/CrawlTargetSchemaTests.cs`

**Interfaces:**
- Consumes: `PostgresFixture` (Task 1); Plan 2's `MigrationRunner` and `game` table.
- Produces: `MUI.Discovery.CrawlTarget` exactly as `CONTRACT.md` declares it, and
  `MUI.Discovery.ICrawlTargetRepository` (the interface only; Task 5 implements it).

- [ ] **Step 1: Confirm the migration glob**

Run: `grep -n "Migrations" src/MUI.Storage/MUI.Storage.csproj`
Expected: a line reading `<EmbeddedResource Include="Migrations\**\*.sql" />` or equivalent glob. If
instead the file lists migrations one by one, replace that `<ItemGroup>` with:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Migrations\**\*.sql" />
  </ItemGroup>
```

so a migration added by this plan is picked up without touching the csproj again.

- [ ] **Step 2: Write the failing schema test**

Create `tests/MUI.Discovery.Tests/CrawlTargetSchemaTests.cs`:

```csharp
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// The registry's shape, asserted against the real database — including the column that is
/// deliberately <em>not</em> there.
/// </summary>
[NotInParallel]
public class CrawlTargetSchemaTests
{
    [Test]
    public async Task TheTableExistsWithTheColumnsTheRecordCarries()
    {
        var source = await PostgresFixture.SourceAsync();

        var columns = await PostgresFixture.ScalarAsync<long>(source, """
            SELECT count(*) FROM information_schema.columns
             WHERE table_name = 'crawl_target'
               AND column_name IN ('id', 'game_id', 'host', 'port', 'use_tls', 'next_probe_at',
                                   'consecutive_failures', 'crawl_delay_seconds', 'first_seen_at',
                                   'last_probed_at', 'discovered_from_game_id', 'depth',
                                   'is_operator_seed');
            """);

        await Assert.That(columns).IsEqualTo(13L);
    }

    [Test]
    public async Task ATargetIsGuardedUnlessSomebodySaidOtherwise()
    {
        // is_operator_seed exempts a target from Task 9's resolved-address gate, so its default decides
        // what happens to every row inserted by code that had not thought about it — every referral and
        // every Plan 4 import. The safe value must be the default, not the considered one.
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);

        await using var connection = await source.OpenConnectionAsync();
        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO crawl_target (id, host, port, next_probe_at, first_seen_at)
            VALUES (gen_random_uuid(), 'unconsidered.example.org', 4201, now(), now());
            """;
        await insert.ExecuteNonQueryAsync();

        var seeded = await PostgresFixture.ScalarAsync<bool>(
            source, "SELECT is_operator_seed FROM crawl_target WHERE host = 'unconsidered.example.org'");

        await Assert.That(seeded).IsFalse();
    }

    [Test]
    public async Task ThereIsNoRetiredColumnAndThereNeverWillBe()
    {
        // Spec §7.4. This registry is permanent, so it has no retirement verdict at all: a game that
        // comes back must re-list itself with no human involved, which is the one thing no incumbent
        // managed. If this test ever fails, the fix is to delete the column, not the test.
        var source = await PostgresFixture.SourceAsync();

        var retired = await PostgresFixture.ScalarAsync<long>(source, """
            SELECT count(*) FROM information_schema.columns
             WHERE table_name = 'crawl_target' AND column_name IN ('retired', 'retired_at', 'abandoned');
            """);

        await Assert.That(retired).IsEqualTo(0L);
    }

    [Test]
    public async Task OneAddressIsOneRow()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);

        await using var connection = await source.OpenConnectionAsync();
        await using var first = connection.CreateCommand();
        first.CommandText = """
            INSERT INTO crawl_target (id, host, port, next_probe_at, first_seen_at)
            VALUES (gen_random_uuid(), 'mud.example.org', 4201, now(), now());
            """;
        await first.ExecuteNonQueryAsync();

        await using var duplicate = connection.CreateCommand();
        duplicate.CommandText = first.CommandText;

        await Assert.That(async () => await duplicate.ExecuteNonQueryAsync())
            .Throws<Npgsql.PostgresException>();
    }
}
```

- [ ] **Step 3: Run it and verify it fails**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: FAIL — `relation "crawl_target" does not exist`.

- [ ] **Step 4: Write the migration**

Create `src/MUI.Storage/Migrations/0010_crawl_target.sql`:

```sql
-- Spec §7.1, §7.4. The crawl registry: monotonic, and nothing is ever removed from it.
--
-- The moment a host answers it is promoted to a target with its own independent next_probe_at and is
-- probed for ever after on its own account. Discovery is how a game is found and never how it is
-- scheduled, so the referring game is recorded (discovered_from_game_id) purely so a poisoned source
-- can be traced — losing the referral does not lose the target.
--
-- THERE IS NO `retired` COLUMN, DELIBERATELY. This is a permanent registry, not one run's work queue:
-- §7.4 says a game dark for two years is still probed weekly, for ever, including after archiving,
-- because a returning game re-listing itself with no human involved is the thing every incumbent failed
-- at (§3 — MudStats returned Sept 2024, TMC Jul 2023, and no directory noticed automatically, including
-- their own). Backoff lengthens the interval; it never says "never".

CREATE TABLE crawl_target (
    id                      uuid        PRIMARY KEY,

    -- NULL until the host answered MSSP for itself. A referral is a candidate hostname, not a fact
    -- (§7.2): a referred host is crawled on its own account long before it is listed as a game.
    game_id                 uuid        NULL REFERENCES game (id),

    host                    text        NOT NULL,
    port                    integer     NOT NULL CHECK (port BETWEEN 1 AND 65535),
    use_tls                 boolean     NOT NULL DEFAULT false,

    -- Teeth for MUI.Catalog.HostName.Normalize, exactly as 0005_game_endpoint.sql has it. The UNIQUE
    -- constraint below is only a "one address, one row" guarantee if one host has one spelling:
    -- 'MUD.Example.ORG' and 'mud.example.org' are different strings and would be two targets for one
    -- machine — which is two probes, twice the traffic at somebody else's server, and eventually the
    -- duplicate listing §7.3 exists to prevent. Postgres cannot check the IP-literal half of the rule,
    -- so this covers the two textual rules and the repository covers the rest; a write path that forgets
    -- to normalise now fails here, loudly.
    CONSTRAINT crawl_target_host_is_canonical CHECK (
        host = lower(host) AND host = btrim(host) AND host NOT LIKE '%.'),

    next_probe_at           timestamptz NOT NULL,
    consecutive_failures    integer     NOT NULL DEFAULT 0 CHECK (consecutive_failures >= 0),

    -- The server's own CRAWL DELAY, honoured as a floor under the interval (§11). NULL means it
    -- expressed no preference; MsspData.CrawlDelay already resolves the spec's -1 to null.
    crawl_delay_seconds     bigint      NULL CHECK (crawl_delay_seconds IS NULL OR crawl_delay_seconds >= 0),

    first_seen_at           timestamptz NOT NULL,
    last_probed_at          timestamptz NULL,
    discovered_from_game_id uuid        NULL REFERENCES game (id),
    depth                   integer     NOT NULL DEFAULT 0 CHECK (depth >= 0),

    -- Exempts this target from the resolved-address gate (§7.2, Plan 03 Task 9). True only for an
    -- address a human operator configured: someone pointing the crawler at 127.0.0.1 to test against
    -- their own server has said what they mean. DEFAULT false is load-bearing — every referral and
    -- every imported row is inserted by code that never considers this column, and the value they get
    -- by not thinking about it must be the guarded one.
    is_operator_seed        boolean     NOT NULL DEFAULT false,

    CONSTRAINT crawl_target_address_unique UNIQUE (host, port)
);

-- The scheduler's only query: what is due now, shallowest and oldest-known first.
CREATE INDEX crawl_target_due_idx ON crawl_target (next_probe_at, depth, first_seen_at);
CREATE INDEX crawl_target_game_idx ON crawl_target (game_id) WHERE game_id IS NOT NULL;
```

- [ ] **Step 5: Write the record and the repository interface**

Create `src/MUI.Discovery/CrawlTarget.cs`:

```csharp
namespace MUI.Discovery;

/// <summary>
/// One address the crawler probes, on its own schedule, for ever.
/// </summary>
/// <remarks>
/// <para>
/// Spec §7.1: "the moment a host answers, it is promoted to a <c>CrawlTarget</c> with its own
/// independent <c>next_probe_at</c> and is probed forever after on its own account". The referral that
/// found it is provenance, not a dependency — <see cref="DiscoveredFromGameId"/> exists so a hostile
/// or careless <c>REFERRAL</c> list can be traced and its whole subtree pruned (§7.2), and a target
/// whose referring game disappears is still due on schedule.
/// </para>
/// <para>
/// There is no "retired" flag. See <see cref="ProbeSchedule"/>.
/// </para>
/// </remarks>
public sealed record CrawlTarget
{
    public required Guid Id { get; init; }

    /// <summary>Null until the host answered for itself (spec §7.2).</summary>
    public Guid? GameId { get; init; }

    public required string Host { get; init; }

    public required int Port { get; init; }

    public bool UseTls { get; init; }

    public required DateTimeOffset NextProbeAt { get; init; }

    public int ConsecutiveFailures { get; init; }

    /// <summary>The server's own request, honoured as a floor under the interval (spec §11).</summary>
    public TimeSpan? CrawlDelay { get; init; }

    public required DateTimeOffset FirstSeenAt { get; init; }

    public DateTimeOffset? LastProbedAt { get; init; }

    public Guid? DiscoveredFromGameId { get; init; }

    public int Depth { get; init; }

    /// <summary>
    /// Exempts this target from <see cref="HostScopeGuard"/>'s resolved-address check (spec §7.2).
    /// </summary>
    /// <remarks>
    /// True only for an address a human operator configured. Someone pointing the crawler at
    /// <c>127.0.0.1</c> to test against their own server has said what they mean; a stranger's
    /// <c>REFERRAL</c> list has not. <b>The default is false, and that is the security-relevant half of
    /// this property</b> — every referral and every Plan 4 import is constructed by code that never
    /// mentions it, and what they get by not thinking about it must be the guarded behaviour.
    /// </remarks>
    public bool IsOperatorSeed { get; init; }
}

/// <summary>
/// The registry. Monotonic by construction: there is no delete, and no method that can stop a target
/// being probed (spec §7.1, §7.4).
/// </summary>
public interface ICrawlTargetRepository
{
    Task<CrawlTarget?> ByAddressAsync(string host, int port, CancellationToken ct);

    /// <summary>
    /// Adds a target, or returns the existing one's id. Adding a known address never resets its
    /// schedule and never resurfaces it — the only thing a repeat sighting may change is the recorded
    /// depth, and only downward.
    /// </summary>
    Task<Guid> AddAsync(CrawlTarget target, CancellationToken ct);

    Task<IReadOnlyList<CrawlTarget>> DueAsync(DateTimeOffset now, int limit, CancellationToken ct);

    Task RecordAttemptAsync(
        Guid id,
        DateTimeOffset at,
        bool succeeded,
        TimeSpan? crawlDelay,
        DateTimeOffset nextProbeAt,
        CancellationToken ct);

    Task AttachGameAsync(Guid id, Guid gameId, CancellationToken ct);
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Discovery/CrawlTarget.cs src/MUI.Storage/Migrations/0010_crawl_target.sql \
        src/MUI.Storage/MUI.Storage.csproj tests/MUI.Discovery.Tests/CrawlTargetSchemaTests.cs
git commit -m "feat: the crawl_target registry — monotonic, with no retirement by construction"
```

---

### Task 5: `NpgsqlCrawlTargetRepository` — and the target that outlives its referrer

**Files:**
- Create: `src/MUI.Discovery/Storage/NpgsqlCrawlTargetRepository.cs`
- Test: `tests/MUI.Discovery.Tests/CrawlTargetRepositoryTests.cs`
- **Modify, not create:** `tests/MUI.Discovery.Tests/HostNormalisationAgreementTests.cs` — Plan 02
  Task 10 declares the class; Step 5 adds one method to it. One test assembly, so a second declaration
  is CS0101.

**Interfaces:**
- Consumes: `ICrawlTargetRepository`, `CrawlTarget` (Task 4); `ProbeSchedule` (Task 3);
  `PostgresFixture` (Task 1); `MUI.Catalog.HostName.Normalize` (Plan 02 Task 10).
- Produces: `MUI.Discovery.Storage.NpgsqlCrawlTargetRepository(NpgsqlDataSource source) : ICrawlTargetRepository`.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/CrawlTargetRepositoryTests.cs`:

```csharp
using MUI.Discovery;
using MUI.Discovery.Storage;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// The registry against a real database. The load-bearing test is the last one: a target whose
/// referring game vanishes is still due on schedule (spec §7.1).
/// </summary>
[NotInParallel]
public class CrawlTargetRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private static CrawlTarget Target(string host, int port = 4201, Guid? from = null, int depth = 0) => new()
    {
        Id = Guid.CreateVersion7(),
        Host = host,
        Port = port,
        NextProbeAt = Now,
        FirstSeenAt = Now,
        DiscoveredFromGameId = from,
        Depth = depth,
    };

    private static async Task<NpgsqlCrawlTargetRepository> RepositoryAsync()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        return new NpgsqlCrawlTargetRepository(source);
    }

    [Test]
    public async Task ATargetIsStoredAndReadBackWholeIncludingItsCrawlDelay()
    {
        var repository = await RepositoryAsync();
        var target = Target("mud.example.org") with { CrawlDelay = TimeSpan.FromHours(23), UseTls = true, Depth = 2 };

        await repository.AddAsync(target, None);
        var found = await repository.ByAddressAsync("mud.example.org", 4201, None);

        await Assert.That(found).IsNotNull();
        await Assert.That(found!.CrawlDelay).IsEqualTo(TimeSpan.FromHours(23));
        await Assert.That(found.UseTls).IsTrue();
        await Assert.That(found.Depth).IsEqualTo(2);
        await Assert.That(found.GameId).IsNull();
        await Assert.That(found.NextProbeAt).IsEqualTo(Now);
    }

    [Test]
    public async Task AddingAKnownAddressReturnsTheExistingIdAndDoesNotResetItsSchedule()
    {
        // Monotonic: a second referral to a host we already back off from must not drag it forward.
        var repository = await RepositoryAsync();
        var first = Target("mud.example.org");
        var id = await repository.AddAsync(first, None);

        await repository.RecordAttemptAsync(id, Now, succeeded: false, null, Now.AddDays(3), None);

        var again = await repository.AddAsync(Target("mud.example.org") with { NextProbeAt = Now }, None);

        await Assert.That(again).IsEqualTo(id);
        var found = await repository.ByAddressAsync("mud.example.org", 4201, None);
        await Assert.That(found!.NextProbeAt).IsEqualTo(Now.AddDays(3));
        await Assert.That(found.ConsecutiveFailures).IsEqualTo(1);
    }

    [Test]
    public async Task HoweverAReferralSpellsAHostItIsTheSameTarget()
    {
        // One host, one spelling — the same rule Plan 02 applies to game_endpoint, adopted here because
        // `MUD.Example.ORG` and `mud.example.org.` are different strings and would otherwise be two
        // targets for one machine: two probes, twice the traffic at one operator's server, and
        // eventually the duplicate listing §7.3 exists to prevent.
        var repository = await RepositoryAsync();
        var id = await repository.AddAsync(Target("mud.example.org"), None);

        foreach (var spelling in new[] { "MUD.Example.ORG", "mud.example.org.", "  MUD.EXAMPLE.ORG.  " })
        {
            var found = await repository.ByAddressAsync(spelling, 4201, None);

            await Assert.That(found).IsNotNull();
            await Assert.That(found!.Id).IsEqualTo(id);
            await Assert.That(found.Host).IsEqualTo("mud.example.org");

            // And adding it again under that spelling is the same row, not a second one.
            await Assert.That(await repository.AddAsync(Target(spelling), None)).IsEqualTo(id);
        }

        var rows = await PostgresFixture.ScalarAsync<long>(
            await PostgresFixture.SourceAsync(), "SELECT count(*) FROM crawl_target");
        await Assert.That(rows).IsEqualTo(1L);
    }

    [Test]
    public async Task AHostReachedAgainByAShorterPathKeepsTheShorterDepth()
    {
        // Depth is what the fan-out cap is measured against, so a host first met at the limit would
        // otherwise never have its own referrals followed.
        var repository = await RepositoryAsync();
        await repository.AddAsync(Target("far.example.org", depth: 4), None);
        await repository.AddAsync(Target("far.example.org", depth: 1), None);

        var found = await repository.ByAddressAsync("far.example.org", 4201, None);
        await Assert.That(found!.Depth).IsEqualTo(1);

        await repository.AddAsync(Target("far.example.org", depth: 3), None);
        found = await repository.ByAddressAsync("far.example.org", 4201, None);
        await Assert.That(found!.Depth).IsEqualTo(1);
    }

    [Test]
    public async Task OnlyDueTargetsComeBackAndTheOldestIsFirst()
    {
        var repository = await RepositoryAsync();
        await repository.AddAsync(Target("a.example.org") with { NextProbeAt = Now.AddMinutes(-10) }, None);
        await repository.AddAsync(Target("b.example.org") with { NextProbeAt = Now.AddMinutes(-30) }, None);
        await repository.AddAsync(Target("c.example.org") with { NextProbeAt = Now.AddHours(6) }, None);

        var due = await repository.DueAsync(Now, 10, None);

        await Assert.That(due.Select(t => t.Host)).IsEquivalentTo(new[] { "b.example.org", "a.example.org" });
        await Assert.That(due[0].Host).IsEqualTo("b.example.org");
    }

    [Test]
    public async Task ASuccessClearsTheFailureCountAndAFailureRaisesIt()
    {
        var repository = await RepositoryAsync();
        var id = await repository.AddAsync(Target("a.example.org"), None);

        await repository.RecordAttemptAsync(id, Now, succeeded: false, null, Now.AddHours(6), None);
        await repository.RecordAttemptAsync(id, Now.AddHours(6), succeeded: false, null, Now.AddHours(18), None);
        var after = await repository.ByAddressAsync("a.example.org", 4201, None);
        await Assert.That(after!.ConsecutiveFailures).IsEqualTo(2);

        await repository.RecordAttemptAsync(id, Now.AddHours(18), succeeded: true, TimeSpan.FromHours(4), Now.AddDays(1), None);
        after = await repository.ByAddressAsync("a.example.org", 4201, None);

        await Assert.That(after!.ConsecutiveFailures).IsEqualTo(0);
        await Assert.That(after.CrawlDelay).IsEqualTo(TimeSpan.FromHours(4));
        await Assert.That(after.LastProbedAt).IsEqualTo(Now.AddHours(18));
    }

    [Test]
    public async Task AProbeThatLearnedNoCrawlDelayLeavesTheRememberedOneAlone()
    {
        // A failed probe tells us nothing about the server's preference. Overwriting it with null would
        // silently make us more aggressive toward a server that had asked us not to be.
        var repository = await RepositoryAsync();
        var id = await repository.AddAsync(Target("a.example.org") with { CrawlDelay = TimeSpan.FromHours(12) }, None);

        await repository.RecordAttemptAsync(id, Now, succeeded: false, null, Now.AddHours(6), None);

        var after = await repository.ByAddressAsync("a.example.org", 4201, None);
        await Assert.That(after!.CrawlDelay).IsEqualTo(TimeSpan.FromHours(12));
    }

    [Test]
    public async Task ATargetWhoseReferringGameVanishesIsStillDueOnSchedule()
    {
        // Spec §7.1. Discovery is how a game is found and never how it is scheduled: the referring
        // game is provenance. Deleting it must not take the target with it, and must not stop it being
        // probed — a returning game has to re-list itself with no human involved.
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var repository = new NpgsqlCrawlTargetRepository(source);

        var referrer = await PostgresFixture.InsertGameAsync(source, "Referrer");
        await repository.AddAsync(Target("orphan.example.org", from: referrer) with { NextProbeAt = Now.AddMinutes(-1) }, None);

        await using var connection = await source.OpenConnectionAsync();
        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM game WHERE id = @id";
        delete.Parameters.AddWithValue("id", referrer);
        await delete.ExecuteNonQueryAsync();

        var due = await repository.DueAsync(Now, 10, None);

        await Assert.That(due.Select(t => t.Host)).Contains("orphan.example.org");
        await Assert.That(due.Single().DiscoveredFromGameId).IsNull();
    }

    [Test]
    public async Task AGameIsAttachedOnceAndNeverRepointed()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var repository = new NpgsqlCrawlTargetRepository(source);

        var first = await PostgresFixture.InsertGameAsync(source, "First");
        var second = await PostgresFixture.InsertGameAsync(source, "Second");
        var id = await repository.AddAsync(Target("a.example.org"), None);

        await repository.AttachGameAsync(id, first, None);
        await repository.AttachGameAsync(id, second, None);

        var found = await repository.ByAddressAsync("a.example.org", 4201, None);
        await Assert.That(found!.GameId).IsEqualTo(first);
    }
}
```

- [ ] **Step 2: Run it and verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `NpgsqlCrawlTargetRepository` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/MUI.Discovery/Storage/NpgsqlCrawlTargetRepository.cs`:

```csharp
using Dapper;
using MUI.Catalog;
using Npgsql;

namespace MUI.Discovery.Storage;

/// <summary>
/// The registry over Postgres. Column names are aliased explicitly in every projection rather than
/// relying on Dapper's underscore matching, which is a global static somebody else's plan may or may
/// not have switched on.
/// </summary>
/// <remarks>
/// This lives in <c>MUI.Discovery</c> rather than <c>MUI.Storage</c> because
/// <see cref="ICrawlTargetRepository"/> lives here and <c>MUI.Storage</c> may not reference this
/// project. The migration is still a <c>MUI.Storage</c> file, so one runner applies every table.
/// </remarks>
public sealed class NpgsqlCrawlTargetRepository(NpgsqlDataSource source) : ICrawlTargetRepository
{
    private const string Projection = """
        SELECT id                      AS "Id",
               game_id                 AS "GameId",
               host                    AS "Host",
               port                    AS "Port",
               use_tls                 AS "UseTls",
               next_probe_at           AS "NextProbeAt",
               consecutive_failures    AS "ConsecutiveFailures",
               crawl_delay_seconds     AS "CrawlDelaySeconds",
               first_seen_at           AS "FirstSeenAt",
               last_probed_at          AS "LastProbedAt",
               discovered_from_game_id AS "DiscoveredFromGameId",
               depth                   AS "Depth",
               is_operator_seed        AS "IsOperatorSeed"
          FROM crawl_target
        """;

    public async Task<CrawlTarget?> ByAddressAsync(string host, int port, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // Canonical form on the way in, then plain ordinal equality — not a lenient comparison. The
        // column is guaranteed canonical by the CHECK constraint, so `host = @host` uses the unique
        // index and there is nothing left for a lower(host) to fold. Same rule as Plan 02 applied to
        // game_endpoint; the fake applies it identically, because a fake kinder than the database hides
        // a duplicate row instead of failing a test.
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"{Projection} WHERE host = @host AND port = @port;",
            new { host = HostName.Normalize(host), port },
            cancellationToken: ct));

        return row?.ToTarget();
    }

    public async Task<Guid> AddAsync(CrawlTarget target, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // Monotonic. A repeat sighting of a known address changes exactly one thing — the recorded
        // depth, and only downward — because depth is what the fan-out cap is measured against. It
        // never touches next_probe_at, so a second referral cannot drag a backed-off host forward.
        //
        // It also never touches is_operator_seed, which matters in one direction: a referral naming an
        // address cannot promote it to an exempt seed. The cost is that an operator seeding an address a
        // referral already produced does not retroactively exempt it — the row stays guarded, and if
        // that is genuinely wanted it is an UPDATE somebody writes deliberately. Failing that way round
        // is the correct way round.
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO crawl_target (id, game_id, host, port, use_tls, next_probe_at,
                                      consecutive_failures, crawl_delay_seconds, first_seen_at,
                                      last_probed_at, discovered_from_game_id, depth,
                                      is_operator_seed)
            VALUES (@Id, @GameId, @Host, @Port, @UseTls, @NextProbeAt,
                    @ConsecutiveFailures, @CrawlDelaySeconds, @FirstSeenAt,
                    @LastProbedAt, @DiscoveredFromGameId, @Depth,
                    @IsOperatorSeed)
            ON CONFLICT (host, port) DO UPDATE
               SET depth = LEAST(crawl_target.depth, EXCLUDED.depth)
            RETURNING id;
            """,
            new
            {
                target.Id,
                target.GameId,
                // The one place a host becomes canonical on the way into the registry. The CHECK
                // constraint would reject anything else, which is the point: this is not a courtesy.
                Host = HostName.Normalize(target.Host),
                target.Port,
                target.UseTls,
                target.NextProbeAt,
                target.ConsecutiveFailures,
                CrawlDelaySeconds = (long?)target.CrawlDelay?.TotalSeconds,
                target.FirstSeenAt,
                target.LastProbedAt,
                target.DiscoveredFromGameId,
                target.Depth,
                target.IsOperatorSeed,
            },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CrawlTarget>> DueAsync(DateTimeOffset now, int limit, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"""
             {Projection}
              WHERE next_probe_at <= @now
              ORDER BY next_probe_at, depth, first_seen_at
              LIMIT @limit;
             """,
            new { now, limit },
            cancellationToken: ct));

        return rows.Select(row => row.ToTarget()).ToList();
    }

    public async Task RecordAttemptAsync(
        Guid id,
        DateTimeOffset at,
        bool succeeded,
        TimeSpan? crawlDelay,
        DateTimeOffset nextProbeAt,
        CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // COALESCE, not assignment: a failed probe learned nothing about the server's CRAWL DELAY, and
        // forgetting it would silently make us more aggressive toward a server that asked us not to be.
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE crawl_target
               SET last_probed_at       = @at,
                   consecutive_failures = CASE WHEN @succeeded THEN 0 ELSE consecutive_failures + 1 END,
                   crawl_delay_seconds  = COALESCE(@crawlDelaySeconds, crawl_delay_seconds),
                   next_probe_at        = @nextProbeAt
             WHERE id = @id;
            """,
            new { id, at, succeeded, crawlDelaySeconds = (long?)crawlDelay?.TotalSeconds, nextProbeAt },
            cancellationToken: ct));
    }

    public async Task AttachGameAsync(Guid id, Guid gameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // Attach once. Re-pointing an address at a different game is what a merge does, and a merge is
        // a redirect on the game row (see NpgsqlMergeLog) so that it stays reversible.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE crawl_target SET game_id = @gameId WHERE id = @id AND game_id IS NULL;",
            new { id, gameId },
            cancellationToken: ct));
    }

    private sealed record Row(
        Guid Id,
        Guid? GameId,
        string Host,
        int Port,
        bool UseTls,
        DateTimeOffset NextProbeAt,
        int ConsecutiveFailures,
        long? CrawlDelaySeconds,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset? LastProbedAt,
        Guid? DiscoveredFromGameId,
        int Depth,
        bool IsOperatorSeed)
    {
        public CrawlTarget ToTarget() => new()
        {
            Id = Id,
            GameId = GameId,
            Host = Host,
            Port = Port,
            UseTls = UseTls,
            NextProbeAt = NextProbeAt,
            ConsecutiveFailures = ConsecutiveFailures,
            CrawlDelay = CrawlDelaySeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            FirstSeenAt = FirstSeenAt,
            LastProbedAt = LastProbedAt,
            DiscoveredFromGameId = DiscoveredFromGameId,
            Depth = Depth,
            IsOperatorSeed = IsOperatorSeed,
        };
    }
}
```

- [ ] **Step 4: Make the orphan test pass**

`ATargetWhoseReferringGameVanishesIsStillDueOnSchedule` deletes the referring game, so the foreign key
must not cascade the target away and must not block the delete. Edit
`src/MUI.Storage/Migrations/0010_crawl_target.sql`, changing the `discovered_from_game_id` line to:

```sql
    -- ON DELETE SET NULL, deliberately. The referral is provenance; the target is scheduled on its own
    -- account (§7.1). Cascading would delete a live crawl target because the game that once mentioned
    -- it went away, which is the exact coupling this design forbids.
    discovered_from_game_id uuid        NULL REFERENCES game (id) ON DELETE SET NULL,
```

and the `game_id` line to:

```sql
    game_id                 uuid        NULL REFERENCES game (id) ON DELETE SET NULL,
```

- [ ] **Step 5: Pin the two host rules against each other**

There are now two implementations of "one host, one spelling" in this solution:
`MUI.Catalog.HostName.Normalize`, which the catalogue and this registry use, and
`MUI.Crawl.Mssp.MsspHost`'s own normalisation, which the referral reader uses on the way in. They must
agree, or a referral produces one spelling and a lookup asks for another.

**Extend, do not create.** `tests/MUI.Discovery.Tests/HostNormalisationAgreementTests.cs` already
exists — Plan 02 Task 10 creates it, with the class comment `HostName`'s own doc comment promises and a
`TheTwoImplementationsAgreeOnEveryHostEitherWillSee` case. Plan 02 lands first, this is one test
assembly, and a second `public class HostNormalisationAgreementTests` is a CS0101 build failure rather
than a merge conflict. **Add this method to the existing class** and leave the class declaration, the
usings and Plan 02's case alone:

```csharp
    /// <summary>
    /// The same agreement, asserted case by case so a failure names the spelling that broke it.
    /// </summary>
    /// <remarks>
    /// <b>Do not "simplify" this away as testing somebody else's code, and do not fold it into the
    /// case above to save a method.</b> Two implementations of one rule is a real cost, accepted only
    /// because <c>MUI.Catalog</c> may never reference <c>MUI.Crawl</c> — so neither can call the other,
    /// and there is no compiler check that they still match. <c>MUI.Discovery.Tests</c> is the only
    /// project that can see both, which makes this the only thing keeping them honest. Where they
    /// diverge, a <c>REFERRAL</c> writes a target under one spelling and every later lookup asks for
    /// another, and the registry quietly grows two rows per machine — which is two probes at somebody
    /// else's server and eventually the duplicate listing §7.3 exists to prevent. This plan added
    /// <c>crawl_target.host</c> to the set of columns that rule governs, which is why it is restated
    /// here and not left as Plan 02's private business.
    /// </remarks>
    [Test]
    [Arguments("MUD.Example.ORG")]
    [Arguments("mud.example.org.")]
    [Arguments("  MUD.EXAMPLE.ORG.  ")]
    [Arguments("203.0.113.10")]
    [Arguments("2001:0DB8:0000:0000:0000:0000:0000:0001")]
    [Arguments("2001:DB8::1")]
    [Arguments("[2001:db8::1]")]
    public async Task TheCatalogueAndTheMsspReaderCanonicaliseAHostTheSameWay(string spelling)
    {
        var viaMssp = MsspHost.Create(spelling, 4201);

        await Assert.That(viaMssp).IsNotNull();
        await Assert.That(HostName.Normalize(spelling)).IsEqualTo(viaMssp!.Host);
    }
```

Both usings this needs (`MUI.Catalog`, `MUI.Crawl.Mssp`) are already at the top of Plan 02's file. If
Plan 02 landed without the file, create it with the class comment from Plan 02 Task 10 Step 6 and this
method in it — do not invent a second class name to dodge the collision.

- [ ] **Step 6: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS — nine `CrawlTargetRepositoryTests`, and `HostNormalisationAgreementTests` now reporting
Plan 02's case plus this method's seven.

If an agreement case fails, **fix whichever normaliser is wrong rather than relaxing the test**, and
fix it in its own plan's suite: the IPv6 cases are the ones most likely to diverge, because zero-run
compression has more than one defensible spelling and only one of them can be canonical.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Discovery/Storage/NpgsqlCrawlTargetRepository.cs \
        src/MUI.Storage/Migrations/0010_crawl_target.sql \
        tests/MUI.Discovery.Tests/CrawlTargetRepositoryTests.cs \
        tests/MUI.Discovery.Tests/HostNormalisationAgreementTests.cs
git commit -m "feat: NpgsqlCrawlTargetRepository — a target outlives the referral that found it"
```

---

### Task 6: `BannerFingerprint`

**Files:**
- Create: `src/MUI.Discovery/BannerFingerprint.cs`
- Test: `tests/MUI.Discovery.Tests/BannerFingerprintTests.cs`

**Interfaces:**
- Produces: `MUI.Discovery.BannerFingerprint` — `static string Of(string banner)`, a lower-case hex
  SHA-256 over the ANSI-stripped, whitespace-collapsed banner.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/BannerFingerprintTests.cs`:

```csharp
using MUI.Discovery;

namespace MUI.Discovery.Tests;

/// <summary>
/// The connect-screen fingerprint: stable enough to survive a host move, sensitive enough to change on
/// a redesign (spec §6.2, §7.3).
/// </summary>
public class BannerFingerprintTests
{
    [Test]
    public async Task TheSameScreenHashesTheSameWayTwice()
    {
        const string banner = "Welcome to Corvid.\r\nType 'connect <name> <password>'.\r\n";

        await Assert.That(BannerFingerprint.Of(banner)).IsEqualTo(BannerFingerprint.Of(banner));
        await Assert.That(BannerFingerprint.Of(banner).Length).IsEqualTo(64);
    }

    [Test]
    public async Task ColourChangesDoNotChangeTheFingerprint()
    {
        // The reason it is ANSI-stripped: a game that recolours its login screen has not become a
        // different game, and re-theming is common.
        var plain = BannerFingerprint.Of("Welcome to Corvid.\nType 'connect'.");
        var coloured = BannerFingerprint.Of("\e[1;36mWelcome to Corvid.\e[0m\nType 'connect'.");

        await Assert.That(coloured).IsEqualTo(plain);
    }

    [Test]
    public async Task LineEndingsAndRunsOfSpacesDoNotChangeTheFingerprint()
    {
        // The reason it is whitespace-collapsed: CRLF versus LF is a transport accident, and box-drawn
        // banners get re-padded when somebody edits one line.
        var unix = BannerFingerprint.Of("Welcome to Corvid.\nType 'connect'.");
        var dos = BannerFingerprint.Of("  Welcome   to  Corvid.\r\n\r\nType 'connect'.  \r\n");

        await Assert.That(dos).IsEqualTo(unix);
    }

    [Test]
    public async Task ADifferentScreenHashesDifferently()
    {
        var corvid = BannerFingerprint.Of("Welcome to Corvid.");
        var magpie = BannerFingerprint.Of("Welcome to Magpie.");

        await Assert.That(magpie).IsNotEqualTo(corvid);
    }

    [Test]
    public async Task AnEmptyOrWhitespaceBannerStillHashesRatherThanThrowing()
    {
        // Plenty of servers send nothing before the first prompt. That is a fact about them, not an
        // error, and it must not take a probe down.
        await Assert.That(BannerFingerprint.Of("")).IsEqualTo(BannerFingerprint.Of("   \r\n  "));
    }

    [Test]
    public async Task OtherControlSequencesAreStrippedToo()
    {
        // OSC title-setting and cursor moves show up in real connect screens; neither is content.
        var plain = BannerFingerprint.Of("Corvid");
        var noisy = BannerFingerprint.Of("\e]0;Corvid\a\e[2J\e[HCorvid");

        await Assert.That(noisy).IsEqualTo(plain);
    }
}
```

- [ ] **Step 2: Run it and verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `BannerFingerprint` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/MUI.Discovery/BannerFingerprint.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace MUI.Discovery;

/// <summary>
/// A stable fingerprint of a connect screen, for the identity matcher's banner signal (spec §7.3).
/// </summary>
/// <remarks>
/// ANSI-stripped and whitespace-collapsed on purpose. A game that recolours its login screen or whose
/// server switched CRLF for LF has not become a different game; a game that rewrote its welcome text
/// has changed something worth noticing. Everything the escape sequences carry is presentation, and
/// the whole point of the signal is that it "survives host moves; changes on redesign".
/// </remarks>
public static class BannerFingerprint
{
    /// <summary>Lower-case hex SHA-256 over the normalised text. Never throws.</summary>
    public static string Of(string banner)
    {
        ArgumentNullException.ThrowIfNull(banner);

        var normalised = Normalise(banner);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
    }

    private static string Normalise(string banner)
    {
        var text = new StringBuilder(banner.Length);
        var pendingSpace = false;

        for (var i = 0; i < banner.Length; i++)
        {
            var ch = banner[i];

            if (ch == '\e')
            {
                i = SkipEscape(banner, i);
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = text.Length > 0;
                continue;
            }

            if (char.IsControl(ch))
            {
                continue;
            }

            if (pendingSpace)
            {
                text.Append(' ');
                pendingSpace = false;
            }

            text.Append(ch);
        }

        return text.ToString();
    }

    /// <summary>Returns the index of the last character of the escape sequence starting at <paramref name="start"/>.</summary>
    private static int SkipEscape(string banner, int start)
    {
        var i = start + 1;
        if (i >= banner.Length)
        {
            return start;
        }

        // CSI: ESC [ … final byte in 0x40–0x7E.
        if (banner[i] == '[')
        {
            for (i++; i < banner.Length; i++)
            {
                if (banner[i] is >= '@' and <= '~')
                {
                    return i;
                }
            }

            return banner.Length - 1;
        }

        // OSC: ESC ] … BEL, or ESC ] … ESC \.
        if (banner[i] == ']')
        {
            for (i++; i < banner.Length; i++)
            {
                if (banner[i] == '\a')
                {
                    return i;
                }

                if (banner[i] == '\e' && i + 1 < banner.Length && banner[i + 1] == '\\')
                {
                    return i + 1;
                }
            }

            return banner.Length - 1;
        }

        // Anything else two-byte: ESC 7, ESC =, ESC ( B and friends.
        return i;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS, all six.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Discovery/BannerFingerprint.cs tests/MUI.Discovery.Tests/BannerFingerprintTests.cs
git commit -m "feat: BannerFingerprint — survives a host move, changes on a redesign"
```

---

### Task 7: `referral_edge` and `NpgsqlReferralRepository`, including subtree tracing

**Files:**
- Create: `src/MUI.Storage/Migrations/0011_referral_edge.sql`
- Create: `src/MUI.Discovery/ReferralGraphWriter.cs` (verdict types and the interface only in this task)
- Create: `src/MUI.Discovery/Storage/NpgsqlReferralRepository.cs`
- Test: `tests/MUI.Discovery.Tests/ReferralRepositoryTests.cs`

**Interfaces:**
- Consumes: the **existing** `MUI.Discovery.ReferralEdge` record — `(Guid FromGameId, string ToHost,
  int ToPort, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, bool Present)`. **Never redefine
  it and never add a member to it.** Also `crawl_target` (Task 4) and `PostgresFixture` (Task 1).
- Produces: `MUI.Discovery.ReferralVerdict` (`Added`, `AlreadyKnown`, `SelfReferral`, `TooDeep`,
  `NotRoutable`, `ReferralsDisabled`, `FanOutExceeded`), `MUI.Discovery.ReferralIntake(int Added,
  IReadOnlyDictionary<ReferralVerdict, int> Verdicts)`, `MUI.Discovery.IReferralRepository`, and
  `MUI.Discovery.Storage.NpgsqlReferralRepository(NpgsqlDataSource source) : IReferralRepository`.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/ReferralRepositoryTests.cs`:

```csharp
using MUI.Discovery;
using MUI.Discovery.Storage;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// Edges are provenance (spec §7.1). Storage therefore has exactly two jobs: never lose one, and be
/// able to walk from a source to everything it ever pointed at, so a poisoned list can be traced and
/// pruned wholesale (spec §7.2).
/// </summary>
[NotInParallel]
public class ReferralRepositoryTests
{
    private static readonly DateTimeOffset Then = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task AnEdgeIsUpsertedWithoutLosingWhenItWasFirstSeen()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var repository = new NpgsqlReferralRepository(source);
        var game = await PostgresFixture.InsertGameAsync(source, "Corvid");

        await repository.UpsertAsync(new ReferralEdge(game, "b.example.org", 4201, Then, Then, Present: true), None);
        await repository.UpsertAsync(
            new ReferralEdge(game, "b.example.org", 4201, Then.AddDays(30), Then.AddDays(30), Present: true), None);

        var edges = await repository.FromGameAsync(game, None);

        await Assert.That(edges.Count).IsEqualTo(1);
        await Assert.That(edges[0].FirstSeenAt).IsEqualTo(Then);
        await Assert.That(edges[0].LastSeenAt).IsEqualTo(Then.AddDays(30));
    }

    [Test]
    public async Task AnEdgeThatDisappearsIsMarkedAbsentAndKeptWhileTheOthersAreUntouched()
    {
        // "An edge disappearing updates present and nothing else" — §7.1. Nothing is deleted, and the
        // dates are left exactly as they were: last_seen_at means "last seen", not "last looked".
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var repository = new NpgsqlReferralRepository(source);
        var game = await PostgresFixture.InsertGameAsync(source, "Corvid");

        await repository.UpsertAsync(new ReferralEdge(game, "b.example.org", 4201, Then, Then, true), None);
        await repository.UpsertAsync(new ReferralEdge(game, "c.example.org", 4201, Then, Then, true), None);

        await repository.MarkAbsentAsync(game, [("c.example.org", 4201)], Then.AddDays(1), None);

        var edges = (await repository.FromGameAsync(game, None)).ToDictionary(e => e.ToHost);

        await Assert.That(edges.Count).IsEqualTo(2);
        await Assert.That(edges["b.example.org"].Present).IsFalse();
        await Assert.That(edges["b.example.org"].LastSeenAt).IsEqualTo(Then);
        await Assert.That(edges["c.example.org"].Present).IsTrue();
    }

    [Test]
    public async Task MarkingAbsentTouchesNobodyElsesEdges()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var repository = new NpgsqlReferralRepository(source);
        var mine = await PostgresFixture.InsertGameAsync(source, "Corvid");
        var theirs = await PostgresFixture.InsertGameAsync(source, "Magpie");

        await repository.UpsertAsync(new ReferralEdge(mine, "b.example.org", 4201, Then, Then, true), None);
        await repository.UpsertAsync(new ReferralEdge(theirs, "b.example.org", 4201, Then, Then, true), None);

        await repository.MarkAbsentAsync(mine, [], Then, None);

        await Assert.That((await repository.FromGameAsync(theirs, None)).Single().Present).IsTrue();
    }

    [Test]
    public async Task ThePoisonedSubtreeCanBeTracedWholesale()
    {
        // §7.2: "the referring game is recorded on the discovered entry so that a hostile or careless
        // REFERRAL list can be traced and its whole subtree pruned". A → B → C, walked through the
        // crawl targets the referred hosts became.
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var repository = new NpgsqlReferralRepository(source);
        var targets = new NpgsqlCrawlTargetRepository(source);

        var a = await PostgresFixture.InsertGameAsync(source, "Alpha");
        var b = await PostgresFixture.InsertGameAsync(source, "Bravo");
        var c = await PostgresFixture.InsertGameAsync(source, "Charlie");
        var unrelated = await PostgresFixture.InsertGameAsync(source, "Delta");

        await AttachAsync(targets, "b.example.org", b);
        await AttachAsync(targets, "c.example.org", c);
        await AttachAsync(targets, "d.example.org", unrelated);

        await repository.UpsertAsync(new ReferralEdge(a, "b.example.org", 4201, Then, Then, true), None);
        await repository.UpsertAsync(new ReferralEdge(b, "c.example.org", 4201, Then, Then, true), None);
        await repository.UpsertAsync(new ReferralEdge(unrelated, "d.example.org", 4201, Then, Then, true), None);

        var subtree = await repository.SubtreeOfAsync(a, None);

        await Assert.That(subtree.Select(e => e.ToHost))
            .IsEquivalentTo(new[] { "b.example.org", "c.example.org" });
    }

    [Test]
    public async Task ACycleInTheGraphDoesNotHangTheTrace()
    {
        // Mutual referrals are the normal case, not an edge case: a referral list is a courtesy and any
        // real graph is full of triangles.
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var repository = new NpgsqlReferralRepository(source);
        var targets = new NpgsqlCrawlTargetRepository(source);

        var a = await PostgresFixture.InsertGameAsync(source, "Alpha");
        var b = await PostgresFixture.InsertGameAsync(source, "Bravo");
        await AttachAsync(targets, "a.example.org", a);
        await AttachAsync(targets, "b.example.org", b);

        await repository.UpsertAsync(new ReferralEdge(a, "b.example.org", 4201, Then, Then, true), None);
        await repository.UpsertAsync(new ReferralEdge(b, "a.example.org", 4201, Then, Then, true), None);

        var subtree = await repository.SubtreeOfAsync(a, None);

        await Assert.That(subtree.Count).IsEqualTo(2);
    }

    private static async Task AttachAsync(NpgsqlCrawlTargetRepository targets, string host, Guid gameId)
    {
        var id = await targets.AddAsync(new CrawlTarget
        {
            Id = Guid.CreateVersion7(),
            Host = host,
            Port = 4201,
            NextProbeAt = Then,
            FirstSeenAt = Then,
        }, None);

        await targets.AttachGameAsync(id, gameId, None);
    }
}
```

- [ ] **Step 2: Run it and verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `IReferralRepository` and `NpgsqlReferralRepository` do not exist.

- [ ] **Step 3: Write the migration**

Create `src/MUI.Storage/Migrations/0011_referral_edge.sql`:

```sql
-- Spec §7.1, §7.2. One game naming another in its MSSP REFERRAL field.
--
-- This table is PROVENANCE and never a schedule. The referred host became a crawl_target the moment it
-- was accepted, with its own next_probe_at, and it is probed on its own account for ever after; an
-- edge going away sets present = false and changes nothing else. The table exists so the graph can be
-- rendered and, more importantly, so a hostile or careless REFERRAL list can be traced and its whole
-- subtree pruned.

CREATE TABLE referral_edge (
    from_game_id  uuid        NOT NULL REFERENCES game (id) ON DELETE CASCADE,
    to_host       text        NOT NULL,
    to_port       integer     NOT NULL CHECK (to_port BETWEEN 1 AND 65535),
    first_seen_at timestamptz NOT NULL,
    last_seen_at  timestamptz NOT NULL,
    present       boolean     NOT NULL DEFAULT true,

    PRIMARY KEY (from_game_id, to_host, to_port)
);

-- CASCADE here and nowhere else in this plan: an edge is a statement *by* a game, so it is meaningless
-- once that game is gone. The crawl target the edge produced is a separate row with a SET NULL foreign
-- key and is untouched — which is the whole point of §7.1.

CREATE INDEX referral_edge_to_idx ON referral_edge (to_host, to_port);
```

- [ ] **Step 4: Write the verdict types and the repository interface**

Create `src/MUI.Discovery/ReferralGraphWriter.cs` holding only the types below for now; Task 8 appends
the writer itself to the same file.

```csharp
namespace MUI.Discovery;

/// <summary>Why a referral was or was not taken up. Counted so a run can explain its own shape.</summary>
public enum ReferralVerdict
{
    /// <summary>A new crawl target, with <c>GameId</c> null — a candidate hostname, not a game (spec §7.2).</summary>
    Added,

    /// <summary>Already a crawl target, from this source or another. The common outcome once a crawl is under way.</summary>
    AlreadyKnown,

    /// <summary>The referral pointed at the server that sent it. Harmless and common.</summary>
    SelfReferral,

    /// <summary>Beyond <see cref="DiscoveryOptions.MaxDepth"/> hops.</summary>
    TooDeep,

    /// <summary>Loopback, RFC 1918, link-local (including 169.254.169.254) or multicast.</summary>
    NotRoutable,

    /// <summary>Referral following is switched off for this deployment.</summary>
    ReferralsDisabled,

    /// <summary>Past <see cref="DiscoveryOptions.MaxFanOutPerSource"/> for this one source.</summary>
    FanOutExceeded,
}

/// <summary>What one game's referral list contributed.</summary>
public sealed record ReferralIntake(int Added, IReadOnlyDictionary<ReferralVerdict, int> Verdicts)
{
    public static readonly ReferralIntake Nothing = new(0, new Dictionary<ReferralVerdict, int>());
}

/// <summary>The referral graph. Nothing here deletes an edge.</summary>
public interface IReferralRepository
{
    Task<IReadOnlyList<ReferralEdge>> FromGameAsync(Guid gameId, CancellationToken ct);

    Task UpsertAsync(ReferralEdge edge, CancellationToken ct);

    /// <summary>
    /// Marks every edge from this game that is <em>not</em> in <paramref name="stillPresent"/> as
    /// absent. It writes <c>present = false</c> and nothing else (spec §7.1).
    /// </summary>
    Task MarkAbsentAsync(
        Guid fromGameId,
        IReadOnlyCollection<(string Host, int Port)> stillPresent,
        DateTimeOffset at,
        CancellationToken ct);

    /// <summary>
    /// Every edge reachable from this game, following referrals through the crawl targets they
    /// produced. This is what makes a poisoned source prunable wholesale (spec §7.2).
    /// </summary>
    Task<IReadOnlyList<ReferralEdge>> SubtreeOfAsync(Guid rootGameId, CancellationToken ct);
}
```

- [ ] **Step 5: Write the repository**

Create `src/MUI.Discovery/Storage/NpgsqlReferralRepository.cs`:

```csharp
using Dapper;
using Npgsql;

namespace MUI.Discovery.Storage;

public sealed class NpgsqlReferralRepository(NpgsqlDataSource source) : IReferralRepository
{
    private const string Columns = """
        from_game_id  AS "FromGameId",
        to_host       AS "ToHost",
        to_port       AS "ToPort",
        first_seen_at AS "FirstSeenAt",
        last_seen_at  AS "LastSeenAt",
        present       AS "Present"
        """;

    public async Task<IReadOnlyList<ReferralEdge>> FromGameAsync(Guid gameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        var edges = await connection.QueryAsync<ReferralEdge>(new CommandDefinition(
            $"SELECT {Columns} FROM referral_edge WHERE from_game_id = @gameId ORDER BY to_host, to_port;",
            new { gameId },
            cancellationToken: ct));

        return edges.ToList();
    }

    public async Task UpsertAsync(ReferralEdge edge, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // first_seen_at is never overwritten: "when did this game first point here" is the fact the
        // column is for, and a re-sighting is not a first sighting.
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO referral_edge (from_game_id, to_host, to_port, first_seen_at, last_seen_at, present)
            VALUES (@FromGameId, @ToHost, @ToPort, @FirstSeenAt, @LastSeenAt, @Present)
            ON CONFLICT (from_game_id, to_host, to_port) DO UPDATE
               SET last_seen_at = EXCLUDED.last_seen_at,
                   present      = EXCLUDED.present;
            """,
            edge,
            cancellationToken: ct));
    }

    public async Task MarkAbsentAsync(
        Guid fromGameId,
        IReadOnlyCollection<(string Host, int Port)> stillPresent,
        DateTimeOffset at,
        CancellationToken ct)
    {
        // `at` is deliberately unused. Spec §7.1: an edge disappearing "updates present and nothing
        // else". Stamping last_seen_at here would turn it into "last looked at", and the site would
        // then claim a game still points somewhere it stopped pointing months ago.
        _ = at;

        await using var connection = await source.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE referral_edge e
               SET present = false
             WHERE e.from_game_id = @fromGameId
               AND e.present
               AND NOT EXISTS (
                   SELECT 1
                     FROM unnest(@hosts::text[], @ports::int[]) AS s (host, port)
                    WHERE s.host = e.to_host AND s.port = e.to_port);
            """,
            new
            {
                fromGameId,
                hosts = stillPresent.Select(p => p.Host).ToArray(),
                ports = stillPresent.Select(p => p.Port).ToArray(),
            },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ReferralEdge>> SubtreeOfAsync(Guid rootGameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // UNION rather than UNION ALL: mutual referrals are the normal case, and the set semantics are
        // what stops the walk rather than a depth counter.
        var edges = await connection.QueryAsync<ReferralEdge>(new CommandDefinition($"""
            WITH RECURSIVE reachable (game_id) AS (
                SELECT @rootGameId::uuid
                UNION
                SELECT ct.game_id
                  FROM referral_edge e
                  JOIN reachable r     ON r.game_id = e.from_game_id
                  JOIN crawl_target ct ON ct.host = e.to_host AND ct.port = e.to_port
                 WHERE ct.game_id IS NOT NULL
            )
            SELECT {Columns}
              FROM referral_edge e
              JOIN reachable r ON r.game_id = e.from_game_id
             ORDER BY e.to_host, e.to_port;
            """,
            new { rootGameId },
            cancellationToken: ct));

        return edges.ToList();
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS, all five, plus the pre-existing `ReferralEdgeTests`.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Storage/Migrations/0011_referral_edge.sql \
        src/MUI.Discovery/ReferralGraphWriter.cs \
        src/MUI.Discovery/Storage/NpgsqlReferralRepository.cs \
        tests/MUI.Discovery.Tests/ReferralRepositoryTests.cs
git commit -m "feat: the referral graph as provenance, with wholesale subtree tracing"
```

---

### Task 8: `ReferralGraphWriter` — verify, don't trust

**Files:**
- Modify: `src/MUI.Discovery/ReferralGraphWriter.cs` (append the writer)
- Create: `tests/MUI.Discovery.Tests/Support/InMemoryReferralRepository.cs`
- Create: `tests/MUI.Discovery.Tests/Support/InMemoryCrawlTargetRepository.cs`
- Create: `tests/MUI.Discovery.Tests/Support/ProbeResults.cs`
- Test: `tests/MUI.Discovery.Tests/ReferralGraphWriterTests.cs`

**Interfaces:**
- Consumes: `IReferralRepository`, `ICrawlTargetRepository`, `DiscoveryOptions`, `ReferralVerdict`,
  `ReferralIntake`, `ProbeResult`, and from Plan 01's `MUI.Crawl.Mssp`: `MsspData`, `MsspHost` and its
  `IsCrawlable`; plus `ManualTimeProvider`.
- Produces: `MUI.Discovery.ReferralGraphWriter(IReferralRepository edges, ICrawlTargetRepository targets, DiscoveryOptions options, TimeProvider time)`
  with `Task<ReferralIntake> ApplyAsync(Guid fromGameId, int fromDepth, ProbeResult result, CancellationToken ct)`.
  Test doubles `InMemoryReferralRepository`, `InMemoryCrawlTargetRepository`, and
  `MUI.Discovery.Tests.Support.ProbeResults.Answered(...)`.

- [ ] **Step 1: Write the test doubles and the fixture builder**

Create `tests/MUI.Discovery.Tests/Support/InMemoryCrawlTargetRepository.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// The registry's contract in memory: monotonic, no delete, depth only ever shrinks.
/// </summary>
/// <remarks>
/// <b>Hosts are canonicalised with <see cref="HostName.Normalize"/> and then compared ordinally</b> —
/// the same rule, in the same order, as <c>NpgsqlCrawlTargetRepository</c> and its CHECK constraint.
/// A convenient <c>OrdinalIgnoreCase</c> dictionary here would be a fake kinder than the database:
/// every test would pass while the real registry minted a second target for the same machine, which is
/// double the traffic at somebody else's server and eventually the duplicate listing §7.3 exists to
/// prevent. That is exactly the bug Plan 02 found in <c>game_endpoint</c>.
/// </remarks>
public sealed class InMemoryCrawlTargetRepository : ICrawlTargetRepository
{
    private readonly Dictionary<(string Host, int Port), CrawlTarget> _targets = new();

    public IReadOnlyCollection<CrawlTarget> All => _targets.Values.ToList();

    public Task<CrawlTarget?> ByAddressAsync(string host, int port, CancellationToken ct) =>
        Task.FromResult(_targets.GetValueOrDefault((HostName.Normalize(host), port)));

    public Task<Guid> AddAsync(CrawlTarget target, CancellationToken ct)
    {
        var canonical = target with { Host = HostName.Normalize(target.Host) };
        var key = (canonical.Host, canonical.Port);
        if (_targets.TryGetValue(key, out var existing))
        {
            _targets[key] = existing with { Depth = Math.Min(existing.Depth, canonical.Depth) };
            return Task.FromResult(existing.Id);
        }

        _targets[key] = canonical;
        return Task.FromResult(canonical.Id);
    }

    public Task<IReadOnlyList<CrawlTarget>> DueAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CrawlTarget>>(_targets.Values
            .Where(t => t.NextProbeAt <= now)
            .OrderBy(t => t.NextProbeAt)
            .ThenBy(t => t.Depth)
            .ThenBy(t => t.FirstSeenAt)
            .Take(limit)
            .ToList());

    public Task RecordAttemptAsync(
        Guid id, DateTimeOffset at, bool succeeded, TimeSpan? crawlDelay, DateTimeOffset nextProbeAt, CancellationToken ct)
    {
        Replace(id, target => target with
        {
            LastProbedAt = at,
            ConsecutiveFailures = succeeded ? 0 : target.ConsecutiveFailures + 1,
            CrawlDelay = crawlDelay ?? target.CrawlDelay,
            NextProbeAt = nextProbeAt,
        });

        return Task.CompletedTask;
    }

    public Task AttachGameAsync(Guid id, Guid gameId, CancellationToken ct)
    {
        Replace(id, target => target.GameId is null ? target with { GameId = gameId } : target);
        return Task.CompletedTask;
    }

    private void Replace(Guid id, Func<CrawlTarget, CrawlTarget> change)
    {
        foreach (var (key, target) in _targets.ToList())
        {
            if (target.Id == id)
            {
                _targets[key] = change(target);
            }
        }
    }
}
```

Create `tests/MUI.Discovery.Tests/Support/InMemoryReferralRepository.cs`:

```csharp
using MUI.Discovery;

namespace MUI.Discovery.Tests.Support;

public sealed class InMemoryReferralRepository : IReferralRepository
{
    private readonly Dictionary<(Guid From, string Host, int Port), ReferralEdge> _edges = new();

    public IReadOnlyCollection<ReferralEdge> All => _edges.Values.ToList();

    public Task<IReadOnlyList<ReferralEdge>> FromGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ReferralEdge>>(
            _edges.Values.Where(e => e.FromGameId == gameId).ToList());

    public Task UpsertAsync(ReferralEdge edge, CancellationToken ct)
    {
        var key = (edge.FromGameId, edge.ToHost, edge.ToPort);
        _edges[key] = _edges.TryGetValue(key, out var existing)
            ? existing with { LastSeenAt = edge.LastSeenAt, Present = edge.Present }
            : edge;

        return Task.CompletedTask;
    }

    public Task MarkAbsentAsync(
        Guid fromGameId,
        IReadOnlyCollection<(string Host, int Port)> stillPresent,
        DateTimeOffset at,
        CancellationToken ct)
    {
        foreach (var (key, edge) in _edges.ToList())
        {
            if (edge.FromGameId == fromGameId && edge.Present && !stillPresent.Contains((edge.ToHost, edge.ToPort)))
            {
                _edges[key] = edge with { Present = false };
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReferralEdge>> SubtreeOfAsync(Guid rootGameId, CancellationToken ct) =>
        FromGameAsync(rootGameId, ct);
}
```

Create `tests/MUI.Discovery.Tests/Support/ProbeResults.cs`:

```csharp
using MUI.Crawl;
using MUI.Crawl.Mssp;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// Captured-fixture-shaped <see cref="ProbeResult"/>s. Spec §6.5 and §13: every downstream behaviour is
/// exercised against one of these with no network anywhere in sight.
/// </summary>
public static class ProbeResults
{
    public static readonly DateTimeOffset Observed = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static MsspData Mssp(params (string Variable, string[] Values)[] variables) =>
        MsspData.From(variables.Select(v =>
            new KeyValuePair<string, IReadOnlyList<string>>(v.Variable, v.Values)));

    public static ProbeResult Answered(
        string host = "mud.example.org",
        int port = 4201,
        MsspData? mssp = null,
        string? banner = null,
        WhoReading? who = null,
        DateTimeOffset? at = null) => new()
    {
        Host = host,
        Port = port,
        ObservedAt = at ?? Observed,
        Outcome = ProbeOutcome.Answered,
        Mssp = mssp ?? MsspData.Empty,
        MsspVia = mssp is null ? MsspTransport.None : MsspTransport.TelnetOption70,
        Banner = banner,
        // NotAttempted, not Unreadable: a fixture that says nothing about WHO must not claim we asked
        // and failed. Plan 01 split those two states apart; `WhoReading.Unread` no longer exists.
        Who = who ?? WhoReading.NotAttempted,
    };

    public static ProbeResult Failed(
        string host = "mud.example.org",
        int port = 4201,
        string cause = ProbeFailureCauses.Refused,
        DateTimeOffset? at = null) => new()
    {
        Host = host,
        Port = port,
        ObservedAt = at ?? Observed,
        Outcome = ProbeOutcome.Failed,
        Failure = new FailureDetail(cause),
    };
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/MUI.Discovery.Tests/ReferralGraphWriterTests.cs`:

```csharp
using MUI.Discovery;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// "Referrals are candidate hostnames, not facts" (spec §7.2). The writer's whole job is to write the
/// provenance, add a target that is explicitly <em>not</em> a game, and refuse the rest out loud.
/// </summary>
public class ReferralGraphWriterTests
{
    private static readonly Guid Source = Guid.CreateVersion7();

    private static (ReferralGraphWriter Writer, InMemoryReferralRepository Edges, InMemoryCrawlTargetRepository Targets)
        Build(DiscoveryOptions? options = null)
    {
        var edges = new InMemoryReferralRepository();
        var targets = new InMemoryCrawlTargetRepository();
        var writer = new ReferralGraphWriter(edges, targets, options ?? new DiscoveryOptions(), new ManualTimeProvider());
        return (writer, edges, targets);
    }

    [Test]
    public async Task AReferredHostBecomesACrawlTargetThatIsNotYetAGame()
    {
        // The single most important assertion in this file. §7.2: a referred host must independently
        // answer MSSP with its own NAME before it is listed, so it is crawled long before it is a game.
        var (writer, edges, targets) = Build();
        var result = ProbeResults.Answered(mssp: ProbeResults.Mssp(("REFERRAL", ["b.example.org 4000"])));

        var intake = await writer.ApplyAsync(Source, fromDepth: 0, result, CancellationToken.None);

        await Assert.That(intake.Added).IsEqualTo(1);
        var target = targets.All.Single();
        await Assert.That(target.Host).IsEqualTo("b.example.org");
        await Assert.That(target.Port).IsEqualTo(4000);
        await Assert.That(target.GameId).IsNull();
        await Assert.That(target.DiscoveredFromGameId).IsEqualTo(Source);
        await Assert.That(target.Depth).IsEqualTo(1);
        await Assert.That(edges.All.Single().Present).IsTrue();
    }

    [Test]
    public async Task ANewTargetIsDueImmediatelySoADiscoveryIsNotAlsoADelay()
    {
        var time = new ManualTimeProvider();
        var edges = new InMemoryReferralRepository();
        var targets = new InMemoryCrawlTargetRepository();
        var writer = new ReferralGraphWriter(edges, targets, new DiscoveryOptions(), time);

        await writer.ApplyAsync(Source, 0,
            ProbeResults.Answered(mssp: ProbeResults.Mssp(("REFERRAL", ["b.example.org 4000"]))),
            CancellationToken.None);

        await Assert.That(targets.All.Single().NextProbeAt).IsEqualTo(time.GetUtcNow());
    }

    [Test]
    [Arguments("127.0.0.1 4201")]
    [Arguments("::1 4201")]
    [Arguments("10.1.2.3 4201")]
    [Arguments("192.168.1.1 4201")]
    [Arguments("172.16.0.1 4201")]
    [Arguments("fd00::1 4201")]
    [Arguments("169.254.169.254 80")]
    [Arguments("239.255.255.250 1900")]
    public async Task AReferralIntoOurOwnNetworkIsRefused(string referral)
    {
        // A stranger must not be able to aim our crawler at our own network. 169.254.169.254 is the one
        // that matters: it is the cloud metadata address, and it hands out credentials.
        var (writer, edges, targets) = Build();
        var result = ProbeResults.Answered(mssp: ProbeResults.Mssp(("REFERRAL", [referral])));

        var intake = await writer.ApplyAsync(Source, 0, result, CancellationToken.None);

        await Assert.That(intake.Added).IsEqualTo(0);
        await Assert.That(intake.Verdicts[ReferralVerdict.NotRoutable]).IsEqualTo(1);
        await Assert.That(targets.All).IsEmpty();

        // And no edge either: an edge is a claim we are willing to render, and rendering "this game
        // refers to 169.254.169.254" is publishing an attacker's payload.
        await Assert.That(edges.All).IsEmpty();
    }

    [Test]
    public async Task AServerReferringToItselfIsCountedAndNotFollowed()
    {
        var (writer, _, targets) = Build();
        var result = ProbeResults.Answered(
            host: "mud.example.org", port: 4201,
            mssp: ProbeResults.Mssp(("REFERRAL", ["MUD.Example.ORG. 4201"])));

        var intake = await writer.ApplyAsync(Source, 0, result, CancellationToken.None);

        await Assert.That(intake.Verdicts[ReferralVerdict.SelfReferral]).IsEqualTo(1);
        await Assert.That(targets.All).IsEmpty();
    }

    [Test]
    public async Task ADepthBeyondTheCapIsRefused()
    {
        var (writer, _, targets) = Build(new DiscoveryOptions { MaxDepth = 2 });
        var result = ProbeResults.Answered(mssp: ProbeResults.Mssp(("REFERRAL", ["b.example.org 4000"])));

        await Assert.That((await writer.ApplyAsync(Source, 1, result, CancellationToken.None))
            .Verdicts[ReferralVerdict.Added]).IsEqualTo(1);
        await Assert.That((await writer.ApplyAsync(Source, 2, result, CancellationToken.None))
            .Verdicts[ReferralVerdict.TooDeep]).IsEqualTo(1);

        await Assert.That(targets.All.Count).IsEqualTo(1);
    }

    [Test]
    public async Task FanOutIsCappedPerSource()
    {
        // Referral graphs are unbounded by construction; without this a single hostile list walks as far
        // as MSSP reaches, unattended.
        var (writer, _, targets) = Build(new DiscoveryOptions { MaxFanOutPerSource = 3 });
        var referrals = Enumerable.Range(0, 10).Select(i => $"h{i}.example.org 4000").ToArray();
        var result = ProbeResults.Answered(mssp: ProbeResults.Mssp(("REFERRAL", referrals)));

        var intake = await writer.ApplyAsync(Source, 0, result, CancellationToken.None);

        await Assert.That(intake.Added).IsEqualTo(3);
        await Assert.That(intake.Verdicts[ReferralVerdict.FanOutExceeded]).IsEqualTo(7);
        await Assert.That(targets.All.Count).IsEqualTo(3);
    }

    [Test]
    public async Task AKnownTargetIsRecognisedAndItsScheduleIsLeftAlone()
    {
        var (writer, _, targets) = Build();
        var result = ProbeResults.Answered(mssp: ProbeResults.Mssp(("REFERRAL", ["b.example.org 4000"])));

        await writer.ApplyAsync(Source, 0, result, CancellationToken.None);
        var intake = await writer.ApplyAsync(Guid.CreateVersion7(), 0, result, CancellationToken.None);

        await Assert.That(intake.Added).IsEqualTo(0);
        await Assert.That(intake.Verdicts[ReferralVerdict.AlreadyKnown]).IsEqualTo(1);
        await Assert.That(targets.All.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AnEdgeThatStopsBeingListedGoesAbsentAndTheTargetSurvives()
    {
        // §7.1 in one test: the edge changes, the schedule does not.
        var (writer, edges, targets) = Build();

        await writer.ApplyAsync(Source, 0,
            ProbeResults.Answered(mssp: ProbeResults.Mssp(("REFERRAL", ["b.example.org 4000", "c.example.org 4000"]))),
            CancellationToken.None);

        await writer.ApplyAsync(Source, 0,
            ProbeResults.Answered(mssp: ProbeResults.Mssp(("REFERRAL", ["c.example.org 4000"]))),
            CancellationToken.None);

        var gone = edges.All.Single(e => e.ToHost == "b.example.org");
        await Assert.That(gone.Present).IsFalse();
        await Assert.That(targets.All.Count).IsEqualTo(2);
        await Assert.That(targets.All.Any(t => t.Host == "b.example.org")).IsTrue();
    }

    [Test]
    public async Task AProbeThatLearnedNoMsspAtAllLeavesTheEdgesAlone()
    {
        // MSSP being absent means we learned nothing about this game's referrals — not that they went
        // away. Marking them absent here would erase a graph on every probe of a server whose MSSP
        // happened to be unavailable that hour.
        var (writer, edges, _) = Build();

        await writer.ApplyAsync(Source, 0,
            ProbeResults.Answered(mssp: ProbeResults.Mssp(("REFERRAL", ["b.example.org 4000"]))),
            CancellationToken.None);

        var intake = await writer.ApplyAsync(Source, 0, ProbeResults.Answered(), CancellationToken.None);

        await Assert.That(intake).IsEqualTo(ReferralIntake.Nothing);
        await Assert.That(edges.All.Single().Present).IsTrue();
    }

    [Test]
    public async Task ReferralsAreNotFollowedAtAllWhenTheDeploymentSaysNotTo()
    {
        var (writer, edges, targets) = Build(new DiscoveryOptions { FollowReferrals = false });
        var result = ProbeResults.Answered(mssp: ProbeResults.Mssp(("REFERRAL", ["b.example.org 4000"])));

        var intake = await writer.ApplyAsync(Source, 0, result, CancellationToken.None);

        await Assert.That(intake.Verdicts[ReferralVerdict.ReferralsDisabled]).IsEqualTo(1);
        await Assert.That(targets.All).IsEmpty();
        await Assert.That(edges.All).IsEmpty();
    }
}
```

- [ ] **Step 3: Run it and verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `ReferralGraphWriter` does not exist.

- [ ] **Step 4: Append the writer**

Append to `src/MUI.Discovery/ReferralGraphWriter.cs`, with `using MUI.Crawl;` and
`using MUI.Crawl.Mssp;` at the top of the file:

```csharp
/// <summary>
/// Turns one game's MSSP <c>REFERRAL</c> list into provenance edges and candidate crawl targets.
/// </summary>
/// <remarks>
/// <para>
/// <b>Verify, don't trust</b> (spec §4.5, §7.2). A referral produces a <see cref="CrawlTarget"/> with a
/// null <c>GameId</c> — a hostname somebody claimed is a game. It becomes a game only when it answers
/// MSSP with its own <c>NAME</c>, which happens in the crawl loop and not here.
/// </para>
/// <para>
/// <b>Scope is checked with <see cref="MsspHost.IsCrawlable"/>, from <c>MUI.Crawl.Mssp</c>.</b> That is
/// MUIndex's own type, written by Plan 01 against its own tests — the gate is not borrowed and not
/// taken on trust from a package. A referral
/// into loopback, RFC 1918, link-local — which includes the cloud metadata address
/// <c>169.254.169.254</c> — or multicast is either a server misreporting itself or an attempt to aim
/// somebody else's crawler at a network it cannot otherwise reach. Neither is worth a packet, and
/// neither gets an edge: an edge is a claim the site is willing to render.
/// </para>
/// </remarks>
public sealed class ReferralGraphWriter(
    IReferralRepository edges,
    ICrawlTargetRepository targets,
    DiscoveryOptions options,
    TimeProvider time)
{
    public async Task<ReferralIntake> ApplyAsync(
        Guid fromGameId,
        int fromDepth,
        ProbeResult result,
        CancellationToken ct)
    {
        if (result.Outcome is not ProbeOutcome.Answered || result.MsspVia is MsspTransport.None)
        {
            // We learned nothing about this game's referrals. That is not the same fact as "it lists
            // none", and treating it as such would erase the graph on any hour MSSP was unavailable.
            return ReferralIntake.Nothing;
        }

        var verdicts = new Dictionary<ReferralVerdict, int>();

        if (!options.FollowReferrals)
        {
            foreach (var _ in result.Mssp.Referrals)
            {
                Count(verdicts, ReferralVerdict.ReferralsDisabled);
            }

            return new ReferralIntake(0, verdicts);
        }

        var now = time.GetUtcNow();
        var self = MsspHost.Create(result.Host, result.Port);
        var present = new List<(string Host, int Port)>();
        var added = 0;
        var accepted = 0;

        foreach (var referral in result.Mssp.Referrals)
        {
            if (self is not null && referral == self)
            {
                Count(verdicts, ReferralVerdict.SelfReferral);
                continue;
            }

            if (!referral.IsCrawlable)
            {
                Count(verdicts, ReferralVerdict.NotRoutable);
                continue;
            }

            if (accepted >= options.MaxFanOutPerSource)
            {
                Count(verdicts, ReferralVerdict.FanOutExceeded);
                continue;
            }

            if (fromDepth + 1 > options.MaxDepth)
            {
                Count(verdicts, ReferralVerdict.TooDeep);
                continue;
            }

            accepted++;
            present.Add((referral.Host, referral.Port));

            await edges.UpsertAsync(
                new ReferralEdge(fromGameId, referral.Host, referral.Port, now, now, Present: true), ct);

            if (await targets.ByAddressAsync(referral.Host, referral.Port, ct) is not null)
            {
                // Already crawled on its own account. Nothing to schedule: the target's next_probe_at is
                // its own business and a second referral must not drag it forward.
                Count(verdicts, ReferralVerdict.AlreadyKnown);
                continue;
            }

            await targets.AddAsync(new CrawlTarget
            {
                Id = Guid.CreateVersion7(),
                GameId = null,
                Host = referral.Host,
                Port = referral.Port,
                NextProbeAt = now,
                FirstSeenAt = now,
                DiscoveredFromGameId = fromGameId,
                Depth = fromDepth + 1,
            }, ct);

            added++;
            Count(verdicts, ReferralVerdict.Added);
        }

        await edges.MarkAbsentAsync(fromGameId, present, now, ct);
        return new ReferralIntake(added, verdicts);
    }

    private static void Count(Dictionary<ReferralVerdict, int> verdicts, ReferralVerdict verdict) =>
        verdicts[verdict] = verdicts.GetValueOrDefault(verdict) + 1;
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS — ten `ReferralGraphWriterTests` cases (the `[Arguments]` one counts eight).

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Discovery/ReferralGraphWriter.cs tests/MUI.Discovery.Tests/Support \
        tests/MUI.Discovery.Tests/ReferralGraphWriterTests.cs
git commit -m "feat: ReferralGraphWriter — candidate hostnames, capped, and never into our own network"
```

---

### Task 9: The three gates a dial passes — `HostGate`, `CrawlRateLimiter`, `HostScopeGuard`

**Files:**
- Create: `src/MUI.Discovery/HostGate.cs`
- Create: `src/MUI.Discovery/CrawlRateLimiter.cs`
- Create: `src/MUI.Discovery/HostScopeGuard.cs`
- Create: `tests/MUI.Discovery.Tests/Support/FakeHostResolver.cs`
- Test: `tests/MUI.Discovery.Tests/HostGateTests.cs`
- Test: `tests/MUI.Discovery.Tests/CrawlRateLimiterTests.cs`
- Test: `tests/MUI.Discovery.Tests/HostScopeGuardTests.cs`

**Interfaces:**
- Consumes: `DiscoveryOptions` (Task 2), `ManualTimeProvider` (Task 1), `CrawlTarget` (Task 4), and
  **`MUI.Crawl.Mssp.MsspHost.ScopeOf(IPAddress)` plus `MsspHostScope`** — Plan 01's classifier, which
  this plan requires be public (see *Names this plan requires from Plan 01*). The ranges are not
  re-implemented here.
- Produces: `MUI.Discovery.HostGate` with `Task<IDisposable> EnterAsync(string host, CancellationToken ct)`;
  `MUI.Discovery.CrawlRateLimiter(DiscoveryOptions options, TimeProvider time)` with
  `TimeSpan DelayBefore(string host)`, `void RecordStart(string host)` and
  `Task WaitForTurnAsync(string host, CancellationToken ct)`;
  `MUI.Discovery.IHostResolver` with
  `Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct)`;
  `MUI.Discovery.SystemHostResolver : IHostResolver`;
  `MUI.Discovery.HostScopeDecision` — `Allowed`, `OperatorSeed`, `ResolvedNonGlobal`, `Unresolvable`;
  `MUI.Discovery.HostScopeRuling(HostScopeDecision Decision, IReadOnlyList<IPAddress> Addresses, MsspHostScope? Offending)`
  with `bool MayDial`;
  `MUI.Discovery.HostScopeGuard(IHostResolver resolver)` with
  `Task<HostScopeRuling> RuleOnAsync(CrawlTarget target, CancellationToken ct)`.
  Test double `MUI.Discovery.Tests.Support.FakeHostResolver`.

**Why there is a third gate, and why the first two are not enough.** `HostGate` and `CrawlRateLimiter`
are politeness. `HostScopeGuard` is not: it implements **spec §7.2's "The gate is on the resolved
address, not the name"**, and every rule below is that subsection's, quoted rather than invented — read
it before changing any of them.

Task 8 already refuses a `REFERRAL` whose *literal* address is loopback, RFC 1918, RFC 6598, `fc00::/7`,
link-local (including `169.254.169.254`) or multicast, via `MsspHost.IsCrawlable`. **That check is
worth nothing against an attacker who owns a domain**, and the spec names it a server-side request
forgery hole: `MsspHost.Scope` is `Unresolved` for every DNS name and `IsCrawlable` is true for
`Unresolved` — correctly, because nothing can be known about a name until DNS answers. So a referral
naming `games.example.com`, with an A record pointing at `127.0.0.1` or `169.254.169.254`, sails
through Task 8 and gets dialled. "The name passes; the socket goes somewhere it must never go."

It cannot be fixed in `MsspHost`, which only ever sees a string. It has to run **after resolution and
before the socket is used**, which is here: every dial resolves first and is refused unless *every*
returned address is globally routable.

Four rules follow, all of them §7.2's, each with a test below:

1. **Refuse, don't filter.** One public address and one private one refuses the whole target.
   "Connecting to 'the good one' is a coin flip we would lose the moment DNS reordered, and a mixed
   answer is itself evidence of intent." Refusing on *any* non-global address, not on the first one
   found, is what implements this.
2. **"Could not resolve" and "resolved somewhere we won't go" are different facts**, and only the
   second is a refusal. The first is an ordinary DNS failure and gets ordinary backoff — hence
   `Unresolvable` and `ResolvedNonGlobal` as distinct verdicts rather than one "no".
3. **A refusal writes no availability sample.** We declined to dial; we did not measure. The spec puts
   this in the same class as recording an unparseable WHO as zero players (§5.4). The guard therefore
   runs *before* anything on the ingest path that could write availability.
4. **Operator-supplied seeds may be exempted, and nothing else may.** `CrawlTarget.IsOperatorSeed` is
   "a stored property defaulting to *not exempt*, never inferred, and never granted by a referral or an
   import — so the dangerous paths are guarded by not having to remember to guard them" (Task 4).

- [ ] **Step 1: Write the failing tests**

Create `tests/MUI.Discovery.Tests/HostGateTests.cs`:

```csharp
using MUI.Discovery;

namespace MUI.Discovery.Tests;

/// <summary>
/// Per-host serialisation (spec §7.7): "prevents a multi-port game from being hit concurrently". Keyed
/// on the host alone, because six advertised ports are one machine.
/// </summary>
public class HostGateTests
{
    [Test]
    public async Task TwoPortsOnOneMachineAreSerialised()
    {
        var gate = new HostGate();

        var first = await gate.EnterAsync("mud.example.org", CancellationToken.None);
        var second = gate.EnterAsync("mud.example.org", CancellationToken.None);

        await Assert.That(second.IsCompleted).IsFalse();

        first.Dispose();
        var taken = await second.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(taken).IsNotNull();
        taken.Dispose();
    }

    [Test]
    public async Task DifferentHostsDoNotWaitForEachOther()
    {
        var gate = new HostGate();

        var first = await gate.EnterAsync("a.example.org", CancellationToken.None);
        var second = await gate.EnterAsync("b.example.org", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(second).IsNotNull();
        first.Dispose();
        second.Dispose();
    }

    [Test]
    public async Task TheHostNameIsMatchedCaseInsensitively()
    {
        var gate = new HostGate();

        var first = await gate.EnterAsync("MUD.Example.ORG", CancellationToken.None);
        var second = gate.EnterAsync("mud.example.org", CancellationToken.None);

        await Assert.That(second.IsCompleted).IsFalse();

        first.Dispose();
        (await second).Dispose();
    }

    [Test]
    public async Task ReleasingTwiceIsHarmless()
    {
        // The loop disposes through a `using`, and a retry path could dispose again. Double-releasing a
        // semaphore would silently let two probes into one host.
        var gate = new HostGate();
        var held = await gate.EnterAsync("a.example.org", CancellationToken.None);

        held.Dispose();
        held.Dispose();

        var again = await gate.EnterAsync("a.example.org", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        var blocked = gate.EnterAsync("a.example.org", CancellationToken.None);

        await Assert.That(blocked.IsCompleted).IsFalse();
        again.Dispose();
        (await blocked).Dispose();
    }
}
```

Create `tests/MUI.Discovery.Tests/CrawlRateLimiterTests.cs`:

```csharp
using MUI.Discovery;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// The rate limiter, driven by a clock the test moves by hand. Nothing here sleeps: a limiter tested by
/// waiting for its own interval proves only that the machine was not too busy.
/// </summary>
public class CrawlRateLimiterTests
{
    private static readonly DiscoveryOptions Options = new()
    {
        GlobalInterval = TimeSpan.FromSeconds(2),
        PerHostInterval = TimeSpan.FromMinutes(5),
    };

    [Test]
    public async Task TheFirstConnectionIsAllowedImmediately()
    {
        var limiter = new CrawlRateLimiter(Options, new ManualTimeProvider());

        await Assert.That(limiter.DelayBefore("a.example.org")).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task TheGlobalIntervalHoldsBackTheNextConnectionToADifferentHost()
    {
        var time = new ManualTimeProvider();
        var limiter = new CrawlRateLimiter(Options, time);

        limiter.RecordStart("a.example.org");

        await Assert.That(limiter.DelayBefore("b.example.org")).IsEqualTo(TimeSpan.FromSeconds(2));
        time.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(limiter.DelayBefore("b.example.org")).IsEqualTo(TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(1));
        await Assert.That(limiter.DelayBefore("b.example.org")).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task TheSameHostWaitsTheLongerPerHostInterval()
    {
        var time = new ManualTimeProvider();
        var limiter = new CrawlRateLimiter(Options, time);

        limiter.RecordStart("a.example.org");
        time.Advance(TimeSpan.FromSeconds(10));

        await Assert.That(limiter.DelayBefore("b.example.org")).IsEqualTo(TimeSpan.Zero);
        await Assert.That(limiter.DelayBefore("a.example.org")).IsEqualTo(TimeSpan.FromSeconds(290));

        time.Advance(TimeSpan.FromSeconds(290));
        await Assert.That(limiter.DelayBefore("a.example.org")).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task PortsOnOneMachineShareTheHostLimitBecauseTheKeyIsTheHost()
    {
        // The key is the host, not (host, port). HostGate owns "not two at once" and the limiter owns
        // "not two in quick succession", and both mean the machine rather than the socket — six
        // advertised ports are one server operator's box, and politeness is owed to the operator.
        var time = new ManualTimeProvider();
        var limiter = new CrawlRateLimiter(Options, time);

        limiter.RecordStart("mud.example.org");

        await Assert.That(limiter.DelayBefore("mud.example.org")).IsEqualTo(TimeSpan.FromMinutes(5));
    }

    [Test]
    public async Task AStreamOfConnectionsIsSpacedByTheGlobalInterval()
    {
        var time = new ManualTimeProvider();
        var limiter = new CrawlRateLimiter(Options, time);
        var starts = new List<DateTimeOffset>();

        for (var i = 0; i < 5; i++)
        {
            var host = $"host{i}.example.org";
            time.Advance(limiter.DelayBefore(host));
            starts.Add(time.GetUtcNow());
            limiter.RecordStart(host);
        }

        var gaps = starts.Zip(starts.Skip(1), (first, second) => second - first).ToList();

        await Assert.That(gaps).IsNotEmpty();
        foreach (var gap in gaps)
        {
            await Assert.That(gap).IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
        }
    }

    [Test]
    public async Task WaitingForATurnStampsTheStartSoTheNextCallerIsHeldBack()
    {
        var time = new ManualTimeProvider();
        var limiter = new CrawlRateLimiter(Options, time);

        await limiter.WaitForTurnAsync("a.example.org", CancellationToken.None);

        await Assert.That(limiter.DelayBefore("b.example.org")).IsEqualTo(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task ZeroIntervalsMeanNoWaitingAtAll()
    {
        var limiter = new CrawlRateLimiter(
            new DiscoveryOptions { GlobalInterval = TimeSpan.Zero, PerHostInterval = TimeSpan.Zero },
            new ManualTimeProvider());

        limiter.RecordStart("a.example.org");

        await Assert.That(limiter.DelayBefore("a.example.org")).IsEqualTo(TimeSpan.Zero);
    }
}
```

- [ ] **Step 2: Run them and verify they fail**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `HostGate` and `CrawlRateLimiter` do not exist.

- [ ] **Step 3: Write `HostGate`**

Create `src/MUI.Discovery/HostGate.cs`:

```csharp
using System.Collections.Concurrent;

namespace MUI.Discovery;

/// <summary>
/// One probe at a time per host (spec §7.7). Keyed on the host alone, deliberately: a game advertising
/// six ports is one machine and one operator, and the point of the rule is not to arrive six times at
/// once.
/// </summary>
/// <remarks>
/// This is not the concurrency cap. How many probes may be in flight <em>in total</em> is a semaphore in
/// the crawl loop, because that is a fact about connections rather than about hosts, and folding the
/// two together would make neither assertable.
/// </remarks>
public sealed class HostGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<IDisposable> EnterAsync(string host, CancellationToken ct)
    {
        var gate = _gates.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        return new Holding(gate);
    }

    /// <summary>
    /// Idempotent on purpose: a second release would let two probes into one host, which is precisely
    /// the thing this type exists to prevent.
    /// </summary>
    private sealed class Holding(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
```

- [ ] **Step 4: Write `CrawlRateLimiter`**

Create `src/MUI.Discovery/CrawlRateLimiter.cs`:

```csharp
namespace MUI.Discovery;

/// <summary>
/// The two time floors on a crawl: a gap between any two connections, and a longer gap between two
/// connections to the same host.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a timer, a queue, or anything that sleeps on your behalf. It answers one question —
/// <see cref="DelayBefore"/>, how long from now until this host may be dialled — and records one fact —
/// <see cref="RecordStart"/>, it just was. That separation is what makes the limit assertable: a test
/// drives an injected <see cref="TimeProvider"/> and reads the answers back, instead of sleeping for
/// the interval and hoping the machine agreed.
/// </para>
/// <para>
/// The third limit, how many connections may be open at once, is not here. It is a semaphore in the
/// crawl loop, because it is a fact about connections in flight rather than about time.
/// </para>
/// </remarks>
public sealed class CrawlRateLimiter(DiscoveryOptions options, TimeProvider time)
{
    private readonly Dictionary<string, DateTimeOffset> _lastPerHost = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    private DateTimeOffset? _lastAny;

    /// <summary>Zero when <paramref name="host"/> may be dialled now, otherwise the longer of the two waits owed.</summary>
    public TimeSpan DelayBefore(string host)
    {
        lock (_gate)
        {
            return DelayLocked(host, time.GetUtcNow());
        }
    }

    /// <summary>
    /// Stamps a connection as starting now, against both limits. Called when the connection is
    /// <em>started</em>, never when it finishes: stamping on completion would let a burst of slow
    /// connections all start together and would make the effective rate depend on how fast the servers
    /// answered, which is the opposite of a rate limit.
    /// </summary>
    public void RecordStart(string host)
    {
        lock (_gate)
        {
            RecordStartLocked(host, time.GetUtcNow());
        }
    }

    /// <summary>
    /// Waits out <see cref="DelayBefore"/> and then stamps the start, re-checking after each wait — two
    /// workers told to wait one second would otherwise both start at the end of it and halve the global
    /// interval.
    /// </summary>
    public async Task WaitForTurnAsync(string host, CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_gate)
            {
                var now = time.GetUtcNow();
                wait = DelayLocked(host, now);
                if (wait <= TimeSpan.Zero)
                {
                    RecordStartLocked(host, now);
                    return;
                }
            }

            await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan DelayLocked(string host, DateTimeOffset now)
    {
        var globalReady = _lastAny is { } lastAny ? lastAny + options.GlobalInterval : now;
        var hostReady = _lastPerHost.TryGetValue(host, out var lastHost)
            ? lastHost + options.PerHostInterval
            : now;

        var ready = globalReady > hostReady ? globalReady : hostReady;
        var wait = ready - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    private void RecordStartLocked(string host, DateTimeOffset now)
    {
        _lastAny = now;
        _lastPerHost[host] = now;
    }
}
```

- [ ] **Step 5: Write the fake resolver and the guard's failing tests**

Create `tests/MUI.Discovery.Tests/Support/FakeHostResolver.cs`:

```csharp
using System.Net;
using MUI.Discovery;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// DNS, scripted. <b>No test in this suite performs a live lookup</b>: a guard tested against real DNS
/// asserts what somebody else's zone file says today, and would go green or red for reasons that have
/// nothing to do with this code.
/// </summary>
public sealed class FakeHostResolver : IHostResolver
{
    private readonly Dictionary<string, IReadOnlyList<IPAddress>> _answers =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> Asked { get; } = [];

    public FakeHostResolver Resolving(string host, params string[] addresses)
    {
        _answers[host] = addresses.Select(IPAddress.Parse).ToList();
        return this;
    }

    /// <summary>A name with no record at all. Not a refusal — a dead host.</summary>
    public FakeHostResolver Failing(string host)
    {
        _answers[host] = [];
        return this;
    }

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct)
    {
        Asked.Add(host);
        return Task.FromResult(_answers.TryGetValue(host, out var addresses) ? addresses : []);
    }
}
```

Create `tests/MUI.Discovery.Tests/HostScopeGuardTests.cs`:

```csharp
using MUI.Crawl.Mssp;
using MUI.Discovery;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// Spec §7.2 closed on the resolved address. The literal-address check in <c>MsspHost.IsCrawlable</c>
/// is bypassed by anybody who owns a domain, so this is the gate that actually holds.
/// </summary>
public class HostScopeGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private static CrawlTarget Target(string host, bool operatorSeed = false) => new()
    {
        Id = Guid.CreateVersion7(),
        Host = host,
        Port = 4201,
        NextProbeAt = Now,
        FirstSeenAt = Now,
        IsOperatorSeed = operatorSeed,
    };

    [Test]
    public async Task ANameResolvingToPublicSpaceIsDialled()
    {
        var resolver = new FakeHostResolver().Resolving("mud.example.org", "203.0.113.10");
        var guard = new HostScopeGuard(resolver);

        var ruling = await guard.RuleOnAsync(Target("mud.example.org"), None);

        await Assert.That(ruling.Decision).IsEqualTo(HostScopeDecision.Allowed);
        await Assert.That(ruling.MayDial).IsTrue();
    }

    [Test]
    [Arguments("127.0.0.1")]
    [Arguments("10.0.0.5")]
    [Arguments("192.168.1.1")]
    [Arguments("172.16.0.1")]
    [Arguments("100.64.0.1")]
    [Arguments("169.254.169.254")]
    [Arguments("::1")]
    [Arguments("fd00::1")]
    [Arguments("fe80::1")]
    [Arguments("239.255.255.250")]
    public async Task ANameResolvingIntoOurOwnNetworkIsRefused(string address)
    {
        // The whole point. Every one of these passes MsspHost.IsCrawlable when it arrives as the name
        // "internal.example.org", because a name is Unresolved and Unresolved is crawlable. Publishing
        // the A record costs an attacker nothing; 169.254.169.254 is the cloud metadata address and
        // hands out credentials.
        var resolver = new FakeHostResolver().Resolving("internal.example.org", address);
        var guard = new HostScopeGuard(resolver);

        var ruling = await guard.RuleOnAsync(Target("internal.example.org"), None);

        await Assert.That(ruling.Decision).IsEqualTo(HostScopeDecision.ResolvedNonGlobal);
        await Assert.That(ruling.MayDial).IsFalse();
        await Assert.That(ruling.Offending).IsNotNull();
    }

    [Test]
    public async Task ANameResolvingToBothPublicAndPrivateIsRefusedRatherThanFiltered()
    {
        // The DNS-rebinding shape. Taking the address we liked and proceeding is the bug: a host whose
        // records straddle the boundary is telling us something inconsistent, and the safe reading of an
        // inconsistent answer is no.
        var resolver = new FakeHostResolver()
            .Resolving("rebind.example.org", "203.0.113.10", "10.0.0.5");
        var guard = new HostScopeGuard(resolver);

        var ruling = await guard.RuleOnAsync(Target("rebind.example.org"), None);

        await Assert.That(ruling.Decision).IsEqualTo(HostScopeDecision.ResolvedNonGlobal);
        await Assert.That(ruling.Offending).IsEqualTo(MsspHostScope.Private);
    }

    [Test]
    public async Task TheOrderOfTheAnswersDoesNotChangeTheRuling()
    {
        // Guards against the "first address wins" implementation, which passes the test above by luck
        // whenever the private record happens to sort first.
        var guard = new HostScopeGuard(new FakeHostResolver()
            .Resolving("a.example.org", "10.0.0.5", "203.0.113.10")
            .Resolving("b.example.org", "203.0.113.10", "10.0.0.5"));

        await Assert.That((await guard.RuleOnAsync(Target("a.example.org"), None)).MayDial).IsFalse();
        await Assert.That((await guard.RuleOnAsync(Target("b.example.org"), None)).MayDial).IsFalse();
    }

    [Test]
    public async Task AnOperatorSeedPointingAtLoopbackIsAllowedWithoutAskingDns()
    {
        // Somebody running the crawler against their own test server has said what they mean. And the
        // exemption short-circuits: there is nothing to verify about an address a human typed.
        var resolver = new FakeHostResolver();
        var guard = new HostScopeGuard(resolver);

        var ruling = await guard.RuleOnAsync(Target("127.0.0.1", operatorSeed: true), None);

        await Assert.That(ruling.Decision).IsEqualTo(HostScopeDecision.OperatorSeed);
        await Assert.That(ruling.MayDial).IsTrue();
        await Assert.That(resolver.Asked).IsEmpty();
    }

    [Test]
    public async Task AReferralNamingLoopbackDirectlyIsStillRefusedHere()
    {
        // Task 8 already refuses this shape, but the guard must not assume Task 8 ran: an imported
        // target never passed through the referral writer at all.
        var guard = new HostScopeGuard(new FakeHostResolver());

        var ruling = await guard.RuleOnAsync(Target("127.0.0.1"), None);

        await Assert.That(ruling.Decision).IsEqualTo(HostScopeDecision.ResolvedNonGlobal);
    }

    [Test]
    public async Task ANameThatResolvesToNothingIsADeadHostRatherThanARefusal()
    {
        // Distinct on purpose: "we could not look it up" and "we looked it up and will not go there"
        // are different facts, and the loop treats them differently — the first is an ordinary DNS
        // failure with ordinary backoff.
        var guard = new HostScopeGuard(new FakeHostResolver().Failing("gone.example.org"));

        var ruling = await guard.RuleOnAsync(Target("gone.example.org"), None);

        await Assert.That(ruling.Decision).IsEqualTo(HostScopeDecision.Unresolvable);
        await Assert.That(ruling.MayDial).IsFalse();
    }

    [Test]
    public async Task AResolverThatThrowsIsUnresolvableAndNotAnAllow()
    {
        // Fail closed. A guard that lets a dial through because DNS was briefly unhappy is not a guard.
        var guard = new HostScopeGuard(new ThrowingResolver());

        var ruling = await guard.RuleOnAsync(Target("mud.example.org"), None);

        await Assert.That(ruling.Decision).IsEqualTo(HostScopeDecision.Unresolvable);
        await Assert.That(ruling.MayDial).IsFalse();
    }

    private sealed class ThrowingResolver : IHostResolver
    {
        public Task<IReadOnlyList<System.Net.IPAddress>> ResolveAsync(string host, CancellationToken ct) =>
            throw new System.Net.Sockets.SocketException(11001);
    }
}
```

- [ ] **Step 6: Write the guard**

Create `src/MUI.Discovery/HostScopeGuard.cs`:

```csharp
using System.Net;
using MUI.Crawl.Mssp;

namespace MUI.Discovery;

/// <summary>Where a name actually points, once DNS has answered.</summary>
public interface IHostResolver
{
    /// <summary>Every address the name resolves to. Empty when it resolves to none.</summary>
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct);
}

/// <summary>The real thing. Injected so no test in this suite performs a live lookup.</summary>
public sealed class SystemHostResolver : IHostResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            // A literal needs no lookup, and asking DNS about one invites a resolver to answer
            // something else.
            return [literal];
        }

        return await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
    }
}

/// <summary>What the guard decided, and why.</summary>
public enum HostScopeDecision
{
    /// <summary>Resolved, and every address is globally routable.</summary>
    Allowed,

    /// <summary>Exempt: a human operator configured this address (<see cref="CrawlTarget.IsOperatorSeed"/>).</summary>
    OperatorSeed,

    /// <summary>At least one resolved address is not globally routable. Refused (spec §7.2).</summary>
    ResolvedNonGlobal,

    /// <summary>DNS returned nothing, or failed. A dead host, not a refusal — but still not a dial.</summary>
    Unresolvable,
}

/// <summary>One ruling on one target.</summary>
public sealed record HostScopeRuling(
    HostScopeDecision Decision,
    IReadOnlyList<IPAddress> Addresses,
    MsspHostScope? Offending)
{
    public bool MayDial => Decision is HostScopeDecision.Allowed or HostScopeDecision.OperatorSeed;
}

/// <summary>
/// Spec §7.2, "The gate is on the resolved address, not the name".
/// </summary>
/// <remarks>
/// <para>
/// <b>Do not delete this as redundant with <see cref="MsspHost.IsCrawlable"/>. It is not.</b>
/// <c>IsCrawlable</c> classifies a string, and <see cref="MsspHostScope.Unresolved"/> — which every DNS
/// name is — counts as crawlable, correctly, because nothing can be known about a name until DNS
/// answers. The consequence is that the literal-address checks are worth nothing against an attacker
/// who owns a domain: a <c>REFERRAL</c> naming <c>internal.example.org</c>, with an A record pointing at
/// <c>10.0.0.5</c>, passes every check Task 8 makes. Publishing that record costs nothing, so a DNS name
/// is the cheapest bypass of the one gate §7.2 exists to provide. The check has to happen after
/// resolution, and <c>MsspHost</c> never sees a resolution.
/// </para>
/// <para>
/// <b>Any non-global address refuses the whole target.</b> Not the first one found, and the good ones
/// are not filtered out and used: a name resolving to one public and one private address is the
/// DNS-rebinding shape, and proceeding on the half we liked is the bug rather than the mitigation.
/// </para>
/// <para>
/// <b>Known limitation, stated rather than implied</b> (§7.2's own words). This is a
/// time-of-check-to-time-of-use gap: the name is resolved, then connected by name, so a DNS answer that
/// changes in between is not caught. The fix is to connect to the pinned <see cref="IPAddress"/> that
/// was checked rather than re-resolving — a <c>MUI.Crawl.Transport.TcpTransport</c> change, Plan 01's
/// to make, and worth doing. Caching resolutions would <em>widen</em> this window, so the crawler
/// resolves per dial. <b>Do not restate this guard as airtight; it raises the cost of the attack, it
/// does not close it.</b>
/// </para>
/// <para>
/// <b>A refusal is not a <c>ProbeOutcome</c>, and there is deliberately no <c>Refused</c> member to
/// reach for.</b> <c>ProbeOutcome</c> has exactly two members, <c>Answered</c> and <c>Failed</c>, and
/// <em>both mean the socket was opened</em>. This guard runs <b>before</b> a <see cref="ProbeResult"/>
/// exists at all, so there is no honest route by which a refusal could produce an availability row —
/// which is how §7.2's "a refusal writes no availability sample" is satisfied structurally rather than
/// by a check somebody has to remember. <b>The tempting shortcut is
/// <c>ProbeResult.Failed(ProbeFailureCauses.Refused, …)</c>, and it is wrong twice:</b>
/// <c>FailureCause.Refused</c> means the far end sent an RST — a real measurement of a real host — so
/// dressing a policy refusal as a probe failure makes the two permanently inseparable downstream, and
/// it writes our own security policy into a game's public reachability history, which is exactly what
/// §7.2 forbids. A refusal is counted on <c>CrawlCycle.Refused</c> instead, which belongs to whatever
/// owns the dial. Do not add the enum member; do not manufacture a <c>ProbeResult</c> here.
/// </para>
/// <para>
/// The range checks themselves are <see cref="MsspHost.ScopeOf(IPAddress)"/>'s. Writing a second copy
/// here would be two sets of rules that must agree for ever, and the day they disagree is the day one
/// of them is wrong about <c>169.254.169.254</c>.
/// </para>
/// </remarks>
public sealed class HostScopeGuard(IHostResolver resolver)
{
    public async Task<HostScopeRuling> RuleOnAsync(CrawlTarget target, CancellationToken ct)
    {
        if (target.IsOperatorSeed)
        {
            // Nothing to verify about an address a human configured, and no lookup worth making.
            return new HostScopeRuling(HostScopeDecision.OperatorSeed, [], null);
        }

        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = await resolver.ResolveAsync(target.Host, ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Fail closed. A guard that allows a dial because DNS was briefly unhappy is not a guard.
            return new HostScopeRuling(HostScopeDecision.Unresolvable, [], null);
        }

        if (addresses.Count == 0)
        {
            return new HostScopeRuling(HostScopeDecision.Unresolvable, [], null);
        }

        // Every address, not the first: the answer as a whole has to be clean.
        foreach (var address in addresses)
        {
            var scope = MsspHost.ScopeOf(address);
            if (scope is not MsspHostScope.Global)
            {
                return new HostScopeRuling(HostScopeDecision.ResolvedNonGlobal, addresses, scope);
            }
        }

        return new HostScopeRuling(HostScopeDecision.Allowed, addresses, null);
    }
}
```

- [ ] **Step 7: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS — four `HostGateTests`, seven `CrawlRateLimiterTests`, and seventeen
`HostScopeGuardTests` (the `[Arguments]` case counts ten).

If `ANameResolvingIntoOurOwnNetworkIsRefused` fails on one address, the gap is in
`MsspHost.ScopeOf` and the fix belongs in Plan 01's suite, not here — add the case there first.

- [ ] **Step 8: Commit**

```bash
git add src/MUI.Discovery/HostGate.cs src/MUI.Discovery/CrawlRateLimiter.cs \
        src/MUI.Discovery/HostScopeGuard.cs \
        tests/MUI.Discovery.Tests/Support/FakeHostResolver.cs \
        tests/MUI.Discovery.Tests/HostGateTests.cs \
        tests/MUI.Discovery.Tests/CrawlRateLimiterTests.cs \
        tests/MUI.Discovery.Tests/HostScopeGuardTests.cs
git commit -m "feat: the three gates a dial passes, including §7.2 on the resolved address"
```

---

### Task 10: The scored identity matcher

**Files:**
- Modify: `src/MUI.Discovery/Identity.cs` (append everything but `IdentityWeights`, which Task 2 wrote)
- Create: `src/MUI.Discovery/IdentityMatcher.cs`
- **Modify (do not create):** `tests/MUI.Discovery.Tests/Support/InMemoryRepositories.cs` — **Plan 02
  Task 13 declares `InMemoryGameRepository`, `InMemoryEndpointRepository`,
  `InMemoryGameFieldRepository`, `InMemoryPresenceRepository` and `InMemoryAvailabilityRepository`
  there.** This is one test assembly, so a second file declaring any of them is a duplicate-type
  compile error. This plan adds one member to one of them; see Step 1.
- Test: `tests/MUI.Discovery.Tests/IdentityMatcherTests.cs`

**Interfaces:**
- Consumes: Plan 2's `IGameRepository`, `IEndpointRepository`, `IGameFieldRepository`, `Game`,
  `GameField`, `GameEndpoint`, `GameQuery`, `FieldSource`, `FieldConfidence`, `EndpointKind`,
  `EndpointState`, `LifecycleState`; **Plan 02 Task 13's test doubles `InMemoryGameRepository`,
  `InMemoryEndpointRepository` and `InMemoryGameFieldRepository`** from
  `tests/MUI.Discovery.Tests/Support/InMemoryRepositories.cs`, which this plan uses as they are and
  extends by exactly one member (Step 1); `BannerFingerprint` (Task 6); `DiscoveryOptions` (Task 2); and
  **Plan 06's `MUI.Catalog.ClaimVocabulary`** — `MsspVariable`, `ConnectScreenPrefix`, `DnsLabel`, the
  wire spellings of §8's three channels. It lives in `MUI.Catalog` because the claim flow that *tells
  an operator what to set* and this reader that *looks for it* must agree, and `MUI.Catalog` is the
  only project both can see. **This plan reads those constants and declares none of them.** If Plan 06
  has not landed, add `ClaimVocabulary` to `MUI.Catalog` with the three values above — it is three
  `const string`s with no dependencies — rather than declaring literals here.
- Produces:
  - `MUI.Discovery.IdentitySignal(string Name, double Weight, bool Matched)`
  - `MUI.Discovery.IdentityScore(Guid? CandidateGameId, double Score, IReadOnlyList<IdentitySignal> Signals)`
  - `MUI.Discovery.IdentityVerdict` with nested `Merge(Guid GameId, IdentityScore Score)`,
    `Review(Guid GameId, IdentityScore Score)`, `Fresh(IdentityScore? Best)`
  - `MUI.Discovery.IdentityFields` — `Name`, `Created`, `BannerHash`, `Website`, `Contact`,
    `Codebase`, `ClaimToken`, `Endpoint` (the `game_field.field` strings)
  - `MUI.Discovery.ClaimTokenBeacon` — `const string MsspVariable`, `const string ConnectScreenPrefix`
    (both aliasing `MUI.Catalog.ClaimVocabulary`, not declaring their own literals), and
    `static string? Read(ProbeResult)`. **Named `ClaimTokenBeacon` and not `ClaimToken`**: Plan 06 owns
    `MUI.Catalog.ClaimToken`, the record modelling an *issued* token, and `MUI.Discovery` references
    `MUI.Catalog`, so both would be in scope in every file here. This one reads a beacon off a probe;
    Plan 06's models and issues the token. `Read` loops
    `ClaimVocabulary.AcceptedMsspVariables` rather than probing one variable, and falls back from the
    labelled connect-screen form to a bare `ClaimTokenFormat.FindIn` scan — **Plan 06 Task 4 Step 6
    carries the authoritative source for this method; take it from there rather than re-deriving it.**
  - `MUI.Discovery.IdentitySignals` — `static string ToJson(IReadOnlyList<IdentitySignal>)`
  - `MUI.Discovery.IGameFieldIndex` — `Task<IReadOnlyList<Guid>> GamesWithFieldAsync(string field, string value, CancellationToken ct)`
  - `MUI.Discovery.IdentityMatcher(IGameRepository games, IEndpointRepository endpoints, IGameFieldRepository fields, IGameFieldIndex index, DiscoveryOptions options)`
    with `Task<IdentityVerdict> ResolveAsync(ProbeResult result, CancellationToken ct)`
  - **On Plan 02's existing `InMemoryGameFieldRepository`**, one added interface and one added method:
    `InMemoryGameFieldRepository : IGameFieldRepository, IGameFieldIndex`, with
    `GamesWithFieldAsync`. It goes on the *existing* type deliberately — the forward store and the
    reverse index then read one dictionary, so a test cannot set up a field the matcher then fails to
    find. `InMemoryGameRepository` is **not** given `IGameFieldIndex`; the reverse lookup is about
    fields, not games.

- [ ] **Step 1: Add the reverse index to Plan 02's field double**

**Do not create `InMemoryGameRepository.cs`, `InMemoryEndpointRepository.cs` or
`InMemoryGameFieldRepository.cs`.** Plan 02 Task 13 declares all three types in
`tests/MUI.Discovery.Tests/Support/InMemoryRepositories.cs`, and this is one test assembly — a second
file declaring any of them fails the build with CS0101 rather than merging. `InMemoryGameRepository`
and `InMemoryEndpointRepository` are used exactly as Plan 02 wrote them and need no change.

`InMemoryGameFieldRepository` needs one thing Plan 02's version does not have, because Plan 02 has no
reverse lookup to fake: `IGameFieldIndex`. **Extend the existing type — do not write a second one.**
Open `Support/InMemoryRepositories.cs`, add the interface to its declaration and add the method:

```csharp
// Was: public sealed class InMemoryGameFieldRepository : IGameFieldRepository
public sealed class InMemoryGameFieldRepository : IGameFieldRepository, IGameFieldIndex
{
    // ... everything Plan 02 wrote is unchanged ...

    /// <summary>
    /// Added by Plan 03. The reverse lookup <see cref="IdentityMatcher"/> gathers candidates with.
    /// </summary>
    /// <remarks>
    /// It belongs on this type rather than on a second double, and that is not tidiness: the forward
    /// store and the reverse index then read <em>one</em> dictionary, so a test cannot seed a field
    /// through <c>UpsertAsync</c> that the matcher then fails to find. Two fakes over two dictionaries
    /// would let the matcher's tests pass against a world the matcher could never see.
    /// </remarks>
    public Task<IReadOnlyList<Guid>> GamesWithFieldAsync(string field, string value, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Guid>>(_fields.Values
            .Where(f => string.Equals(f.Field, field, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(f.Value.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(f => f.GameId)
            .Distinct()
            .ToList());
}
```

Two assumptions about Plan 02's file, both cheap to reconcile:

- **the backing dictionary is `_fields`, keyed by `(Guid GameId, string Field)`, holding `GameField`.**
  If Plan 02 keyed it differently, adapt the `Where` — the behaviour to preserve is
  *case-insensitive on both field name and value, trimmed on both sides, distinct game ids*, which is
  what `NpgsqlGameFieldIndex`'s `lower(value)` comparison does in Task 12. The two must agree, or the
  matcher passes its tests and misses candidates in production.
- **the double exposes `Changes`** (used by `MergeApplierTests`). If Plan 02's does not, add
  `public IReadOnlyList<FieldChange> Changes => _changes;` alongside — again on the existing type.

- [ ] **Step 2: Write the failing test**

Create `tests/MUI.Discovery.Tests/IdentityMatcherTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Discovery;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// The scored fingerprint of spec §7.3. Duplicate listings are the specific failure that clutters every
/// incumbent catalogue, and this is the component that prevents it.
/// </summary>
public class IdentityMatcherTests
{
    private static readonly DateTimeOffset Then = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class World
    {
        public InMemoryGameRepository Games { get; } = new();
        public InMemoryEndpointRepository Endpoints { get; } = new();
        public InMemoryGameFieldRepository Fields { get; } = new();
        public DiscoveryOptions Options { get; init; } = new();

        public IdentityMatcher Matcher => new(Games, Endpoints, Fields, Fields, Options);

        public async Task<Guid> GameAsync(string name, params (string Field, string Value)[] fields)
        {
            var id = Guid.CreateVersion7();
            await Games.InsertAsync(
                new Game(id, name.ToLowerInvariant(), name, LifecycleState.Active, false, Then, Then, null), None);

            foreach (var (field, value) in fields)
            {
                await Fields.UpsertAsync(
                    new GameField(id, field, value, FieldSource.Mssp, FieldConfidence.Reported, Then, Then), None);
            }

            return id;
        }

        public async Task EndpointAsync(Guid gameId, string host, int port) =>
            await Endpoints.UpsertAsync(
                new GameEndpoint(gameId, host, port, EndpointKind.Telnet, Then, Then, EndpointState.Active), None);
    }

    [Test]
    public async Task AnUnknownGameIsFresh()
    {
        var world = new World();

        var verdict = await world.Matcher.ResolveAsync(
            ProbeResults.Answered(mssp: ProbeResults.Mssp(("NAME", ["Corvid"]))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
        await Assert.That(((IdentityVerdict.Fresh)verdict).Best).IsNull();
    }

    [Test]
    public async Task AKnownEndpointIsTheGameAndMergesOnItsOwn()
    {
        // Weight 1.00 equals the auto-merge threshold, deliberately: a previously-seen (host, port) is
        // direct continuity and needs no corroboration.
        var world = new World();
        var corvid = await world.GameAsync("Corvid");
        await world.EndpointAsync(corvid, "mud.example.org", 4201);

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
        var merge = (IdentityVerdict.Merge)verdict;
        await Assert.That(merge.GameId).IsEqualTo(corvid);
        await Assert.That(merge.Score.Score).IsGreaterThanOrEqualTo(IdentityWeights.AutoMergeThreshold);
    }

    [Test]
    public async Task NameAndCreatedTogetherAreMiddlingAndOpenAReview()
    {
        var world = new World();
        await world.GameAsync("Corvid",
            (IdentityFields.Name, "Corvid"), (IdentityFields.Created, "2003"));

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.example.org",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), ("CREATED", ["2003"]))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
        await Assert.That(((IdentityVerdict.Review)verdict).Score.Score)
            .IsEqualTo(IdentityWeights.MsspNameAndCreated);
    }

    [Test]
    public async Task NameAloneScoresNothingBecauseCreatedIsWhatMakesItSpecific()
    {
        var world = new World();
        await world.GameAsync("Corvid",
            (IdentityFields.Name, "Corvid"), (IdentityFields.Created, "2003"));

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.example.org",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
        await Assert.That(((IdentityVerdict.Fresh)verdict).Best!.Score).IsEqualTo(0d);
    }

    [Test]
    public async Task NameAndCreatedWithABannerMatchIsEnoughToMerge()
    {
        // 0.60 + 0.50 = 1.10. This is the known-move shape, and it is the reason the two weights add to
        // more than the threshold rather than exactly to it.
        var world = new World();
        const string banner = "Welcome to Corvid.\nType 'connect'.";
        var corvid = await world.GameAsync("Corvid",
            (IdentityFields.Name, "Corvid"),
            (IdentityFields.Created, "2003"),
            (IdentityFields.BannerHash, BannerFingerprint.Of(banner)));

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.example.org",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), ("CREATED", ["2003"])),
            banner: banner), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
        await Assert.That(((IdentityVerdict.Merge)verdict).GameId).IsEqualTo(corvid);
    }

    [Test]
    public async Task TheSignalsAreReportedWhetherOrNotTheyMatched()
    {
        // A review is a thing a human reads. "Which six signals were considered and how did each land"
        // is the whole content of that judgement, so the losing signals are carried too.
        var world = new World();
        await world.GameAsync("Corvid", (IdentityFields.Name, "Corvid"), (IdentityFields.Created, "2003"));

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.example.org",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), ("CREATED", ["2003"]))), None);

        var score = ((IdentityVerdict.Review)verdict).Score;

        await Assert.That(score.Signals.Count).IsEqualTo(6);
        await Assert.That(score.Signals.Count(s => s.Matched)).IsEqualTo(1);
        await Assert.That(score.Signals.Sum(s => s.Weight)).IsGreaterThan(score.Score);
    }

    [Test]
    public async Task WebsiteAndCodebaseTogetherReachReviewButNotMerge()
    {
        // 0.40 + 0.15 = 0.55: worth a human's eye, nowhere near enough to fold two games together.
        var world = new World();
        await world.GameAsync("Corvid",
            (IdentityFields.Website, "https://corvid.example"),
            (IdentityFields.Codebase, "PennMUSH 1.8.8p2"));

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.example.org",
            mssp: ProbeResults.Mssp(
                ("WEBSITE", ["https://corvid.example"]),
                ("CODEBASE", ["PennMUSH 1.8.8p2"]))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
    }

    [Test]
    public async Task TheThresholdsAreConfigurableBecauseTheyNeedCalibrating()
    {
        // Spec §15.5. Ship conservative, tune against real data — without a redeploy of the constants.
        var strict = new World { Options = new DiscoveryOptions { AutoMergeThreshold = 2.0, ReviewThreshold = 1.5 } };
        var corvid = await strict.GameAsync("Corvid");
        await strict.EndpointAsync(corvid, "mud.example.org", 4201);

        var verdict = await strict.Matcher.ResolveAsync(ProbeResults.Answered(), None);

        // The endpoint's 1.00 no longer clears either bar.
        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task ACandidateWhoseGameHasGoneIsIgnoredRatherThanReturned()
    {
        var world = new World();
        await world.Endpoints.UpsertAsync(
            new GameEndpoint(Guid.CreateVersion7(), "mud.example.org", 4201,
                EndpointKind.Telnet, Then, Then, EndpointState.Active), None);

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Answered(), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task AFailedProbeResolvesToNothingRatherThanGuessing()
    {
        // Parsers never fabricate, and neither does this. A refused connection carries no MSSP, no
        // banner and no evidence of any kind.
        var world = new World();
        var corvid = await world.GameAsync("Corvid");
        await world.EndpointAsync(corvid, "mud.example.org", 4201);

        var verdict = await world.Matcher.ResolveAsync(ProbeResults.Failed(), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
        await Assert.That(((IdentityVerdict.Fresh)verdict).Best).IsNull();
    }
}
```

- [ ] **Step 3: Run it and verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `IdentityMatcher`, `IdentityFields`, `IdentityVerdict` do not exist.

- [ ] **Step 4: Append the identity vocabulary**

Append to `src/MUI.Discovery/Identity.cs`, adding `using System.Text.Json;`, `using MUI.Catalog;`,
`using MUI.Crawl;`, `using MUI.Crawl.Mssp;` and `using MUI.Crawl.Who;` at the top — `MUI.Catalog` for
`ClaimVocabulary` and `ClaimTokenFormat`, whose constants are the wire spellings this file reads and
never re-declares, and `MUI.Crawl.Who` for `AnsiText.Strip` (Plan 01), because a connect screen arrives
with colour in it and a beacon must not be missed for sitting inside an SGR run:

```csharp
/// <summary>One weighted signal and whether it fired, kept whether it fired or not.</summary>
/// <remarks>
/// The losing signals are carried deliberately: a review is a judgement a person makes, and "which of
/// the six were considered and how did each land" is the whole content of that judgement.
/// </remarks>
public sealed record IdentitySignal(string Name, double Weight, bool Matched);

/// <summary>How well one probe matched one candidate game.</summary>
public sealed record IdentityScore(
    Guid? CandidateGameId,
    double Score,
    IReadOnlyList<IdentitySignal> Signals);

/// <summary>What to do about it (spec §7.3).</summary>
public abstract record IdentityVerdict
{
    private IdentityVerdict()
    {
    }

    /// <summary>Above threshold: this probe is that game. The endpoint change is recorded as a FieldChange.</summary>
    public sealed record Merge(Guid GameId, IdentityScore Score) : IdentityVerdict;

    /// <summary>
    /// Middling: open a suspected-duplicate pair. Both pages stay live and link to each other
    /// reciprocally, because a wrongly hidden game is worse than a visible duplicate.
    /// </summary>
    public sealed record Review(Guid GameId, IdentityScore Score) : IdentityVerdict;

    /// <summary>Below threshold: a new game. <paramref name="Best"/> is null when there was no candidate at all.</summary>
    public sealed record Fresh(IdentityScore? Best) : IdentityVerdict;
}

/// <summary>
/// The <c>game_field.field</c> names the matcher compares on. These must be the same strings Plan 2's
/// <c>FieldRegistry</c> registers; <see cref="BannerHash"/> and <see cref="ClaimToken"/> are additions
/// this plan writes, and an unregistered name simply gets the registry's permissive default window.
/// <see cref="ClaimToken"/> here is a <em>field name</em>, <c>claim_token</c>, and keeps that spelling:
/// the type that reads a beacon off a probe is <see cref="ClaimTokenBeacon"/>, and the record modelling
/// an issued token is Plan 06's <c>MUI.Catalog.ClaimToken</c>.
/// </summary>
public static class IdentityFields
{
    public const string Name = "name";
    public const string Created = "created";
    public const string BannerHash = "banner_hash";
    public const string Website = "website";
    public const string Contact = "contact";
    public const string Codebase = "codebase";
    public const string ClaimToken = "claim_token";

    /// <summary>The pseudo-field a moved connection address is recorded under in the change feed.</summary>
    public const string Endpoint = "endpoint";
}

/// <summary>
/// Where a site-issued claim token may be <em>seen on the wire</em>, and how to read it off a probe
/// (spec §8, §7.3's beacon).
/// </summary>
/// <remarks>
/// <para>
/// <b>This type reads a beacon; it does not model a token.</b> The two halves of §8 belong to different
/// plans and must not be confused: Plan 06 owns <c>MUI.Catalog.ClaimToken</c> — the record for an
/// <em>issued</em> token, with its state, validity window and verification channel — and owns issuing
/// and verifying it. This plan owns only the reading: given a <see cref="ProbeResult"/>, what token
/// string, if any, did the server show us. That is why it is called <c>ClaimTokenBeacon</c>: both types
/// are in scope in every file here, because <c>MUI.Discovery</c> references <c>MUI.Catalog</c>.
/// </para>
/// <para>
/// Two of §8's three channels are visible to a probe — an MSSP variable and a line on the connect
/// screen. The third, a DNS TXT record at <c>ClaimVocabulary.DnsNameFor(host)</c>, is not: nothing in a
/// telnet session can see it, so Plan 06 carries its own <c>IDnsTxtResolver</c> for that channel. Until
/// Plan 06 lands and something writes a verified token into <c>game_field["claim_token"]</c>, this
/// signal never fires and the matcher scores as though the weight were absent — which is correct, not
/// degraded.
/// </para>
/// <para>
/// <b>The spellings themselves are not declared here.</b> They are
/// <see cref="MUI.Catalog.ClaimVocabulary"/>'s, because the claim flow that <em>tells an operator</em>
/// what to set and this reader that <em>looks for it</em> have to agree, and <c>MUI.Catalog</c> is the
/// only project both sides can see (<c>MUI.Catalog</c> may never reference <c>MUI.Discovery</c>). One
/// literal, one owner: an operator is told the same variable name the crawler looks for, and neither
/// half can drift without the other.
/// </para>
/// <para>
/// <b>Both channels accept more than one spelling, and that is not sloppiness.</b> The MSSP side loops
/// <see cref="ClaimVocabulary.AcceptedMsspVariables"/> — canonical <c>MUINDEX CLAIM</c> first, then
/// aliases including <c>CONTACT_TOKEN</c>, which is what the delivered design handoff shows an operator
/// typing. The connect-screen side tries the label and then falls back to scanning for a well-formed
/// bare token, because the handoff shows operators pasting the token in with no label at all. An
/// operator who does exactly what a screenshot told them must not be informed their claim failed;
/// that support mail is the thing §8's diagnostic exists to prevent. The bare scan is only safe because
/// <see cref="ClaimTokenFormat"/>'s rendering is self-identifying — <c>muidx-</c> plus three groups from
/// a fixed unambiguous alphabet is not a string that occurs in a MUSH banner by accident.
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

        // Stripped first: a beacon inside an SGR run is still a beacon, and a colourised banner is the
        // normal case rather than the exception.
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

/// <summary>Signals as stored on a merge or review row, so a decision can be explained later.</summary>
public static class IdentitySignals
{
    public static string ToJson(IReadOnlyList<IdentitySignal> signals) => JsonSerializer.Serialize(signals);
}

/// <summary>
/// The reverse field lookup identity needs: "which games carry this value for this field".
/// </summary>
/// <remarks>
/// Plan 2's <see cref="MUI.Storage.IGameFieldRepository"/> reads forward, by game id, which cannot
/// answer "who else calls themselves Corvid" without scanning every game. This is the missing arrow.
/// </remarks>
public interface IGameFieldIndex
{
    Task<IReadOnlyList<Guid>> GamesWithFieldAsync(string field, string value, CancellationToken ct);
}
```

- [ ] **Step 5: Write the matcher**

Create `src/MUI.Discovery/IdentityMatcher.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Crawl.Mssp;
using MUI.Discovery.Ownership;   // ClaimBeaconPolicy, BeaconVerdict — Plan 06
using MUI.Storage;

namespace MUI.Discovery;

/// <summary>
/// Scores a probe against the games it might already be (spec §7.3).
/// </summary>
/// <remarks>
/// <para>
/// Candidates are gathered by reverse lookup rather than by scanning: the endpoint, then every game
/// sharing this probe's claim token, name, banner hash, website or contact. Each candidate is then
/// scored over all six signals, so a candidate found by one signal is still credited for the others.
/// </para>
/// <para>
/// <b><paramref name="beacons"/> is optional, and what it guards is not optional at all.</b> A claim
/// token is <em>published</em> — that is the whole point of §8's channels — and it is weighted 10.0,
/// four times the auto-merge threshold. So on the bare comparison, anyone can read a claimed game's
/// MSSP, republish that token from their own host, and be auto-merged into that game. Secrecy cannot
/// fix a credential whose job is to be public. <c>ClaimBeaconPolicy</c> (Plan 06) narrows it: the token
/// is decisive on the game's own known endpoints, and from a strange host only once the game's own
/// addresses have stopped answering — which is what a genuine move looks like and what a clone does
/// not, because the original keeps answering. Where it declines, the score falls back to the other
/// signals and the verdict becomes <c>Review</c> rather than <c>Merge</c>, which is §7.3's own answer
/// under uncertainty. Passing <c>null</c> keeps the pre-claiming bare comparison, which is what this
/// plan's own tests assert; <b>a deployment that issues tokens must pass a policy.</b>
/// </para>
/// </remarks>
public sealed class IdentityMatcher(
    IGameRepository games,
    IEndpointRepository endpoints,
    IGameFieldRepository fields,
    IGameFieldIndex index,
    DiscoveryOptions options,
    ClaimBeaconPolicy? beacons = null)
{
    public async Task<IdentityVerdict> ResolveAsync(ProbeResult result, CancellationToken ct)
    {
        if (result.Outcome is not ProbeOutcome.Answered)
        {
            // No evidence of any kind. Guessing from an address alone is how duplicates and
            // mis-merges both happen.
            return new IdentityVerdict.Fresh(null);
        }

        var bannerHash = result.Banner is { Length: > 0 } banner ? BannerFingerprint.Of(banner) : null;
        var token = ClaimTokenBeacon.Read(result);
        var endpoint = await endpoints.ByAddressAsync(result.Host, result.Port, ct);

        var candidates = new HashSet<Guid>();
        if (endpoint is not null)
        {
            candidates.Add(endpoint.GameId);
        }

        await GatherAsync(candidates, IdentityFields.ClaimToken, token, ct);
        await GatherAsync(candidates, IdentityFields.Name, result.Mssp.Name, ct);
        await GatherAsync(candidates, IdentityFields.BannerHash, bannerHash, ct);
        await GatherAsync(candidates, IdentityFields.Website, result.Mssp.Website, ct);
        await GatherAsync(candidates, IdentityFields.Contact, result.Mssp.Contact, ct);

        // CODEBASE is scored but never gathered on, deliberately. Nearly every MUSH in the catalogue
        // reports the same string, so gathering on it would make every probe's candidate set the whole
        // catalogue — at 0.15 it can corroborate a candidate found some other way and nothing else.

        IdentityScore? best = null;
        foreach (var candidate in candidates)
        {
            if (await games.ByIdAsync(candidate, ct) is null)
            {
                // An endpoint or field row outliving its game is a repair job, not a match.
                continue;
            }

            var score = await ScoreAsync(candidate, result, endpoint, bannerHash, token, ct);
            if (best is null || score.Score > best.Score)
            {
                best = score;
            }
        }

        if (best?.CandidateGameId is not { } gameId)
        {
            return new IdentityVerdict.Fresh(best);
        }

        return best.Score >= options.AutoMergeThreshold ? new IdentityVerdict.Merge(gameId, best)
            : best.Score >= options.ReviewThreshold ? new IdentityVerdict.Review(gameId, best)
            : new IdentityVerdict.Fresh(best);
    }

    private async Task GatherAsync(HashSet<Guid> candidates, string field, string? value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var id in await index.GamesWithFieldAsync(field, value.Trim(), ct))
        {
            candidates.Add(id);
        }
    }

    private async Task<IdentityScore> ScoreAsync(
        Guid gameId,
        ProbeResult result,
        GameEndpoint? endpoint,
        string? bannerHash,
        string? token,
        CancellationToken ct)
    {
        var stored = (await fields.ForGameAsync(gameId, ct))
            .ToDictionary(field => field.Field, field => field.Value, StringComparer.OrdinalIgnoreCase);

        var signals = new List<IdentitySignal>
        {
            new(nameof(IdentityWeights.Endpoint), IdentityWeights.Endpoint,
                endpoint is not null && endpoint.GameId == gameId),

            new(nameof(IdentityWeights.MsspNameAndCreated), IdentityWeights.MsspNameAndCreated,
                Same(stored, IdentityFields.Name, result.Mssp.Name)
                && Same(stored, IdentityFields.Created, result.Mssp.Default(MsspVariables.Created))),

            new(nameof(IdentityWeights.BannerHash), IdentityWeights.BannerHash,
                Same(stored, IdentityFields.BannerHash, bannerHash)),

            new(nameof(IdentityWeights.WebsiteOrContact), IdentityWeights.WebsiteOrContact,
                Same(stored, IdentityFields.Website, result.Mssp.Website)
                || Same(stored, IdentityFields.Contact, result.Mssp.Contact)),

            new(nameof(IdentityWeights.CodebaseAndVersion), IdentityWeights.CodebaseAndVersion,
                Same(stored, IdentityFields.Codebase, result.Mssp.Codebase)),

            new(nameof(IdentityWeights.ClaimToken), IdentityWeights.ClaimToken,
                await IsBeaconDecisiveAsync(gameId, stored, token, result.Host, ct)),
        };

        return new IdentityScore(gameId, signals.Where(s => s.Matched).Sum(s => s.Weight), signals);
    }

    /// <summary>
    /// Whether a presented claim token counts for this game.
    /// </summary>
    /// <remarks>
    /// With no policy supplied this is the bare value comparison every other signal uses, which is the
    /// pre-claiming behaviour and what this plan's tests assert. With one, the token is decisive only
    /// where <c>ClaimBeaconPolicy</c> says it could legitimately have come from this game — see the
    /// type's remarks for why a published credential needs that guard at all.
    /// </remarks>
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

        // No clock argument: ClaimBeaconPolicy takes no `now` (Plan 06). A policy that later needs
        // time takes an injected TimeProvider, so ManualTimeProvider can drive it — never an ambient
        // DateTimeOffset.UtcNow reached from inside this otherwise-deterministic matcher.
        return await beacons.WeighAsync(gameId, presented, storedToken, probedHost, ct)
            is BeaconVerdict.Decisive;
    }

    private static bool Same(IReadOnlyDictionary<string, string> stored, string field, string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate)
        && stored.TryGetValue(field, out var value)
        && string.Equals(value.Trim(), candidate.Trim(), StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS, all ten `IdentityMatcherTests`.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Discovery/Identity.cs src/MUI.Discovery/IdentityMatcher.cs \
        tests/MUI.Discovery.Tests/Support tests/MUI.Discovery.Tests/IdentityMatcherTests.cs
git commit -m "feat: the scored identity matcher, with configurable thresholds"
```

---

### Task 11: The identity corpus — known moves and deliberate near-collisions

**Files:**
- Test: `tests/MUI.Discovery.Tests/IdentityCorpusTests.cs`
- Modify: `src/MUI.Discovery/IdentityMatcher.cs` only if a corpus case fails

**Interfaces:**
- Consumes: everything Task 10 produced, plus `MUI.Catalog.ClaimVocabulary`. Adds no new types.

Spec §13 asks for the matcher to be "tested against known move events and against deliberate
near-collisions". Task 10 tested the mechanism; this task tests the *judgement*, and it is a separate
deliverable because a reviewer can reasonably accept the mechanism and reject the calibration.

One case here is not about judgement at all: `TheWireSpellingsAreWhatWeTellOperatorsToType` pins
`ClaimVocabulary`'s three literals. It is the single place in this plan where a wire spelling is the
subject rather than a dependency, and it earns its place because those strings are a published contract
with server operators shared across two plans — Plan 06 prints them in its claim instructions, this
plan looks for them, and a silent edit on either side breaks claiming with no failing test anywhere.

- [ ] **Step 1: Write the corpus**

Create `tests/MUI.Discovery.Tests/IdentityCorpusTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// Spec §13: the matcher against known move events and deliberate near-collisions. Each case names the
/// real-world shape it stands for.
/// </summary>
public class IdentityCorpusTests
{
    private static readonly DateTimeOffset Then = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly InMemoryGameRepository _games = new();
    private readonly InMemoryEndpointRepository _endpoints = new();
    private readonly InMemoryGameFieldRepository _fields = new();

    private IdentityMatcher Matcher => new(_games, _endpoints, _fields, _fields, new DiscoveryOptions());

    private async Task<Guid> GameAsync(string name, params (string Field, string Value)[] fields)
    {
        var id = Guid.CreateVersion7();
        await _games.InsertAsync(
            new Game(id, $"{name.ToLowerInvariant()}-{id:N}"[..20], name,
                LifecycleState.Active, IsClaimed: false, Then, Then, null), None);

        foreach (var (field, value) in fields)
        {
            await _fields.UpsertAsync(
                new GameField(id, field, value, FieldSource.Mssp, FieldConfidence.Reported, Then, Then), None);
        }

        return id;
    }

    private async Task EndpointAsync(Guid gameId, string host, int port) =>
        await _endpoints.UpsertAsync(
            new GameEndpoint(gameId, host, port, EndpointKind.Telnet, Then, Then, EndpointState.Active), None);

    // ---- Known move events ----

    [Test]
    public async Task AGameThatChangedHostingProviderIsRecognised()
    {
        // The commonest real move: new host, new port, same everything else. §5.5 says a game that moves
        // must not become unfindable, and this is the mechanism that keeps it findable.
        const string banner = "  ---===  CORVID  ===---\r\n  A place for slow stories.\r\n";
        var corvid = await GameAsync("Corvid",
            (IdentityFields.Name, "Corvid"),
            (IdentityFields.Created, "2003"),
            (IdentityFields.BannerHash, BannerFingerprint.Of(banner)),
            (IdentityFields.Website, "https://corvid.example"),
            (IdentityFields.Codebase, "PennMUSH 1.8.8p2"));
        await EndpointAsync(corvid, "old.hoster.example", 4201);

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.hoster.example", port: 6250,
            mssp: ProbeResults.Mssp(
                ("NAME", ["Corvid"]), ("CREATED", ["2003"]),
                ("WEBSITE", ["https://corvid.example"]), ("CODEBASE", ["PennMUSH 1.8.8p2"])),
            banner: banner), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
        await Assert.That(((IdentityVerdict.Merge)verdict).GameId).IsEqualTo(corvid);
    }

    [Test]
    public async Task AGameThatMovedAndRedesignedItsLoginScreenIsStillRecognised()
    {
        // The banner signal is the one that goes away on a redesign — by design. Name + CREATED +
        // WEBSITE is 1.00 exactly, which is what stops a redesign during a move minting a duplicate.
        var corvid = await GameAsync("Corvid",
            (IdentityFields.Name, "Corvid"),
            (IdentityFields.Created, "2003"),
            (IdentityFields.BannerHash, BannerFingerprint.Of("the old screen")),
            (IdentityFields.Website, "https://corvid.example"));
        await EndpointAsync(corvid, "old.hoster.example", 4201);

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "new.hoster.example",
            mssp: ProbeResults.Mssp(
                ("NAME", ["Corvid"]), ("CREATED", ["2003"]), ("WEBSITE", ["https://corvid.example"])),
            banner: "an entirely new screen"), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
        await Assert.That(((IdentityVerdict.Merge)verdict).GameId).IsEqualTo(corvid);
    }

    [Test]
    public async Task AGameThatMovedWithNothingButItsBannerOpensAReviewRatherThanMerging()
    {
        // A game with no MSSP at all that moved host. 0.50 is a real signal and not a proof, so both
        // pages stay live and a person decides.
        const string banner = "Welcome to the Rookery.";
        await GameAsync("Rookery", (IdentityFields.BannerHash, BannerFingerprint.Of(banner)));

        var verdict = await Matcher.ResolveAsync(
            ProbeResults.Answered(host: "moved.example.org", banner: banner), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
    }

    // ---- Deliberate near-collisions ----

    [Test]
    public async Task TwoGamesOnOneHostWithDifferentPortsStaySeparate()
    {
        // Shared hosting, and a common shape for a hobby community running several games on one box.
        // The endpoint signal is keyed on (host, port) precisely so this does not collapse.
        var first = await GameAsync("Corvid",
            (IdentityFields.Name, "Corvid"), (IdentityFields.Created, "2003"),
            (IdentityFields.Codebase, "PennMUSH 1.8.8p2"));
        await EndpointAsync(first, "shared.example.org", 4201);

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "shared.example.org", port: 4202,
            mssp: ProbeResults.Mssp(
                ("NAME", ["Magpie"]), ("CREATED", ["2019"]), ("CODEBASE", ["PennMUSH 1.8.8p2"]))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
        await Assert.That(((IdentityVerdict.Fresh)verdict).Best?.Score ?? 0d)
            .IsLessThan(IdentityWeights.ReviewThreshold);
    }

    [Test]
    public async Task TwoGamesCalledFantasyMudWithNothingElseInCommonStaySeparate()
    {
        // The generic-name collision. NAME alone must score nothing, or every "Fantasy MUD",
        // "The Realm" and "Midnight Sun" in the hobby folds into one page.
        await GameAsync("Fantasy MUD",
            (IdentityFields.Name, "Fantasy MUD"),
            (IdentityFields.Created, "1997"),
            (IdentityFields.Website, "https://one.example"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "other.example.org",
            mssp: ProbeResults.Mssp(
                ("NAME", ["Fantasy MUD"]), ("CREATED", ["2021"]), ("WEBSITE", ["https://two.example"]))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task TwoGamesSharingOnlyACodebaseStaySeparate()
    {
        // Nearly every MUSH in the catalogue will report the same CODEBASE string. 0.15 alone must be
        // far below the review bar or the review queue becomes the whole catalogue.
        await GameAsync("Corvid", (IdentityFields.Codebase, "PennMUSH 1.8.8p2"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "other.example.org",
            mssp: ProbeResults.Mssp(("NAME", ["Magpie"]), ("CODEBASE", ["PennMUSH 1.8.8p2"]))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }

    [Test]
    public async Task TwoGamesSharingOneAdminsContactAddressOnlyReachReview()
    {
        // One person running two games, with the same CONTACT on both. Worth a look; never an automatic
        // merge, because folding a person's two games together is a visible, embarrassing error.
        await GameAsync("Corvid", (IdentityFields.Contact, "admin@example.org"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "other.example.org",
            mssp: ProbeResults.Mssp(("NAME", ["Magpie"]), ("CONTACT", ["admin@example.org"]))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Review>();
    }

    // ---- The claim token ----

    [Test]
    public async Task TheWireSpellingsAreWhatWeTellOperatorsToType()
    {
        // The ONE place in this suite where the literal is the subject rather than a dependency. These
        // three strings are a published contract with server operators: they appear in Plan 06's claim
        // instructions and in this plan's reader, and if either side edits one the other stops seeing a
        // token that is sitting right there in the MSSP report. Changing a value here is changing what
        // every already-claimed game has typed into its config, so it is a migration, not an edit.
        await Assert.That(ClaimVocabulary.MsspVariable).IsEqualTo("MUINDEX CLAIM");
        await Assert.That(ClaimVocabulary.ConnectScreenPrefix).IsEqualTo("MUINDEX-CLAIM:");
        await Assert.That(ClaimVocabulary.DnsLabel).IsEqualTo("_muindex");

        // The canonical name must be first, because Read returns the first accepted variable it finds
        // and a game setting both should be read as having set the canonical one.
        await Assert.That(ClaimVocabulary.AcceptedMsspVariables[0]).IsEqualTo(ClaimVocabulary.MsspVariable);

        // And the alias the delivered design handoff actually shows an operator typing.
        await Assert.That(ClaimVocabulary.AcceptedMsspVariables).Contains("CONTACT_TOKEN");

        // And the reader really does read the shared constants rather than a copy that could drift.
        await Assert.That(ClaimTokenBeacon.MsspVariable).IsEqualTo(ClaimVocabulary.MsspVariable);
        await Assert.That(ClaimTokenBeacon.ConnectScreenPrefix).IsEqualTo(ClaimVocabulary.ConnectScreenPrefix);
    }

    [Test]
    public async Task AnOperatorWhoFollowedTheScreenshotIsStillRecognised()
    {
        // The delivered handoff shows `mssp CONTACT_TOKEN/…` and shows the bare token pasted into the
        // banner with no label. Both are accepted, because someone who did exactly what the design told
        // them must not be informed their claim failed — that support mail is what §8's diagnostic
        // exists to prevent. Canonical spellings are covered by the two tests below.
        var token = ClaimTokenFormat.New();
        var alias = await GameAsync("Alias", (IdentityFields.ClaimToken, token));

        var viaAlias = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "somewhere.else.example",
            mssp: ProbeResults.Mssp(("NAME", ["Totally Different"]), ("CONTACT_TOKEN", [token]))), None);

        await Assert.That(viaAlias).IsTypeOf<IdentityVerdict.Merge>();
        await Assert.That(((IdentityVerdict.Merge)viaAlias).GameId).IsEqualTo(alias);

        var viaBareBanner = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "elsewhere.example",
            banner: $"Welcome to the game.\r\n\r\n  {token}\r\n\r\nType 'connect'.\r\n"), None);

        await Assert.That(viaBareBanner).IsTypeOf<IdentityVerdict.Merge>();
        await Assert.That(((IdentityVerdict.Merge)viaBareBanner).GameId).IsEqualTo(alias);
    }

    [Test]
    public async Task AClaimedGameIsNeverDuplicated()
    {
        // §7.3: "decisive when present — a claimed game is never duplicated". Everything else disagrees
        // here: different host, different name, different banner, no shared field at all.
        //
        // This matcher is built with no ClaimBeaconPolicy, so it takes the bare-comparison arm — which
        // is exactly the arm a stranger can exploit by republishing a public token, and exactly why a
        // deployment that issues tokens passes a policy. Plan 06 Task 10 owns the guarded cases; what
        // is asserted here is that the signal reaches the threshold at all.
        var corvid = await GameAsync("Corvid", (IdentityFields.ClaimToken, "7f3a91c4e2"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "somewhere.else.example", port: 9999,
            mssp: ProbeResults.Mssp(
                ("NAME", ["Totally Different"]),
                (ClaimVocabulary.MsspVariable, ["7f3a91c4e2"]))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
        await Assert.That(((IdentityVerdict.Merge)verdict).GameId).IsEqualTo(corvid);
    }

    [Test]
    public async Task AClaimTokenOnTheConnectScreenIsJustAsDecisive()
    {
        // §8's second channel. A server operator who cannot edit MSSP can always edit the login screen.
        var corvid = await GameAsync("Corvid", (IdentityFields.ClaimToken, "7f3a91c4e2"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "somewhere.else.example",
            banner: $"Welcome!\r\n{ClaimVocabulary.ConnectScreenPrefix} 7f3a91c4e2\r\nType 'connect'.\r\n"), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Merge>();
        await Assert.That(((IdentityVerdict.Merge)verdict).GameId).IsEqualTo(corvid);
    }

    [Test]
    public async Task AWrongClaimTokenIsNotASignalAtAll()
    {
        // Weight 10.0 means a token that matched the wrong game would be unrecoverable, so the match is
        // exact and a near miss contributes nothing.
        await GameAsync("Corvid", (IdentityFields.ClaimToken, "7f3a91c4e2"));

        var verdict = await Matcher.ResolveAsync(ProbeResults.Answered(
            host: "somewhere.else.example",
            mssp: ProbeResults.Mssp(
                ("NAME", ["Corvid"]),
                (ClaimVocabulary.MsspVariable, ["7f3a91c4e3"]))), None);

        await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
    }
}
```

- [ ] **Step 2: Run the corpus**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS, all twelve. If a judgement case fails, fix `IdentityMatcher` — the corpus encodes
§7.3's judgement and the weights in `IdentityWeights` are what implements it. Do **not** move a weight
to make a case pass without re-running every other case in this file and in `IdentityMatcherTests`. If
`TheWireSpellingsAreWhatWeTellOperatorsToType` fails, do **not** change the expected strings: somebody
edited a published contract, and the fix is in `MUI.Catalog.ClaimVocabulary`.

- [ ] **Step 3: Record what needs calibrating**

Append to `src/MUI.Discovery/Identity.cs`, inside `IdentityWeights`, above `AutoMergeThreshold`:

```csharp
    // Spec §15.5 — open question. These thresholds are reasoned but unvalidated: they need calibration
    // against real data, so they ship conservative and DiscoveryOptions is what a deployment tunes.
    // The corpus in IdentityCorpusTests is the thing to re-run after any change; if a real merge is
    // ever reverted twice for the same shape, that shape belongs in the corpus before the number moves.
```

- [ ] **Step 4: Commit**

```bash
git add tests/MUI.Discovery.Tests/IdentityCorpusTests.cs src/MUI.Discovery/Identity.cs
git commit -m "test: the identity corpus — real move events and the collisions that must not merge"
```

---

### Task 12: `merge_log` — merges that are reversible, logged, and never delete anything

**Files:**
- Create: `src/MUI.Storage/Migrations/0012_merge_log.sql`
- Create: `src/MUI.Discovery/MergeLog.cs`
- Create: `src/MUI.Discovery/Storage/NpgsqlMergeLog.cs`
- Create: `src/MUI.Discovery/Storage/NpgsqlGameFieldIndex.cs`
- Test: `tests/MUI.Discovery.Tests/MergeLogTests.cs`

**Interfaces:**
- Consumes: `IGameFieldIndex` (Task 10), `PostgresFixture` (Task 1), Plan 2's `game` and `game_field`.
- Produces: `MUI.Discovery.MergeRecord(Guid Id, Guid IntoGameId, Guid FromGameId, double Score, string SignalsJson, DateTimeOffset At, DateTimeOffset? RevertedAt)`;
  `MUI.Discovery.IMergeLog` with `RecordAsync`, `RevertAsync`, `ForGameAsync`;
  `MUI.Discovery.Storage.NpgsqlMergeLog(NpgsqlDataSource source) : IMergeLog`;
  `MUI.Discovery.Storage.NpgsqlGameFieldIndex(NpgsqlDataSource source) : IGameFieldIndex`.

**How a merge is represented, and why it is trivially reversible.** A merge does **not** move endpoint
rows, field rows or history from one game to another. It writes a `merge_log` row and sets
`game.merged_into_game_id` on the absorbed game — a redirect. Its page 301s, listings skip it, and its
own history stays exactly where it was. Reverting is therefore clearing one column, which is why
"merges are reversible and logged" (§7.3) costs no bookkeeping and cannot half-fail. It is also the only
representation consistent with rule 3 of `CLAUDE.md`: nothing is ever deleted.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/MergeLogTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery;
using MUI.Discovery.Storage;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// "Merges are reversible and logged" (spec §7.3). A merge is a redirect, so reverting is undoing one
/// column and the absorbed game's own history never went anywhere.
/// </summary>
[NotInParallel]
public class MergeLogTests
{
    private static readonly DateTimeOffset Then = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task AMergeRedirectsTheAbsorbedGameAndIsRecorded()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var log = new NpgsqlMergeLog(source);

        var into = await PostgresFixture.InsertGameAsync(source, "Corvid");
        var from = await PostgresFixture.InsertGameAsync(source, "CorvidDuplicate");

        var mergeId = await log.RecordAsync(
            new MergeRecord(Guid.CreateVersion7(), into, from, 1.1, """[{"Name":"Endpoint"}]""", Then, null), None);

        var redirect = await PostgresFixture.ScalarAsync<Guid?>(
            source, "SELECT merged_into_game_id FROM game WHERE id = @from", new { from });

        await Assert.That(redirect).IsEqualTo(into);
        await Assert.That((await log.ForGameAsync(into, None)).Single().Id).IsEqualTo(mergeId);
        await Assert.That((await log.ForGameAsync(from, None)).Single().Id).IsEqualTo(mergeId);
    }

    [Test]
    public async Task AMergeNeverDeletesTheAbsorbedGame()
    {
        // CLAUDE.md rule 3. The page, the URL and the history survive a merge exactly as they survive
        // archiving.
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var log = new NpgsqlMergeLog(source);

        var into = await PostgresFixture.InsertGameAsync(source, "Corvid");
        var from = await PostgresFixture.InsertGameAsync(source, "CorvidDuplicate");
        await log.RecordAsync(new MergeRecord(Guid.CreateVersion7(), into, from, 1.1, "[]", Then, null), None);

        var stillThere = await PostgresFixture.ScalarAsync<int>(
            source, "SELECT count(*) FROM game WHERE id = @from", new { from });

        await Assert.That(stillThere).IsEqualTo(1);
    }

    [Test]
    public async Task ARevertedMergeRestoresBothGames()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var log = new NpgsqlMergeLog(source);

        var into = await PostgresFixture.InsertGameAsync(source, "Corvid");
        var from = await PostgresFixture.InsertGameAsync(source, "Magpie");
        var mergeId = await log.RecordAsync(
            new MergeRecord(Guid.CreateVersion7(), into, from, 0.6, "[]", Then, null), None);

        await log.RevertAsync(mergeId, Then.AddDays(1), None);

        var redirect = await PostgresFixture.ScalarAsync<Guid?>(
            source, "SELECT merged_into_game_id FROM game WHERE id = @from", new { from });
        var both = await PostgresFixture.ScalarAsync<int>(
            source, "SELECT count(*) FROM game WHERE id IN (@into, @from)", new { into, from });
        var record = (await log.ForGameAsync(into, None)).Single();

        await Assert.That(redirect).IsNull();
        await Assert.That(both).IsEqualTo(2);
        await Assert.That(record.RevertedAt).IsEqualTo(Then.AddDays(1));
    }

    [Test]
    public async Task RevertingTwiceDoesNotRewriteTheFirstRevertsTimestamp()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var log = new NpgsqlMergeLog(source);

        var into = await PostgresFixture.InsertGameAsync(source, "Corvid");
        var from = await PostgresFixture.InsertGameAsync(source, "Magpie");
        var mergeId = await log.RecordAsync(
            new MergeRecord(Guid.CreateVersion7(), into, from, 0.6, "[]", Then, null), None);

        await log.RevertAsync(mergeId, Then.AddDays(1), None);
        await log.RevertAsync(mergeId, Then.AddDays(9), None);

        await Assert.That((await log.ForGameAsync(from, None)).Single().RevertedAt)
            .IsEqualTo(Then.AddDays(1));
    }

    [Test]
    public async Task AGameCannotBeMergedIntoItself()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var log = new NpgsqlMergeLog(source);
        var game = await PostgresFixture.InsertGameAsync(source, "Corvid");

        await Assert.That(async () => await log.RecordAsync(
                new MergeRecord(Guid.CreateVersion7(), game, game, 1.0, "[]", Then, null), None))
            .Throws<Npgsql.PostgresException>();
    }

    [Test]
    public async Task TheFieldIndexFindsAGameByAFieldValueCaseInsensitively()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var gameId = await PostgresFixture.InsertGameAsync(source, "Corvid");

        await new MUI.Storage.NpgsqlGameFieldRepository(source).UpsertAsync(
            new GameField(gameId, IdentityFields.Name, "Corvid", FieldSource.Mssp,
                FieldConfidence.Reported, Then, Then), None);

        var index = new NpgsqlGameFieldIndex(source);

        await Assert.That(await index.GamesWithFieldAsync(IdentityFields.Name, "corvid", None))
            .IsEquivalentTo(new[] { gameId });
        await Assert.That(await index.GamesWithFieldAsync(IdentityFields.Name, "Magpie", None)).IsEmpty();
    }
}
```

- [ ] **Step 2: Run it and verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `MergeRecord`, `IMergeLog`, `NpgsqlMergeLog`, `NpgsqlGameFieldIndex` do not exist.

- [ ] **Step 3: Write the migration**

Create `src/MUI.Storage/Migrations/0012_merge_log.sql`:

```sql
-- Spec §7.3. "Merges are reversible and logged."
--
-- A merge is a REDIRECT, not a move. Nothing is copied between games and nothing is deleted: the
-- absorbed game keeps its endpoints, its fields, its presence history and its URL, and gains a pointer
-- saying where a reader should be sent. Reverting is therefore clearing one column, which is why a
-- revert cannot half-fail and why a wrong merge is a recoverable mistake rather than a lossy one.

ALTER TABLE game ADD COLUMN merged_into_game_id uuid NULL REFERENCES game (id) ON DELETE SET NULL;

CREATE INDEX game_merged_into_idx ON game (merged_into_game_id) WHERE merged_into_game_id IS NOT NULL;

CREATE TABLE merge_log (
    id           uuid             PRIMARY KEY,
    into_game_id uuid             NOT NULL REFERENCES game (id) ON DELETE CASCADE,
    from_game_id uuid             NOT NULL REFERENCES game (id) ON DELETE CASCADE,
    score        double precision NOT NULL,

    -- Every signal that was considered and how each landed, so a decision can be explained a year
    -- later — including the ones that did not fire.
    signals_json jsonb            NOT NULL,

    at           timestamptz      NOT NULL,
    reverted_at  timestamptz      NULL,

    CONSTRAINT merge_log_not_self CHECK (into_game_id <> from_game_id)
);

CREATE INDEX merge_log_into_idx ON merge_log (into_game_id);
CREATE INDEX merge_log_from_idx ON merge_log (from_game_id);

-- The identity matcher's reverse lookup: "which games carry this value for this field". Without the
-- expression index every probe would sequential-scan game_field five times.
CREATE INDEX game_field_value_idx ON game_field (field, lower(value));
```

- [ ] **Step 4: Write the record and interface**

Create `src/MUI.Discovery/MergeLog.cs`:

```csharp
namespace MUI.Discovery;

/// <summary>
/// One merge, and the evidence for it (spec §7.3).
/// </summary>
/// <remarks>
/// A merge is a redirect on <c>game.merged_into_game_id</c>. Nothing is moved and nothing is deleted,
/// so <see cref="RevertedAt"/> being set restores both games completely.
/// </remarks>
public sealed record MergeRecord(
    Guid Id,
    Guid IntoGameId,
    Guid FromGameId,
    double Score,
    string SignalsJson,
    DateTimeOffset At,
    DateTimeOffset? RevertedAt);

/// <summary>
/// The merge audit trail. Recording a merge <em>is</em> performing it: the implementation writes the
/// row and the redirect in one transaction, so a merge cannot exist unlogged and a log entry cannot
/// describe a merge that did not happen.
/// </summary>
public interface IMergeLog
{
    Task<Guid> RecordAsync(MergeRecord record, CancellationToken ct);

    Task RevertAsync(Guid mergeId, DateTimeOffset at, CancellationToken ct);

    /// <summary>Every merge this game was on either side of, reverted or not.</summary>
    Task<IReadOnlyList<MergeRecord>> ForGameAsync(Guid gameId, CancellationToken ct);
}
```

- [ ] **Step 5: Write the two storage classes**

Create `src/MUI.Discovery/Storage/NpgsqlMergeLog.cs`:

```csharp
using Dapper;
using Npgsql;

namespace MUI.Discovery.Storage;

public sealed class NpgsqlMergeLog(NpgsqlDataSource source) : IMergeLog
{
    public async Task<Guid> RecordAsync(MergeRecord record, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO merge_log (id, into_game_id, from_game_id, score, signals_json, at, reverted_at)
            VALUES (@Id, @IntoGameId, @FromGameId, @Score, @SignalsJson::jsonb, @At, @RevertedAt);
            """,
            record, transaction, cancellationToken: ct));

        // The redirect, in the same transaction. A merge that was logged but not applied — or applied
        // but not logged — would be worse than either.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE game SET merged_into_game_id = @IntoGameId WHERE id = @FromGameId;",
            new { record.IntoGameId, record.FromGameId }, transaction, cancellationToken: ct));

        await transaction.CommitAsync(ct);
        return record.Id;
    }

    public async Task RevertAsync(Guid mergeId, DateTimeOffset at, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // `reverted_at IS NULL` guards a second revert from rewriting when the first one happened.
        var reverted = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE merge_log SET reverted_at = @at WHERE id = @mergeId AND reverted_at IS NULL;",
            new { mergeId, at }, transaction, cancellationToken: ct));

        if (reverted > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE game
                   SET merged_into_game_id = NULL
                 WHERE id = (SELECT from_game_id FROM merge_log WHERE id = @mergeId);
                """,
                new { mergeId }, transaction, cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<MergeRecord>> ForGameAsync(Guid gameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        var records = await connection.QueryAsync<MergeRecord>(new CommandDefinition("""
            SELECT id                 AS "Id",
                   into_game_id       AS "IntoGameId",
                   from_game_id       AS "FromGameId",
                   score              AS "Score",
                   signals_json::text AS "SignalsJson",
                   at                 AS "At",
                   reverted_at        AS "RevertedAt"
              FROM merge_log
             WHERE into_game_id = @gameId OR from_game_id = @gameId
             ORDER BY at;
            """,
            new { gameId }, cancellationToken: ct));

        return records.ToList();
    }
}
```

Create `src/MUI.Discovery/Storage/NpgsqlGameFieldIndex.cs`:

```csharp
using Dapper;
using Npgsql;

namespace MUI.Discovery.Storage;

/// <summary>
/// The reverse field lookup identity gathers candidates with. Backed by the
/// <c>game_field (field, lower(value))</c> index added in migration 0012.
/// </summary>
public sealed class NpgsqlGameFieldIndex(NpgsqlDataSource source) : IGameFieldIndex
{
    public async Task<IReadOnlyList<Guid>> GamesWithFieldAsync(string field, string value, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        var ids = await connection.QueryAsync<Guid>(new CommandDefinition("""
            SELECT DISTINCT game_id
              FROM game_field
             WHERE field = @field AND lower(value) = lower(@value);
            """,
            new { field, value = value.Trim() }, cancellationToken: ct));

        return ids.ToList();
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS, all six `MergeLogTests`.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Storage/Migrations/0012_merge_log.sql src/MUI.Discovery/MergeLog.cs \
        src/MUI.Discovery/Storage/NpgsqlMergeLog.cs src/MUI.Discovery/Storage/NpgsqlGameFieldIndex.cs \
        tests/MUI.Discovery.Tests/MergeLogTests.cs
git commit -m "feat: merges as reversible redirects, logged with their evidence"
```

---

### Task 13: `duplicate_review` — both pages live, linked reciprocally

**Files:**
- Create: `src/MUI.Storage/Migrations/0013_duplicate_review.sql`
- Create: `src/MUI.Discovery/DuplicateReview.cs`
- Create: `src/MUI.Discovery/Storage/NpgsqlDuplicateReviewRepository.cs`
- Create: `tests/MUI.Discovery.Tests/Support/InMemoryDuplicateReviewRepository.cs`
- Test: `tests/MUI.Discovery.Tests/DuplicateReviewTests.cs`

**Interfaces:**
- Consumes: `IdentityScore`, `IdentitySignals` (Task 10); `PostgresFixture` (Task 1).
- Produces:
  - `MUI.Discovery.DuplicateReview(Guid Id, Guid LeftGameId, Guid RightGameId, double Score, string SignalsJson, DateTimeOffset OpenedAt, DateTimeOffset? ResolvedAt, string? Resolution)`
    with `Guid OtherThan(Guid gameId)`
  - `MUI.Discovery.IDuplicateReviewRepository` with
    `Task<Guid> OpenAsync(Guid a, Guid b, IdentityScore score, DateTimeOffset at, CancellationToken ct)`,
    `Task<IReadOnlyList<DuplicateReview>> OpenPairsForAsync(Guid gameId, CancellationToken ct)`,
    `Task ResolveAsync(Guid id, string resolution, DateTimeOffset at, CancellationToken ct)`
  - `MUI.Discovery.Storage.NpgsqlDuplicateReviewRepository(NpgsqlDataSource source)`
  - `InMemoryDuplicateReviewRepository` for the loop tests.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/DuplicateReviewTests.cs`:

```csharp
using MUI.Discovery;
using MUI.Discovery.Storage;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// §7.3's middle case: "both pages stay live and link to each other reciprocally, because a wrongly
/// hidden game is worse than a visible duplicate".
/// </summary>
[NotInParallel]
public class DuplicateReviewTests
{
    private static readonly DateTimeOffset Then = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private static IdentityScore Score(double value = 0.6) =>
        new(null, value, [new IdentitySignal("MsspNameAndCreated", 0.6, true)]);

    [Test]
    public async Task APairIsVisibleFromBothSidesAndEachSideNamesTheOther()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var reviews = new NpgsqlDuplicateReviewRepository(source);

        var corvid = await PostgresFixture.InsertGameAsync(source, "Corvid");
        var maybe = await PostgresFixture.InsertGameAsync(source, "CorvidMaybe");

        await reviews.OpenAsync(corvid, maybe, Score(), Then, None);

        var fromLeft = (await reviews.OpenPairsForAsync(corvid, None)).Single();
        var fromRight = (await reviews.OpenPairsForAsync(maybe, None)).Single();

        await Assert.That(fromLeft.Id).IsEqualTo(fromRight.Id);
        await Assert.That(fromLeft.OtherThan(corvid)).IsEqualTo(maybe);
        await Assert.That(fromRight.OtherThan(maybe)).IsEqualTo(corvid);
    }

    [Test]
    public async Task NeitherGameIsHiddenOrRedirected()
    {
        // The whole point of the middle band. A review must change no presentational state whatsoever:
        // if it archived or redirected one side it would be an unreviewed merge.
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var reviews = new NpgsqlDuplicateReviewRepository(source);

        var corvid = await PostgresFixture.InsertGameAsync(source, "Corvid");
        var maybe = await PostgresFixture.InsertGameAsync(source, "CorvidMaybe");
        await reviews.OpenAsync(corvid, maybe, Score(), Then, None);

        var redirected = await PostgresFixture.ScalarAsync<int>(source, """
            SELECT count(*) FROM game
             WHERE id IN (@corvid, @maybe)
               AND (merged_into_game_id IS NOT NULL OR state = 'archived');
            """, new { corvid, maybe });

        await Assert.That(redirected).IsEqualTo(0);
    }

    [Test]
    public async Task OpeningTheSamePairTwiceKeepsOneOpenRow()
    {
        // The pair is reported on every probe of either endpoint until somebody resolves it. It must
        // not accumulate a row per probe.
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var reviews = new NpgsqlDuplicateReviewRepository(source);

        var corvid = await PostgresFixture.InsertGameAsync(source, "Corvid");
        var maybe = await PostgresFixture.InsertGameAsync(source, "CorvidMaybe");

        var first = await reviews.OpenAsync(corvid, maybe, Score(), Then, None);
        var second = await reviews.OpenAsync(maybe, corvid, Score(0.7), Then.AddDays(1), None);

        await Assert.That(second).IsEqualTo(first);
        await Assert.That((await reviews.OpenPairsForAsync(corvid, None)).Count).IsEqualTo(1);
    }

    [Test]
    public async Task AResolvedPairStopsBeingOpenButIsKept()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var reviews = new NpgsqlDuplicateReviewRepository(source);

        var corvid = await PostgresFixture.InsertGameAsync(source, "Corvid");
        var maybe = await PostgresFixture.InsertGameAsync(source, "CorvidMaybe");
        var id = await reviews.OpenAsync(corvid, maybe, Score(), Then, None);

        await reviews.ResolveAsync(id, "distinct", Then.AddDays(2), None);

        await Assert.That(await reviews.OpenPairsForAsync(corvid, None)).IsEmpty();
        await Assert.That(await PostgresFixture.ScalarAsync<int>(
                source, "SELECT count(*) FROM duplicate_review WHERE id = @id", new { id }))
            .IsEqualTo(1);
    }

    [Test]
    public async Task AGameIsNeverPairedWithItself()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);
        var reviews = new NpgsqlDuplicateReviewRepository(source);
        var corvid = await PostgresFixture.InsertGameAsync(source, "Corvid");

        await Assert.That(async () => await reviews.OpenAsync(corvid, corvid, Score(), Then, None))
            .Throws<ArgumentException>();
    }
}
```

- [ ] **Step 2: Run it and verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `NpgsqlDuplicateReviewRepository` does not exist.

- [ ] **Step 3: Write the migration**

Create `src/MUI.Storage/Migrations/0013_duplicate_review.sql`:

```sql
-- Spec §7.3, the middle band. "Open a suspected-duplicate pair for review — both pages stay live and
-- link to each other reciprocally, because a wrongly hidden game is worse than a visible duplicate."
--
-- Note what this table does NOT do: it does not archive, hide, redirect or reorder either game. It is a
-- note attached to both, and the site renders it as "this may be the same game as X" on each page.

CREATE TABLE duplicate_review (
    id            uuid             PRIMARY KEY,

    -- Unordered pair, stored ordered. left < right is what makes "have we already opened this?" a
    -- single unique index rather than two queries that can race each other into two rows.
    left_game_id  uuid             NOT NULL REFERENCES game (id) ON DELETE CASCADE,
    right_game_id uuid             NOT NULL REFERENCES game (id) ON DELETE CASCADE,

    score         double precision NOT NULL,
    signals_json  jsonb            NOT NULL,
    opened_at     timestamptz      NOT NULL,
    resolved_at   timestamptz      NULL,
    resolution    text             NULL,

    CONSTRAINT duplicate_review_ordered CHECK (left_game_id < right_game_id)
);

-- Only one *open* pair at a time; resolved ones are kept for ever, so a pair somebody judged distinct
-- and which later turns up again is a second row with its own history rather than a silent overwrite.
CREATE UNIQUE INDEX duplicate_review_open_pair_idx
    ON duplicate_review (left_game_id, right_game_id)
 WHERE resolved_at IS NULL;

CREATE INDEX duplicate_review_left_idx  ON duplicate_review (left_game_id)  WHERE resolved_at IS NULL;
CREATE INDEX duplicate_review_right_idx ON duplicate_review (right_game_id) WHERE resolved_at IS NULL;
```

- [ ] **Step 4: Write the record and the interface**

Create `src/MUI.Discovery/DuplicateReview.cs`:

```csharp
namespace MUI.Discovery;

/// <summary>
/// Two games that might be one, held open for a person to judge (spec §7.3).
/// </summary>
/// <remarks>
/// Both pages stay live and link to each other. This record changes no presentational state at all: a
/// wrongly hidden game is worse than a visible duplicate, and hiding one side would make this an
/// unreviewed merge wearing a review's name.
/// </remarks>
public sealed record DuplicateReview(
    Guid Id,
    Guid LeftGameId,
    Guid RightGameId,
    double Score,
    string SignalsJson,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ResolvedAt,
    string? Resolution)
{
    /// <summary>The counterpart, from whichever side is being rendered — the reciprocal link.</summary>
    public Guid OtherThan(Guid gameId) =>
        gameId == LeftGameId ? RightGameId
        : gameId == RightGameId ? LeftGameId
        : throw new ArgumentException($"Game {gameId} is not part of review {Id}.", nameof(gameId));
}

/// <summary>Suspected-duplicate pairs. The pair is unordered; the storage orders it.</summary>
public interface IDuplicateReviewRepository
{
    /// <summary>Opens the pair, or returns the id of the one already open. Order of the arguments is irrelevant.</summary>
    Task<Guid> OpenAsync(Guid a, Guid b, IdentityScore score, DateTimeOffset at, CancellationToken ct);

    Task<IReadOnlyList<DuplicateReview>> OpenPairsForAsync(Guid gameId, CancellationToken ct);

    Task ResolveAsync(Guid id, string resolution, DateTimeOffset at, CancellationToken ct);
}
```

- [ ] **Step 5: Write the repository and the in-memory double**

Create `src/MUI.Discovery/Storage/NpgsqlDuplicateReviewRepository.cs`:

```csharp
using Dapper;
using Npgsql;

namespace MUI.Discovery.Storage;

public sealed class NpgsqlDuplicateReviewRepository(NpgsqlDataSource source) : IDuplicateReviewRepository
{
    private const string Projection = """
        SELECT id            AS "Id",
               left_game_id  AS "LeftGameId",
               right_game_id AS "RightGameId",
               score         AS "Score",
               signals_json::text AS "SignalsJson",
               opened_at     AS "OpenedAt",
               resolved_at   AS "ResolvedAt",
               resolution    AS "Resolution"
          FROM duplicate_review
        """;

    public async Task<Guid> OpenAsync(Guid a, Guid b, IdentityScore score, DateTimeOffset at, CancellationToken ct)
    {
        if (a == b)
        {
            throw new ArgumentException("A game cannot be a suspected duplicate of itself.", nameof(b));
        }

        var (left, right) = a.CompareTo(b) < 0 ? (a, b) : (b, a);

        await using var connection = await source.OpenConnectionAsync(ct);

        // DO NOTHING then re-select: the partial unique index is on open rows only, so ON CONFLICT
        // needs the same predicate, and re-selecting is clearer than repeating it.
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO duplicate_review
                (id, left_game_id, right_game_id, score, signals_json, opened_at, resolved_at, resolution)
            VALUES (@id, @left, @right, @score, @signals::jsonb, @at, NULL, NULL)
            ON CONFLICT (left_game_id, right_game_id) WHERE resolved_at IS NULL DO NOTHING;
            """,
            new
            {
                id = Guid.CreateVersion7(),
                left,
                right,
                score = score.Score,
                signals = IdentitySignals.ToJson(score.Signals),
                at,
            },
            cancellationToken: ct));

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            SELECT id FROM duplicate_review
             WHERE left_game_id = @left AND right_game_id = @right AND resolved_at IS NULL;
            """,
            new { left, right }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<DuplicateReview>> OpenPairsForAsync(Guid gameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        var reviews = await connection.QueryAsync<DuplicateReview>(new CommandDefinition(
            $"""
             {Projection}
              WHERE resolved_at IS NULL AND (left_game_id = @gameId OR right_game_id = @gameId)
              ORDER BY opened_at;
             """,
            new { gameId }, cancellationToken: ct));

        return reviews.ToList();
    }

    public async Task ResolveAsync(Guid id, string resolution, DateTimeOffset at, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE duplicate_review
               SET resolved_at = @at, resolution = @resolution
             WHERE id = @id AND resolved_at IS NULL;
            """,
            new { id, at, resolution }, cancellationToken: ct));
    }
}
```

Create `tests/MUI.Discovery.Tests/Support/InMemoryDuplicateReviewRepository.cs`:

```csharp
using MUI.Discovery;

namespace MUI.Discovery.Tests.Support;

public sealed class InMemoryDuplicateReviewRepository : IDuplicateReviewRepository
{
    private readonly List<DuplicateReview> _reviews = [];

    public IReadOnlyList<DuplicateReview> All => _reviews;

    public Task<Guid> OpenAsync(Guid a, Guid b, IdentityScore score, DateTimeOffset at, CancellationToken ct)
    {
        if (a == b)
        {
            throw new ArgumentException("A game cannot be a suspected duplicate of itself.", nameof(b));
        }

        var (left, right) = a.CompareTo(b) < 0 ? (a, b) : (b, a);
        var existing = _reviews.FirstOrDefault(
            r => r.LeftGameId == left && r.RightGameId == right && r.ResolvedAt is null);

        if (existing is not null)
        {
            return Task.FromResult(existing.Id);
        }

        var review = new DuplicateReview(
            Guid.CreateVersion7(), left, right, score.Score, IdentitySignals.ToJson(score.Signals), at, null, null);
        _reviews.Add(review);
        return Task.FromResult(review.Id);
    }

    public Task<IReadOnlyList<DuplicateReview>> OpenPairsForAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DuplicateReview>>(_reviews
            .Where(r => r.ResolvedAt is null && (r.LeftGameId == gameId || r.RightGameId == gameId))
            .ToList());

    public Task ResolveAsync(Guid id, string resolution, DateTimeOffset at, CancellationToken ct)
    {
        var index = _reviews.FindIndex(r => r.Id == id && r.ResolvedAt is null);
        if (index >= 0)
        {
            _reviews[index] = _reviews[index] with { ResolvedAt = at, Resolution = resolution };
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS, all five. If `NeitherGameIsHiddenOrRedirected` fails on the `state = 'archived'`
comparison, Plan 2 stored `LifecycleState` as an integer rather than text; change that clause to
`state <> 0` and leave a comment saying which representation Plan 2 chose.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Storage/Migrations/0013_duplicate_review.sql src/MUI.Discovery/DuplicateReview.cs \
        src/MUI.Discovery/Storage/NpgsqlDuplicateReviewRepository.cs \
        tests/MUI.Discovery.Tests/Support/InMemoryDuplicateReviewRepository.cs \
        tests/MUI.Discovery.Tests/DuplicateReviewTests.cs
git commit -m "feat: suspected-duplicate pairs, reciprocal and non-destructive"
```

---

### Task 14: `MergeApplier` — the endpoint change is a `FieldChange`

**Files:**
- Create: `src/MUI.Discovery/MergeApplier.cs`
- Test: `tests/MUI.Discovery.Tests/MergeApplierTests.cs`

**Interfaces:**
- Consumes: `IEndpointRepository`, `IGameFieldRepository` (Plan 2); `IMergeLog` (Task 12);
  `IdentityFields`, `IdentityScore`, `IdentitySignals` (Task 10); `BannerFingerprint` (Task 6).
- Produces: `MUI.Discovery.MergeApplier(IEndpointRepository endpoints, IGameFieldRepository fields, IMergeLog merges, TimeProvider time)`
  with `Task AttachAsync(Guid gameId, ProbeResult result, CancellationToken ct)` and
  `Task<Guid> MergeGamesAsync(Guid intoGameId, Guid fromGameId, IdentityScore score, CancellationToken ct)`.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/MergeApplierTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// §7.3: "auto-merge into the existing game, recording the endpoint change as a FieldChange". The
/// change feed is "a table of events that actually happened" (spec §5.1), and a game moving house is
/// exactly such an event.
/// </summary>
public class MergeApplierTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class World
    {
        public InMemoryEndpointRepository Endpoints { get; } = new();
        public InMemoryGameFieldRepository Fields { get; } = new();
        public InMemoryMergeLog Merges { get; } = new();
        public ManualTimeProvider Time { get; } = new();

        public MergeApplier Applier => new(Endpoints, Fields, Merges, Time);
    }

    [Test]
    public async Task AFirstSightingRecordsTheEndpointAndTheChange()
    {
        var world = new World();
        var game = Guid.CreateVersion7();

        await world.Applier.AttachAsync(game, ProbeResults.Answered(), None);

        var endpoint = world.Endpoints.All.Single();
        await Assert.That(endpoint.GameId).IsEqualTo(game);
        await Assert.That(endpoint.Host).IsEqualTo("mud.example.org");
        await Assert.That(endpoint.State).IsEqualTo(EndpointState.Active);

        var change = world.Fields.Changes.Single();
        await Assert.That(change.Field).IsEqualTo(IdentityFields.Endpoint);
        await Assert.That(change.OldValue).IsNull();
        await Assert.That(change.NewValue).IsEqualTo("mud.example.org 4201");
        await Assert.That(change.Source).IsEqualTo(FieldSource.Handshake);
    }

    [Test]
    public async Task ASecondSightingOfTheSameEndpointWritesNoFurtherChange()
    {
        // The change feed must be events, not a heartbeat. A game that never moves costs one row.
        var world = new World();
        var game = Guid.CreateVersion7();

        await world.Applier.AttachAsync(game, ProbeResults.Answered(), None);
        await world.Applier.AttachAsync(game, ProbeResults.Answered(), None);

        await Assert.That(world.Fields.Changes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ASecondSightingKeepsTheOriginalFirstSeenAt()
    {
        var world = new World();
        var game = Guid.CreateVersion7();

        await world.Applier.AttachAsync(game, ProbeResults.Answered(), None);
        var firstSeen = world.Endpoints.All.Single().FirstSeenAt;

        world.Time.Advance(TimeSpan.FromDays(30));
        await world.Applier.AttachAsync(game, ProbeResults.Answered(), None);

        var endpoint = world.Endpoints.All.Single();
        await Assert.That(endpoint.FirstSeenAt).IsEqualTo(firstSeen);
        await Assert.That(endpoint.LastSeenAt).IsEqualTo(world.Time.GetUtcNow());
    }

    [Test]
    public async Task AnEndpointArrivingFromAnotherGameIsRecordedAsAMoveWithItsOldAddress()
    {
        var world = new World();
        var older = Guid.CreateVersion7();
        var game = Guid.CreateVersion7();

        await world.Applier.AttachAsync(older, ProbeResults.Answered(host: "old.example.org"), None);
        await world.Applier.AttachAsync(game, ProbeResults.Answered(host: "old.example.org"), None);

        var latest = world.Fields.Changes.Last();
        await Assert.That(latest.GameId).IsEqualTo(game);
        await Assert.That(latest.OldValue).IsEqualTo("old.example.org 4201");
        await Assert.That(latest.NewValue).IsEqualTo("old.example.org 4201");
    }

    [Test]
    public async Task TheBannerHashIsStoredSoALaterMoveCanBeRecognised()
    {
        // Nothing else writes this field, and without it the banner signal could never fire — a game
        // would have to move before we had ever fingerprinted it.
        var world = new World();
        var game = Guid.CreateVersion7();
        const string banner = "Welcome to Corvid.";

        await world.Applier.AttachAsync(game, ProbeResults.Answered(banner: banner), None);

        var stored = (await world.Fields.ForGameAsync(game, None))
            .Single(f => f.Field == IdentityFields.BannerHash);

        await Assert.That(stored.Value).IsEqualTo(BannerFingerprint.Of(banner));
        await Assert.That(stored.Source).IsEqualTo(FieldSource.Banner);
        await Assert.That(stored.Confidence).IsEqualTo(FieldConfidence.Observed);
    }

    [Test]
    public async Task AServerThatSendsNoBannerLeavesTheStoredFingerprintAlone()
    {
        // A quiet connection is not a redesign. Overwriting the hash with one of "" would break the
        // signal for every game whose greeting arrived after our quiet period once.
        var world = new World();
        var game = Guid.CreateVersion7();

        await world.Applier.AttachAsync(game, ProbeResults.Answered(banner: "Welcome to Corvid."), None);
        await world.Applier.AttachAsync(game, ProbeResults.Answered(banner: null), None);

        var stored = (await world.Fields.ForGameAsync(game, None))
            .Single(f => f.Field == IdentityFields.BannerHash);

        await Assert.That(stored.Value).IsEqualTo(BannerFingerprint.Of("Welcome to Corvid."));
    }

    [Test]
    public async Task MergingTwoGamesLogsTheScoreAndEverySignal()
    {
        var world = new World();
        var into = Guid.CreateVersion7();
        var from = Guid.CreateVersion7();
        var score = new IdentityScore(into, 1.1,
        [
            new IdentitySignal("MsspNameAndCreated", 0.60, true),
            new IdentitySignal("BannerHash", 0.50, true),
            new IdentitySignal("Endpoint", 1.00, false),
        ]);

        var mergeId = await world.Applier.MergeGamesAsync(into, from, score, None);
        var record = world.Merges.All.Single();

        await Assert.That(record.Id).IsEqualTo(mergeId);
        await Assert.That(record.IntoGameId).IsEqualTo(into);
        await Assert.That(record.FromGameId).IsEqualTo(from);
        await Assert.That(record.Score).IsEqualTo(1.1);
        await Assert.That(record.SignalsJson).Contains("BannerHash");
        await Assert.That(record.SignalsJson).Contains("Endpoint");
        await Assert.That(record.RevertedAt).IsNull();
    }
}
```

- [ ] **Step 2: Write the in-memory merge log the test needs**

Create `tests/MUI.Discovery.Tests/Support/InMemoryMergeLog.cs`:

```csharp
using MUI.Discovery;

namespace MUI.Discovery.Tests.Support;

public sealed class InMemoryMergeLog : IMergeLog
{
    private readonly List<MergeRecord> _records = [];

    public IReadOnlyList<MergeRecord> All => _records;

    public Task<Guid> RecordAsync(MergeRecord record, CancellationToken ct)
    {
        _records.Add(record);
        return Task.FromResult(record.Id);
    }

    public Task RevertAsync(Guid mergeId, DateTimeOffset at, CancellationToken ct)
    {
        var index = _records.FindIndex(r => r.Id == mergeId && r.RevertedAt is null);
        if (index >= 0)
        {
            _records[index] = _records[index] with { RevertedAt = at };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MergeRecord>> ForGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MergeRecord>>(
            _records.Where(r => r.IntoGameId == gameId || r.FromGameId == gameId).ToList());
}
```

- [ ] **Step 3: Run it and verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `MergeApplier` does not exist.

- [ ] **Step 4: Write the implementation**

Create `src/MUI.Discovery/MergeApplier.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Storage;

namespace MUI.Discovery;

/// <summary>
/// Carries out what <see cref="IdentityMatcher"/> decided.
/// </summary>
/// <remarks>
/// The matcher only judges. Attaching the endpoint, writing the change-feed entry and fingerprinting
/// the connect screen are separate acts with their own failure modes, and separating them is what lets
/// the judgement be tested without a repository in sight.
/// </remarks>
public sealed class MergeApplier(
    IEndpointRepository endpoints,
    IGameFieldRepository fields,
    IMergeLog merges,
    TimeProvider time)
{
    /// <summary>
    /// Records that this game answers at this address, and — if that is new or moved — appends the
    /// endpoint change to the change feed (spec §7.3, §5.1).
    /// </summary>
    public async Task AttachAsync(Guid gameId, ProbeResult result, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var existing = await endpoints.ByAddressAsync(result.Host, result.Port, ct);

        await endpoints.UpsertAsync(new GameEndpoint(
            gameId,
            result.Host,
            result.Port,
            result.TlsObserved ? EndpointKind.Tls : EndpointKind.Telnet,
            existing?.FirstSeenAt ?? now,
            now,
            EndpointState.Active), ct);

        if (existing is null || existing.GameId != gameId)
        {
            // A change feed of events that actually happened (§5.1). A second sighting of an address we
            // already attribute to this game is not an event, and writing one per probe would bury the
            // real ones.
            await fields.AppendChangeAsync(new FieldChange(
                Id: 0,                        // assigned by the store; Plan 2's repository ignores it
                GameId: gameId,
                Field: IdentityFields.Endpoint,
                OldValue: existing is null ? null : $"{existing.Host} {existing.Port}",
                NewValue: $"{result.Host} {result.Port}",
                Source: FieldSource.Handshake,
                At: now), ct);
        }

        if (result.Banner is { Length: > 0 } banner)
        {
            // Nothing else writes this. Without it the banner signal could never fire: a game would have
            // to move house before we had ever fingerprinted it. A probe that saw no banner writes
            // nothing — silence is not a redesign.
            await fields.UpsertAsync(new GameField(
                gameId,
                IdentityFields.BannerHash,
                BannerFingerprint.Of(banner),
                FieldSource.Banner,
                FieldConfidence.Observed,
                now,
                now), ct);
        }
    }

    /// <summary>
    /// Folds one game into another. A redirect, logged with every signal that was weighed — including
    /// the ones that did not fire, because a merge that has to be explained a year later is explained
    /// by what was considered.
    /// </summary>
    public Task<Guid> MergeGamesAsync(Guid intoGameId, Guid fromGameId, IdentityScore score, CancellationToken ct) =>
        merges.RecordAsync(new MergeRecord(
            Guid.CreateVersion7(),
            intoGameId,
            fromGameId,
            score.Score,
            IdentitySignals.ToJson(score.Signals),
            time.GetUtcNow(),
            null), ct);
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS, all seven.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Discovery/MergeApplier.cs tests/MUI.Discovery.Tests/MergeApplierTests.cs \
        tests/MUI.Discovery.Tests/Support/InMemoryMergeLog.cs
git commit -m "feat: MergeApplier — the endpoint change lands in the change feed"
```

---

### Task 15: `AdvisoryLock` — one crawler across N replicas

**Files:**
- Create: `src/MUI.Discovery/AdvisoryLock.cs`
- Test: `tests/MUI.Discovery.Tests/AdvisoryLockTests.cs`

**Interfaces:**
- Consumes: `PostgresFixture` (Task 1).
- Produces: `MUI.Discovery.AdvisoryLock(NpgsqlDataSource source)` with
  `const long CrawlerKey = 0x4D55495F4352_4C31` and
  `Task<IAsyncDisposable?> TryAcquireAsync(long key, CancellationToken ct)` — null when somebody else
  holds it.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/AdvisoryLockTests.cs`:

```csharp
using MUI.Discovery;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// Spec §12: "multi-replica deployments gate the crawl loop behind a Postgres advisory lock, so the
/// worker is conditionally active and N web replicas still run exactly one crawler".
/// </summary>
[NotInParallel]
public class AdvisoryLockTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task TheSecondClaimantIsRefusedAndTheFirstKeepsIt()
    {
        var source = await PostgresFixture.SourceAsync();
        var first = new AdvisoryLock(source);
        var second = new AdvisoryLock(source);

        await using var held = await first.TryAcquireAsync(AdvisoryLock.CrawlerKey, None);

        await Assert.That(held).IsNotNull();
        await Assert.That(await second.TryAcquireAsync(AdvisoryLock.CrawlerKey, None)).IsNull();
    }

    [Test]
    public async Task ReleasingTheLeaseLetsTheNextClaimantIn()
    {
        // A graceful stop must release it, or a rolling deploy leaves the crawler dark until the
        // database connection is reaped.
        var source = await PostgresFixture.SourceAsync();
        var first = new AdvisoryLock(source);
        var second = new AdvisoryLock(source);

        var held = await first.TryAcquireAsync(AdvisoryLock.CrawlerKey, None);
        await Assert.That(held).IsNotNull();
        await held!.DisposeAsync();

        await using var next = await second.TryAcquireAsync(AdvisoryLock.CrawlerKey, None);
        await Assert.That(next).IsNotNull();
    }

    [Test]
    public async Task DifferentKeysDoNotCollide()
    {
        var source = await PostgresFixture.SourceAsync();
        var locks = new AdvisoryLock(source);

        await using var crawler = await locks.TryAcquireAsync(AdvisoryLock.CrawlerKey, None);
        await using var other = await locks.TryAcquireAsync(AdvisoryLock.CrawlerKey + 1, None);

        await Assert.That(crawler).IsNotNull();
        await Assert.That(other).IsNotNull();
    }

    [Test]
    public async Task TheLeaseSurvivesTheCallerDoingOtherDatabaseWork()
    {
        // The lock lives on its own dedicated connection, not on a pooled one the next query might
        // return to the pool — a session advisory lock dies with its session.
        var source = await PostgresFixture.SourceAsync();
        var locks = new AdvisoryLock(source);

        await using var held = await locks.TryAcquireAsync(AdvisoryLock.CrawlerKey, None);
        await PostgresFixture.ScalarAsync<int>(source, "SELECT count(*) FROM game");
        await PostgresFixture.ScalarAsync<int>(source, "SELECT count(*) FROM crawl_target");

        await Assert.That(await new AdvisoryLock(source).TryAcquireAsync(AdvisoryLock.CrawlerKey, None))
            .IsNull();
    }
}
```

- [ ] **Step 2: Run it and verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `AdvisoryLock` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/MUI.Discovery/AdvisoryLock.cs`:

```csharp
using Npgsql;

namespace MUI.Discovery;

/// <summary>
/// A Postgres session advisory lock, held on a connection of its own for the lifetime of the lease.
/// </summary>
/// <remarks>
/// <para>
/// Spec §12. The crawler runs in-process with the web tier, so a deployment with three web replicas
/// starts three crawlers unless something arbitrates. <c>pg_try_advisory_lock</c> arbitrates using the
/// database that is already there: no leader election, no extra infrastructure, and a replica that dies
/// releases its lock when its connection drops.
/// </para>
/// <para>
/// <b>The dedicated connection is the whole design.</b> A session advisory lock lives on the session
/// that took it, so taking one on a pooled connection and handing that connection back would release
/// the lock the moment anybody else ran a query.
/// </para>
/// </remarks>
public sealed class AdvisoryLock(NpgsqlDataSource source)
{
    /// <summary>The crawl loop's key. Arbitrary and constant: "MUI_CRL1" as bytes.</summary>
    public const long CrawlerKey = 0x4D55495F4352_4C31;

    /// <summary>The lease, or null when another replica holds it. Disposing releases it.</summary>
    public async Task<IAsyncDisposable?> TryAcquireAsync(long key, CancellationToken ct)
    {
        var connection = await source.OpenConnectionAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@key);";
            command.Parameters.AddWithValue("key", key);

            var acquired = await command.ExecuteScalarAsync(ct) as bool? ?? false;
            if (!acquired)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new Lease(connection, key);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(@key);";
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteScalarAsync();
            }
            catch (NpgsqlException)
            {
                // The connection has already gone, which released the lock anyway. Failing to unlock a
                // lock that no longer exists must not take a graceful shutdown down with it.
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS, all four.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Discovery/AdvisoryLock.cs tests/MUI.Discovery.Tests/AdvisoryLockTests.cs
git commit -m "feat: AdvisoryLock — N web replicas, exactly one crawler"
```

---

### Task 16: `CrawlerService` — the loop

**Files:**
- Create: `src/MUI.Discovery/CrawlerService.cs`
- Create: `tests/MUI.Discovery.Tests/Support/FakeProbe.cs`
- Test: `tests/MUI.Discovery.Tests/CrawlLoopTests.cs`

**Interfaces:**
- Consumes: everything above, plus Plan 1's `IProbe`, `ProbeTarget`, `ProbeResult`,
  `FailureClassifier`, `ProbeFailureCauses`, and Plan 2's `ProbeIngestor`, `IGameRepository`.
- Produces:
  - `MUI.Discovery.CrawlCycle(int Taken, int Answered, int Failed, int Merged, int Created, int Review, int ReferralsAdded)`
    with `static readonly CrawlCycle Empty`
  - `MUI.Discovery.GameListingGate` — `static bool MayList(ProbeResult result)`
  - `MUI.Discovery.Slug` — `static string For(string name)`
  - `MUI.Discovery.CrawlerService(IProbe probe, ICrawlTargetRepository targets, ProbeIngestor ingestor, IdentityMatcher identity, IGameRepository games, MergeApplier merges, IDuplicateReviewRepository reviews, ReferralGraphWriter referrals, HostScopeGuard scopes, AdvisoryLock advisoryLock, DiscoveryOptions options, TimeProvider time, ILogger<CrawlerService> logger, ClaimCycle? claims = null) : BackgroundService` —
    **fourteen parameters**, thirteen required plus the trailing optional `claims`. This order is the
    order in the Step 5 definition below and in all three construction sites (`World.Service` here,
    and Task 17's `Build` and `CrawlAgainstARealServerTests`); none of the three passes `claims`, which
    is why Plan 03 builds and goes green before Plan 06 exists. The count is a recorded smell — see
    *Known limitations* item 4 and *What this plan owes itself when the code exists* item 1.
    with `bool HoldsLease { get; }` and `Task<CrawlCycle> RunCycleAsync(CancellationToken ct)`
  - `MUI.Discovery.Tests.Support.FakeProbe`

- [ ] **Step 1: Write the fake probe**

Create `tests/MUI.Discovery.Tests/Support/FakeProbe.cs`:

```csharp
using System.Collections.Concurrent;
using MUI.Crawl;

namespace MUI.Discovery.Tests.Support;

/// <summary>A scripted probe. No sockets: spec §6.5's seam is exactly what makes this possible.</summary>
public sealed class FakeProbe(TimeProvider time) : IProbe
{
    private readonly ConcurrentDictionary<(string Host, int Port), Func<ProbeResult>> _answers = new();
    private readonly ConcurrentQueue<(string Host, int Port)> _visited = new();

    public IReadOnlyCollection<(string Host, int Port)> Visited => _visited.ToList();

    /// <summary>Set to make every probe block until it is released.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Set to make every probe never return, so the loop's own hard bound is what ends it.</summary>
    public bool Hang { get; set; }

    public FakeProbe Answering(string host, int port, Func<ProbeResult> answer)
    {
        _answers[(host, port)] = answer;
        return this;
    }

    public async Task<ProbeResult> ProbeAsync(ProbeTarget target, CancellationToken cancellationToken)
    {
        _visited.Enqueue((target.Host, target.Port));

        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        if (Hang)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return _answers.TryGetValue((target.Host, target.Port), out var answer)
            ? answer()
            : ProbeResults.Failed(target.Host, target.Port, at: time.GetUtcNow());
    }
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/MUI.Discovery.Tests/CrawlLoopTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using MUI.Catalog;
using MUI.Crawl;
using MUI.Discovery;
using MUI.Discovery.Tests.Support;
using MUI.Discovery.Writers;

namespace MUI.Discovery.Tests;

/// <summary>
/// One pass of the crawl loop, against a scripted probe and scripted DNS. The lease is exercised
/// separately (Task 17); <c>RunCycleAsync</c> is the unit the loop is built from and the unit worth
/// asserting.
/// </summary>
public class CrawlLoopTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class World
    {
        public ManualTimeProvider Time { get; } = new();
        public FakeProbe Probe { get; }
        public InMemoryCrawlTargetRepository Targets { get; } = new();
        public InMemoryGameRepository Games { get; } = new();
        public InMemoryEndpointRepository Endpoints { get; } = new();
        public InMemoryGameFieldRepository Fields { get; } = new();
        public InMemoryReferralRepository Edges { get; } = new();
        public InMemoryMergeLog Merges { get; } = new();
        public InMemoryDuplicateReviewRepository Reviews { get; } = new();

        public DiscoveryOptions Options { get; init; } = new()
        {
            GlobalInterval = TimeSpan.Zero,
            PerHostInterval = TimeSpan.Zero,
            MaxConcurrency = 1,
        };

        /// <summary>
        /// Scripted DNS. Every target this harness makes is an operator seed by default, so the guard
        /// short-circuits and no test touches live DNS by accident; the cases that exercise the guard
        /// ask for <c>operatorSeed: false</c> and script an answer here.
        /// </summary>
        public FakeHostResolver Dns { get; } = new();

        public World() => Probe = new FakeProbe(Time);

        public CrawlerService Service => new(
            Probe,
            Targets,
            Ingestor,
            new IdentityMatcher(Games, Endpoints, Fields, Fields, Options),
            Games,
            new MergeApplier(Endpoints, Fields, Merges, Time),
            Reviews,
            new ReferralGraphWriter(Edges, Targets, Options, Time),
            new HostScopeGuard(Dns),
            advisoryLock: null!,     // RunCycleAsync never touches it; ExecuteAsync is Task 17's test
            Options,
            Time,
            NullLogger<CrawlerService>.Instance);

        public InMemoryPresenceRepository Presence { get; } = new();
        public InMemoryAvailabilityRepository Availability { get; } = new();

        // A real Plan 2 ingestor over *this World's* repositories rather than over private ones, so a
        // test can assert what the loop did and did not write. What it writes is Plan 2's behaviour and
        // is asserted in Plan 2's suite; what matters here is whether the loop called it at all.
        public ProbeIngestor Ingestor => new(
            new FieldReconciler(Fields, Time),
            new PresenceWriter(Presence),
            new AvailabilityWriter(Availability));

        public async Task<Guid> TargetAsync(
            string host,
            int port = 4201,
            Guid? gameId = null,
            bool operatorSeed = true)
        {
            var id = await Targets.AddAsync(new CrawlTarget
            {
                Id = Guid.CreateVersion7(),
                Host = host,
                Port = port,
                NextProbeAt = Time.GetUtcNow(),
                FirstSeenAt = Time.GetUtcNow(),
                IsOperatorSeed = operatorSeed,
            }, None);

            if (gameId is { } game)
            {
                await Targets.AttachGameAsync(id, game, None);
            }

            return id;
        }
    }

    [Test]
    public async Task AnEmptyRegistryIsAnEmptyCycle()
    {
        var world = new World();

        await Assert.That(await world.Service.RunCycleAsync(None)).IsEqualTo(CrawlCycle.Empty);
        await Assert.That(world.Probe.Visited).IsEmpty();
    }

    [Test]
    public async Task AHostThatAnswersWithItsOwnNameBecomesAGame()
    {
        // §7.2's promotion: it answered MSSP for itself, so now it is listed.
        var world = new World();
        await world.TargetAsync("mud.example.org");
        world.Probe.Answering("mud.example.org", 4201, () => ProbeResults.Answered(
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), ("HOSTNAME", ["mud.example.org"]))));

        var cycle = await world.Service.RunCycleAsync(None);

        await Assert.That(cycle.Created).IsEqualTo(1);
        var game = world.Games.All.Single();
        await Assert.That(game.Name).IsEqualTo("Corvid");
        await Assert.That(game.Slug).StartsWith("corvid");
        await Assert.That(world.Targets.All.Single().GameId).IsEqualTo(game.Id);
        await Assert.That(world.Endpoints.All.Single().GameId).IsEqualTo(game.Id);
    }

    [Test]
    public async Task AHostThatAnswersWithoutANameIsCrawledForEverAndListedNever()
    {
        // The heart of §7.2. A referred host that connects but never identifies itself is a candidate
        // hostname and stays one — probed on its own account, absent from the catalogue.
        var world = new World();
        await world.TargetAsync("mystery.example.org");
        world.Probe.Answering("mystery.example.org", 4201, () => ProbeResults.Answered(
            host: "mystery.example.org", banner: "Login:"));

        var cycle = await world.Service.RunCycleAsync(None);

        await Assert.That(cycle.Answered).IsEqualTo(1);
        await Assert.That(cycle.Created).IsEqualTo(0);
        await Assert.That(world.Games.All).IsEmpty();

        var target = world.Targets.All.Single();
        await Assert.That(target.GameId).IsNull();
        await Assert.That(target.NextProbeAt).IsEqualTo(world.Time.GetUtcNow() + ProbeSchedule.BaseInterval);
    }

    [Test]
    public async Task AProbeThatMatchesAKnownEndpointMergesInsteadOfCreating()
    {
        var world = new World();
        var corvid = Guid.CreateVersion7();
        await world.Games.InsertAsync(new Game(corvid, "corvid", "Corvid",
            LifecycleState.Active, false, ProbeResults.Observed, ProbeResults.Observed, null), None);
        await world.Endpoints.UpsertAsync(new GameEndpoint(corvid, "mud.example.org", 4201,
            EndpointKind.Telnet, ProbeResults.Observed, ProbeResults.Observed, EndpointState.Active), None);

        await world.TargetAsync("mud.example.org");
        world.Probe.Answering("mud.example.org", 4201, () => ProbeResults.Answered(
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]))));

        var cycle = await world.Service.RunCycleAsync(None);

        await Assert.That(cycle.Merged).IsEqualTo(1);
        await Assert.That(cycle.Created).IsEqualTo(0);
        await Assert.That(world.Games.All.Count).IsEqualTo(1);
        await Assert.That(world.Targets.All.Single().GameId).IsEqualTo(corvid);
    }

    [Test]
    public async Task AMiddlingMatchCreatesTheGameAndOpensAReciprocalPair()
    {
        // Both pages live. The new game is created — it is not withheld pending judgement — and the two
        // are linked to each other.
        var world = new World();
        var corvid = Guid.CreateVersion7();
        await world.Games.InsertAsync(new Game(corvid, "corvid", "Corvid",
            LifecycleState.Active, false, ProbeResults.Observed, ProbeResults.Observed, null), None);
        await world.Fields.UpsertAsync(new GameField(corvid, IdentityFields.Name, "Corvid",
            FieldSource.Mssp, FieldConfidence.Reported, ProbeResults.Observed, ProbeResults.Observed), None);
        await world.Fields.UpsertAsync(new GameField(corvid, IdentityFields.Created, "2003",
            FieldSource.Mssp, FieldConfidence.Reported, ProbeResults.Observed, ProbeResults.Observed), None);

        await world.TargetAsync("elsewhere.example.org");
        world.Probe.Answering("elsewhere.example.org", 4201, () => ProbeResults.Answered(
            host: "elsewhere.example.org",
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]), ("CREATED", ["2003"]))));

        var cycle = await world.Service.RunCycleAsync(None);

        await Assert.That(cycle.Review).IsEqualTo(1);
        await Assert.That(world.Games.All.Count).IsEqualTo(2);

        var fresh = world.Games.All.Single(g => g.Id != corvid);
        var pair = world.Reviews.All.Single();
        await Assert.That(pair.OtherThan(corvid)).IsEqualTo(fresh.Id);
        await Assert.That(pair.OtherThan(fresh.Id)).IsEqualTo(corvid);
        await Assert.That(world.Games.All.All(g => g.State is LifecycleState.Active)).IsTrue();
    }

    [Test]
    public async Task AFailedProbeReschedulesWithBackoffAndIsNeverRetired()
    {
        var world = new World();
        await world.TargetAsync("dead.example.org");

        for (var attempt = 1; attempt <= 12; attempt++)
        {
            var cycle = await world.Service.RunCycleAsync(None);
            await Assert.That(cycle.Failed).IsEqualTo(1);

            var target = world.Targets.All.Single();
            await Assert.That(target.ConsecutiveFailures).IsEqualTo(attempt);

            // And it is still on the books, still due, for ever.
            world.Time.Advance(target.NextProbeAt - world.Time.GetUtcNow());
        }

        var final = world.Targets.All.Single();
        await Assert.That(final.NextProbeAt - final.LastProbedAt!.Value).IsEqualTo(ProbeSchedule.LongestInterval);
        await Assert.That((await world.Targets.DueAsync(world.Time.GetUtcNow(), 10, None))).IsNotEmpty();
    }

    [Test]
    public async Task AGameWithPlayersIsProbedSooner()
    {
        var world = new World();
        await world.TargetAsync("busy.example.org");
        world.Probe.Answering("busy.example.org", 4201, () => ProbeResults.Answered(
            host: "busy.example.org",
            mssp: ProbeResults.Mssp(("NAME", ["Busy"])),
            who: new WhoReading(WhoConfidence.Count, Count: 12)));

        await world.Service.RunCycleAsync(None);

        await Assert.That(world.Targets.All.Single().NextProbeAt)
            .IsEqualTo(world.Time.GetUtcNow() + ProbeSchedule.BusyInterval);
    }

    [Test]
    public async Task AMeasuredZeroIsNotBusyAndIsNotAFailure()
    {
        // Spec §5.4: zero players is a real fact about a running game. It must not read as activity and
        // it must not read as downtime.
        var world = new World();
        await world.TargetAsync("quiet.example.org");
        world.Probe.Answering("quiet.example.org", 4201, () => ProbeResults.Answered(
            host: "quiet.example.org",
            mssp: ProbeResults.Mssp(("NAME", ["Quiet"])),
            who: new WhoReading(WhoConfidence.Count, Count: 0)));

        var cycle = await world.Service.RunCycleAsync(None);

        await Assert.That(cycle.Failed).IsEqualTo(0);
        await Assert.That(world.Targets.All.Single().NextProbeAt)
            .IsEqualTo(world.Time.GetUtcNow() + ProbeSchedule.BaseInterval);
    }

    [Test]
    public async Task ReferralsFromAListedGameBecomeTargetsInTheSameCycle()
    {
        var world = new World();
        await world.TargetAsync("a.example.org");
        world.Probe.Answering("a.example.org", 4201, () => ProbeResults.Answered(
            host: "a.example.org",
            mssp: ProbeResults.Mssp(("NAME", ["Alpha"]), ("REFERRAL", ["b.example.org 4000"]))));

        var cycle = await world.Service.RunCycleAsync(None);

        await Assert.That(cycle.ReferralsAdded).IsEqualTo(1);
        await Assert.That(world.Targets.All.Select(t => t.Host))
            .IsEquivalentTo(new[] { "a.example.org", "b.example.org" });
        await Assert.That(world.Targets.All.Single(t => t.Host == "b.example.org").GameId).IsNull();
    }

    [Test]
    public async Task AReferredHostThatResolvesIntoOurOwnNetworkIsNeverDialled()
    {
        // Spec §7.2, and the reason HostScopeGuard exists. This target passed every check Task 8 makes:
        // "internal.example.org" is a name, a name is MsspHostScope.Unresolved, and Unresolved is
        // crawlable. Only the resolved address gives it away, and only the loop can see that.
        var world = new World();
        await world.TargetAsync("internal.example.org", operatorSeed: false);
        world.Dns.Resolving("internal.example.org", "10.0.0.5");
        world.Probe.Answering("internal.example.org", 4201, () =>
            throw new InvalidOperationException("the guard should have stopped this before the socket"));

        var cycle = await world.Service.RunCycleAsync(None);

        await Assert.That(cycle.Refused).IsEqualTo(1);
        await Assert.That(cycle.Answered).IsEqualTo(0);
        await Assert.That(world.Probe.Visited).IsEmpty();
        await Assert.That(world.Games.All).IsEmpty();
    }

    [Test]
    public async Task ARefusedTargetIsRescheduledWithBackoffAndNeverRetired()
    {
        // §7.4 does not stop applying because we refused: a name pointing somewhere bad today may be a
        // real game tomorrow. It backs off like any other failure, and it stays on the books for ever.
        var world = new World();
        await world.TargetAsync("internal.example.org", operatorSeed: false);
        world.Dns.Resolving("internal.example.org", "10.0.0.5");

        await world.Service.RunCycleAsync(None);
        var after = world.Targets.All.Single();

        await Assert.That(after.ConsecutiveFailures).IsEqualTo(1);
        await Assert.That(after.NextProbeAt).IsGreaterThan(world.Time.GetUtcNow());
    }

    [Test]
    public async Task ARefusalIsNotRecordedAsTheGameBeingUnreachable()
    {
        // We did not measure this game and find it down; we declined to open the socket. Writing an
        // availability sample would put our own security policy into a game's public reachability
        // history as though it were a fact about them.
        var world = new World();
        var corvid = Guid.CreateVersion7();
        await world.Games.InsertAsync(new Game(corvid, "corvid", "Corvid",
            LifecycleState.Active, false, ProbeResults.Observed, ProbeResults.Observed, null), None);
        await world.TargetAsync("moved.example.org", gameId: corvid, operatorSeed: false);
        world.Dns.Resolving("moved.example.org", "192.168.1.1");

        var cycle = await world.Service.RunCycleAsync(None);

        await Assert.That(cycle.Refused).IsEqualTo(1);
        await Assert.That(world.Probe.Visited).IsEmpty();
        await Assert.That(world.Availability.All).IsEmpty();
        await Assert.That(world.Presence.All).IsEmpty();
    }

    [Test]
    public async Task AHostThatResolvesCleanlyIsDialledAsNormal()
    {
        // The guard must not be a blanket refusal of everything that is not an operator seed.
        var world = new World();
        await world.TargetAsync("mud.example.org", operatorSeed: false);
        world.Dns.Resolving("mud.example.org", "203.0.113.10");
        world.Probe.Answering("mud.example.org", 4201, () => ProbeResults.Answered(
            mssp: ProbeResults.Mssp(("NAME", ["Corvid"]))));

        var cycle = await world.Service.RunCycleAsync(None);

        await Assert.That(cycle.Refused).IsEqualTo(0);
        await Assert.That(cycle.Created).IsEqualTo(1);
    }

    [Test]
    public async Task AWedgedProbeIsBoundedAndBecomesATimeoutRatherThanStallingTheCycle()
    {
        // Spec §12: the crawler shares a process with the web tier, so bounding is a correctness
        // requirement. The bound runs on the injected clock, so this test is instant and exact.
        var world = new World { Options = new DiscoveryOptions
        {
            GlobalInterval = TimeSpan.Zero,
            PerHostInterval = TimeSpan.Zero,
            MaxConcurrency = 1,
            ProbeTimeout = TimeSpan.FromSeconds(5),
        } };

        await world.TargetAsync("wedged.example.org");
        world.Probe.Hang = true;

        var cycle = world.Service.RunCycleAsync(None);
        await Assert.That(cycle.IsCompleted).IsFalse();

        world.Time.Advance(TimeSpan.FromSeconds(5));

        await Assert.That((await cycle.WaitAsync(TimeSpan.FromSeconds(10))).Failed).IsEqualTo(1);
        await Assert.That(world.Targets.All.Single().ConsecutiveFailures).IsEqualTo(1);
    }

    [Test]
    public async Task AProbeThatThrowsIsRecordedRatherThanTakingTheCycleDown()
    {
        var world = new World();
        await world.TargetAsync("broken.example.org");
        world.Probe.Answering("broken.example.org", 4201,
            () => throw new InvalidOperationException("a bug in the probe"));

        var cycle = await world.Service.RunCycleAsync(None);

        await Assert.That(cycle.Failed).IsEqualTo(1);
        await Assert.That(world.Targets.All.Single().ConsecutiveFailures).IsEqualTo(1);
    }

    [Test]
    public async Task TwoPortsOnOneMachineAreNotProbedConcurrently()
    {
        var world = new World { Options = new DiscoveryOptions
        {
            GlobalInterval = TimeSpan.Zero,
            PerHostInterval = TimeSpan.Zero,
            MaxConcurrency = 4,
        } };

        await world.TargetAsync("shared.example.org", 4201);
        await world.TargetAsync("shared.example.org", 4202);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Probe.Gate = gate;

        var cycle = world.Service.RunCycleAsync(None);
        await Task.Delay(200);

        // One is inside the probe; the other is queued behind the host gate.
        await Assert.That(world.Probe.Visited.Count).IsEqualTo(1);

        gate.SetResult();
        await cycle.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(world.Probe.Visited.Count).IsEqualTo(2);
    }
}
```

- [ ] **Step 3: Write the ingestor helper the tests need**

`ProbeIngestor` is Plan 2's, and its collaborators are Plan 2's writers over Plan 2's interfaces. Add
one factory so the simpler loop tests construct a real one in a single expression.

**`InMemoryPresenceRepository` and `InMemoryAvailabilityRepository` are not created here.** Plan 02
Task 13 declares them, alongside `InMemoryGameFieldRepository` and the rest, in
`tests/MUI.Discovery.Tests/Support/InMemoryRepositories.cs`; this is one test assembly, so declaring
them again is a CS0101 build failure rather than a merge conflict. Use them as they are. This step
needs only two things of them, both of which Plan 02's have: they implement their interface, and they
expose what they collected (`All`), which is what lets `CrawlLoopTests` assert that a refused target
wrote no presence sample and no availability row.

Create `tests/MUI.Discovery.Tests/Support/ProbeIngestors.cs`:

```csharp
using MUI.Discovery.Writers;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// A real Plan 2 ingestor over Plan 2's in-memory repositories. What it writes is Plan 2's behaviour
/// and is asserted in Plan 2's suite; here it only has to be genuinely wired, so that the loop's calls
/// into it are exercised rather than mocked away.
/// </summary>
/// <remarks>
/// <para>
/// This factory keeps no references to what it built, so a test that needs to assert what was written
/// composes the ingestor from its own repositories instead — <c>CrawlLoopTests.World</c> does exactly
/// that. This one is for the sites that only need the loop to have a real collaborator to call.
/// </para>
/// <para>
/// <b>Compose over held repositories wherever a test must see what was written, and keep this factory
/// for the many sites that only need a working collaborator. Both forms exist on purpose.</b> The
/// reason is that the assertions which matter most in this suite are about writes that did <em>not</em>
/// happen — a refused target writes no presence sample and no availability row (§7.2) — and you cannot
/// assert an absence against a repository you cannot reach. A factory that owns its repositories
/// privately can only ever prove that something was written, never that nothing was.
/// <b>So this must never become the only door.</b> If a future test needs to look at what the ingestor
/// wrote, compose one from that test's own repositories; do not add a getter here and turn a
/// convenience into the single construction path.
/// </para>
/// </remarks>
public static class ProbeIngestors
{
    public static ProbeIngestor InMemory(TimeProvider? time = null)
    {
        var clock = time ?? new ManualTimeProvider();

        return new ProbeIngestor(
            new FieldReconciler(new InMemoryGameFieldRepository(), clock),
            new PresenceWriter(new InMemoryPresenceRepository()),
            new AvailabilityWriter(new InMemoryAvailabilityRepository()));
    }
}
```

- [ ] **Step 4: Run it and verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CrawlerService`, `CrawlCycle`, `GameListingGate`, `Slug` do not exist.

- [ ] **Step 5: Write the loop**

Create `src/MUI.Discovery/CrawlerService.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MUI.Catalog;
using MUI.Crawl;
using MUI.Discovery.Writers;
using MUI.Storage;

namespace MUI.Discovery;

/// <summary>What one pass over the due targets did.</summary>
public sealed record CrawlCycle(
    int Taken,
    int Answered,
    int Failed,
    int Merged,
    int Created,
    int Review,
    int ReferralsAdded,
    int Refused)
{
    public static readonly CrawlCycle Empty = new(0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Whether a probe is enough to put a host in the catalogue (spec §7.2).
/// </summary>
/// <remarks>
/// "A referred host must independently answer MSSP with its own <c>NAME</c>/<c>HOSTNAME</c> before it
/// is listed." <c>NAME</c> is the identifying half and is what this requires; <c>HOSTNAME</c> is the
/// address, which the probe already knows because it just connected to it. A host that answers telnet
/// but never names itself stays a crawl target for ever and never becomes a page.
/// </remarks>
public static class GameListingGate
{
    public static bool MayList(ProbeResult result) =>
        result.Outcome is ProbeOutcome.Answered && !string.IsNullOrWhiteSpace(result.Mssp.Name);
}

/// <summary>A URL-safe form of a game's name.</summary>
public static class Slug
{
    public static string For(string name)
    {
        var slug = new string(name.ToLowerInvariant()
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-')
            .ToArray());

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        return slug.Length switch
        {
            0 => "game",
            > 48 => slug[..48].TrimEnd('-'),
            _ => slug,
        };
    }
}

/// <summary>
/// The crawl loop: take what is due, probe it, work out what it is, write what it said, follow what it
/// points at, and reschedule it.
/// </summary>
/// <remarks>
/// <para>
/// Gated on a Postgres advisory lock held for the lifetime of the lease, so N web replicas run exactly
/// one crawler (spec §12). A graceful stop disposes the lease, which releases it.
/// </para>
/// <para>
/// Three separate limits, deliberately kept apart: <see cref="HostGate"/> is "not two at once against
/// one machine", <see cref="CrawlRateLimiter"/> is "not two in quick succession", and the semaphore
/// below is "not too many in flight anywhere". Only the last is about connections, which is why it is
/// here and not in the limiter.
/// </para>
/// <para>
/// A fourth gate, <see cref="HostScopeGuard"/>, is not a limit at all — it is spec §7.2, and it is the
/// only place §7.2 can actually be enforced, because a referral or an import names a host as a
/// <em>string</em> and where that string points is not known until DNS answers. It runs after the
/// politeness gates and before the socket.
/// </para>
/// <para>
/// <b><see cref="ClaimCycle"/> is called from <c>ApplyAsync</c>, between the ingestor and the
/// rescheduler, and that position is load-bearing rather than incidental.</b> Claim verification is a
/// <em>passenger</em> on a visit the crawler was going to make anyway: sitting there, it can see the
/// probe result and it structurally cannot reach the schedule, so a pending claim can never cause an
/// extra dial. Anything that runs when a claim is <em>created</em> — a second loop, a timer, a "check
/// now" button wired to a probe — reintroduces exactly the polling §11 forbids, and does it on somebody
/// else's server because a stranger clicked a button. A claim is not an exemption from the politeness
/// contract. It is optional only so that Plan 03 builds before Plan 06 lands; a deployment with
/// claiming passes one.
/// </para>
/// </remarks>
public sealed class CrawlerService(
    IProbe probe,
    ICrawlTargetRepository targets,
    ProbeIngestor ingestor,
    IdentityMatcher identity,
    IGameRepository games,
    MergeApplier merges,
    IDuplicateReviewRepository reviews,
    ReferralGraphWriter referrals,
    HostScopeGuard scopes,
    AdvisoryLock advisoryLock,
    DiscoveryOptions options,
    TimeProvider time,
    ILogger<CrawlerService> logger,
    ClaimCycle? claims = null) : BackgroundService
{
    private readonly HostGate _hosts = new();
    private readonly CrawlRateLimiter _limiter = new(options, time);

    /// <summary>Whether this replica is the one crawling.</summary>
    public bool HoldsLease { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        options.Validate();

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var lease = await advisoryLock.TryAcquireAsync(AdvisoryLock.CrawlerKey, stoppingToken);
            if (lease is null)
            {
                logger.LogDebug("Another replica holds the crawl lease; standing by.");
                await RestAsync(options.LeaseRetryInterval, stoppingToken);
                continue;
            }

            HoldsLease = true;
            logger.LogInformation("Crawl lease acquired; this replica is the crawler.");
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var cycle = await RunCycleAsync(stoppingToken);
                    if (cycle.Taken > 0)
                    {
                        logger.LogInformation("Crawl cycle: {Cycle}", cycle);
                    }

                    await RestAsync(options.PollInterval, stoppingToken);
                }
            }
            finally
            {
                HoldsLease = false;
            }
        }
    }

    /// <summary>One pass over everything due. The unit the loop is built from, and the unit tests drive.</summary>
    public async Task<CrawlCycle> RunCycleAsync(CancellationToken cancellationToken)
    {
        var due = await targets.DueAsync(time.GetUtcNow(), options.BatchSize, cancellationToken);
        if (due.Count == 0)
        {
            return CrawlCycle.Empty;
        }

        using var slots = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        var outcomes = await Task.WhenAll(due.Select(target => ProbeOneAsync(target, slots, cancellationToken)));

        return new CrawlCycle(
            Taken: outcomes.Length,
            Answered: outcomes.Count(outcome => outcome.Answered),
            Failed: outcomes.Count(outcome => !outcome.Answered),
            Merged: outcomes.Count(outcome => outcome.Merged),
            Created: outcomes.Count(outcome => outcome.Created),
            Review: outcomes.Count(outcome => outcome.Review),
            ReferralsAdded: outcomes.Sum(outcome => outcome.ReferralsAdded),
            Refused: outcomes.Count(outcome => outcome.Refused));
    }

    private async Task<TargetOutcome> ProbeOneAsync(
        CrawlTarget target,
        SemaphoreSlim slots,
        CancellationToken cancellationToken)
    {
        await slots.WaitAsync(cancellationToken);
        try
        {
            using var host = await _hosts.EnterAsync(target.Host, cancellationToken);
            await _limiter.WaitForTurnAsync(target.Host, cancellationToken);

            // Spec §7.2, on the resolved address. Inside the host gate and after the rate limit, because
            // a DNS lookup is still traffic aimed at somebody's infrastructure — and before the socket,
            // because that is the whole point.
            var ruling = await scopes.RuleOnAsync(target, cancellationToken);
            if (!ruling.MayDial)
            {
                return await RefuseAsync(target, ruling, cancellationToken);
            }

            var result = await ProbeBoundedAsync(target, cancellationToken);
            return await ApplyAsync(target, result, cancellationToken);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            // A bug in a writer must not take the whole cycle down with it — every other target in this
            // batch is unrelated to whatever went wrong here.
            logger.LogError(error, "Crawling {Host}:{Port} failed outside the probe.", target.Host, target.Port);
            return TargetOutcome.Failure;
        }
        finally
        {
            slots.Release();
        }
    }

    /// <summary>
    /// A target we declined to dial. Rescheduled with ordinary backoff so a hostile or broken name is
    /// re-checked at a decreasing rate for ever rather than every six hours for ever — §7.4 still
    /// applies, because a name that points somewhere bad today may be a real game tomorrow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No availability sample is written, deliberately.</b> We did not measure this game and find it
    /// unreachable; we declined to open the socket. Recording it as downtime would put our own security
    /// policy into a game's public reachability history, which is a claim about them that is not true.
    /// Parsers never fabricate, and neither does this.
    /// </para>
    /// <para>
    /// <b>Note what this method does not do: it never builds a <see cref="ProbeResult"/>.</b> That is
    /// what makes the paragraph above structural instead of a promise — the ingestor is the only thing
    /// that writes an availability row, it is only reachable from a <c>ProbeResult</c>, and no refusal
    /// ever produces one. <b>Do not "fix" this by adding a <c>Refused</c> member to
    /// <c>ProbeOutcome</c>, and do not return
    /// <c>ProbeResult.Failed(ProbeFailureCauses.Refused, …)</c> from here.</b> <c>ProbeOutcome</c> has
    /// two members and both mean the socket was opened; <c>FailureCause.Refused</c> means the far end
    /// sent an RST, which is a real measurement of a real host. Reusing it for a policy decision of ours
    /// makes the two permanently inseparable everywhere downstream and writes our security policy into
    /// somebody else's reachability history — the precise thing §7.2 forbids. The refusal is carried on
    /// <see cref="CrawlCycle.Refused"/>, which is the right level: the invariant belongs to whatever
    /// owns the dial, and the guard runs before a probe result can exist.
    /// </para>
    /// </remarks>
    private async Task<TargetOutcome> RefuseAsync(
        CrawlTarget target,
        HostScopeRuling ruling,
        CancellationToken cancellationToken)
    {
        if (ruling.Decision is HostScopeDecision.ResolvedNonGlobal)
        {
            logger.LogWarning(
                "Refused {Host}:{Port}: it resolves into {Scope} space ({Addresses}). Spec §7.2.",
                target.Host, target.Port, ruling.Offending,
                string.Join(", ", ruling.Addresses));
        }

        var now = time.GetUtcNow();
        await targets.RecordAttemptAsync(
            target.Id,
            now,
            succeeded: false,
            crawlDelay: null,
            ProbeSchedule.NextProbeAt(now, target.ConsecutiveFailures + 1, target.CrawlDelay),
            cancellationToken);

        return TargetOutcome.Refusal;
    }

    private async Task<ProbeResult> ProbeBoundedAsync(CrawlTarget target, CancellationToken cancellationToken)
    {
        // The loop's own hard bound, on top of whatever the probe promises. Spec §12: the crawler shares
        // a process with the web tier, so this is correctness rather than hygiene, and the loop does not
        // get to trust a collaborator for it.
        using var bound = new CancellationTokenSource(options.ProbeTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, bound.Token);

        try
        {
            return await probe.ProbeAsync(
                new ProbeTarget { Host = target.Host, Port = target.Port, UseTls = target.UseTls },
                linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(target, new FailureDetail(
                ProbeFailureCauses.Timeout, $"exceeded the crawler's {options.ProbeTimeout} bound"));
        }
        catch (Exception error)
        {
            return Failed(target, FailureClassifier.Classify(error));
        }
    }

    private ProbeResult Failed(CrawlTarget target, FailureDetail failure) => new()
    {
        Host = target.Host,
        Port = target.Port,
        ObservedAt = time.GetUtcNow(),
        Outcome = ProbeOutcome.Failed,
        Failure = failure,
    };

    private async Task<TargetOutcome> ApplyAsync(
        CrawlTarget target,
        ProbeResult result,
        CancellationToken cancellationToken)
    {
        var gameId = target.GameId;
        var merged = false;
        var created = false;
        var review = false;

        if (result.Outcome is ProbeOutcome.Answered && gameId is null && GameListingGate.MayList(result))
        {
            switch (await identity.ResolveAsync(result, cancellationToken))
            {
                case IdentityVerdict.Merge merge:
                    gameId = merge.GameId;
                    merged = true;
                    break;

                case IdentityVerdict.Review candidate:
                    // Both pages stay live: the new game is created, not withheld, and the pair is
                    // opened so each page can link to the other (spec §7.3).
                    gameId = await CreateGameAsync(result, cancellationToken);
                    await reviews.OpenAsync(
                        candidate.GameId, gameId.Value, candidate.Score, time.GetUtcNow(), cancellationToken);
                    review = true;
                    break;

                default:
                    gameId = await CreateGameAsync(result, cancellationToken);
                    created = true;
                    break;
            }

            await targets.AttachGameAsync(target.Id, gameId.Value, cancellationToken);
        }

        if (result.Outcome is ProbeOutcome.Answered && gameId is { } listed)
        {
            await merges.AttachAsync(listed, result, cancellationToken);
        }

        if (gameId is { } known)
        {
            await ingestor.IngestAsync(known, result, cancellationToken);

            // Spec §8, and the position matters more than the call. Verification rides this visit: it
            // reads the ProbeResult we already have, and from here it cannot reach the schedule at all,
            // so a pending claim can never buy a game an extra dial. See the type's remarks.
            if (claims is not null)
            {
                await claims.OnProbeAsync(known, result, cancellationToken);
            }
        }

        var intake = gameId is { } source && result.Outcome is ProbeOutcome.Answered
            ? await referrals.ApplyAsync(source, target.Depth, result, cancellationToken)
            : ReferralIntake.Nothing;

        await RescheduleAsync(target, result, cancellationToken);

        return new TargetOutcome(
            result.Outcome is ProbeOutcome.Answered, merged, created, review, intake.Added, Refused: false);
    }

    private async Task RescheduleAsync(CrawlTarget target, ProbeResult result, CancellationToken cancellationToken)
    {
        var succeeded = result.Outcome is ProbeOutcome.Answered;
        var failures = succeeded ? 0 : target.ConsecutiveFailures + 1;
        var crawlDelay = result.Mssp.CrawlDelay ?? target.CrawlDelay;

        await targets.RecordAttemptAsync(
            target.Id,
            result.ObservedAt,
            succeeded,
            result.Mssp.CrawlDelay,
            ProbeSchedule.NextProbeAt(result.ObservedAt, failures, crawlDelay, ActivityOf(result)),
            cancellationToken);
    }

    private async Task<Guid> CreateGameAsync(ProbeResult result, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7();
        var name = result.Mssp.Name!.Trim();

        var basis = Slug.For(name);
        var slug = await games.BySlugAsync(basis, cancellationToken) is null
            ? basis
            : $"{basis}-{id:N}"[..(basis.Length + 7)];

        await games.InsertAsync(
            new Game(id, slug, name, LifecycleState.Active, IsClaimed: false, now, now, null),
            cancellationToken);

        return id;
    }

    /// <summary>
    /// A measured zero is <see cref="ActivityBand.Quiet"/> and never <see cref="ActivityBand.Unknown"/>:
    /// we got in and nobody was there, which is a real fact about a game (spec §5.4).
    /// </summary>
    private static ActivityBand ActivityOf(ProbeResult result) =>
        result.Outcome is not ProbeOutcome.Answered ? ActivityBand.Unknown
        : (result.Who.HasCount && result.Who.Count > 0) || result.Mssp.Players > 0 ? ActivityBand.Busy
        : ActivityBand.Quiet;

    private async Task RestAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(interval, time, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down. The lease is released by the `await using` on the way out.
        }
    }

    private sealed record TargetOutcome(
        bool Answered, bool Merged, bool Created, bool Review, int ReferralsAdded, bool Refused)
    {
        public static readonly TargetOutcome Failure = new(false, false, false, false, 0, false);

        /// <summary>A refusal counts as a failure too — it is a cycle in which nothing was reached.</summary>
        public static readonly TargetOutcome Refusal = new(false, false, false, false, 0, true);
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS, all sixteen `CrawlLoopTests`.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Discovery/CrawlerService.cs tests/MUI.Discovery.Tests/CrawlLoopTests.cs \
        tests/MUI.Discovery.Tests/Support
git commit -m "feat: the crawl loop — probe, identify, ingest, follow, reschedule"
```

---

### Task 17: The lease, a real socket, and the composition root

**Files:**
- Create: `src/MUI.Discovery/DiscoveryServiceCollectionExtensions.cs`
- Modify: `src/MUI.Web/Program.cs`
- Modify: `tests/MUI.Discovery.Tests/MUI.Discovery.Tests.csproj`
- Test: `tests/MUI.Discovery.Tests/CrawlerLeaseTests.cs`
- Test: `tests/MUI.Discovery.Tests/CrawlAgainstARealServerTests.cs`
- Test: `tests/MUI.Discovery.Tests/CrawlerCompositionTests.cs`

**Interfaces:**
- Consumes: `CrawlerService`, `AdvisoryLock`, all repositories; Plan 1's `ProbeSession`, `ProbeOptions`
  and `tests/MUI.Crawl.Tests/Support/ScriptedMuServer.cs`.
- Produces: `MUI.Discovery.DiscoveryServiceCollectionExtensions` with
  `static IServiceCollection AddMuiCrawler(this IServiceCollection services, DiscoveryOptions? options = null)`.

**What this task needs from Plan 01.** `ScriptedMuServer` is Plan 1's. This task uses exactly four
members of it — `Task StartAsync()`, `int Port`, an initialiser-settable `string Greeting`, an
initialiser-settable `IReadOnlyDictionary<string, string> Mssp`, and `IAsyncDisposable`. **Plan 1 must
expose that surface.** If it landed with different names, adapt the four lines in Step 4; the
assertion is what matters, not the spelling.

- [ ] **Step 1: Link the scripted server into this suite**

Add to `tests/MUI.Discovery.Tests/MUI.Discovery.Tests.csproj`:

```xml
  <ItemGroup>
    <!--
      Compiled in rather than project-referenced. A ProjectReference to another TUnit test project would
      make its source generator discover the Crawl suite's tests a second time and run them twice.
    -->
    <Compile Include="..\MUI.Crawl.Tests\Support\ScriptedMuServer.cs" Link="Support\ScriptedMuServer.cs" />
  </ItemGroup>
```

- [ ] **Step 2: Write the lease test**

Create `tests/MUI.Discovery.Tests/CrawlerLeaseTests.cs`:

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using MUI.Discovery;
using MUI.Discovery.Storage;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// Two crawlers, one database, one crawl (spec §12). This is the one test in the plan that uses real
/// time deliberately: what is being tested is a database lock, not a schedule.
/// </summary>
[NotInParallel]
public class CrawlerLeaseTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>
    /// A fixed instant for seeded rows. The lease is what this test exercises, not the schedule, so
    /// the seed only has to be safely in the past — and a literal keeps it out of the ambient clock.
    /// </summary>
    private static readonly DateTimeOffset Seeded = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static CrawlerService Build(Npgsql.NpgsqlDataSource source, FakeProbe probe, TimeProvider time)
    {
        var options = new DiscoveryOptions
        {
            GlobalInterval = TimeSpan.Zero,
            PerHostInterval = TimeSpan.Zero,
            MaxConcurrency = 1,
            PollInterval = TimeSpan.FromMilliseconds(50),
            LeaseRetryInterval = TimeSpan.FromMilliseconds(50),
        };

        var games = new InMemoryGameRepository();
        var endpoints = new InMemoryEndpointRepository();
        var fields = new InMemoryGameFieldRepository();

        return new CrawlerService(
            probe,
            new NpgsqlCrawlTargetRepository(source),
            ProbeIngestors.InMemory(time),
            new IdentityMatcher(games, endpoints, fields, fields, options),
            games,
            new MergeApplier(endpoints, fields, new InMemoryMergeLog(), time),
            new InMemoryDuplicateReviewRepository(),
            new ReferralGraphWriter(new InMemoryReferralRepository(), new InMemoryCrawlTargetRepository(), options, time),
            // A resolver that answers nothing. This suite's targets are operator seeds, so the guard
            // short-circuits before asking — which is also what keeps a lease test off live DNS.
            new HostScopeGuard(new FakeHostResolver()),
            new AdvisoryLock(source),
            options,
            time,
            NullLogger<CrawlerService>.Instance);
    }

    private static async Task UntilAsync(Func<bool> condition, string what)
    {
        // Stopwatch, not the wall clock: this measures real elapsed time against a real database,
        // so it is monotonic and immune to a clock adjustment mid-test. TimeProvider would be wrong
        // here — nothing about this wait is virtual.
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Timed out waiting for {what}.");
    }

    [Test]
    public async Task OnlyOneOfTwoReplicasCrawls()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);

        await new NpgsqlCrawlTargetRepository(source).AddAsync(new CrawlTarget
        {
            Id = Guid.CreateVersion7(),
            Host = "a.example.org",
            Port = 4201,
            NextProbeAt = Seeded.AddMinutes(-1),
            FirstSeenAt = Seeded.AddMinutes(-1),
            // What is under test is the lease, not the gate. Seeding it keeps HostScopeGuard from
            // resolving a name against real DNS in a test that has nothing to do with DNS.
            IsOperatorSeed = true,
        }, None);

        var probeA = new FakeProbe(TimeProvider.System);
        var probeB = new FakeProbe(TimeProvider.System);
        var first = Build(source, probeA, TimeProvider.System);
        var second = Build(source, probeB, TimeProvider.System);

        await first.StartAsync(None);
        await UntilAsync(() => first.HoldsLease, "the first replica to take the lease");

        await second.StartAsync(None);
        await UntilAsync(() => probeA.Visited.Count > 0, "the leaseholder to crawl");

        await Assert.That(second.HoldsLease).IsFalse();
        await Assert.That(probeB.Visited).IsEmpty();

        // A graceful stop releases the lock, and the standby takes over without human involvement.
        await first.StopAsync(None);
        await UntilAsync(() => second.HoldsLease, "the standby to take over");

        await Assert.That(second.HoldsLease).IsTrue();
        await second.StopAsync(None);
    }

    [Test]
    public async Task StoppingReleasesTheLockForAnybodyAtAll()
    {
        var source = await PostgresFixture.SourceAsync();
        await PostgresFixture.ResetAsync(source);

        var service = Build(source, new FakeProbe(TimeProvider.System), TimeProvider.System);
        await service.StartAsync(None);
        await UntilAsync(() => service.HoldsLease, "the crawler to take the lease");

        await service.StopAsync(None);

        await using var taken = await new AdvisoryLock(source).TryAcquireAsync(AdvisoryLock.CrawlerKey, None);
        await Assert.That(taken).IsNotNull();
    }
}
```

- [ ] **Step 3: Run it and verify it fails**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: FAIL — `ProbeIngestors`, `FakeProbe` exist, but nothing has exercised `ExecuteAsync` yet, so
this is the first run that can fail on the lease path. Fix `CrawlerService.ExecuteAsync` if it does.

- [ ] **Step 4: Write the real-socket test**

Create `tests/MUI.Discovery.Tests/CrawlAgainstARealServerTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using MUI.Crawl;
using MUI.Crawl.Tests.Support;
using MUI.Discovery;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// One pass of the crawl loop over a genuine TCP socket, against Plan 1's scripted server. Every other
/// loop test runs on fixtures; this one proves the seam between them is real.
/// </summary>
public class CrawlAgainstARealServerTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task AGameThatReallyAnswersOverASocketIsListed()
    {
        await using var server = new ScriptedMuServer
        {
            Greeting = "Welcome to Corvid.\r\nType 'connect <name> <password>'.\r\n",
            Mssp = new Dictionary<string, string>
            {
                ["NAME"] = "Corvid",
                ["CREATED"] = "2003",
                ["HOSTNAME"] = "127.0.0.1",
                ["CODEBASE"] = "PennMUSH 1.8.8p2",
            },
        };

        await server.StartAsync();

        var time = TimeProvider.System;
        var options = new DiscoveryOptions
        {
            GlobalInterval = TimeSpan.Zero,
            PerHostInterval = TimeSpan.Zero,
            MaxConcurrency = 1,
            ProbeTimeout = TimeSpan.FromSeconds(30),
        };

        var targets = new InMemoryCrawlTargetRepository();
        await targets.AddAsync(new CrawlTarget
        {
            Id = Guid.CreateVersion7(),
            Host = "127.0.0.1",
            Port = server.Port,
            NextProbeAt = time.GetUtcNow(),
            FirstSeenAt = time.GetUtcNow(),

            // Loopback, and dialled anyway — this is the seed exemption doing its job, and it is worth
            // seeing once in a test that opens a real socket. Without it HostScopeGuard refuses this
            // target, correctly: 127.0.0.1 arriving from a REFERRAL or an import is exactly what §7.2
            // exists to stop. A human pointing the crawler at their own test server has said what they
            // mean, and that is the only way to say it.
            IsOperatorSeed = true,
        }, None);

        var games = new InMemoryGameRepository();
        var endpoints = new InMemoryEndpointRepository();
        var fields = new InMemoryGameFieldRepository();

        var service = new CrawlerService(
            new ProbeSession(new ProbeOptions { HardTimeout = TimeSpan.FromSeconds(20) }),
            targets,
            ProbeIngestors.InMemory(time),
            new IdentityMatcher(games, endpoints, fields, fields, options),
            games,
            new MergeApplier(endpoints, fields, new InMemoryMergeLog(), time),
            new InMemoryDuplicateReviewRepository(),
            new ReferralGraphWriter(new InMemoryReferralRepository(), targets, options, time),
            new HostScopeGuard(new SystemHostResolver()),
            advisoryLock: null!,
            options,
            time,
            NullLogger<CrawlerService>.Instance);

        var cycle = await service.RunCycleAsync(None);

        await Assert.That(cycle.Answered).IsEqualTo(1);
        await Assert.That(cycle.Created).IsEqualTo(1);

        var game = games.All.Single();
        await Assert.That(game.Name).IsEqualTo("Corvid");
        await Assert.That(endpoints.All.Single().Port).IsEqualTo(server.Port);

        // And the banner really was fingerprinted, so a later move of this game would be recognisable.
        await Assert.That((await fields.ForGameAsync(game.Id, None))
                .Any(field => field.Field == IdentityFields.BannerHash))
            .IsTrue();

        // Rescheduled on its own account, for ever.
        await Assert.That(targets.All.Single().NextProbeAt).IsGreaterThan(time.GetUtcNow());
    }
}
```

- [ ] **Step 5: Write the composition root**

Create `src/MUI.Discovery/DiscoveryServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MUI.Discovery.Storage;

namespace MUI.Discovery;

/// <summary>
/// Registers everything this plan owns. What it deliberately does <em>not</em> register:
/// <c>NpgsqlDataSource</c>, <c>TimeProvider</c>, <see cref="MUI.Crawl.IProbe"/>,
/// <see cref="Writers.ProbeIngestor"/> and Plan 2's three repositories — those belong to the plans that
/// built them, and duplicating their registrations here is how two lifetimes of one data source end up
/// in one process.
/// </summary>
public static class DiscoveryServiceCollectionExtensions
{
    public static IServiceCollection AddMuiCrawler(this IServiceCollection services, DiscoveryOptions? options = null)
    {
        var settings = options ?? new DiscoveryOptions();
        settings.Validate();

        services.AddSingleton(settings);
        services.AddSingleton<AdvisoryLock>();
        services.AddSingleton<ICrawlTargetRepository, NpgsqlCrawlTargetRepository>();
        services.AddSingleton<IReferralRepository, NpgsqlReferralRepository>();
        services.AddSingleton<IMergeLog, NpgsqlMergeLog>();
        services.AddSingleton<IDuplicateReviewRepository, NpgsqlDuplicateReviewRepository>();
        services.AddSingleton<IGameFieldIndex, NpgsqlGameFieldIndex>();
        services.AddSingleton<IdentityMatcher>();
        services.AddSingleton<MergeApplier>();
        services.AddSingleton<ReferralGraphWriter>();

        // Spec §7.2, on the resolved address. Registered here rather than left to the host, because a
        // crawler wired up without this gate looks completely healthy and will happily dial whatever an
        // attacker's A record points at. A deployment may replace IHostResolver — TryAdd, not Add — but
        // it has to do so on purpose.
        services.TryAddSingleton<IHostResolver, SystemHostResolver>();
        services.AddSingleton<HostScopeGuard>();

        services.AddHostedService<CrawlerService>();

        return services;
    }
}
```

Add to `src/MUI.Web/Program.cs`, immediately after the `CreateBuilder` line:

```csharp
// The crawler runs in-process with the web tier (spec §4.11), gated on a Postgres advisory lock so N
// replicas still run exactly one (spec §12). Plan 5 completes the composition: an NpgsqlDataSource,
// TimeProvider.System, an IProbe (Plan 1's ProbeSession), and Plan 2's ProbeIngestor, IGameRepository,
// IEndpointRepository and IGameFieldRepository.
builder.Services.AddMuiCrawler();
```

with `using MUI.Discovery;` at the top of the file.

- [ ] **Step 6: Write the composition test**

Create `tests/MUI.Discovery.Tests/CrawlerCompositionTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MUI.Discovery;

namespace MUI.Discovery.Tests;

/// <summary>
/// The registration surface, asserted on descriptors rather than by resolving: the graph is only
/// complete once Plans 1, 2 and 5 have added their own halves, and this plan must not pretend otherwise.
/// </summary>
public class CrawlerCompositionTests
{
    [Test]
    public async Task TheCrawlerIsRegisteredAsAHostedService()
    {
        var services = new ServiceCollection().AddMuiCrawler();

        await Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(CrawlerService)))
            .IsTrue();
    }

    [Test]
    [Arguments(typeof(ICrawlTargetRepository))]
    [Arguments(typeof(IReferralRepository))]
    [Arguments(typeof(IMergeLog))]
    [Arguments(typeof(IDuplicateReviewRepository))]
    [Arguments(typeof(IGameFieldIndex))]
    [Arguments(typeof(AdvisoryLock))]
    [Arguments(typeof(IdentityMatcher))]
    [Arguments(typeof(MergeApplier))]
    [Arguments(typeof(ReferralGraphWriter))]
    [Arguments(typeof(HostScopeGuard))]
    [Arguments(typeof(IHostResolver))]
    [Arguments(typeof(DiscoveryOptions))]
    public async Task EveryTypeThisPlanOwnsIsRegistered(Type service)
    {
        var services = new ServiceCollection().AddMuiCrawler();

        await Assert.That(services.Any(descriptor => descriptor.ServiceType == service)).IsTrue();
    }

    [Test]
    public async Task TheScopeGuardIsWiredByDefaultRatherThanLeftToTheHost()
    {
        // A crawler assembled without §7.2's resolved-address gate looks completely healthy and will
        // dial whatever an attacker's A record points at. Forgetting one line in Program.cs must not be
        // able to produce that, so the default resolver is registered here and a deployment has to
        // replace it on purpose.
        var services = new ServiceCollection().AddMuiCrawler();

        var resolver = services.Single(descriptor => descriptor.ServiceType == typeof(IHostResolver));

        await Assert.That(resolver.ImplementationType).IsEqualTo(typeof(SystemHostResolver));
    }

    [Test]
    public async Task InvalidOptionsAreRefusedAtRegistrationRatherThanAtTheFirstCycle()
    {
        // A misconfigured crawler must fail at start-up, not silently on a background thread at 3 a.m.
        await Assert.That(() => new ServiceCollection()
                .AddMuiCrawler(new DiscoveryOptions { ProbeTimeout = TimeSpan.Zero }))
            .Throws<ArgumentException>();
    }
}
```

- [ ] **Step 7: Run everything**

```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Web.Tests </dev/null
```

Expected: a warning-free build and every suite green.

- [ ] **Step 8: Confirm CI runs what you just ran**

Run: `grep -n "Test — " .github/workflows/ci.yml`
Expected: a step per suite, and the Discovery step carrying `MUI_SKIP_POSTGRES_TESTS` from Task 1. No
new test project was added by this plan, so `MUIndex.slnx` needs no change — confirm with
`grep -c "Tests.csproj" MUIndex.slnx`, which must equal the number of test projects on disk.

- [ ] **Step 9: Commit and open the PR**

```bash
git add src/MUI.Discovery/DiscoveryServiceCollectionExtensions.cs src/MUI.Web/Program.cs \
        tests/MUI.Discovery.Tests/MUI.Discovery.Tests.csproj \
        tests/MUI.Discovery.Tests/CrawlerLeaseTests.cs \
        tests/MUI.Discovery.Tests/CrawlAgainstARealServerTests.cs \
        tests/MUI.Discovery.Tests/CrawlerCompositionTests.cs
git commit -m "feat: the crawler as a leased BackgroundService, proven over a real socket"
git push -u origin HEAD
gh pr create --title "Discovery, scheduling and identity (Plan 03)" --body "$(cat <<'BODY'
Spec §7.1–§7.4, §7.7, §11, §12, §13. Makes the crawler run unattended.

- `crawl_target`: MUIndex's own permanent registry, selected by `next_probe_at` — monotonic, and with
  no `retired` column by construction (§7.4). A game dark for two years is still probed weekly, for
  ever, including after archiving.
- `ProbeSchedule`: exponential backoff clamped to `LongestInterval` (7 days), tightened for games with
  players, with `CRAWL DELAY` applied afterwards as a floor so a server asking for longer gets it.
- `ReferralGraphWriter`: referrals are candidate hostnames. A referred host is crawled on its own
  account and listed only once it answers MSSP with its own `NAME`; a literal address is checked with
  `MUI.Crawl.Mssp.MsspHost.IsCrawlable`.
- `HostScopeGuard`: §7.2 closed on the **resolved** address, which is where it actually holds. A name
  is `Unresolved` and therefore crawlable, so the literal checks alone are bypassed by anyone who owns
  a domain and publishes an A record into private space. The guard resolves before the socket and
  refuses if *any* address is non-global — refusing rather than filtering, because a name straddling
  the boundary is the DNS-rebinding shape. Operator-configured seeds are exempt; referrals and imports
  are not. Known limitation, documented: this narrows the TOCTOU window rather than shutting it, and
  the named fix is connecting to a pinned address in `TcpTransport` (Plan 01).
- `IdentityMatcher`: the six weighted signals of §7.3, thresholds configurable per §15.5, tested
  against known move events and deliberate near-collisions.
- Merges are redirects — reversible, logged with every signal weighed, and nothing is deleted.
- `CrawlerService`: a `BackgroundService` gated on a Postgres advisory lock, every probe hard-bounded.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
BODY
)"
```

---

## What this plan owes itself when the code exists

Decisions deliberately deferred to implementation, because they are much easier to judge with the code
in hand than from a plan. **Do these before the PR, not after it.**

1. **Split `CrawlerService`. Fourteen constructor parameters is too many, and it is the plan admitting
   the type does more than one job.** Count it exactly, because a smell that is not named precisely is
   a smell nobody acts on: `probe`, `targets`, `ingestor`, `identity`, `games`, `merges`, `reviews`,
   `referrals`, `scopes`, `advisoryLock`, `options`, `time`, `logger` — thirteen required — plus the
   trailing optional `ClaimCycle? claims`, which is fourteen. The
   coordinator's ruling is that the shape stands *while the plans are being written* — three are in
   flight, and reshaping a `CONTRACT.md` type now buys a refactor whose right seam nobody can see yet.
   That ruling is about timing, not about the shape being acceptable. Once Task 17 is green the seam
   should be visible, and the likely one is already legible in `RunCycleAsync`: **"take what is due,
   pace it, dial it, reschedule it"** needs `targets`, `probe`, `scopes`, the gates, `options` and
   `time`; **"turn one probe into catalogue state"** needs `identity`, `games`, `merges`, `reviews`,
   `ingestor`, `claims` and `referrals` and touches the schedule not at all. That second half is a
   collaborator with one method, and the fact that it must not reach the scheduler is a property this
   plan already argues for twice — once for `ClaimCycle`'s call position and once for referrals being
   provenance rather than scheduling. **The person who has just written it decides**; what is not
   acceptable is shipping fourteen and saying nothing. This is expected to be resolved *during*
   implementation by whoever writes the type, not deferred indefinitely into a backlog. If the split
   turns out to be wrong, say so here and leave the reasoning.

---

## Notes for the other plans

These are handoffs this plan could not make itself. Each one is a real coupling, not a nicety.

1. **Plan 01 must expose `ScriptedMuServer` with `StartAsync()`, `Port`, `Greeting`, `Mssp` and
   `IAsyncDisposable`** (Task 17). It is compiled into this suite by file link.

1a. **Plan 01 must expose `MsspHost.ScopeOf(IPAddress) → MsspHostScope` as a public static** (Task 9).
   `MsspHost.Create` already computes it internally for the literal-address case, so this is a refactor
   rather than new behaviour. `HostScopeGuard` classifies what DNS returned, and a second copy of the
   loopback / RFC 1918 / RFC 6598 / `fc00::/7` / link-local / multicast ranges in `MUI.Discovery` is two
   sets of rules that must agree for ever — the day they disagree is the day one of them is wrong about
   `169.254.169.254`.

1b. **Plan 01 should let `TcpTransport` connect to an already-resolved `IPAddress`, not only a host
   string** — the named fix for the TOCTOU residue in the *Known limitations* section below. Today
   `HostScopeGuard` resolves and then `TcpTransport` resolves again, and the record can change in
   between. Handing the transport the address the guard vetted closes it. This is a real change to a
   Plan 01 type and is deliberately **not** attempted from here; until it lands, the guard narrows the
   window rather than shutting it, which is what the doc comment and the limitations section both say.
2. **Plan 02 must name its Npgsql implementations `NpgsqlGameRepository`,
   `NpgsqlGameFieldRepository`, `NpgsqlEndpointRepository`, `NpgsqlPresenceRepository`,
   `NpgsqlAvailabilityRepository`, each taking one `NpgsqlDataSource`,** and must embed
   `Migrations/*.sql` by glob. `CONTRACT.md` fixes the interfaces but not these names, and this plan's
   fixtures construct one.
3. **Plan 02's `FieldRegistry` should register `name`, `created`, `banner_hash`, `website`, `contact`,
   `codebase` and `claim_token`** — the strings in `IdentityFields`. An unregistered field only gets the
   permissive default window, so a mismatch degrades staleness rather than breaking, but it degrades
   silently.
4. **Plan 05's `GameQuery` must exclude games with `merged_into_game_id IS NOT NULL` from the default
   listing, and the game page must 301 to the surviving game.** Migration 0012 adds the column;
   `IGameRepository.ListAsync` as contracted cannot see it. Without this a merge is invisible to readers
   and the duplicate stays on the list, which defeats the point of §7.3.
5. **Plan 05 should render `IDuplicateReviewRepository.OpenPairsForAsync` on both pages** — "this may be
   the same game as X", each side linking to the other (§7.3).
6. **Plan 05 completes `AddMuiCrawler`'s graph**: `NpgsqlDataSource`, `TimeProvider.System`, `IProbe`,
   `ProbeIngestor`, `IGameRepository`, `IEndpointRepository`, `IGameFieldRepository`.
7. **Claiming (§8) is split between this plan and Plan 06, and the seam is two names.** The *reading*
   half is here: `IdentityWeights.ClaimToken` (10.0), `IdentityFields.ClaimToken` (the `game_field` name
   `claim_token`), and `ClaimTokenBeacon.Read`, which pulls a token off an MSSP variable or a connect
   screen. The *issuing and verifying* half is Plan 06's, along with `MUI.Catalog.ClaimToken` — the
   record for an issued token — which is why this plan's reader is called `ClaimTokenBeacon`: both are
   in scope in every file here. **Plan 06 must write a verified token into
   `game_field["claim_token"]`**, because that row is what the matcher compares against; until it does,
   the signal never fires. Plan 06 additionally carries its own DNS TXT resolver, for §8's third
   channel, which a telnet probe cannot see at all.

   **The wire spellings live in `MUI.Catalog.ClaimVocabulary`** — `MsspVariable`,
   `ConnectScreenPrefix`, `DnsLabel` — declared by Plan 06 and read by both sides. They were briefly
   about to be declared twice: this plan's reader had them as its own literals, and Plan 06's claim
   instructions and `ClaimAttempt.MsspFieldsSeen` diagnostic need the same three strings. `MUI.Catalog`
   is where they landed because it is the lowest project both can see — `MUI.Catalog` may never
   reference `MUI.Discovery`, so the constant could not live here and be shared. `ClaimTokenBeacon`
   keeps `MsspVariable` and `ConnectScreenPrefix` as **forwarding properties**, not `const` aliases: a
   `const` copy is baked into the consuming assembly at compile time, which is the drift a shared
   vocabulary exists to prevent. `IdentityCorpusTests.TheWireSpellingsAreWhatWeTellOperatorsToType` is
   the pin, and it is the only place in this plan where one of those strings is written as a literal.

8. **Plan 04's importers must leave `CrawlTarget.IsOperatorSeed` alone — that is, false.** An imported
   directory row is a stranger's assertion about a hostname, exactly like a `REFERRAL`, and it must
   pass Task 9's resolved-address gate. The property defaults to false, so the requirement is *do
   nothing*, and the only way to get this wrong is to set it deliberately. The exemption exists for an
   address a human operator typed into our own configuration and for nothing else; if an import ever
   needs one, that is a per-row human decision and not a property of the importer.

---

## Self-review

**1. Spec coverage.**

| Spec | Task |
|---|---|
| §7.1 promotion to an independently scheduled `CrawlTarget` | 4, 5 |
| §7.1 an edge disappearing sets `Present = false` and nothing else | 7, 8 |
| §7.2 referrals are candidates, `GameId = null` until it answers | 8, 16 (`GameListingGate`) |
| §7.2 depth and fan-out caps | 8 |
| §7.2 refuse referrals into non-routable space — *literal* address | 8 |
| §7.2 "the gate is on the resolved address, not the name" — refuse-don't-filter, unresolvable ≠ refused, no availability sample, seeds exempt only | 9 (`HostScopeGuard`), 16 |
| §8 read a claim beacon off a probe (MSSP aliases + labelled and bare connect-screen forms) | 10 (`ClaimTokenBeacon`) |
| §8 verification rides the crawl schedule and cannot cause a dial | 16 (`ClaimCycle` between ingest and reschedule) |
| §7.2 trace and prune a poisoned subtree | 7 (`SubtreeOfAsync`) |
| §7.3 the six weighted signals | 10 |
| §7.3 auto-merge, recording the endpoint change as a `FieldChange` | 14, 16 |
| §7.3 review pair, both live, reciprocal | 13, 16 |
| §7.3 merges reversible and logged | 12 |
| §7.4 never retired, exponential backoff, weekly ceiling | 3, 4, 16 |
| §7.7 `max(CRAWL DELAY, base)`, tightened on activity, lengthened on failure | 3, 16 |
| §7.7 per-host serialisation | 9 |
| §11 `CRAWL DELAY` honoured as a floor | 3, 5 |
| §12 hard-bounded probes | 2 (`ProbeTimeout`), 16 |
| §12 global concurrency cap | 2, 16 |
| §12 advisory-lock gating, graceful release | 15, 17 |
| §13 known moves and near-collisions | 11 |
| §15.5 conservative and configurable thresholds | 2, 10, 11 |

Not covered, and deliberately: §7.5 archiving (Plan 2's `ArchiveSweeper`), §7.6 imports (Plan 4), the
issuing and verifying half of §8 claiming (Plan 6 — see below; the reading half is Task 10's
`ClaimTokenBeacon`).

**2. Placeholder scan.** No "TBD", no "add error handling", no "similar to Task N". Every code step
carries the code. Three steps are conditional on what a sibling plan did (Task 4 Step 1's migration
glob, Task 13 Step 6's `state` representation, Task 17's `ScriptedMuServer` surface); each states both
branches and the exact text for each.

**3. Type consistency.** `ProbeSchedule.LongestInterval`, `BaseInterval`, `BusyInterval` are spelled the
same in Tasks 3, 5, 16. `IdentityFields.*` constants are introduced in Task 10 and used in 11, 12, 14,
16, 17. `MergeApplier.AttachAsync`/`MergeGamesAsync` are declared in 14 and called in 16 and 17.
`IDuplicateReviewRepository.OpenAsync(a, b, score, at, ct)` matches its two implementations and its
call site. `CrawlerService`'s **fourteen** constructor parameters — thirteen required, with
`HostScopeGuard scopes` sitting between `referrals` and `advisoryLock`, plus the optional
`ClaimCycle? claims = null` trailing
`logger` — are in the same order in Task 16's definition and in Tasks 16 and 17's three construction
sites, none of which passes the optional one. (Fourteen is too many, and it is a symptom that this type
does more than one job; see *Known limitations* item 4 and
*What this plan owes itself* item 1.) `IdentityMatcher`'s trailing optional `ClaimBeaconPolicy? beacons
= null` is likewise unpassed at all four of its construction sites, which is what keeps every identity
test on the pre-claiming arm. `CrawlCycle`'s eighth field, `Refused`, is set in `RunCycleAsync`, carried by
`TargetOutcome`, and defaulted in `CrawlCycle.Empty`. `ClaimTokenBeacon` — declared in Task 10,
called once from `IdentityMatcher.ResolveAsync` — is spelled that way everywhere, including the names
table and the file-structure table, because `MUI.Catalog.ClaimToken` (Plan 06's issued-token record) is
in scope in every file of this project and the two must not be confusable at a glance. The three
constants that keep the word plain — `IdentityFields.ClaimToken`, `IdentityWeights.ClaimToken` and the
`game_field` value `claim_token` — are field and weight names, not types, and are unchanged. The wire
spellings `ClaimTokenBeacon` reads by are `MUI.Catalog.ClaimVocabulary`'s and are not declared here;
`"MUINDEX CLAIM"` and `"MUINDEX-CLAIM:"` appear as literals in exactly one place in this plan, the test
that pins them.

**3a. Test-support ownership.** `tests/MUI.Discovery.Tests` is a single assembly shared with Plan 02,
so every fixture type has exactly one declaring plan and this plan says which for each of them. Plan 02
lands first and owns `ManualTimeProvider` and `Support/InMemoryRepositories.cs`
(`InMemoryGameRepository`, `InMemoryEndpointRepository`, `InMemoryGameFieldRepository`,
`InMemoryPresenceRepository`, `InMemoryAvailabilityRepository`); this plan **extends** two of those in
place — `CreateTimer` on the clock (Task 1 Step 3) and `IGameFieldIndex` on the field double (Task 10
Step 1) — and creates nothing that would shadow them. The doubles for interfaces this plan alone
declares — `InMemoryCrawlTargetRepository`, `InMemoryReferralRepository`, `InMemoryMergeLog`,
`InMemoryDuplicateReviewRepository` — stay this plan's, because `ICrawlTargetRepository`,
`IReferralRepository`, `IMergeLog` and `IDuplicateReviewRepository` do. Both extension points state
what they assume about Plan 02's file and what to do if it landed differently; neither says "create".

**4. External dependencies.** None. `MUI.Discovery` references `MUI.Catalog`, `MUI.Crawl`,
`MUI.Storage`, `Npgsql`, `Dapper` and the hosting/DI/logging abstractions, and nothing else. The MSSP
types this plan leans on (`MsspData`, `MsspHost.IsCrawlable`, `MsspHostScope`) are MUIndex's own, in
`MUI.Crawl.Mssp`, written by Plan 01 — there is no shared MSSP package, and nothing here is blocked on
anything outside this repository.

### Three places the spec is genuinely unresolved

These are not gaps in the plan; they are gaps in the spec that the plan had to decide.

1. **"Permanent weekly floor" (§7.4) versus "`CRAWL DELAY` honoured as a floor" (§11).** A floor under
   frequency is a ceiling on the interval, and the two sections then want opposite things about a
   server that asks for a month between visits. The plan clamps the backoff first and applies the
   server's request afterwards, so politeness wins and the ceiling is not absolute. Named
   `LongestInterval` with both readings in the doc comment, pinned by
   `AServerAskingForLongerThanTheCeilingGetsIt`.
2. **§7.3's claim-token signal depends on §8 — found unplanned here, and now Plan 06.** It is the
   highest-weighted signal (10.0) and the one thing that guarantees a claimed game is never duplicated,
   and when this plan was written there was nothing in any of the five plans that issued a token,
   verified it, or wrote it to `game_field`. **That finding is why Plan 06 exists**, and it is recorded
   rather than deleted so the reason survives the fix. The split is now explicit: the *reading* half is
   this plan's (`ClaimTokenBeacon.Read` off an MSSP variable or a connect screen, plus
   `IdentityFields.ClaimToken` and `IdentityWeights.ClaimToken`), and the *issuing and verifying* half
   is Plan 06's, along
   with `MUI.Catalog.ClaimToken` — the record for an issued token, whose name collision with this plan's
   reader is what forced the `ClaimTokenBeacon` spelling. Plan 06 also plans an `IDnsTxtResolver`,
   because §8's third channel is a DNS TXT record at `ClaimVocabulary.DnsLabel`.`<host>` and no telnet
   probe can see it. Nothing in this plan changes when Plan 06 lands: the signal starts firing the first
   time a verified token reaches `game_field["claim_token"]`, which was the point of building the
   reading half early. The wire spellings both halves depend on are `MUI.Catalog.ClaimVocabulary`'s —
   see handoff note 7 for why they could not live in `MUI.Discovery`.
3. **§7.2's "answer MSSP with its own `NAME`/`HOSTNAME`" is ambiguous.** Read as a conjunction it would
   refuse to list a great many real games — `HOSTNAME` is one of the fields §3.1 identifies as
   hand-typed and commonly unset. The plan reads it as "identifies itself", requires a non-blank `NAME`,
   and treats `HOSTNAME` as an endpoint hint. `GameListingGate` is one line so the decision is
   reversible.

### Known limitations, stated rather than implied

1. **`HostScopeGuard` narrows the SSRF window; it does not shut it.** Recorded in spec §7.2 in the same
   terms, and repeated here so an implementer meets it. Resolution and connection are separate
   syscalls: the guard resolves `games.example.com`, sees `203.0.113.10`, allows the dial — and
   `TcpTransport` then resolves the name *again* to open the socket, by which time the record may say
   `10.0.0.5`. No amount of care inside the guard closes that, because the guard does not own the
   connect. **The fix is named and assigned:** connect to the pinned address from
   `HostScopeRuling.Addresses` rather than re-resolving the host string — a
   `MUI.Crawl.Transport.TcpTransport` change, Plan 01's to make (handoff note 1b), and worth doing.
   **Do not restate the guard as airtight; it raises the cost of the attack, it does not close it.**
2. **A refusal writes no availability sample**, so a listed game whose DNS starts pointing into private
   space stops accruing reachability history rather than being recorded as unreachable. That is the
   honest reading — we declined to dial, we did not measure — but it does mean a game can go quiet in
   the record for a reason that is ours rather than theirs. The refusal is logged at warning level and
   counted in `CrawlCycle.Refused`, which is where an operator would see it. If this ever needs to be
   visible to *readers* rather than operators, it is a new `AvailabilityState`, not a reuse of an
   existing one.
3. **The guard costs one DNS lookup per due target per cycle**, on top of whatever the transport does.
   It is inside the host gate and after the rate limiter, so it is serialised and paced like everything
   else, and **no cache is introduced, deliberately** — §7.2: "caching resolutions would *widen* this
   window, so the crawler resolves per dial". If this becomes a load problem the answer is a shorter
   batch, not a longer-lived answer.
4. **`CrawlerService` takes fourteen constructor parameters** — thirteen required, plus the trailing
   optional `ClaimCycle? claims`. That is a smell and it is recorded rather than hidden: a type needing that
   many collaborators is doing more than one job, and the loop plausibly splits into "take what is due
   and reschedule it" and "turn one probe into catalogue state". The coordinator's ruling is that the
   shape stays for now — three plans are in flight and reshaping a `CONTRACT.md` type buys a refactor
   whose right seam is much easier to judge with the code in hand than from a plan. **It is expected to
   be resolved during implementation, by whoever has just finished writing it, and not deferred
   indefinitely.** See item 1 of *What this plan owes itself when the code exists*, which is the first
   thing that section asks for after Task 16 and carries the exact parameter list and the likely seam.

