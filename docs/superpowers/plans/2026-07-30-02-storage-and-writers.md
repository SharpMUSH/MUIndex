# Storage and Writers Implementation Plan (Plan 02)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the PostgreSQL schema, the field registry and precedence ladder, and the three probe-result writers, so that a captured `ProbeResult` becomes durable catalogue state with per-field provenance, three-state presence, interval-shaped availability, and automatic tiered archiving.

**Architecture:** A new `src/MUI.Storage` project owns migrations and Npgsql+Dapper repositories against interfaces it also declares; `src/MUI.Catalog` gains the pure domain records, the §5.6 field registry and the §5.1 precedence ladder (no dependencies, no I/O); and `src/MUI.Discovery` — the only project that may see both `MUI.Crawl` and `MUI.Catalog` — hosts `FieldReconciler`, `PresenceWriter`, `AvailabilityWriter`, `ProbeIngestor` and `ArchiveSweeper`. Writer tests run against in-memory fake repositories and hand-built `ProbeResult` fixtures; repository tests run against a real PostgreSQL 17 container.

**Tech Stack:** .NET 10, Npgsql 10, Dapper 2.1, PostgreSQL 17, `Testcontainers.PostgreSql` 4.7, TUnit 1.61 on Microsoft.Testing.Platform.

**Depends on:** Plan 01 (probe engine) for the `ProbeResult` shape, `MUI.Crawl.Mssp.MsspData`, the four-state `WhoReading`/`WhoConfidence`, `PresenceAggregates` and the captured JSON fixtures. This plan touches no socket: every test here runs against a `ProbeResult` fixture.

---

## Naming convention for repository implementations — binding on Plans 03, 04 and 05

`CONTRACT.md` names the repository *interfaces* and not their implementations, and the five plans
were drafted in parallel and guessed three different conventions. **Settled here, because this plan
owns `MUI.Storage`:** an implementation is its interface's name with the `I` replaced by the driver —

| Kind | Spelling | Example |
|---|---|---|
| Interface | `I<Thing>Repository` | `IAvailabilityRepository` |
| PostgreSQL implementation | `Npgsql<Thing>Repository` | `NpgsqlAvailabilityRepository` |
| Test fake | `InMemory<Thing>Repository` | `InMemoryAvailabilityRepository` |

`IMergeLog` follows the same rule as `NpgsqlMergeLog` / `InMemoryMergeLog`. The driver is in the name
because the alternative — a bare `AvailabilityRepository` — reads as *the* repository and makes the
in-memory fake look like the deviant one, when in this codebase both are ordinary implementations and
most tests use the fake.

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
- **Parsers never fabricate.** An unreadable `WHO` yields `WhoConfidence.Unknown`, never zero — and a
  `WHO` that was never sent yields `WhoConfidence.NotAttempted`, which is a different state. Task 15
  turns on that difference.
- **Vocabulary is "reachable", never "uptime"** — schema, API, code and copy alike (spec §5.7).
- **Branch from `main`, open a PR, never commit directly to `main`.**
- **Any new test project goes into `MUIndex.slnx` AND `.github/workflows/ci.yml`**, which runs each
  suite as its own explicit step.
- **MUIndex owns the MSSP domain** — namespace `MUI.Crawl.Mssp`, in `src/MUI.Crawl`, Plan 01's code
  written against Plan 01's own tests. There is **no shared package**: nothing named `SharpMU.Mssp`
  was ever published and the repository that would have produced it is archived, so MUIndex
  implements its crawler end to end and shares no code with SharpMUTerm. `MsspData`, `MsspHost`,
  `MsspHostScope` and `MsspVariables` live there; telnet option 70 is parsed by
  `TelnetNegotiationCore` 2.7.0 itself, and `MUI.Crawl.Mssp.MsspPlaintextReply` handles only the
  out-of-band `MSSP-REQUEST` text reply. This plan **consumes** those types through the
  `MUI.Discovery` → `MUI.Crawl` reference and adds no package of its own; never re-declare them.
- **Persistence is PostgreSQL 17 with Npgsql + Dapper and plain numbered `.sql` migration files
  applied by a small idempotent runner. No EF Core**, ever. Integration tests use
  `Testcontainers.PostgreSql`.

---

## File Structure

**`src/MUI.Catalog`** (existing project; no dependencies, and none may be added)

| File | Responsibility |
|---|---|
| `Provenance.cs` | **Exists.** `FieldSource` (declared order *is* the §5.1 ladder), `Provenance`. Do not reorder the enum. |
| `Lifecycle.cs` | **Exists.** `AvailabilityState`, `FailureCause`, `LifecycleState`. Gains `FailureCause.Unknown` in Task 3. |
| `ArchivePolicy.cs` | **Exists.** The §7.5 grace formula. Never reimplemented, only called. |
| `FieldRegistry.cs` | New — `FieldValueKind`, `FieldDefinition`, `FieldRegistry`, `CapabilityFields` (§5.6). |
| `CatalogRecords.cs` | New — `FieldConfidence`, `FieldConfidences`, `Game`, `GameField`, `FieldChange`, `PresenceSource`, `UnmeasurableReasons`, `PresenceSample`, `AvailabilityInterval`, `EndpointKind`, `EndpointState`, `GameEndpoint`. |
| `SourcePrecedence.cs` | New — the §5.1 ladder as a decision function. |
| `HostName.cs` | New — one canonical spelling of a host (§7.3). Deliberately mirrors `MsspHost`'s normalisation, which `MUI.Catalog` cannot reference. |
| `AvailabilityArithmetic.cs` | New — cumulative reachable, reachable percent, longest outage (§13). Pure, so §5.3's arithmetic is testable without a database. |

**`src/MUI.Storage`** (new; references `MUI.Catalog`, `Npgsql`, `Dapper`, `Microsoft.Extensions.Logging.Abstractions`)

| File | Responsibility |
|---|---|
| `MigrationRunner.cs` | Embedded `.sql` resources applied in lexical order inside a transaction against the `mui_migration` ledger. Idempotent. |
| `SqlEnums.cs` | The one place a C# enum becomes a schema string and back. |
| `Repositories.cs` | The six repository interfaces and `GameQuery`. Interfaces live with the schema they describe so a consumer takes one reference. |
| `NpgsqlGameRepository.cs`, `NpgsqlGameFieldRepository.cs`, `NpgsqlPresenceRepository.cs`, `NpgsqlAvailabilityRepository.cs`, `NpgsqlEndpointRepository.cs` | One file per aggregate; each is the only place its table's SQL is written. |
| `Migrations/0001_game.sql` … `0005_game_endpoint.sql` | The schema. Plan 3 continues at `0006`. |

**`src/MUI.Discovery`** (existing; gains a `MUI.Storage` reference)

| File | Responsibility |
|---|---|
| `Writers/FailureCauseMap.cs` | The `MUI.Crawl` failure-cause vocabulary crossing into `MUI.Catalog`. |
| `Writers/FieldReconciler.cs` | §5.1 — confirm, change or reject, once per field per probe. |
| `Writers/PresenceWriter.cs` | §5.4 — the three states an hour can be in. |
| `Writers/AvailabilityWriter.cs` | §5.3 — intervals, and only a cause change writes a transition. |
| `Writers/ProbeIngestor.cs` | Runs all three, returns `IngestOutcome`. |
| `Writers/ArchiveSweeper.cs` | §7.5 — applies `ArchivePolicy`, and un-archives on a single reachable interval. |

**`tests/MUI.Storage.Tests`** (new; Testcontainers — a real database, no fakes)
**`tests/MUI.Catalog.Tests`** (existing; pure unit tests, no container)
**`tests/MUI.Discovery.Tests`** (existing; `Support/` fakes + `ProbeResult` fixtures, **no sockets, no container**)

---

## Deviations from CONTRACT.md, declared

The contract says a plan may change one of its names or signatures only if it says so. This plan
changes four things, and every one is a hole the contract cannot express:

1. **`IAvailabilityRepository` gains `CumulativeImportedMeasuredReachableAsync(Guid, DateTimeOffset, CancellationToken)`.**
   `ArchivePolicy.GraceFor` takes first-party and imported-measured reachable time as *separate*
   arguments and weights the second at half (§7.6). The contract's single `CumulativeReachableAsync`
   cannot feed both. `availability_interval` therefore also carries an `origin` column.
2. **`FailureCauseMap` gains `To(FailureCause)`.** The contract declares only `From`; the required
   "mapping is total in both directions" test needs the other direction to exist.
3. **`MUI.Catalog.AvailabilityArithmetic`, `CapabilityFields`, `FieldConfidences` and
   `MUI.Storage.SqlEnums` are new public types** the contract does not name. §13 asks for
   availability arithmetic and §5.1 asks for a confidence value; neither had a home.
4. **`FieldRegistry` is populated from §5.6's *argument*, not a table.** The spec gives two
   calibration anchors ("stale in hours" for a count, "unremarkable at six months and notable at six
   years" for `GENRE`) and no list. The list in Task 2 is derived from the MSSP taxonomy and pinned
   by tests against those two anchors.

---

## Spec gaps this plan resolves, and how

Each is flagged here rather than papered over. Where a resolution is a judgement call, the code
carries the reasoning as a comment and a test pins the choice.

| # | Gap | Resolution taken |
|---|---|---|
| 1 | §5.1 declares a `confidence` column and never says what it holds. | `FieldConfidence { Observed, Reported, Inferred }` (from CONTRACT.md), derived from the source by `FieldConfidences.For`, aligned with the existing `Provenance.IsMeasured`. |
| 2 | §5.1's ladder omits `who` entirely, though `who` is in the source set. | `FieldSource`'s declared order is the ladder (`Who` between `Owner` and `Mssp`), per the enum's own doc comment. `RankOf` is `(int)source`. |
| 3 | §5.1 says one row per `(game, field)` **and** that a page "offers the losing ones with their sources". One row cannot hold both. | Key stays `(game_id, field)`; capabilities are stored under two *different* field names — `capability.<x>.measured` (handshake) and `capability.<x>.declared` (MSSP) — so measured and declared never contend and both survive. §9's capability matrix is served; other losing values are genuinely not retained. |
| 4 | §5.3 names a `degraded` state and never says what produces it. | `handshake_stalled` — the one failure where the socket answered — maps to `Degraded`; every other failure maps to `Unreachable`. |
| 5 | §5.1 says every field a probe yields is confirmed or changed, but MSSP's required trio includes `PLAYERS` and `UPTIME`, which move on every probe and would write a `field_change` row per hour — the exact cost §5.1 forbids. | `FieldReconciler.VolatileVariables` skips both. `PLAYERS` is presence (§5.2); `UPTIME` is a counter, not a description. |
| 6 | `ProbeFailureCauses` has six strings; `FailureCause` has seven members. `None` has no probe string. | The bijection is `ProbeFailureCauses` ↔ `FailureCause \ { None }`. `None` is the cause on a *reachable* interval; `To(None)` throws. |
| 7 | §7.4 names `active → quiet → dark` and never gives thresholds. | Out of scope here. `ArchiveSweeper` moves only `→ Archived` and `Archived → Active`. The intermediate presentational bands are Plan 5's. |
| 8 | §5.2 mentions hourly and daily rollups; no interface, table or plan owns them. | Not built here. `presence_sample` is partitioned monthly so a rollup can later work on whole partitions. |
| 9 | `Game.Slug` is stored but nothing says who mints it. | The schema enforces uniqueness; minting belongs to Plan 3's identity matcher, which is what creates games. |
| 10 | `WhoReading.Unread` was `new(WhoConfidence.Unknown)`, and record equality made "we never sent WHO" indistinguishable from "we sent WHO and could not read the answer" — so `PresenceWriter` could not tell §5.4's own named bug case from never having asked. | **Fixed at source in Plan 01; the workaround here is withdrawn.** `WhoConfidence` gains `NotAttempted` as its *zero* value, so a default-constructed reading claims nothing; `WhoReading` gains the `NotAttempted` and `Unreadable` statics and a `WasAttempted` predicate, and `WhoReading.Unread` no longer exists. Task 15's `PresenceWriter` reads `Who.Confidence` directly and **derives** the reason from it — `MsspVia` no longer enters the decision at all. `PresenceWriterTests.NeverHavingAskedAndAskingAndFailingAreDifferentReasons` is the pin that stops it regressing. |
| 11 | The no-"uptime" rule collides with MSSP's own `UPTIME` variable. | The rule binds schema *identifiers*. `UPTIME` may appear as a field *value* in `game_field.field`; the test greps `information_schema.columns` only. |

---

### Task 1: `MUI.Storage` project, test project, and the PostgreSQL container harness

Spec: §12 (integration tests need a real database), Global Constraints (new test project → `slnx` + CI).

**Files:**
- Create: `src/MUI.Storage/MUI.Storage.csproj`
- Create: `tests/MUI.Storage.Tests/MUI.Storage.Tests.csproj`
- Create: `tests/MUI.Storage.Tests/Support/PostgresFixture.cs`
- Create: `tests/MUI.Storage.Tests/ContainerHarnessTests.cs`
- Modify: `Directory.Packages.props`
- Modify: `MUIndex.slnx`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: nothing.
- Produces: `MUI.Storage.Tests.Support.PostgresFixture.FreshDatabaseAsync()` returning
  `Task<TestDatabase>`; `TestDatabase.DataSource` of type `NpgsqlDataSource`; `TestDatabase` is
  `IAsyncDisposable`. Every later Storage task uses these.

- [ ] **Step 1: Add the package versions**

In `Directory.Packages.props`, inside the first `<ItemGroup>` (after the logging entry), add:

```xml
    <!--
      PostgreSQL access. Npgsql plus Dapper and hand-written SQL, never EF Core: the schema in
      src/MUI.Storage/Migrations is the source of truth and a mapper that can invent one is a
      liability rather than a convenience.
    -->
    <PackageVersion Include="Npgsql" Version="10.0.0" />
    <PackageVersion Include="Dapper" Version="2.1.66" />
```

In the test `<ItemGroup>` (after the TUnit entry), add:

```xml
    <!-- A real PostgreSQL 17 for the storage suite. A fake database proves nothing about a CHECK
         constraint, a partition or a partial unique index, and all three carry correctness here. -->
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.7.0" />
```

- [ ] **Step 2: Create the storage project**

`src/MUI.Storage/MUI.Storage.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <RootNamespace>MUI.Storage</RootNamespace>
    <AssemblyName>MUI.Storage</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\MUI.Catalog\MUI.Catalog.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Npgsql" />
    <PackageReference Include="Dapper" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

  <!--
    Migrations are embedded so the runner cannot be defeated by a missing content file on a
    published deployment. The LogicalName is explicit because the default name mangler is free to
    rewrite a path segment beginning with a digit, and MigrationRunner orders by that exact string.
  -->
  <ItemGroup>
    <EmbeddedResource Include="Migrations\*.sql">
      <LogicalName>MUI.Storage.Migrations.%(Filename)%(Extension)</LogicalName>
    </EmbeddedResource>
  </ItemGroup>

  <!--
    No reference to MUI.Crawl or MUI.Discovery. Storage knows the catalogue's shape and nothing
    about how a fact was obtained.
  -->

</Project>
```

- [ ] **Step 3: Create the test project**

`tests/MUI.Storage.Tests/MUI.Storage.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>MUI.Storage.Tests</RootNamespace>
    <AssemblyName>MUI.Storage.Tests</AssemblyName>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="TUnit" />
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Dapper" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\MUI.Storage\MUI.Storage.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Register both projects in the solution**

In `MUIndex.slnx`, add to the `/src/` folder after `MUI.Discovery`:

```xml
    <Project Path="src/MUI.Storage/MUI.Storage.csproj" />
```

and to the `/tests/` folder after `MUI.Discovery.Tests`:

```xml
    <Project Path="tests/MUI.Storage.Tests/MUI.Storage.Tests.csproj" />
```

- [ ] **Step 5: Add the CI step**

In `.github/workflows/ci.yml`, after the *Test — Discovery* step:

```yaml
      # Storage's tests run PostgreSQL 17 in Docker via Testcontainers. The Windows runner has no
      # Linux container engine, so this one suite is Linux-only; every other suite still runs on both.
      - name: Test — Storage
        if: runner.os == 'Linux'
        shell: bash
        run: dotnet run -c Release --no-build --project tests/MUI.Storage.Tests/MUI.Storage.Tests.csproj
```

- [ ] **Step 6: Write the failing harness test**

`tests/MUI.Storage.Tests/ContainerHarnessTests.cs`:

```csharp
using Dapper;

using MUI.Storage.Tests.Support;

namespace MUI.Storage.Tests;

/// <summary>
/// The harness itself, tested before anything is built on it. If this fails, nothing else in this
/// suite is telling the truth about the schema.
/// </summary>
public class ContainerHarnessTests
{
    [Test]
    public async Task AFreshDatabaseAnswersAQuery()
    {
        await using var db = await PostgresFixture.FreshDatabaseAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();

        var answer = await connection.QuerySingleAsync<int>("SELECT 1");

        await Assert.That(answer).IsEqualTo(1);
    }

    [Test]
    public async Task TwoFreshDatabasesAreGenuinelySeparate()
    {
        // Tests share one container and must not share a schema, or a CREATE TABLE in one test
        // becomes a silent precondition of another.
        await using var first = await PostgresFixture.FreshDatabaseAsync();
        await using var second = await PostgresFixture.FreshDatabaseAsync();

        await using var firstConnection = await first.DataSource.OpenConnectionAsync();
        await firstConnection.ExecuteAsync("CREATE TABLE only_here (id integer)");

        await using var secondConnection = await second.DataSource.OpenConnectionAsync();
        var exists = await secondConnection.QuerySingleAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'only_here')");

        await Assert.That(exists).IsFalse();
    }
}
```

- [ ] **Step 7: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0246: The type or namespace name 'PostgresFixture' could not be found`.

- [ ] **Step 8: Write the fixture**

`tests/MUI.Storage.Tests/Support/PostgresFixture.cs`:

```csharp
using Npgsql;

using Testcontainers.PostgreSql;

namespace MUI.Storage.Tests.Support;

/// <summary>
/// One PostgreSQL 17 container for the whole test session, and a brand-new database inside it for
/// every test that asks.
/// </summary>
/// <remarks>
/// Per-test databases rather than per-test transactions, because the things worth testing here are
/// DDL: partitions, partial unique indices and CHECK constraints. A rolled-back transaction cannot
/// exercise a migration runner that creates tables. The container is deliberately never stopped —
/// Testcontainers' Ryuk reaper removes it when the test process exits, and a session-teardown hook
/// would be one more piece of framework API to get wrong.
/// </remarks>
public static class PostgresFixture
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgreSqlContainer? _container;

    public static async Task<TestDatabase> FreshDatabaseAsync()
    {
        var container = await ContainerAsync();
        var name = "mui_" + Guid.NewGuid().ToString("N");

        await using (var admin = new NpgsqlConnection(container.GetConnectionString()))
        {
            await admin.OpenAsync();

            // The name is a fresh GUID with the dashes removed, so there is nothing to inject; a
            // database name cannot be a parameter in any case.
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name };
        return new TestDatabase(NpgsqlDataSource.Create(builder.ConnectionString));
    }

    private static async Task<PostgreSqlContainer> ContainerAsync()
    {
        if (_container is not null)
        {
            return _container;
        }

        await Gate.WaitAsync();
        try
        {
            if (_container is null)
            {
                var container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
                await container.StartAsync();
                _container = container;
            }

            return _container;
        }
        finally
        {
            Gate.Release();
        }
    }
}

public sealed class TestDatabase(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    public NpgsqlDataSource DataSource { get; } = dataSource;

    public async ValueTask DisposeAsync() => await DataSource.DisposeAsync();
}
```

- [ ] **Step 9: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests </dev/null
```
Expected: PASS — 2 tests.

- [ ] **Step 10: Commit**

```bash
git add Directory.Packages.props MUIndex.slnx .github/workflows/ci.yml \
        src/MUI.Storage tests/MUI.Storage.Tests
git commit -m "build: add MUI.Storage and a PostgreSQL 17 container test harness"
```

---

### Task 2: The field registry (spec §5.6)

Every descriptive field is declared once, with its expected refresh window, so the API, the plain
surface and the rendered page share one idea of when a value has aged out.

**Files:**
- Create: `src/MUI.Catalog/FieldRegistry.cs`
- Create: `tests/MUI.Catalog.Tests/FieldRegistryTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum MUI.Catalog.FieldValueKind { Text, Integer, Boolean, Url, Email, Enum, Timestamp }`
  - `sealed record MUI.Catalog.FieldDefinition(string Name, FieldValueKind Kind, bool OwnerEnrichable, TimeSpan ExpectedRefresh)`
  - `static class MUI.Catalog.FieldRegistry` with `IReadOnlyList<FieldDefinition> All`,
    `FieldDefinition For(string field)`, `bool IsStale(string field, DateTimeOffset lastConfirmedAt, DateTimeOffset now)`
  - `static class MUI.Catalog.CapabilityFields` with `string Measured(string capability)`,
    `string Declared(string capability)`, `IReadOnlyList<string> Names`

- [ ] **Step 1: Write the failing test**

`tests/MUI.Catalog.Tests/FieldRegistryTests.cs`:

```csharp
namespace MUI.Catalog.Tests;

/// <summary>
/// Spec §5.6: "old" is not one duration. The two anchors the spec argues from — a player count
/// stale in hours, a hand-typed GENRE unremarkable at six months and notable at six years — are
/// what these tests pin. If a window moves past one of them, this file is what says so.
/// </summary>
public class FieldRegistryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task APlayerCountIsStaleInHours()
    {
        var stale = FieldRegistry.IsStale("PLAYERS", Now.AddHours(-3), Now);

        await Assert.That(stale).IsTrue();
    }

    [Test]
    public async Task AHandTypedGenreIsUnremarkableAtSixMonths()
    {
        var stale = FieldRegistry.IsStale("GENRE", Now.AddDays(-183), Now);

        await Assert.That(stale).IsFalse();
    }

    [Test]
    public async Task AHandTypedGenreIsNotableAtSixYears()
    {
        var stale = FieldRegistry.IsStale("GENRE", Now.AddDays(-2192), Now);

        await Assert.That(stale).IsTrue();
    }

    [Test]
    public async Task AMeasuredCapabilityGoesStaleFasterThanADeclaredOne()
    {
        // We re-measure the handshake on every probe, so a measured capability that has not been
        // confirmed for a day is a fact about our crawler, not about the game. The game's own
        // claim is hand-typed and expected to sit still.
        var measured = FieldRegistry.For(CapabilityFields.Measured("GMCP")).ExpectedRefresh;
        var declared = FieldRegistry.For(CapabilityFields.Declared("GMCP")).ExpectedRefresh;

        await Assert.That(measured).IsLessThan(declared);
    }

    [Test]
    public async Task TheOwnerEnrichableFieldsAreTheOnesMsspCannotExpress()
    {
        // Spec §3.2 names exactly these as absent from MSSP.
        var enrichable = FieldRegistry.All.Where(f => f.OwnerEnrichable).Select(f => f.Name).ToList();

        await Assert.That(enrichable).Contains("FANDOM");
        await Assert.That(enrichable).Contains("APPLICATION PROCESS");
        await Assert.That(enrichable).Contains("RP ENFORCEMENT");
        await Assert.That(enrichable).Contains("CONSENT TOOLS");
    }

    [Test]
    public async Task TheRequiredMsspTrioIsDeclared()
    {
        var names = FieldRegistry.All.Select(f => f.Name).ToList();

        await Assert.That(names).Contains("NAME");
        await Assert.That(names).Contains("PLAYERS");
        await Assert.That(names).Contains("UPTIME");
    }

    [Test]
    public async Task AnUnknownFieldGetsAPermissiveDefaultRatherThanAThrow()
    {
        // A game may emit any unofficial MSSP variable it likes. Refusing to describe one would
        // make the registry a gate on ingestion, which it is not.
        var definition = FieldRegistry.For("SOME UNOFFICIAL THING");

        await Assert.That(definition.Name).IsEqualTo("SOME UNOFFICIAL THING");
        await Assert.That(definition.Kind).IsEqualTo(FieldValueKind.Text);
        await Assert.That(definition.OwnerEnrichable).IsFalse();
        await Assert.That(definition.ExpectedRefresh).IsEqualTo(TimeSpan.FromDays(365));
    }

    [Test]
    public async Task NoFieldIsDeclaredTwice()
    {
        var duplicates = FieldRegistry.All
            .GroupBy(f => f.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        await Assert.That(duplicates).IsEmpty();
    }

    [Test]
    public async Task ACapabilityFieldNameIsNamespacedAndSaysWhichSideItCameFrom()
    {
        await Assert.That(CapabilityFields.Measured("XTERM 256 COLORS"))
            .IsEqualTo("capability.xterm-256-colors.measured");
        await Assert.That(CapabilityFields.Declared("GMCP")).IsEqualTo("capability.gmcp.declared");
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0103: The name 'FieldRegistry' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

`src/MUI.Catalog/FieldRegistry.cs`:

```csharp
namespace MUI.Catalog;

/// <summary>What a field's value is, so a surface can render and validate it without guessing.</summary>
public enum FieldValueKind
{
    Text,
    Integer,
    Boolean,
    Url,
    Email,
    Enum,
    Timestamp,
}

/// <summary>
/// One descriptive field, declared once (spec §5.6).
/// </summary>
/// <param name="ExpectedRefresh">
/// How long a value may go unconfirmed before it is stale. The part that matters: "old" is not one
/// duration, and the window belongs beside the field definition rather than in a front-end
/// conditional, because the API, the plain-text surface and the rendered page must all agree on
/// when a value has aged out and only one of them is a front end.
/// </param>
public sealed record FieldDefinition(
    string Name,
    FieldValueKind Kind,
    bool OwnerEnrichable,
    TimeSpan ExpectedRefresh);

/// <summary>
/// Capability field names. A capability is stored twice under two names rather than once under one
/// (spec §3.1, §5.1, §9): <c>capability.gmcp.measured</c> is what the telnet handshake offered and
/// <c>capability.gmcp.declared</c> is what the game's MSSP claims. Keeping them apart is what lets
/// the game page show "declared GMCP, not offered in handshake" — the disagreement is the
/// interesting fact, and one row per (game, field) cannot hold both sides of it.
/// </summary>
public static class CapabilityFields
{
    public const string Prefix = "capability.";
    public const string MeasuredSuffix = ".measured";
    public const string DeclaredSuffix = ".declared";

    /// <summary>The capabilities worth carrying both sides of. Spec §6.1 for the measured set.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "ANSI", "ATCP", "CHARSET", "EOR", "GMCP", "MCCP", "MCP", "MSDP", "MSP", "MXP",
        "NAWS", "NEW-ENVIRON", "PUEBLO", "SSL", "TLS", "TTYPE", "UTF-8", "VT100",
        "XTERM 256 COLORS", "ZMP",
    ];

    public static string Measured(string capability) => Prefix + Normalise(capability) + MeasuredSuffix;

    public static string Declared(string capability) => Prefix + Normalise(capability) + DeclaredSuffix;

    private static string Normalise(string capability) =>
        capability.Trim().ToLowerInvariant().Replace(' ', '-');
}

/// <summary>
/// Every descriptive field this site stores, and how long each may go unconfirmed (spec §5.6).
/// </summary>
/// <remarks>
/// The windows are calibrated against the two anchors the spec argues from: a player count is stale
/// in hours, and a hand-typed <c>GENRE</c> is unremarkable at six months and notable at six years.
/// Everything else is placed relative to those, on one question — does the codebase fill this in
/// automatically (short window, because a stale one means something went wrong) or did a human type
/// it into <c>mush.cnf</c> once (long window, because sitting still is its normal state)?
/// </remarks>
public static class FieldRegistry
{
    /// <summary>A count moves constantly, so anything measured hourly is stale within hours.</summary>
    private static readonly TimeSpan Volatile = TimeSpan.FromHours(2);

    /// <summary>Re-measured on every probe; a day without confirmation means our crawler, not the game.</summary>
    private static readonly TimeSpan Measured = TimeSpan.FromDays(1);

    /// <summary>Auto-filled by the codebase and expected to change only on a move or an upgrade.</summary>
    private static readonly TimeSpan Automatic = TimeSpan.FromDays(30);

    /// <summary>Hand-typed contact details: worth chasing at a quarter, not at a month.</summary>
    private static readonly TimeSpan Contactable = TimeSpan.FromDays(90);

    /// <summary>
    /// Hand-typed description. Six months is unremarkable and six years is notable, so the window is
    /// a year: the first anchor sits inside it and the second is six times past it.
    /// </summary>
    private static readonly TimeSpan HandTyped = TimeSpan.FromDays(365);

    public static IReadOnlyList<FieldDefinition> All { get; } = Build();

    private static readonly Dictionary<string, FieldDefinition> Index =
        All.ToDictionary(definition => definition.Name, StringComparer.Ordinal);

    /// <summary>
    /// The definition for a field, or a permissive default for one nobody declared. A game may emit
    /// any unofficial MSSP variable it likes and the registry is not a gate on ingestion.
    /// </summary>
    public static FieldDefinition For(string field) =>
        Index.TryGetValue(field, out var known)
            ? known
            : new FieldDefinition(field, FieldValueKind.Text, OwnerEnrichable: false, HandTyped);

    public static bool IsStale(string field, DateTimeOffset lastConfirmedAt, DateTimeOffset now) =>
        now - lastConfirmedAt > For(field).ExpectedRefresh;

    private static IReadOnlyList<FieldDefinition> Build()
    {
        var fields = new List<FieldDefinition>();

        void Add(string name, FieldValueKind kind, TimeSpan window, bool ownerEnrichable = false) =>
            fields.Add(new FieldDefinition(name, kind, ownerEnrichable, window));

        // The required trio (MSSP). PLAYERS is declared here because it is the staleness anchor §5.6
        // argues from — but it is NEVER stored as a GameField: the count lives in §5.2's presence
        // series, where `who` outranks `mssp`. FieldReconciler skips it, and skips UPTIME with it.
        Add("NAME", FieldValueKind.Text, Automatic);
        Add("PLAYERS", FieldValueKind.Integer, Volatile);
        Add("UPTIME", FieldValueKind.Timestamp, Volatile);

        // The generic set — mostly auto-filled by the codebase.
        Add("CRAWL DELAY", FieldValueKind.Integer, Automatic);
        Add("HOSTNAME", FieldValueKind.Text, Automatic);
        Add("PORT", FieldValueKind.Integer, Automatic);
        Add("CODEBASE", FieldValueKind.Text, Automatic);
        Add("IP", FieldValueKind.Text, Automatic);
        Add("IPV6", FieldValueKind.Text, Automatic);
        Add("CONTACT", FieldValueKind.Email, Contactable);
        Add("WEBSITE", FieldValueKind.Url, Contactable);
        Add("DISCORD", FieldValueKind.Url, Contactable);
        Add("ICON", FieldValueKind.Url, HandTyped);
        Add("CREATED", FieldValueKind.Integer, HandTyped);
        Add("LANGUAGE", FieldValueKind.Text, HandTyped);
        Add("LOCATION", FieldValueKind.Text, HandTyped);
        Add("MINIMUM AGE", FieldValueKind.Integer, HandTyped);
        Add("CHARSET", FieldValueKind.Text, Automatic);

        // The categorisation set — hand-typed once, at install, and then left alone. This is the
        // set §3.1 warns about: a crawler presenting these with the same confidence as the
        // handshake is publishing a 2017 answer as a live one.
        Add("FAMILY", FieldValueKind.Enum, Automatic);
        Add("GENRE", FieldValueKind.Enum, HandTyped);
        Add("GAMEPLAY", FieldValueKind.Enum, HandTyped);
        Add("GAMESYSTEM", FieldValueKind.Enum, HandTyped);
        Add("SUBGENRE", FieldValueKind.Enum, HandTyped);
        Add("STATUS", FieldValueKind.Enum, HandTyped);
        Add("INTERMUD", FieldValueKind.Text, HandTyped);

        // Our own non-MSSP fields are lower-case and namespaced, so an MSSP variable and one of ours
        // can never collide however unofficial the variable.
        Add("connect_screen", FieldValueKind.Text, Automatic);

        // Capabilities, both sides. Measured is re-observed every probe; declared is hand-typed.
        foreach (var capability in CapabilityFields.Names)
        {
            Add(CapabilityFields.Measured(capability), FieldValueKind.Boolean, Measured);
            Add(CapabilityFields.Declared(capability), FieldValueKind.Boolean, Automatic);
        }

        // Owner enrichment — spec §3.2 names exactly these as genuinely absent from MSSP. SUBGENRE
        // cannot say "Marvel" or "Exalted", and nothing in the taxonomy expresses how a character
        // application works, how RP is enforced, or what consent tooling exists.
        Add("FANDOM", FieldValueKind.Text, HandTyped, ownerEnrichable: true);
        Add("APPLICATION PROCESS", FieldValueKind.Text, HandTyped, ownerEnrichable: true);
        Add("RP ENFORCEMENT", FieldValueKind.Enum, HandTyped, ownerEnrichable: true);
        Add("CONSENT TOOLS", FieldValueKind.Text, HandTyped, ownerEnrichable: true);

        return fields;
    }
}
```

- [ ] **Step 4: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
```
Expected: PASS — the 9 new tests plus the existing `ArchivePolicyTests`.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Catalog/FieldRegistry.cs tests/MUI.Catalog.Tests/FieldRegistryTests.cs
git commit -m "feat(catalog): declare every field once, with its expected refresh window (spec 5.6)"
```

---

### Task 3: The catalogue records, and `GameField.IsStale` (spec §5.1–§5.6)

**Files:**
- Create: `src/MUI.Catalog/CatalogRecords.cs`
- Modify: `src/MUI.Catalog/Lifecycle.cs` (add `FailureCause.Unknown`)
- Create: `tests/MUI.Catalog.Tests/CatalogRecordTests.cs`

**Interfaces:**
- Consumes: `FieldRegistry`, `CapabilityFields`, `FieldSource`, `Provenance.IsMeasured` (Task 2 and existing).
- Produces:
  - `enum FieldConfidence { Observed, Reported, Inferred }`
  - `static class FieldConfidences` with `FieldConfidence For(FieldSource source)`
  - `sealed record Game(Guid Id, string Slug, string Name, LifecycleState State, bool IsClaimed, DateTimeOffset FirstSeenAt, DateTimeOffset? LastReachableAt, DateTimeOffset? ArchivedAt)`
  - `sealed record GameField(Guid GameId, string Field, string Value, FieldSource Source, FieldConfidence Confidence, DateTimeOffset FirstSeenAt, DateTimeOffset LastConfirmedAt)` with `bool IsStale(DateTimeOffset now)`
  - `sealed record FieldChange(long Id, Guid GameId, string Field, string? OldValue, string NewValue, FieldSource Source, DateTimeOffset At)`
  - `enum PresenceSource { Who, Mssp, ImportedMeasured }`
  - `static class UnmeasurableReasons` with `WhoUnparseable`, `NoMsspPlayers`, `OwnerSuppressed`
  - `sealed record PresenceSample(Guid GameId, DateTimeOffset At, int? Count, PresenceSource Source, string? UnmeasurableReason, string? AggregatesJson)`
  - `sealed record AvailabilityInterval(long Id, Guid GameId, AvailabilityState State, DateTimeOffset FromAt, DateTimeOffset? ToAt, FailureCause Cause)`
  - `enum EndpointKind { Telnet, Tls, WebSocket, Http }`, `enum EndpointState { Active, Stale, Gone }`
  - `sealed record GameEndpoint(Guid GameId, string Host, int Port, EndpointKind Kind, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, EndpointState State)`
  - `FailureCause.Unknown`

- [ ] **Step 1: Write the failing test**

`tests/MUI.Catalog.Tests/CatalogRecordTests.cs`:

```csharp
namespace MUI.Catalog.Tests;

/// <summary>
/// The stored shapes, and the two derivations that must not be re-done anywhere downstream:
/// a field's staleness (spec §5.6) and a value's confidence (spec §5.1).
/// </summary>
public class CatalogRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid AGame = Guid.Parse("6f1d5b1e-0c4a-4a4e-9b7a-6a1d5c2f8b31");

    private static GameField Field(string name, DateTimeOffset lastConfirmedAt) =>
        new(AGame, name, "value", FieldSource.Mssp, FieldConfidence.Reported, Now.AddYears(-3), lastConfirmedAt);

    [Test]
    public async Task AFieldAsksTheRegistryWhetherItIsStaleRatherThanBeingToldSeparately()
    {
        // "Nothing downstream re-derives it" (§5.6) — so the record itself is the one place that
        // knows, and it knows by asking the registry.
        var fresh = Field("GENRE", Now.AddDays(-100));
        var aged = Field("GENRE", Now.AddDays(-1000));

        await Assert.That(fresh.IsStale(Now)).IsFalse();
        await Assert.That(aged.IsStale(Now)).IsTrue();
    }

    [Test]
    public async Task TheSameAgeIsFreshForOneFieldAndStaleForAnother()
    {
        // The whole argument of §5.6 in one assertion: a single duration cannot answer this.
        var age = Now.AddDays(-10);

        await Assert.That(Field("GENRE", age).IsStale(Now)).IsFalse();
        await Assert.That(Field(CapabilityFields.Measured("GMCP"), age).IsStale(Now)).IsTrue();
    }

    [Test]
    public async Task AnObservedSourceCarriesObservedConfidence()
    {
        await Assert.That(FieldConfidences.For(FieldSource.Handshake)).IsEqualTo(FieldConfidence.Observed);
        await Assert.That(FieldConfidences.For(FieldSource.Who)).IsEqualTo(FieldConfidence.Observed);
        await Assert.That(FieldConfidences.For(FieldSource.ImportedMeasured)).IsEqualTo(FieldConfidence.Observed);
    }

    [Test]
    public async Task AGamesOwnClaimIsReportedAndABannerGuessIsInferred()
    {
        await Assert.That(FieldConfidences.For(FieldSource.Mssp)).IsEqualTo(FieldConfidence.Reported);
        await Assert.That(FieldConfidences.For(FieldSource.Owner)).IsEqualTo(FieldConfidence.Reported);
        await Assert.That(FieldConfidences.For(FieldSource.Banner)).IsEqualTo(FieldConfidence.Inferred);
    }

    [Test]
    public async Task ConfidenceAgreesWithProvenanceAboutWhatCountsAsMeasured()
    {
        // Two types answering the same question must not disagree. Provenance.IsMeasured already
        // exists; FieldConfidence.Observed has to mean the same thing.
        foreach (var source in Enum.GetValues<FieldSource>())
        {
            var provenance = new Provenance(source, Now, Now);
            var observed = FieldConfidences.For(source) is FieldConfidence.Observed;

            await Assert.That(observed).IsEqualTo(provenance.IsMeasured);
        }
    }

    [Test]
    public async Task ThereIsAFailureCauseForSomethingWeCouldNotClassify()
    {
        // Spec §5.3's cause list ends in an ellipsis and §12 says parser failures degrade to
        // unknown. Without this member the availability writer would have to invent one.
        await Assert.That(Enum.IsDefined(FailureCause.Unknown)).IsTrue();
    }

    [Test]
    public async Task AReachableIntervalIsOpenUntilItIsClosed()
    {
        var interval = new AvailabilityInterval(
            1, AGame, AvailabilityState.Reachable, Now.AddDays(-90), null, FailureCause.None);

        await Assert.That(interval.ToAt).IsNull();
        await Assert.That(interval.Cause).IsEqualTo(FailureCause.None);
    }

    [Test]
    public async Task AMeasuredZeroIsACountAndNotAnAbsence()
    {
        // Spec §5.4: a measured zero is a filled cell. It means we got in and nobody was there,
        // which is a real and useful fact about a game.
        var sample = new PresenceSample(AGame, Now, 0, PresenceSource.Who, null, null);

        await Assert.That(sample.Count).IsEqualTo(0);
        await Assert.That(sample.UnmeasurableReason).IsNull();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0246: The type or namespace name 'FieldConfidence' could not be found`.

- [ ] **Step 3: Add `FailureCause.Unknown`**

In `src/MUI.Catalog/Lifecycle.cs`, replace the `FailureCause` enum body:

```csharp
/// <summary>Why a probe failed. Only a change of cause writes a new interval.</summary>
public enum FailureCause
{
    /// <summary>The cause carried by a <em>reachable</em> interval. Never a probe's answer.</summary>
    None,
    Dns,
    Refused,
    Tls,
    Timeout,
    HandshakeStalled,

    /// <summary>
    /// Something we could not classify. Spec §5.3's cause list ends in an ellipsis and §12 says a
    /// parser failure degrades to unknown and is logged with the response redacted — so this is the
    /// landing place, and having it is what stops a writer inventing a wrong cause that would then
    /// look like a genuine transition.
    /// </summary>
    Unknown,
}
```

- [ ] **Step 4: Write the records**

`src/MUI.Catalog/CatalogRecords.cs`:

```csharp
namespace MUI.Catalog;

/// <summary>
/// How directly a value was come by. Spec §5.1 declares a <c>confidence</c> column beside
/// <c>source</c> and does not say what it holds; this is the reading that keeps it from being a
/// duplicate of the source: <see cref="Observed"/> means somebody's probe saw it,
/// <see cref="Reported"/> means someone stated it, <see cref="Inferred"/> means we derived it from
/// something else that was observed.
/// </summary>
public enum FieldConfidence
{
    Observed,
    Reported,
    Inferred,
}

/// <summary>The confidence a source implies. One mapping, so no writer picks its own.</summary>
public static class FieldConfidences
{
    public static FieldConfidence For(FieldSource source) => source switch
    {
        // A handshake and a WHO are things we watched happen. An imported measurement is something
        // a third party watched happen — worth less than ours, but still a measurement (§7.6), and
        // Provenance.IsMeasured already says so.
        FieldSource.Handshake or FieldSource.Who or FieldSource.ImportedMeasured => FieldConfidence.Observed,

        // A version string read out of a connect screen is a deduction about the codebase, not a
        // sighting of it.
        FieldSource.Banner => FieldConfidence.Inferred,

        _ => FieldConfidence.Reported,
    };
}

/// <summary>A game. Its slug is its permanent URL — an archived game keeps it (spec §7.5).</summary>
public sealed record Game(
    Guid Id,
    string Slug,
    string Name,
    LifecycleState State,
    bool IsClaimed,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastReachableAt,
    DateTimeOffset? ArchivedAt);

/// <summary>
/// One descriptive field's current value (spec §5.1). There is exactly one row per
/// <c>(game, field)</c> and no ledger, so a game whose <c>GENRE</c> never moves costs one row for
/// ever rather than one per hour.
/// </summary>
public sealed record GameField(
    Guid GameId,
    string Field,
    string Value,
    FieldSource Source,
    FieldConfidence Confidence,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastConfirmedAt)
{
    /// <summary>
    /// Whether this value has aged past its field's expected refresh window (spec §5.6). Derived
    /// here and nowhere else: the API, the plain surface and the rendered page must agree, and only
    /// one of them is a front end.
    /// </summary>
    public bool IsStale(DateTimeOffset now) => FieldRegistry.IsStale(Field, LastConfirmedAt, now);
}

/// <summary>
/// One entry in a game's change feed (spec §5.1) — a table of events that actually happened, which
/// is also what one wants to render. A first sighting is not one of them; that is the
/// <em>newly discovered</em> feed's business.
/// </summary>
public sealed record FieldChange(
    long Id,
    Guid GameId,
    string Field,
    string? OldValue,
    string NewValue,
    FieldSource Source,
    DateTimeOffset At);

/// <summary>Which channel produced a presence reading (spec §5.2).</summary>
public enum PresenceSource
{
    /// <summary>Parsed from <c>WHO</c>/<c>DOING</c> at the connect screen. Live, so it wins (§6.3).</summary>
    Who,

    /// <summary>MSSP <c>PLAYERS</c> — whatever the codebase last cached.</summary>
    Mssp,

    ImportedMeasured,
}

/// <summary>
/// Why a probe that succeeded could not produce a number. Spec §5.4's middle row: the cell is
/// hatched — <em>probed, unmeasurable</em> — and is emphatically not the empty cell that downtime
/// draws.
/// </summary>
public static class UnmeasurableReasons
{
    public const string WhoUnparseable = "who_unparseable";
    public const string NoMsspPlayers = "no_mssp_players";
    public const string OwnerSuppressed = "owner_suppressed";
}

/// <summary>
/// One presence reading (spec §5.2). <see cref="Count"/> is nullable and that is load-bearing: a
/// null count with a reason is a probe that got in and could not count, and writing nothing instead
/// would be indistinguishable from not having probed, which renders identically to downtime.
/// </summary>
public sealed record PresenceSample(
    Guid GameId,
    DateTimeOffset At,
    int? Count,
    PresenceSource Source,
    string? UnmeasurableReason,
    string? AggregatesJson);

/// <summary>
/// A span during which a game was in one availability state for one reason (spec §5.3). A game
/// reachable for three years is one open row, not twenty-six thousand samples.
/// </summary>
public sealed record AvailabilityInterval(
    long Id,
    Guid GameId,
    AvailabilityState State,
    DateTimeOffset FromAt,
    DateTimeOffset? ToAt,
    FailureCause Cause);

public enum EndpointKind
{
    Telnet,
    Tls,
    WebSocket,
    Http,
}

public enum EndpointState
{
    Active,
    Stale,
    Gone,
}

/// <summary>
/// An address a game answers on (spec §5.5). Plural and historical: a game that moves does not
/// become unfindable, because old endpoints keep being probed at the §7.4 floor and a referral
/// pointing at an old address re-links to the existing game rather than minting a duplicate.
/// </summary>
public sealed record GameEndpoint(
    Guid GameId,
    string Host,
    int Port,
    EndpointKind Kind,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    EndpointState State);
```

- [ ] **Step 5: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
```
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Catalog/CatalogRecords.cs src/MUI.Catalog/Lifecycle.cs \
        tests/MUI.Catalog.Tests/CatalogRecordTests.cs
git commit -m "feat(catalog): the stored shapes, with staleness and confidence derived once"
```

---

### Task 4: `SourcePrecedence` — the §5.1 ladder

**Files:**
- Create: `src/MUI.Catalog/SourcePrecedence.cs`
- Create: `tests/MUI.Catalog.Tests/SourcePrecedenceTests.cs`

**Interfaces:**
- Consumes: `FieldSource` (existing), `FieldRegistry.For`, `CapabilityFields` (Task 2).
- Produces: `static class MUI.Catalog.SourcePrecedence` with
  `int RankOf(FieldSource source)`, `bool IsCapabilityField(string field)`,
  `bool Wins(FieldSource candidate, FieldSource incumbent, string field)`.

**Note for the implementer:** `FieldSource`'s **declared order already encodes this ladder** and its
doc comment says so. `RankOf` is `(int)source` and nothing else. Do not reorder that enum casually —
inserting a member in the middle silently re-ranks every source above it, and no compiler error
follows.

- [ ] **Step 1: Write the failing test**

`tests/MUI.Catalog.Tests/SourcePrecedenceTests.cs`:

```csharp
namespace MUI.Catalog.Tests;

/// <summary>
/// Spec §5.1's ladder, highest first: handshake for capability fields since it is observed; owner
/// for enrichment-only fields; then mssp, banner, imported_measured, imported_asserted. Staff
/// overrides anything.
/// </summary>
public class SourcePrecedenceTests
{
    [Test]
    public async Task StaffOverridesAnything()
    {
        await Assert.That(SourcePrecedence.RankOf(FieldSource.Staff)).IsEqualTo(0);

        foreach (var incumbent in Enum.GetValues<FieldSource>())
        {
            await Assert.That(SourcePrecedence.Wins(FieldSource.Staff, incumbent, "GENRE")).IsTrue();
        }
    }

    [Test]
    public async Task TheDeclaredEnumOrderIsTheLadder()
    {
        // If someone reorders FieldSource, this is the test that notices.
        var expected = new[]
        {
            FieldSource.Staff, FieldSource.Handshake, FieldSource.Owner, FieldSource.Who,
            FieldSource.Mssp, FieldSource.Banner, FieldSource.ImportedMeasured, FieldSource.ImportedAsserted,
        };

        await Assert.That(Enum.GetValues<FieldSource>()).IsEquivalentTo(expected);

        for (var i = 1; i < expected.Length; i++)
        {
            await Assert.That(SourcePrecedence.RankOf(expected[i]))
                .IsGreaterThan(SourcePrecedence.RankOf(expected[i - 1]));
        }
    }

    [Test]
    public async Task ImportedFactsNeverOutrankMeasuredOnes()
    {
        // Spec §7.6, stated as a rule rather than a hope.
        await Assert.That(SourcePrecedence.Wins(FieldSource.ImportedMeasured, FieldSource.Mssp, "GENRE")).IsFalse();
        await Assert.That(SourcePrecedence.Wins(FieldSource.ImportedAsserted, FieldSource.ImportedMeasured, "GENRE")).IsFalse();
        await Assert.That(SourcePrecedence.Wins(FieldSource.Mssp, FieldSource.ImportedMeasured, "GENRE")).IsTrue();
    }

    [Test]
    public async Task AnOwnerWinsAnEnrichmentFieldAndNotACodebaseField()
    {
        // §5.1 gives the owner enrichment-only fields. CODEBASE is auto-filled by the server and an
        // owner asserting one does not beat the game itself reporting it.
        await Assert.That(SourcePrecedence.Wins(FieldSource.Owner, FieldSource.Mssp, "FANDOM")).IsTrue();
        await Assert.That(SourcePrecedence.Wins(FieldSource.Owner, FieldSource.Handshake, "CODEBASE")).IsFalse();
    }

    [Test]
    public async Task AMeasuredCapabilityIsRecognisedAsOne()
    {
        await Assert.That(SourcePrecedence.IsCapabilityField(CapabilityFields.Measured("GMCP"))).IsTrue();
        await Assert.That(SourcePrecedence.IsCapabilityField(CapabilityFields.Declared("GMCP"))).IsFalse();
        await Assert.That(SourcePrecedence.IsCapabilityField("GENRE")).IsFalse();
    }

    [Test]
    public async Task AnImportedCapabilityCannotDisplaceOneWeMeasuredOurselves()
    {
        // This is where the capability promotion earns its keep: a handshake and an MSSP claim are
        // stored under different names and never contend, but an importer writes into the measured
        // name and must lose to our own handshake.
        var field = CapabilityFields.Measured("GMCP");

        await Assert.That(SourcePrecedence.Wins(FieldSource.ImportedMeasured, FieldSource.Handshake, field)).IsFalse();
        await Assert.That(SourcePrecedence.Wins(FieldSource.Handshake, FieldSource.ImportedMeasured, field)).IsTrue();
    }

    [Test]
    public async Task ASourceAlwaysRefreshesItsOwnValue()
    {
        // Otherwise a game's second MSSP reading could never correct its first.
        foreach (var source in Enum.GetValues<FieldSource>())
        {
            await Assert.That(SourcePrecedence.Wins(source, source, "GENRE")).IsTrue();
        }
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0103: The name 'SourcePrecedence' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

`src/MUI.Catalog/SourcePrecedence.cs`:

```csharp
namespace MUI.Catalog;

/// <summary>
/// Which source wins when two disagree about one field (spec §5.1).
/// </summary>
/// <remarks>
/// <para>
/// The ladder, highest first: <c>handshake</c> for capability fields, since it is observed;
/// <c>owner</c> for enrichment-only fields; then <c>mssp</c>, <c>banner</c>,
/// <c>imported_measured</c>, <c>imported_asserted</c>. <c>staff</c> overrides anything and is logged.
/// </para>
/// <para>
/// <see cref="FieldSource"/>'s <em>declared order is</em> that ladder — see its own doc comment — so
/// <see cref="RankOf"/> is a cast and nothing more. Do not reorder that enum casually: inserting a
/// member in the middle re-ranks every source below it and no compiler error follows.
/// </para>
/// <para>
/// Two sources are promoted for particular fields rather than being given a second flat ordering,
/// because the promotion is a property of the <em>pair</em> — an owner outranks MSSP on a field MSSP
/// cannot express, and outranks nothing on a field the server fills in itself.
/// </para>
/// </remarks>
public static class SourcePrecedence
{
    /// <summary>Immediately below <see cref="FieldSource.Staff"/>, which is rank 0 and beats everything.</summary>
    private const int PromotedRank = 1;

    public static int RankOf(FieldSource source) => (int)source;

    /// <summary>
    /// Whether this field holds a capability we <em>measured</em> — the handshake side of the pair
    /// <see cref="CapabilityFields"/> stores, as opposed to the game's own MSSP claim.
    /// </summary>
    public static bool IsCapabilityField(string field) =>
        field.StartsWith(CapabilityFields.Prefix, StringComparison.Ordinal)
        && field.EndsWith(CapabilityFields.MeasuredSuffix, StringComparison.Ordinal);

    public static bool Wins(FieldSource candidate, FieldSource incumbent, string field) =>
        candidate == incumbent
        || EffectiveRank(candidate, field) < EffectiveRank(incumbent, field);

    private static int EffectiveRank(FieldSource source, string field) => source switch
    {
        // A server offering an option is an observation; a game claiming it is an assertion (§3.1).
        FieldSource.Handshake when IsCapabilityField(field) => PromotedRank,

        // Fandom/IP, application process, RP enforcement and consent tooling are absent from MSSP
        // (§3.2), so on those fields the owner is the only party who can be right.
        FieldSource.Owner when FieldRegistry.For(field).OwnerEnrichable => PromotedRank,

        _ => RankOf(source),
    };
}
```

- [ ] **Step 4: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Catalog/SourcePrecedence.cs tests/MUI.Catalog.Tests/SourcePrecedenceTests.cs
git commit -m "feat(catalog): the source precedence ladder, keyed off the enum's declared order"
```

---

### Task 5: `AvailabilityArithmetic` — reachable time, reachable percent, longest outage (spec §5.3, §5.7, §13)

§13 asks for availability arithmetic tested against synthetic interval sequences. It lives in
`MUI.Catalog` as pure functions so the arithmetic is provable without a database, and so
`ArchiveSweeper` (Task 18) and Plan 5's `ReachabilityView` compute it the same way.

**Files:**
- Create: `src/MUI.Catalog/AvailabilityArithmetic.cs`
- Create: `tests/MUI.Catalog.Tests/AvailabilityArithmeticTests.cs`

**Interfaces:**
- Consumes: `AvailabilityInterval`, `AvailabilityState` (Task 3, existing).
- Produces: `static class MUI.Catalog.AvailabilityArithmetic` with
  `TimeSpan CumulativeReachable(IEnumerable<AvailabilityInterval> intervals, DateTimeOffset now)`,
  `double ReachablePercent(IEnumerable<AvailabilityInterval> intervals, DateTimeOffset from, DateTimeOffset to)`,
  `TimeSpan LongestOutage(IEnumerable<AvailabilityInterval> intervals, DateTimeOffset now)`.

- [ ] **Step 1: Write the failing test**

`tests/MUI.Catalog.Tests/AvailabilityArithmeticTests.cs`:

```csharp
namespace MUI.Catalog.Tests;

/// <summary>
/// Spec §13: availability arithmetic against synthetic interval sequences. §5.3's whole claim is
/// that "reachable over 90 days" and "longest outage" become arithmetic over a handful of rows,
/// and this is that arithmetic.
/// </summary>
public class AvailabilityArithmeticTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid AGame = Guid.Parse("6f1d5b1e-0c4a-4a4e-9b7a-6a1d5c2f8b31");

    private static AvailabilityInterval Reachable(double fromDaysAgo, double? toDaysAgo) =>
        new(0, AGame, AvailabilityState.Reachable, Now.AddDays(-fromDaysAgo),
            toDaysAgo is null ? null : Now.AddDays(-toDaysAgo.Value), FailureCause.None);

    private static AvailabilityInterval Down(double fromDaysAgo, double? toDaysAgo, FailureCause cause) =>
        new(0, AGame, AvailabilityState.Unreachable, Now.AddDays(-fromDaysAgo),
            toDaysAgo is null ? null : Now.AddDays(-toDaysAgo.Value), cause);

    [Test]
    public async Task CumulativeReachableSumsTheReachableSpansAndIgnoresTheGaps()
    {
        // Spec §7.5: cumulative, not span. Reachable for two years out of five is credited with two,
        // and a history of flapping accrues nothing for the gaps.
        var intervals = new[]
        {
            Reachable(1825, 1460),      // one year up
            Down(1460, 730, FailureCause.Timeout),
            Reachable(730, 365),        // one more year up
            Down(365, 0, FailureCause.Refused),
        };

        var cumulative = AvailabilityArithmetic.CumulativeReachable(intervals, Now);

        await Assert.That(cumulative.TotalDays).IsEqualTo(730).Within(0.01);
    }

    [Test]
    public async Task AnOpenReachableIntervalIsCountedUpToNow()
    {
        var intervals = new[] { Reachable(400, null) };

        var cumulative = AvailabilityArithmetic.CumulativeReachable(intervals, Now);

        await Assert.That(cumulative.TotalDays).IsEqualTo(400).Within(0.01);
    }

    [Test]
    public async Task ReachablePercentIsClippedToTheWindowAtBothEnds()
    {
        // An interval that starts before the window and ends inside it contributes only the overlap.
        var intervals = new[] { Reachable(365, 45), Down(45, null, FailureCause.Dns) };

        var percent = AvailabilityArithmetic.ReachablePercent(intervals, Now.AddDays(-90), Now);

        await Assert.That(percent).IsEqualTo(50d).Within(0.01);
    }

    [Test]
    public async Task AGameReachableThroughoutTheWindowIsAHundredPercent()
    {
        var intervals = new[] { Reachable(1000, null) };

        var percent = AvailabilityArithmetic.ReachablePercent(intervals, Now.AddDays(-90), Now);

        await Assert.That(percent).IsEqualTo(100d).Within(0.01);
    }

    [Test]
    public async Task AnEmptyWindowIsZeroPercentRatherThanADivideByZero()
    {
        var percent = AvailabilityArithmetic.ReachablePercent([Reachable(10, null)], Now, Now);

        await Assert.That(percent).IsEqualTo(0d);
    }

    [Test]
    public async Task LongestOutageIsTheLongestUnreachableSpanAndNotTheSumOfThem()
    {
        var intervals = new[]
        {
            Reachable(400, 300),
            Down(300, 290, FailureCause.Timeout),   // 10 days
            Reachable(290, 200),
            Down(200, 160, FailureCause.Dns),       // 40 days
            Reachable(160, null),
        };

        var longest = AvailabilityArithmetic.LongestOutage(intervals, Now);

        await Assert.That(longest.TotalDays).IsEqualTo(40).Within(0.01);
    }

    [Test]
    public async Task AnOngoingOutageCountsUpToNow()
    {
        var intervals = new[] { Reachable(400, 100), Down(100, null, FailureCause.Refused) };

        var longest = AvailabilityArithmetic.LongestOutage(intervals, Now);

        await Assert.That(longest.TotalDays).IsEqualTo(100).Within(0.01);
    }

    [Test]
    public async Task DegradedIsAnOutageBecauseItIsNotReachable()
    {
        // Spec §5.7 measures reachability, and "we connected and the session stalled" is not it.
        var degraded = new AvailabilityInterval(
            0, AGame, AvailabilityState.Degraded, Now.AddDays(-30), null, FailureCause.HandshakeStalled);

        await Assert.That(AvailabilityArithmetic.CumulativeReachable([degraded], Now)).IsEqualTo(TimeSpan.Zero);
        await Assert.That(AvailabilityArithmetic.LongestOutage([degraded], Now).TotalDays).IsEqualTo(30).Within(0.01);
    }

    [Test]
    public async Task AGameThatWasNeverDownHasNoLongestOutage()
    {
        var longest = AvailabilityArithmetic.LongestOutage([Reachable(400, null)], Now);

        await Assert.That(longest).IsEqualTo(TimeSpan.Zero);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0103: The name 'AvailabilityArithmetic' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

`src/MUI.Catalog/AvailabilityArithmetic.cs`:

```csharp
namespace MUI.Catalog;

/// <summary>
/// The arithmetic §5.3 promises: with availability stored as intervals rather than samples,
/// "reachable over 90 days" and "longest outage" are sums over a handful of rows.
/// </summary>
/// <remarks>
/// Deliberately pure and deliberately in <c>MUI.Catalog</c>. §13 wants this tested against synthetic
/// interval sequences, which means it must be provable with no database in the room, and the archive
/// sweeper and the web tier must not each grow their own slightly different version.
/// </remarks>
public static class AvailabilityArithmetic
{
    /// <summary>
    /// Total time spent reachable, summing the reachable intervals only. <em>Cumulative, not span</em>
    /// (spec §7.5): a game reachable for two years out of five is credited with two, and the gaps
    /// earn nothing. An open interval is counted up to <paramref name="now"/>.
    /// </summary>
    public static TimeSpan CumulativeReachable(IEnumerable<AvailabilityInterval> intervals, DateTimeOffset now) =>
        intervals
            .Where(interval => interval.State is AvailabilityState.Reachable)
            .Aggregate(TimeSpan.Zero, (total, interval) => total + Duration(interval, now));

    /// <summary>
    /// The share of a window the game was reachable for, as a percentage. The API calls this
    /// <c>reachablePercent</c> and never <c>uptime</c> (spec §5.7): we measured a socket from one
    /// vantage point, and a game with a routing problem to our host is unreachable and perfectly alive.
    /// </summary>
    public static double ReachablePercent(
        IEnumerable<AvailabilityInterval> intervals,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var window = to - from;
        if (window <= TimeSpan.Zero)
        {
            return 0d;
        }

        var reachable = intervals
            .Where(interval => interval.State is AvailabilityState.Reachable)
            .Aggregate(TimeSpan.Zero, (total, interval) => total + Overlap(interval, from, to));

        return reachable / window * 100d;
    }

    /// <summary>
    /// The longest single span the game was not reachable for — the longest one, not the sum. Degraded
    /// counts: the question is reachability, and "we connected and the session stalled" is not it.
    /// </summary>
    public static TimeSpan LongestOutage(IEnumerable<AvailabilityInterval> intervals, DateTimeOffset now) =>
        intervals
            .Where(interval => interval.State is not AvailabilityState.Reachable)
            .Select(interval => Duration(interval, now))
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();

    private static TimeSpan Duration(AvailabilityInterval interval, DateTimeOffset now) =>
        (interval.ToAt ?? now) - interval.FromAt;

    private static TimeSpan Overlap(AvailabilityInterval interval, DateTimeOffset from, DateTimeOffset to)
    {
        var start = interval.FromAt > from ? interval.FromAt : from;
        var end = interval.ToAt ?? to;

        if (end > to)
        {
            end = to;
        }

        return end > start ? end - start : TimeSpan.Zero;
    }
}
```

- [ ] **Step 4: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Catalog/AvailabilityArithmetic.cs tests/MUI.Catalog.Tests/AvailabilityArithmeticTests.cs
git commit -m "feat(catalog): cumulative reachable time, reachable percent and longest outage"
```

---

### Task 6: `MigrationRunner`, the `mui_migration` ledger, and `0001_game.sql`

**Files:**
- Create: `src/MUI.Storage/MigrationRunner.cs`
- Create: `src/MUI.Storage/SqlEnums.cs`
- Create: `src/MUI.Storage/Migrations/0001_game.sql`
- Create: `tests/MUI.Storage.Tests/MigrationRunnerTests.cs`
- Modify: `tests/MUI.Storage.Tests/Support/PostgresFixture.cs` (add `MigratedAsync`)

**Interfaces:**
- Consumes: `PostgresFixture.FreshDatabaseAsync` (Task 1); `FieldSource`, `FailureCause`, `LifecycleState`,
  `AvailabilityState`, `PresenceSource`, `EndpointKind`, `EndpointState` (Task 3, existing).
- Produces:
  - `sealed class MUI.Storage.MigrationRunner(NpgsqlDataSource source, ILogger? logger = null)` with
    `Task<IReadOnlyList<string>> ApplyAsync(CancellationToken cancellationToken)`
  - `static class MUI.Storage.SqlEnums` with `string ToDb<T>(T value) where T : struct, Enum` and
    `T Parse<T>(string value) where T : struct, Enum`
  - `PostgresFixture.MigratedAsync()` returning `Task<TestDatabase>`

- [ ] **Step 1: Write the failing test**

`tests/MUI.Storage.Tests/MigrationRunnerTests.cs`:

```csharp
using Dapper;

using MUI.Catalog;
using MUI.Storage.Tests.Support;

namespace MUI.Storage.Tests;

/// <summary>
/// The runner has one interesting property and it is idempotence: it will be run on every process
/// start, by every replica, for ever.
/// </summary>
public class MigrationRunnerTests
{
    [Test]
    public async Task TheFirstRunAppliesEveryMigration()
    {
        await using var db = await PostgresFixture.FreshDatabaseAsync();

        var applied = await new MigrationRunner(db.DataSource).ApplyAsync(CancellationToken.None);

        await Assert.That(applied).IsNotEmpty();
        await Assert.That(applied).Contains("0001_game.sql");
    }

    [Test]
    public async Task TheSecondRunAppliesNothing()
    {
        await using var db = await PostgresFixture.FreshDatabaseAsync();
        var runner = new MigrationRunner(db.DataSource);

        await runner.ApplyAsync(CancellationToken.None);
        var second = await runner.ApplyAsync(CancellationToken.None);

        await Assert.That(second).IsEmpty();
    }

    [Test]
    public async Task MigrationsAreAppliedInLexicalOrder()
    {
        await using var db = await PostgresFixture.FreshDatabaseAsync();

        var applied = await new MigrationRunner(db.DataSource).ApplyAsync(CancellationToken.None);

        await Assert.That(applied).IsEquivalentTo(applied.OrderBy(name => name, StringComparer.Ordinal).ToList());
    }

    [Test]
    public async Task EveryAppliedMigrationIsRecordedInTheLedger()
    {
        await using var db = await PostgresFixture.FreshDatabaseAsync();
        var applied = await new MigrationRunner(db.DataSource).ApplyAsync(CancellationToken.None);

        await using var connection = await db.DataSource.OpenConnectionAsync();
        var ledger = (await connection.QueryAsync<string>(
            "SELECT name FROM mui_migration ORDER BY name")).ToList();

        await Assert.That(ledger).IsEquivalentTo(applied.ToList());
    }

    [Test]
    public async Task AGameRowRoundTripsThroughTheSchema()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();

        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO game (id, slug, name, state, is_claimed, first_seen_at)
            VALUES (@id, 'corvid', 'Corvid', 'active', false, now())
            """,
            new { id });

        var state = await connection.QuerySingleAsync<string>("SELECT state FROM game WHERE id = @id", new { id });

        await Assert.That(state).IsEqualTo(SqlEnums.ToDb(LifecycleState.Active));
    }

    [Test]
    public async Task AGameCannotBeGivenALifecycleStateNobodyDeclared()
    {
        // The vocabulary lives in the schema as well as in the enum, so a bad write fails at the
        // database rather than becoming a value the reader has to cope with.
        await using var db = await PostgresFixture.MigratedAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
            """
            INSERT INTO game (id, slug, name, state, is_claimed, first_seen_at)
            VALUES (@id, 'nope', 'Nope', 'zombie', false, now())
            """,
            new { id = Guid.NewGuid() })).Throws<Npgsql.PostgresException>();
    }

    [Test]
    public async Task TwoGamesCannotShareASlugBecauseTheSlugIsThePermanentUrl()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO game (id, slug, name, state, is_claimed, first_seen_at)
            VALUES (@id, 'corvid', 'Corvid', 'active', false, now())
            """,
            new { id = Guid.NewGuid() });

        await Assert.That(async () => await connection.ExecuteAsync(
            """
            INSERT INTO game (id, slug, name, state, is_claimed, first_seen_at)
            VALUES (@id, 'corvid', 'Corvid Two', 'active', false, now())
            """,
            new { id = Guid.NewGuid() })).Throws<Npgsql.PostgresException>();
    }

    [Test]
    public async Task EveryEnumTheSchemaStoresRoundTripsThroughSqlEnums()
    {
        await Assert.That(SqlEnums.ToDb(FailureCause.HandshakeStalled)).IsEqualTo("handshake_stalled");
        await Assert.That(SqlEnums.ToDb(FieldSource.ImportedMeasured)).IsEqualTo("imported_measured");
        await Assert.That(SqlEnums.ToDb(AvailabilityState.Reachable)).IsEqualTo("reachable");
        // Spec §5.5 spells this one "websocket", which snake-casing WebSocket does not produce, so
        // it is one of the declared overrides rather than a silently different word in the schema.
        await Assert.That(SqlEnums.ToDb(EndpointKind.WebSocket)).IsEqualTo("websocket");

        await Assert.That(SqlEnums.Parse<FailureCause>("handshake_stalled")).IsEqualTo(FailureCause.HandshakeStalled);
        await Assert.That(SqlEnums.Parse<FieldSource>("imported_asserted")).IsEqualTo(FieldSource.ImportedAsserted);
        await Assert.That(SqlEnums.Parse<PresenceSource>("who")).IsEqualTo(PresenceSource.Who);
        await Assert.That(SqlEnums.Parse<EndpointState>("gone")).IsEqualTo(EndpointState.Gone);
        await Assert.That(SqlEnums.Parse<EndpointKind>("websocket")).IsEqualTo(EndpointKind.WebSocket);
    }

    [Test]
    public async Task EveryStoredEnumMemberRoundTripsBothWays()
    {
        // A one-way mapping that loses a member is a row nobody can read back.
        foreach (var value in Enum.GetValues<FailureCause>())
        {
            await Assert.That(SqlEnums.Parse<FailureCause>(SqlEnums.ToDb(value))).IsEqualTo(value);
        }

        foreach (var value in Enum.GetValues<FieldSource>())
        {
            await Assert.That(SqlEnums.Parse<FieldSource>(SqlEnums.ToDb(value))).IsEqualTo(value);
        }

        foreach (var value in Enum.GetValues<EndpointKind>())
        {
            await Assert.That(SqlEnums.Parse<EndpointKind>(SqlEnums.ToDb(value))).IsEqualTo(value);
        }
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0246: The type or namespace name 'MigrationRunner' could not be found`.

- [ ] **Step 3: Write `SqlEnums`**

`src/MUI.Storage/SqlEnums.cs`:

```csharp
namespace MUI.Storage;

using System.Text;

/// <summary>
/// The one place a C# enum becomes a schema string and back.
/// </summary>
/// <remarks>
/// The schema stores these as <c>text</c> with a CHECK constraint rather than as PostgreSQL enum
/// types, because adding a value to a PostgreSQL enum is a migration and the vocabulary here is
/// still moving (spec §5.3's cause list ends in an ellipsis). A CHECK constraint is edited by the
/// same numbered <c>.sql</c> file as everything else.
/// </remarks>
public static class SqlEnums
{
    /// <summary>
    /// Members whose schema spelling is not their snake-cased name. Spec §5.5 writes
    /// <c>websocket</c> as one word, and the schema says what the spec says.
    /// </summary>
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal)
    {
        ["WebSocket"] = "websocket",
    };

    public static string ToDb<T>(T value) where T : struct, Enum
    {
        var name = value.ToString();

        return Overrides.TryGetValue(name, out var spelling) ? spelling : SnakeCase(name);
    }

    public static T Parse<T>(string value) where T : struct, Enum =>
        Enum.Parse<T>(value.Replace("_", string.Empty), ignoreCase: true);

    private static string SnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);

        for (var index = 0; index < name.Length; index++)
        {
            if (index > 0 && char.IsUpper(name[index]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[index]));
        }

        return builder.ToString();
    }
}
```

- [ ] **Step 4: Write `0001_game.sql`**

`src/MUI.Storage/Migrations/0001_game.sql`:

```sql
-- spec §5 — the game itself. Everything else in this schema hangs off it.
CREATE TABLE game (
    id                uuid PRIMARY KEY,
    slug              text NOT NULL,
    name              text NOT NULL,
    state             text NOT NULL,
    is_claimed        boolean NOT NULL DEFAULT false,
    first_seen_at     timestamptz NOT NULL,
    last_reachable_at timestamptz,
    archived_at       timestamptz,

    -- §7.4's lifecycle states, derived from availability history and never set by hand. The
    -- vocabulary lives here as well as in LifecycleState so a bad write fails at the database
    -- rather than becoming a value every reader has to cope with.
    CONSTRAINT game_state_vocabulary CHECK (state IN ('active', 'quiet', 'dark', 'archived')),

    -- §7.5: archiving is a presentation change, so an archived game has a date and nothing else
    -- about it is erased. A game that is not archived has no archive date.
    CONSTRAINT game_archived_games_have_a_date CHECK ((state = 'archived') = (archived_at IS NOT NULL))
);

-- §7.5: an archived game keeps its page, its history and its URL, and the URL is the slug — so it
-- is unique for ever, and it is also the lookup the game page performs on every request.
CREATE UNIQUE INDEX game_slug_key ON game (slug);

-- §9's default listing excludes archived games, which is most reads of this table. A partial index
-- keeps that query off the archive entirely rather than filtering it out afterwards.
CREATE INDEX game_state_idx ON game (state) WHERE state <> 'archived';

-- §7.5's sweep asks "which games have been dark longer than the grace they earned", which is an
-- ordered scan over how long ago each was last reachable.
CREATE INDEX game_last_reachable_at_idx ON game (last_reachable_at);
```

- [ ] **Step 5: Write `MigrationRunner`**

`src/MUI.Storage/MigrationRunner.cs`:

```csharp
namespace MUI.Storage;

using System.Reflection;

using Dapper;

using Microsoft.Extensions.Logging;

using Npgsql;

/// <summary>
/// Applies the numbered <c>.sql</c> files under <c>Migrations/</c>, in lexical order, each inside its
/// own transaction, recording each in the <c>mui_migration</c> ledger.
/// </summary>
/// <remarks>
/// Idempotent by construction: it will be run on every process start, by every replica, for ever, and
/// a second run must apply nothing. Deliberately not a migration framework — plain SQL files and a
/// ledger table are legible to anyone with <c>psql</c>, which is the property that matters when
/// something has gone wrong in production at four in the morning.
/// </remarks>
public sealed class MigrationRunner(NpgsqlDataSource source, ILogger? logger = null)
{
    private const string ResourcePrefix = "MUI.Storage.Migrations.";

    private const string LedgerDdl = """
        CREATE TABLE IF NOT EXISTS mui_migration (
            name       text PRIMARY KEY,
            applied_at timestamptz NOT NULL DEFAULT now()
        )
        """;

    /// <summary>Every embedded migration, in the order it will be applied.</summary>
    public static IReadOnlyList<(string Name, string Sql)> Scripts { get; } = LoadScripts();

    /// <summary>Applies whatever has not been applied yet, and returns the names of what it ran.</summary>
    public async Task<IReadOnlyList<string>> ApplyAsync(CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(LedgerDdl, cancellationToken: cancellationToken));

        var already = (await connection.QueryAsync<string>(
                new CommandDefinition("SELECT name FROM mui_migration", cancellationToken: cancellationToken)))
            .ToHashSet(StringComparer.Ordinal);

        var applied = new List<string>();

        foreach (var (name, sql) in Scripts)
        {
            if (already.Contains(name))
            {
                continue;
            }

            // DDL is transactional in PostgreSQL, so a migration that fails half way leaves nothing
            // behind and the ledger entry is written by the same transaction as the schema change.
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await connection.ExecuteAsync(new CommandDefinition(
                sql, transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO mui_migration (name) VALUES (@name)",
                new { name },
                transaction,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            logger?.LogInformation("Applied migration {Migration}", name);
            applied.Add(name);
        }

        return applied;
    }

    private static IReadOnlyList<(string Name, string Sql)> LoadScripts()
    {
        var assembly = typeof(MigrationRunner).Assembly;

        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => (name[ResourcePrefix.Length..], Read(assembly, name)))
            .ToList();
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded migration '{resource}' is missing.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 6: Add `MigratedAsync` to the fixture**

In `tests/MUI.Storage.Tests/Support/PostgresFixture.cs`, add after `FreshDatabaseAsync`:

```csharp
    /// <summary>A fresh database with the whole schema already applied.</summary>
    public static async Task<TestDatabase> MigratedAsync()
    {
        var database = await FreshDatabaseAsync();
        await new MigrationRunner(database.DataSource).ApplyAsync(CancellationToken.None);

        return database;
    }
```

and add `using MUI.Storage;` to that file's using block.

- [ ] **Step 7: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests </dev/null
```
Expected: PASS — 8 new tests plus the 2 harness tests.

- [ ] **Step 8: Commit**

```bash
git add src/MUI.Storage tests/MUI.Storage.Tests
git commit -m "feat(storage): an idempotent migration runner and the game table"
```

---

### Task 7: `0002_game_field.sql` — fields, changes, and the no-"uptime" rule (spec §5.1, §5.7)

**Files:**
- Create: `src/MUI.Storage/Migrations/0002_game_field.sql`
- Create: `tests/MUI.Storage.Tests/SchemaVocabularyTests.cs`

**Interfaces:**
- Consumes: `MigrationRunner`, `PostgresFixture.MigratedAsync`, `SqlEnums` (Task 6).
- Produces: tables `game_field` and `field_change`. No new C# types.

- [ ] **Step 1: Write the failing test**

`tests/MUI.Storage.Tests/SchemaVocabularyTests.cs`:

```csharp
using Dapper;

using MUI.Storage.Tests.Support;

namespace MUI.Storage.Tests;

/// <summary>
/// Spec §5.7 is a naming rule with teeth, so it is enforced against the migrated database rather
/// than against anybody's intentions.
/// </summary>
public class SchemaVocabularyTests
{
    [Test]
    public async Task NoColumnAnywhereInTheSchemaIsNamedForUptime()
    {
        // "We measure a socket from one vantage point at intervals; we did not measure whether the
        // game was up, and 'uptime' claims we did." The word leaks, so the schema is grepped.
        //
        // Note the scope: this binds schema IDENTIFIERS. MSSP's own UPTIME variable may perfectly
        // well appear as a VALUE in game_field.field — that is the game's vocabulary, not ours.
        await using var db = await PostgresFixture.MigratedAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();

        var offenders = (await connection.QueryAsync<string>(
            """
            SELECT table_name || '.' || column_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name ILIKE '%uptime%'
            ORDER BY 1
            """)).ToList();

        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task NoTableAnywhereInTheSchemaIsNamedForUptime()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();

        var offenders = (await connection.QueryAsync<string>(
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name ILIKE '%uptime%'
            ORDER BY 1
            """)).ToList();

        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task AFieldRowIsUniquePerGameAndFieldSoThereIsNoLedger()
    {
        // Spec §5.1: one row per (game, field). The economy of the whole design rests on this.
        await using var db = await PostgresFixture.MigratedAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();

        var gameId = await InsertGame(connection);

        await connection.ExecuteAsync(
            """
            INSERT INTO game_field (game_id, field, value, source, confidence, first_seen_at, last_confirmed_at)
            VALUES (@gameId, 'GENRE', 'Fantasy', 'mssp', 'reported', now(), now())
            """,
            new { gameId });

        await Assert.That(async () => await connection.ExecuteAsync(
            """
            INSERT INTO game_field (game_id, field, value, source, confidence, first_seen_at, last_confirmed_at)
            VALUES (@gameId, 'GENRE', 'Science Fiction', 'mssp', 'reported', now(), now())
            """,
            new { gameId })).Throws<Npgsql.PostgresException>();
    }

    [Test]
    public async Task AFieldCannotCarryASourceNobodyDeclared()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();

        var gameId = await InsertGame(connection);

        await Assert.That(async () => await connection.ExecuteAsync(
            """
            INSERT INTO game_field (game_id, field, value, source, confidence, first_seen_at, last_confirmed_at)
            VALUES (@gameId, 'GENRE', 'Fantasy', 'a_friend_told_me', 'reported', now(), now())
            """,
            new { gameId })).Throws<Npgsql.PostgresException>();
    }

    [Test]
    public async Task AChangeRowMayHaveNoOldValueBecauseAFirstSightingHasNone()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();

        var gameId = await InsertGame(connection);

        await connection.ExecuteAsync(
            """
            INSERT INTO field_change (game_id, field, old_value, new_value, source, at)
            VALUES (@gameId, 'GENRE', NULL, 'Fantasy', 'mssp', now())
            """,
            new { gameId });

        var count = await connection.QuerySingleAsync<int>(
            "SELECT count(*) FROM field_change WHERE game_id = @gameId", new { gameId });

        await Assert.That(count).IsEqualTo(1);
    }

    private static async Task<Guid> InsertGame(Npgsql.NpgsqlConnection connection)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            """
            INSERT INTO game (id, slug, name, state, is_claimed, first_seen_at)
            VALUES (@id, @slug, 'Corvid', 'active', false, now())
            """,
            new { id, slug = id.ToString("N") });

        return id;
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests </dev/null
```
Expected: FAIL — `42P01: relation "game_field" does not exist`.

- [ ] **Step 3: Write the migration**

`src/MUI.Storage/Migrations/0002_game_field.sql`:

```sql
-- spec §5.1 — one row per (game, field), and no append-only ledger. Every probe does exactly one of
-- two things to each field: confirm (bump last_confirmed_at, write nothing else) or change (rewrite
-- this row AND append one row to field_change). A game whose GENRE never moves therefore costs one
-- row for ever, not one per hour.
CREATE TABLE game_field (
    game_id           uuid NOT NULL REFERENCES game (id),
    field             text NOT NULL,
    value             text NOT NULL,
    source            text NOT NULL,
    confidence        text NOT NULL,
    first_seen_at     timestamptz NOT NULL,
    last_confirmed_at timestamptz NOT NULL,

    PRIMARY KEY (game_id, field),

    -- The §5.1 precedence ladder's vocabulary. Declared order in MUI.Catalog.FieldSource is the
    -- ladder itself; this list is only the spelling.
    CONSTRAINT game_field_source_vocabulary CHECK (source IN (
        'staff', 'handshake', 'owner', 'who', 'mssp', 'banner', 'imported_measured', 'imported_asserted')),

    CONSTRAINT game_field_confidence_vocabulary CHECK (confidence IN ('observed', 'reported', 'inferred')),

    -- A value cannot have been confirmed before it was first seen.
    CONSTRAINT game_field_confirmed_after_first_seen CHECK (last_confirmed_at >= first_seen_at)
);

-- The game page renders every field of one game at once (§9), which the primary key's leading column
-- already serves. This index serves the other direction: §9's faceted search asks which games have
-- CODEBASE = PennMUSH, or capability.gmcp.measured = true.
CREATE INDEX game_field_field_value_idx ON game_field (field, value);

-- spec §5.1 — the per-game change feed, which is a table of events that actually happened, and which
-- is also what one wants to render. A first sighting is deliberately not one of them: that is the
-- "newly discovered" feed's business (§9), and old_value is NULL only where an importer or a staff
-- correction genuinely had nothing to replace.
CREATE TABLE field_change (
    id        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    game_id   uuid NOT NULL REFERENCES game (id),
    field     text NOT NULL,
    old_value text,
    new_value text NOT NULL,
    source    text NOT NULL,
    at        timestamptz NOT NULL,

    CONSTRAINT field_change_source_vocabulary CHECK (source IN (
        'staff', 'handshake', 'owner', 'who', 'mssp', 'banner', 'imported_measured', 'imported_asserted'))
);

-- §9's change feed is "the most recent N changes for this game", newest first, which is exactly this.
CREATE INDEX field_change_game_at_idx ON field_change (game_id, at DESC);
```

- [ ] **Step 4: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests </dev/null
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Storage/Migrations/0002_game_field.sql tests/MUI.Storage.Tests/SchemaVocabularyTests.cs
git commit -m "feat(storage): game_field and field_change, with the no-uptime rule tested against the schema"
```

---

### Task 8: `0003_presence_sample.sql` and `NpgsqlPresenceRepository` (spec §5.2, §5.4)

The only table growing linearly with games × time, hence RANGE-partitioned monthly.

**Files:**
- Create: `src/MUI.Storage/Migrations/0003_presence_sample.sql`
- Create: `src/MUI.Storage/Repositories.cs`
- Create: `src/MUI.Storage/NpgsqlPresenceRepository.cs`
- Create: `tests/MUI.Storage.Tests/PresenceRepositoryTests.cs`
- Create: `tests/MUI.Storage.Tests/Support/GameSeed.cs`

**Interfaces:**
- Consumes: `PresenceSample`, `PresenceSource`, `UnmeasurableReasons` (Task 3); `SqlEnums`, `MigrationRunner` (Task 6).
- Produces:
  - `interface MUI.Storage.IPresenceRepository` with
    `Task AppendAsync(PresenceSample sample, CancellationToken ct)`,
    `Task<IReadOnlyList<PresenceSample>> RangeAsync(Guid gameId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)`,
    `Task EnsurePartitionAsync(DateTimeOffset month, CancellationToken ct)`
  - `sealed class MUI.Storage.NpgsqlPresenceRepository(NpgsqlDataSource source) : IPresenceRepository`
  - `MUI.Storage.Tests.Support.GameSeed.InsertAsync(NpgsqlDataSource source)` returning `Task<Guid>`

- [ ] **Step 1: Write the failing test**

`tests/MUI.Storage.Tests/Support/GameSeed.cs`:

```csharp
using Dapper;

using Npgsql;

namespace MUI.Storage.Tests.Support;

/// <summary>Every other table references <c>game</c>, so every other test needs one.</summary>
public static class GameSeed
{
    public static async Task<Guid> InsertAsync(NpgsqlDataSource source)
    {
        var id = Guid.NewGuid();

        await using var connection = await source.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO game (id, slug, name, state, is_claimed, first_seen_at)
            VALUES (@id, @slug, 'Corvid', 'active', false, now())
            """,
            new { id, slug = id.ToString("N") });

        return id;
    }
}
```

`tests/MUI.Storage.Tests/PresenceRepositoryTests.cs`:

```csharp
using Dapper;

using MUI.Catalog;
using MUI.Storage.Tests.Support;

namespace MUI.Storage.Tests;

/// <summary>
/// Spec §5.2 and §5.4. The three renderings an hour can have must survive a round trip, and the
/// partitioning must actually be partitioning.
/// </summary>
public class PresenceRepositoryTests
{
    private static readonly DateTimeOffset March = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ACountedSampleRoundTrips()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlPresenceRepository(db.DataSource);

        await repository.EnsurePartitionAsync(March, CancellationToken.None);
        await repository.AppendAsync(
            new PresenceSample(gameId, March, 42, PresenceSource.Who, null, """{"saltEpoch":"e1"}"""),
            CancellationToken.None);

        var samples = await repository.RangeAsync(
            gameId, March.AddDays(-1), March.AddDays(1), CancellationToken.None);

        await Assert.That(samples).HasCount(1);
        await Assert.That(samples[0].Count).IsEqualTo(42);
        await Assert.That(samples[0].Source).IsEqualTo(PresenceSource.Who);
        await Assert.That(samples[0].UnmeasurableReason).IsNull();
        await Assert.That(samples[0].AggregatesJson).IsNotNull();
    }

    [Test]
    public async Task AMeasuredZeroIsStoredAsAZeroAndNotAsAnAbsence()
    {
        // Spec §5.4: a measured zero is a filled cell. We got in and nobody was there.
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlPresenceRepository(db.DataSource);

        await repository.EnsurePartitionAsync(March, CancellationToken.None);
        await repository.AppendAsync(
            new PresenceSample(gameId, March, 0, PresenceSource.Who, null, null), CancellationToken.None);

        var samples = await repository.RangeAsync(
            gameId, March.AddDays(-1), March.AddDays(1), CancellationToken.None);

        await Assert.That(samples[0].Count).IsEqualTo(0);
    }

    [Test]
    public async Task AnUnmeasurableSampleRoundTripsWithItsReason()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlPresenceRepository(db.DataSource);

        await repository.EnsurePartitionAsync(March, CancellationToken.None);
        await repository.AppendAsync(
            new PresenceSample(gameId, March, null, PresenceSource.Who, UnmeasurableReasons.WhoUnparseable, null),
            CancellationToken.None);

        var samples = await repository.RangeAsync(
            gameId, March.AddDays(-1), March.AddDays(1), CancellationToken.None);

        await Assert.That(samples[0].Count).IsNull();
        await Assert.That(samples[0].UnmeasurableReason).IsEqualTo(UnmeasurableReasons.WhoUnparseable);
    }

    [Test]
    public async Task ANullCountWithoutAReasonIsRefusedBySchema()
    {
        // The two renderings §5.4 distinguishes are different facts, and a null count with nothing
        // to say would be neither of them.
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);

        await using var connection = await db.DataSource.OpenConnectionAsync();
        await new NpgsqlPresenceRepository(db.DataSource).EnsurePartitionAsync(March, CancellationToken.None);

        await Assert.That(async () => await connection.ExecuteAsync(
            """
            INSERT INTO presence_sample (game_id, at, count, source, unmeasurable_reason)
            VALUES (@gameId, @at, NULL, 'who', NULL)
            """,
            new { gameId, at = March })).Throws<Npgsql.PostgresException>();
    }

    [Test]
    public async Task ACountedSampleCarryingAnUnmeasurableReasonIsRefusedBySchema()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);

        await using var connection = await db.DataSource.OpenConnectionAsync();
        await new NpgsqlPresenceRepository(db.DataSource).EnsurePartitionAsync(March, CancellationToken.None);

        await Assert.That(async () => await connection.ExecuteAsync(
            """
            INSERT INTO presence_sample (game_id, at, count, source, unmeasurable_reason)
            VALUES (@gameId, @at, 7, 'who', 'who_unparseable')
            """,
            new { gameId, at = March })).Throws<Npgsql.PostgresException>();
    }

    [Test]
    public async Task EnsuringAPartitionTwiceIsHarmless()
    {
        // It runs on every probe. It cannot be a once-only ceremony.
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlPresenceRepository(db.DataSource);

        await repository.EnsurePartitionAsync(March, CancellationToken.None);
        await repository.EnsurePartitionAsync(March.AddDays(5), CancellationToken.None);

        await using var connection = await db.DataSource.OpenConnectionAsync();
        var partitions = await connection.QuerySingleAsync<int>(
            """
            SELECT count(*) FROM pg_class c
            JOIN pg_inherits i ON i.inhrelid = c.oid
            JOIN pg_class p ON p.oid = i.inhparent
            WHERE p.relname = 'presence_sample'
            """);

        await Assert.That(partitions).IsEqualTo(1);
    }

    [Test]
    public async Task SamplesInDifferentMonthsLandInDifferentPartitions()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlPresenceRepository(db.DataSource);
        var april = new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero);

        await repository.EnsurePartitionAsync(March, CancellationToken.None);
        await repository.EnsurePartitionAsync(april, CancellationToken.None);
        await repository.AppendAsync(new PresenceSample(gameId, March, 3, PresenceSource.Who, null, null), CancellationToken.None);
        await repository.AppendAsync(new PresenceSample(gameId, april, 5, PresenceSource.Mssp, null, null), CancellationToken.None);

        await using var connection = await db.DataSource.OpenConnectionAsync();
        var inMarch = await connection.QuerySingleAsync<int>("SELECT count(*) FROM presence_sample_202603");
        var inApril = await connection.QuerySingleAsync<int>("SELECT count(*) FROM presence_sample_202604");

        await Assert.That(inMarch).IsEqualTo(1);
        await Assert.That(inApril).IsEqualTo(1);
    }

    [Test]
    public async Task ARangeReturnsOneGamesSamplesInTimeOrder()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var mine = await GameSeed.InsertAsync(db.DataSource);
        var theirs = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlPresenceRepository(db.DataSource);

        await repository.EnsurePartitionAsync(March, CancellationToken.None);
        await repository.AppendAsync(new PresenceSample(mine, March.AddHours(2), 2, PresenceSource.Who, null, null), CancellationToken.None);
        await repository.AppendAsync(new PresenceSample(mine, March, 1, PresenceSource.Who, null, null), CancellationToken.None);
        await repository.AppendAsync(new PresenceSample(theirs, March.AddHours(1), 9, PresenceSource.Who, null, null), CancellationToken.None);

        var samples = await repository.RangeAsync(mine, March.AddDays(-1), March.AddDays(1), CancellationToken.None);

        await Assert.That(samples.Select(s => s.Count).ToList()).IsEquivalentTo(new int?[] { 1, 2 });
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0246: The type or namespace name 'NpgsqlPresenceRepository' could not be found`.

- [ ] **Step 3: Write the migration**

`src/MUI.Storage/Migrations/0003_presence_sample.sql`:

```sql
-- spec §5.2 — the only table in this schema growing linearly with games × time, which is why it is
-- the only partitioned one. RANGE on `at`, monthly, so retention and the §5.2 rollups can later work
-- on whole partitions rather than row-by-row deletes over hundreds of millions of rows.
CREATE TABLE presence_sample (
    game_id             uuid NOT NULL REFERENCES game (id),
    at                  timestamptz NOT NULL,
    count               integer,
    source              text NOT NULL,
    unmeasurable_reason text,

    -- §5.2/§11: idle-time histogram buckets, session-length estimates and a unique-player estimate
    -- derived from salted rotating hashes. Populated only when the WHO parser reached per-player
    -- confidence (§6.3). Never player names.
    aggregates          jsonb,

    -- A partitioned table's primary key must contain the partition key.
    PRIMARY KEY (game_id, at),

    CONSTRAINT presence_sample_source_vocabulary CHECK (source IN ('who', 'mssp', 'imported_measured')),

    -- §5.4, and the most important constraint in this schema. A NULL count is a probed-but-
    -- unmeasurable cell and must say why; a counted cell must not carry a reason. Those are two
    -- different facts with two different renderings, and the schema keeps them apart so no writer
    -- can quietly produce a third thing.
    CONSTRAINT presence_sample_null_count_has_a_reason CHECK (
        (count IS NULL) = (unmeasurable_reason IS NOT NULL)),

    -- Parsers never fabricate, and they never go negative either.
    CONSTRAINT presence_sample_count_is_not_negative CHECK (count IS NULL OR count >= 0)
) PARTITION BY RANGE (at);

-- §9's heatmap and trend lines read one game over a date window, which the primary key plus
-- partition pruning already serve. This one is for the ecosystem dashboard, which reads every game
-- over a window and would otherwise scan each partition whole.
CREATE INDEX presence_sample_at_idx ON presence_sample (at);
```

- [ ] **Step 4: Write the repository interfaces file**

`src/MUI.Storage/Repositories.cs`:

```csharp
namespace MUI.Storage;

using MUI.Catalog;

/// <summary>
/// One game's presence series (spec §5.2). Append-only; nothing here ever updates a sample.
/// </summary>
public interface IPresenceRepository
{
    Task AppendAsync(PresenceSample sample, CancellationToken ct);

    Task<IReadOnlyList<PresenceSample>> RangeAsync(
        Guid gameId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>
    /// Makes sure the monthly partition covering <paramref name="month"/> exists. Called on the hot
    /// path before every append, because a missing partition is an insert error and a crawler is not
    /// entitled to lose a measurement to a calendar rollover.
    /// </summary>
    Task EnsurePartitionAsync(DateTimeOffset month, CancellationToken ct);
}
```

- [ ] **Step 5: Write the repository**

`src/MUI.Storage/NpgsqlPresenceRepository.cs`:

```csharp
namespace MUI.Storage;

using Dapper;

using MUI.Catalog;

using Npgsql;

public sealed class NpgsqlPresenceRepository(NpgsqlDataSource source) : IPresenceRepository
{
    /// <summary>PostgreSQL's <c>duplicate_table</c>, which two workers racing a partition will see.</summary>
    private const string DuplicateTable = "42P07";

    public async Task AppendAsync(PresenceSample sample, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // Nullable parameters carry an explicit cast because Npgsql cannot infer a type from a null,
        // and `aggregates` needs one regardless: Dapper sends a string as text, and text is not jsonb.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO presence_sample (game_id, at, count, source, unmeasurable_reason, aggregates)
            VALUES (@gameId, @at, @count::integer, @source, @reason::text, @aggregates::jsonb)
            ON CONFLICT (game_id, at) DO NOTHING
            """,
            new
            {
                gameId = sample.GameId,
                at = sample.At,
                count = sample.Count,
                source = SqlEnums.ToDb(sample.Source),
                reason = sample.UnmeasurableReason,
                aggregates = sample.AggregatesJson,
            },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PresenceSample>> RangeAsync(
        Guid gameId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<PresenceRow>(new CommandDefinition(
            """
            SELECT game_id AS GameId, at AS At, count AS Count, source AS Source,
                   unmeasurable_reason AS UnmeasurableReason, aggregates::text AS AggregatesJson
            FROM presence_sample
            WHERE game_id = @gameId AND at >= @from AND at < @to
            ORDER BY at
            """,
            new { gameId, from, to },
            cancellationToken: ct));

        return rows.Select(row => new PresenceSample(
            row.GameId, row.At, row.Count, SqlEnums.Parse<PresenceSource>(row.Source),
            row.UnmeasurableReason, row.AggregatesJson)).ToList();
    }

    public async Task EnsurePartitionAsync(DateTimeOffset month, CancellationToken ct)
    {
        var utc = month.UtcDateTime;
        var start = new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddMonths(1);
        var name = $"presence_sample_{start:yyyyMM}";

        // Interpolated rather than parameterised because a partition bound and a table name cannot be
        // parameters in PostgreSQL. Both values are derived from a DateTimeOffset, so there is no
        // caller-controlled text anywhere in this statement.
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {name}
            PARTITION OF presence_sample
            FOR VALUES FROM ('{start:yyyy-MM-dd HH:mm:sszzz}') TO ('{end:yyyy-MM-dd HH:mm:sszzz}')
            """;

        await using var connection = await source.OpenConnectionAsync(ct);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
        }
        catch (PostgresException error) when (error.SqlState == DuplicateTable)
        {
            // IF NOT EXISTS is checked, not locked, so two workers crossing a month boundary at the
            // same moment can both decide to create it. The loser's job is already done.
        }
    }

    private sealed record PresenceRow(
        Guid GameId,
        DateTimeOffset At,
        int? Count,
        string Source,
        string? UnmeasurableReason,
        string? AggregatesJson);
}
```

- [ ] **Step 6: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests </dev/null
```
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Storage tests/MUI.Storage.Tests
git commit -m "feat(storage): partitioned presence_sample, with the three-state contract in CHECK constraints"
```

---

### Task 9: `0004_availability_interval.sql` and `NpgsqlAvailabilityRepository` (spec §5.3, §5.7, §7.6)

**Files:**
- Create: `src/MUI.Storage/Migrations/0004_availability_interval.sql`
- Modify: `src/MUI.Storage/Repositories.cs`
- Create: `src/MUI.Storage/NpgsqlAvailabilityRepository.cs`
- Create: `tests/MUI.Storage.Tests/AvailabilityRepositoryTests.cs`

**Interfaces:**
- Consumes: `AvailabilityInterval`, `AvailabilityState`, `FailureCause` (Task 3); `SqlEnums` (Task 6); `GameSeed` (Task 8).
- Produces:
  - `interface MUI.Storage.IAvailabilityRepository` with
    `Task<AvailabilityInterval?> OpenIntervalAsync(Guid gameId, CancellationToken ct)`,
    `Task<long> OpenAsync(Guid gameId, AvailabilityState state, FailureCause cause, DateTimeOffset from, CancellationToken ct)`,
    `Task CloseAsync(long intervalId, DateTimeOffset at, CancellationToken ct)`,
    `Task<IReadOnlyList<AvailabilityInterval>> RangeAsync(Guid gameId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)`,
    `Task<TimeSpan> CumulativeReachableAsync(Guid gameId, DateTimeOffset now, CancellationToken ct)`,
    `Task<TimeSpan> CumulativeImportedMeasuredReachableAsync(Guid gameId, DateTimeOffset now, CancellationToken ct)`
  - `sealed class MUI.Storage.NpgsqlAvailabilityRepository(NpgsqlDataSource source) : IAvailabilityRepository`

**Declared deviation:** `CumulativeImportedMeasuredReachableAsync` is not in CONTRACT.md. `ArchivePolicy.GraceFor`
takes first-party and imported-measured reachable time as separate arguments and weights the second
at half (§7.6); one method returning one total cannot feed both. The `origin` column exists for the
same reason.

- [ ] **Step 1: Write the failing test**

`tests/MUI.Storage.Tests/AvailabilityRepositoryTests.cs`:

```csharp
using Dapper;

using MUI.Catalog;
using MUI.Storage.Tests.Support;

namespace MUI.Storage.Tests;

/// <summary>
/// Spec §5.3: intervals, not samples. A game reachable for three years is one open row, and the
/// arithmetic §7.5 needs is a sum over a handful of them.
/// </summary>
public class AvailabilityRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AGameWithNoHistoryHasNoOpenInterval()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);

        var open = await new NpgsqlAvailabilityRepository(db.DataSource).OpenIntervalAsync(gameId, CancellationToken.None);

        await Assert.That(open).IsNull();
    }

    [Test]
    public async Task AnOpenedIntervalIsTheOpenOne()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlAvailabilityRepository(db.DataSource);

        var id = await repository.OpenAsync(
            gameId, AvailabilityState.Reachable, FailureCause.None, Now.AddDays(-30), CancellationToken.None);
        var open = await repository.OpenIntervalAsync(gameId, CancellationToken.None);

        await Assert.That(open).IsNotNull();
        await Assert.That(open!.Id).IsEqualTo(id);
        await Assert.That(open.State).IsEqualTo(AvailabilityState.Reachable);
        await Assert.That(open.Cause).IsEqualTo(FailureCause.None);
        await Assert.That(open.ToAt).IsNull();
    }

    [Test]
    public async Task AGameCannotHaveTwoOpenIntervalsAtOnce()
    {
        // Enforced by a partial unique index rather than by every caller remembering to close first.
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlAvailabilityRepository(db.DataSource);

        await repository.OpenAsync(gameId, AvailabilityState.Reachable, FailureCause.None, Now.AddDays(-30), CancellationToken.None);

        await Assert.That(async () => await repository.OpenAsync(
            gameId, AvailabilityState.Unreachable, FailureCause.Timeout, Now, CancellationToken.None))
            .Throws<Npgsql.PostgresException>();
    }

    [Test]
    public async Task ClosingAnIntervalFreesTheGameForANewOne()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlAvailabilityRepository(db.DataSource);

        var first = await repository.OpenAsync(
            gameId, AvailabilityState.Reachable, FailureCause.None, Now.AddDays(-30), CancellationToken.None);
        await repository.CloseAsync(first, Now.AddDays(-2), CancellationToken.None);
        await repository.OpenAsync(
            gameId, AvailabilityState.Unreachable, FailureCause.Timeout, Now.AddDays(-2), CancellationToken.None);

        var open = await repository.OpenIntervalAsync(gameId, CancellationToken.None);

        await Assert.That(open!.State).IsEqualTo(AvailabilityState.Unreachable);
        await Assert.That(open.Cause).IsEqualTo(FailureCause.Timeout);
    }

    [Test]
    public async Task AnIntervalCannotEndBeforeItStarts()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlAvailabilityRepository(db.DataSource);

        var id = await repository.OpenAsync(
            gameId, AvailabilityState.Reachable, FailureCause.None, Now, CancellationToken.None);

        await Assert.That(async () => await repository.CloseAsync(id, Now.AddDays(-1), CancellationToken.None))
            .Throws<Npgsql.PostgresException>();
    }

    [Test]
    public async Task CumulativeReachableSumsOurOwnReachableIntervalsIncludingTheOpenOne()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlAvailabilityRepository(db.DataSource);

        var first = await repository.OpenAsync(
            gameId, AvailabilityState.Reachable, FailureCause.None, Now.AddDays(-100), CancellationToken.None);
        await repository.CloseAsync(first, Now.AddDays(-60), CancellationToken.None);

        var down = await repository.OpenAsync(
            gameId, AvailabilityState.Unreachable, FailureCause.Dns, Now.AddDays(-60), CancellationToken.None);
        await repository.CloseAsync(down, Now.AddDays(-50), CancellationToken.None);

        await repository.OpenAsync(
            gameId, AvailabilityState.Reachable, FailureCause.None, Now.AddDays(-50), CancellationToken.None);

        var cumulative = await repository.CumulativeReachableAsync(gameId, Now, CancellationToken.None);

        // 40 days closed plus 50 days still open. The unreachable ten earn nothing.
        await Assert.That(cumulative.TotalDays).IsEqualTo(90).Within(0.01);
    }

    [Test]
    public async Task ImportedReachableTimeIsSummedSeparatelyFromOurOwn()
    {
        // §7.6 weights it at half, which ArchivePolicy does — so the two totals must arrive apart.
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlAvailabilityRepository(db.DataSource);

        await repository.OpenAsync(
            gameId, AvailabilityState.Reachable, FailureCause.None, Now.AddDays(-20), CancellationToken.None);

        await using var connection = await db.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO availability_interval (game_id, state, from_at, to_at, cause, origin)
            VALUES (@gameId, 'reachable', @from, @to, 'none', 'imported_measured')
            """,
            new { gameId, from = Now.AddDays(-1000), to = Now.AddDays(-600) });

        var ours = await repository.CumulativeReachableAsync(gameId, Now, CancellationToken.None);
        var theirs = await repository.CumulativeImportedMeasuredReachableAsync(gameId, Now, CancellationToken.None);

        await Assert.That(ours.TotalDays).IsEqualTo(20).Within(0.01);
        await Assert.That(theirs.TotalDays).IsEqualTo(400).Within(0.01);
    }

    [Test]
    public async Task ARangeReturnsOneGamesIntervalsInTimeOrder()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlAvailabilityRepository(db.DataSource);

        var first = await repository.OpenAsync(
            gameId, AvailabilityState.Reachable, FailureCause.None, Now.AddDays(-100), CancellationToken.None);
        await repository.CloseAsync(first, Now.AddDays(-60), CancellationToken.None);
        await repository.OpenAsync(
            gameId, AvailabilityState.Degraded, FailureCause.HandshakeStalled, Now.AddDays(-60), CancellationToken.None);

        var intervals = await repository.RangeAsync(gameId, Now.AddDays(-365), Now, CancellationToken.None);

        await Assert.That(intervals).HasCount(2);
        await Assert.That(intervals[0].State).IsEqualTo(AvailabilityState.Reachable);
        await Assert.That(intervals[1].State).IsEqualTo(AvailabilityState.Degraded);
        await Assert.That(intervals[1].Cause).IsEqualTo(FailureCause.HandshakeStalled);
    }

    [Test]
    public async Task AnIntervalCannotBeGivenAStateNobodyDeclared()
    {
        // §5.7 has teeth in the schema: 'up' is not one of the three words.
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
            """
            INSERT INTO availability_interval (game_id, state, from_at, cause)
            VALUES (@gameId, 'up', now(), 'none')
            """,
            new { gameId })).Throws<Npgsql.PostgresException>();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0246: The type or namespace name 'NpgsqlAvailabilityRepository' could not be found`.

- [ ] **Step 3: Write the migration**

`src/MUI.Storage/Migrations/0004_availability_interval.sql`:

```sql
-- spec §5.3 — intervals, not samples. A game reachable for three years is one open row, not
-- twenty-six thousand samples, and "reachable over 90 days" and "longest outage" become arithmetic
-- over a handful of rows. Each probe either extends the open interval or closes it and opens a new
-- one, and ONLY A CAUSE CHANGE WRITES A TRANSITION: a hundred consecutive timeouts are one interval.
CREATE TABLE availability_interval (
    id      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    game_id uuid NOT NULL REFERENCES game (id),
    state   text NOT NULL,
    from_at timestamptz NOT NULL,
    to_at   timestamptz,
    cause   text NOT NULL,

    -- §7.6 — imported history counts toward archive grace at HALF weight, so it has to be summable
    -- apart from our own. Not a provenance nicety: ArchivePolicy.GraceFor takes the two as separate
    -- arguments and weights them differently, so one undifferentiated total cannot feed it.
    origin  text NOT NULL DEFAULT 'first_party',

    -- §5.7's vocabulary, in the schema so the word cannot leak. Reachable, never up.
    CONSTRAINT availability_interval_state_vocabulary CHECK (state IN ('reachable', 'degraded', 'unreachable')),

    -- 'none' is the cause a reachable interval carries; it is never a probe's answer.
    CONSTRAINT availability_interval_cause_vocabulary CHECK (cause IN (
        'none', 'dns', 'refused', 'tls', 'timeout', 'handshake_stalled', 'unknown')),

    CONSTRAINT availability_interval_origin_vocabulary CHECK (origin IN ('first_party', 'imported_measured')),

    CONSTRAINT availability_interval_does_not_end_before_it_starts CHECK (to_at IS NULL OR to_at >= from_at)
);

-- Every probe asks "what is this game's open interval", which is the one query on the hot path. As a
-- partial UNIQUE index it also enforces the invariant the whole design rests on: at most one interval
-- per game is open, so no caller can leave two running by forgetting to close the first.
CREATE UNIQUE INDEX availability_interval_open_idx ON availability_interval (game_id) WHERE to_at IS NULL;

-- §13's availability arithmetic — cumulative reachable time, reachable percent over a window, longest
-- outage — all read one game's intervals in time order.
CREATE INDEX availability_interval_game_from_idx ON availability_interval (game_id, from_at);
```

- [ ] **Step 4: Add the interface**

Append to `src/MUI.Storage/Repositories.cs`:

```csharp
/// <summary>
/// One game's availability history as intervals (spec §5.3).
/// </summary>
public interface IAvailabilityRepository
{
    /// <summary>The interval that has not been closed, if there is one. At most one ever is.</summary>
    Task<AvailabilityInterval?> OpenIntervalAsync(Guid gameId, CancellationToken ct);

    Task<long> OpenAsync(
        Guid gameId, AvailabilityState state, FailureCause cause, DateTimeOffset from, CancellationToken ct);

    Task CloseAsync(long intervalId, DateTimeOffset at, CancellationToken ct);

    Task<IReadOnlyList<AvailabilityInterval>> RangeAsync(
        Guid gameId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>
    /// Time this site measured the game as reachable, summed over intervals, with the open one
    /// counted to <paramref name="now"/>. Cumulative, not span (spec §7.5).
    /// </summary>
    Task<TimeSpan> CumulativeReachableAsync(Guid gameId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// The same sum over intervals imported from a directory that ran its own probe. Separate from
    /// <see cref="CumulativeReachableAsync"/> because §7.6 credits it at half weight and
    /// <c>ArchivePolicy.GraceFor</c> takes the two apart.
    /// </summary>
    Task<TimeSpan> CumulativeImportedMeasuredReachableAsync(Guid gameId, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Writes one already-closed interval imported from a third party, stamped
    /// <c>origin = 'imported_measured'</c> (spec §7.6).
    /// <para>
    /// It exists so Plan 04's backfill cannot reach <see cref="OpenAsync"/>, which defaults the origin
    /// to <c>first_party</c> and would credit somebody else's history at full weight — the exact
    /// inversion of §7.5's half-weight rule, and a silent one, because the resulting sum still looks
    /// plausible. An imported span is always closed: a third party's export cannot tell us a game is
    /// reachable *now*, and an open imported interval would collide with our own crawler's.
    /// </para>
    /// </summary>
    Task<long> InsertImportedAsync(
        Guid gameId,
        AvailabilityState state,
        FailureCause cause,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}
```

**Cross-plan note.** The integration test above seeds its imported row with raw SQL, because at that
point in this plan no write path for one exists. `InsertImportedAsync` is the public form of exactly
that statement and Plan 04's `MeasuredHistorySink` is its only caller. Add one more test in this task
asserting that `InsertImportedAsync` and the raw insert produce identical rows, so the two spellings
of the same write cannot drift apart.

- [ ] **Step 5: Write the repository**

`src/MUI.Storage/NpgsqlAvailabilityRepository.cs`:

```csharp
namespace MUI.Storage;

using Dapper;

using MUI.Catalog;

using Npgsql;

public sealed class NpgsqlAvailabilityRepository(NpgsqlDataSource source) : IAvailabilityRepository
{
    private const string Columns =
        "id AS Id, game_id AS GameId, state AS State, from_at AS FromAt, to_at AS ToAt, cause AS Cause";

    private const string FirstParty = "first_party";
    private const string ImportedMeasured = "imported_measured";

    public async Task<AvailabilityInterval?> OpenIntervalAsync(Guid gameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var row = await connection.QuerySingleOrDefaultAsync<IntervalRow>(new CommandDefinition(
            $"SELECT {Columns} FROM availability_interval WHERE game_id = @gameId AND to_at IS NULL",
            new { gameId },
            cancellationToken: ct));

        return row is null ? null : Map(row);
    }

    public async Task<long> OpenAsync(
        Guid gameId, AvailabilityState state, FailureCause cause, DateTimeOffset from, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        return await connection.QuerySingleAsync<long>(new CommandDefinition(
            """
            INSERT INTO availability_interval (game_id, state, from_at, cause, origin)
            VALUES (@gameId, @state, @from, @cause, @origin)
            RETURNING id
            """,
            new
            {
                gameId,
                state = SqlEnums.ToDb(state),
                from,
                cause = SqlEnums.ToDb(cause),
                origin = FirstParty,
            },
            cancellationToken: ct));
    }

    public async Task CloseAsync(long intervalId, DateTimeOffset at, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE availability_interval SET to_at = @at WHERE id = @intervalId AND to_at IS NULL",
            new { intervalId, at },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AvailabilityInterval>> RangeAsync(
        Guid gameId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // An interval overlapping the window is wanted whole; the arithmetic clips it.
        var rows = await connection.QueryAsync<IntervalRow>(new CommandDefinition(
            $"""
            SELECT {Columns}
            FROM availability_interval
            WHERE game_id = @gameId AND from_at < @to AND (to_at IS NULL OR to_at > @from)
            ORDER BY from_at
            """,
            new { gameId, from, to },
            cancellationToken: ct));

        return rows.Select(Map).ToList();
    }

    public Task<TimeSpan> CumulativeReachableAsync(Guid gameId, DateTimeOffset now, CancellationToken ct) =>
        SumReachableAsync(gameId, now, FirstParty, ct);

    public Task<TimeSpan> CumulativeImportedMeasuredReachableAsync(
        Guid gameId, DateTimeOffset now, CancellationToken ct) =>
        SumReachableAsync(gameId, now, ImportedMeasured, ct);

    private async Task<TimeSpan> SumReachableAsync(Guid gameId, DateTimeOffset now, string origin, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // Summed in SQL rather than read into memory: a decade-old game has few intervals, but the
        // archive sweep asks this of every game in the catalogue on every pass.
        var seconds = await connection.QuerySingleAsync<double>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(EXTRACT(EPOCH FROM (COALESCE(to_at, @now) - from_at))), 0)
            FROM availability_interval
            WHERE game_id = @gameId AND state = 'reachable' AND origin = @origin
            """,
            new { gameId, now, origin },
            cancellationToken: ct));

        return TimeSpan.FromSeconds(seconds);
    }

    private static AvailabilityInterval Map(IntervalRow row) => new(
        row.Id,
        row.GameId,
        SqlEnums.Parse<AvailabilityState>(row.State),
        row.FromAt,
        row.ToAt,
        SqlEnums.Parse<FailureCause>(row.Cause));

    private sealed record IntervalRow(
        long Id, Guid GameId, string State, DateTimeOffset FromAt, DateTimeOffset? ToAt, string Cause);
}
```

- [ ] **Step 6: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests </dev/null
```
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Storage tests/MUI.Storage.Tests
git commit -m "feat(storage): availability as intervals, with at most one open per game enforced by index"
```

---

### Task 10: `0005_game_endpoint.sql` and `NpgsqlEndpointRepository` (spec §5.5)

**Files:**
- Create: `src/MUI.Catalog/HostName.cs`
- Create: `tests/MUI.Catalog.Tests/HostNameTests.cs`
- Create: `src/MUI.Storage/Migrations/0005_game_endpoint.sql`
- Modify: `src/MUI.Storage/Repositories.cs`
- Create: `src/MUI.Storage/NpgsqlEndpointRepository.cs`
- Create: `tests/MUI.Storage.Tests/EndpointRepositoryTests.cs`

**Interfaces:**
- Consumes: `GameEndpoint`, `EndpointKind`, `EndpointState` (Task 3); `SqlEnums` (Task 6); `GameSeed` (Task 8).
- Produces:
  - `static class MUI.Catalog.HostName` with `string Normalize(string host)`
  - `interface MUI.Storage.IEndpointRepository` with
    `Task<IReadOnlyList<GameEndpoint>> ForGameAsync(Guid gameId, CancellationToken ct)`,
    `Task<GameEndpoint?> ByAddressAsync(string host, int port, CancellationToken ct)`,
    `Task UpsertAsync(GameEndpoint endpoint, CancellationToken ct)`
  - `sealed class MUI.Storage.NpgsqlEndpointRepository(NpgsqlDataSource source) : IEndpointRepository`

**Why a host has one spelling, decided here.** `ByAddressAsync` is §7.3's strongest identity signal and
the query that decides whether a host re-links to a game we have or becomes a new one. If `host` can be
stored two ways then `MUD.Example.ORG` and `mud.example.org` are two rows, the unique index on
`(host, port)` does not stop it, and the second spelling mints a duplicate endpoint — and through Plan
03's identity matcher, a duplicate *game*. That is precisely the failure §7.3 exists to prevent.

The fix is **one canonical form, not a lenient comparison.** `HostName.Normalize` produces it, both
ends of this repository call it, the schema refuses anything else, and every comparison is then
ordinal — which is also the only kind an index can serve. A case-insensitive comparison would have
papered over the same problem while leaving two rows in the table.

- [ ] **Step 1: Write the failing test**

`tests/MUI.Storage.Tests/EndpointRepositoryTests.cs`:

```csharp
using Dapper;

using MUI.Catalog;
using MUI.Storage.Tests.Support;

namespace MUI.Storage.Tests;

/// <summary>
/// Spec §5.5: endpoints are plural and historical, so a game that moves does not become unfindable
/// and a referral pointing at an old address re-links rather than minting a duplicate — which it can
/// only do if two spellings of one host are one row (spec §7.3).
/// </summary>
public class EndpointRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    private static GameEndpoint Endpoint(Guid gameId, string host, int port, EndpointState state) =>
        new(gameId, host, port, EndpointKind.Telnet, Now.AddYears(-1), Now, state);

    [Test]
    public async Task AnEndpointRoundTrips()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlEndpointRepository(db.DataSource);

        await repository.UpsertAsync(Endpoint(gameId, "corvid.example", 4201, EndpointState.Active), CancellationToken.None);
        var endpoints = await repository.ForGameAsync(gameId, CancellationToken.None);

        await Assert.That(endpoints).HasCount(1);
        await Assert.That(endpoints[0].Host).IsEqualTo("corvid.example");
        await Assert.That(endpoints[0].Port).IsEqualTo(4201);
        await Assert.That(endpoints[0].Kind).IsEqualTo(EndpointKind.Telnet);
        await Assert.That(endpoints[0].State).IsEqualTo(EndpointState.Active);
    }

    [Test]
    public async Task AGameKeepsEveryAddressItHasEverAnsweredOn()
    {
        // A move adds a row; it does not replace one. The old address keeps being probed at the §7.4
        // floor, which is how a returning game re-links to itself.
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlEndpointRepository(db.DataSource);

        await repository.UpsertAsync(Endpoint(gameId, "old.example", 4201, EndpointState.Stale), CancellationToken.None);
        await repository.UpsertAsync(Endpoint(gameId, "new.example", 4201, EndpointState.Active), CancellationToken.None);

        var endpoints = await repository.ForGameAsync(gameId, CancellationToken.None);

        await Assert.That(endpoints).HasCount(2);
    }

    [Test]
    public async Task UpsertingTheSameAddressUpdatesItRatherThanDuplicatingIt()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlEndpointRepository(db.DataSource);

        await repository.UpsertAsync(
            new GameEndpoint(gameId, "corvid.example", 4201, EndpointKind.Telnet, Now.AddYears(-2), Now.AddYears(-2), EndpointState.Active),
            CancellationToken.None);
        await repository.UpsertAsync(
            new GameEndpoint(gameId, "corvid.example", 4201, EndpointKind.Telnet, Now, Now, EndpointState.Active),
            CancellationToken.None);

        var endpoints = await repository.ForGameAsync(gameId, CancellationToken.None);

        await Assert.That(endpoints).HasCount(1);
        await Assert.That(endpoints[0].LastSeenAt).IsEqualTo(Now);

        // first_seen_at is when WE first saw it, so a later sighting must not move it forward.
        await Assert.That(endpoints[0].FirstSeenAt).IsEqualTo(Now.AddYears(-2));
    }

    [Test]
    public async Task AnAddressBelongsToAtMostOneGame()
    {
        // §7.3's strongest identity signal is a previously-seen endpoint, which is only a signal if
        // one address cannot be claimed by two games.
        await using var db = await PostgresFixture.MigratedAsync();
        var mine = await GameSeed.InsertAsync(db.DataSource);
        var theirs = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlEndpointRepository(db.DataSource);

        await repository.UpsertAsync(Endpoint(mine, "corvid.example", 4201, EndpointState.Active), CancellationToken.None);

        await Assert.That(async () => await repository.UpsertAsync(
            Endpoint(theirs, "corvid.example", 4201, EndpointState.Active), CancellationToken.None))
            .Throws<Npgsql.PostgresException>();
    }

    [Test]
    public async Task AnAddressCanBeLookedUpWithNoGameInHand()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlEndpointRepository(db.DataSource);

        await repository.UpsertAsync(Endpoint(gameId, "corvid.example", 4201, EndpointState.Active), CancellationToken.None);

        var found = await repository.ByAddressAsync("corvid.example", 4201, CancellationToken.None);
        var missing = await repository.ByAddressAsync("corvid.example", 4202, CancellationToken.None);

        await Assert.That(found!.GameId).IsEqualTo(gameId);
        await Assert.That(missing).IsNull();
    }

    [Test]
    public async Task APortOutsideTheRangeIsRefused()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlEndpointRepository(db.DataSource);

        await Assert.That(async () => await repository.UpsertAsync(
            Endpoint(gameId, "corvid.example", 0, EndpointState.Active), CancellationToken.None))
            .Throws<Npgsql.PostgresException>();
    }

    /// <summary>
    /// The spellings that must all be one endpoint. Shared, in this order, with
    /// <c>InMemoryEndpointRepositoryTests.TheFakeCanonicalisesAHostExactlyAsTheRealRepositoryDoes</c>
    /// in MUI.Discovery.Tests — the fake and the real thing cannot be driven by one test because one
    /// needs a container and the other must never touch one, so the two tests are named for each other
    /// and assert the same table.
    /// </summary>
    public static readonly string[] OneHostManySpellings =
    [
        "corvid.example",
        "Corvid.Example",
        "CORVID.EXAMPLE",
        "corvid.example.",
        "  corvid.example  ",
    ];

    [Test]
    public async Task EverySpellingOfOneHostIsOneEndpointAndNotSeveral()
    {
        // The bug this prevents is silent and expensive: a second spelling arrives, the unique index
        // on (host, port) sees a different string, a second endpoint row appears, and §7.3's endpoint
        // signal then fails to match a game we already have. A duplicate listing is the outcome.
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlEndpointRepository(db.DataSource);

        foreach (var spelling in OneHostManySpellings)
        {
            await repository.UpsertAsync(Endpoint(gameId, spelling, 4201, EndpointState.Active), CancellationToken.None);
        }

        var endpoints = await repository.ForGameAsync(gameId, CancellationToken.None);

        await Assert.That(endpoints).HasCount(1);
        await Assert.That(endpoints[0].Host).IsEqualTo("corvid.example");

        foreach (var spelling in OneHostManySpellings)
        {
            var found = await repository.ByAddressAsync(spelling, 4201, CancellationToken.None);

            await Assert.That(found).IsNotNull();
            await Assert.That(found!.GameId).IsEqualTo(gameId);
        }
    }

    [Test]
    public async Task WhatIsStoredIsCharacterForCharacterWhatHostNameProduces()
    {
        // The repository and the helper cannot drift: this asserts the stored value against
        // HostName.Normalize itself rather than against a literal somebody typed twice.
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlEndpointRepository(db.DataSource);

        foreach (var (spelling, index) in OneHostManySpellings.Select((s, i) => (s, i)))
        {
            var gameId = await GameSeed.InsertAsync(db.DataSource);
            await repository.UpsertAsync(Endpoint(gameId, spelling, 5000 + index, EndpointState.Active), CancellationToken.None);

            var endpoints = await repository.ForGameAsync(gameId, CancellationToken.None);

            await Assert.That(endpoints[0].Host).IsEqualTo(HostName.Normalize(spelling));
        }
    }

    [Test]
    public async Task TheSchemaItselfRefusesAHostNobodyCanonicalised()
    {
        // Teeth, like §5.7's vocabulary constraints: a write path that forgets to normalise fails at
        // the database instead of quietly adding a second row for a host we already know.
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
            """
            INSERT INTO game_endpoint (game_id, host, port, kind, first_seen_at, last_seen_at, state)
            VALUES (@gameId, 'Corvid.Example', 4201, 'telnet', now(), now(), 'active')
            """,
            new { gameId })).Throws<Npgsql.PostgresException>();
    }
}
```

- [ ] **Step 2: Write the failing `HostName` test**

`tests/MUI.Catalog.Tests/HostNameTests.cs` — pure, no container, and the only place the rules
themselves are asserted:

```csharp
namespace MUI.Catalog.Tests;

/// <summary>
/// One host has one spelling. Every rule here exists because two spellings of one address become two
/// endpoints, and two endpoints become two games (spec §7.3).
/// </summary>
public class HostNameTests
{
    [Test]
    public async Task CaseIsNotPartOfAHostName()
    {
        await Assert.That(HostName.Normalize("MUD.Example.ORG")).IsEqualTo("mud.example.org");
        await Assert.That(HostName.Normalize("MUD.EXAMPLE.ORG")).IsEqualTo("mud.example.org");
    }

    [Test]
    public async Task TheRootLabelsTrailingDotMeansTheSameName()
    {
        await Assert.That(HostName.Normalize("mud.example.org.")).IsEqualTo("mud.example.org");
        await Assert.That(HostName.Normalize("MUD.Example.ORG.")).IsEqualTo("mud.example.org");
    }

    [Test]
    public async Task SurroundingWhitespaceIsNotPartOfAHostEither()
    {
        await Assert.That(HostName.Normalize("  mud.example.org  ")).IsEqualTo("mud.example.org");
    }

    [Test]
    public async Task TwoSpellingsOfOneIpLiteralAreOneAddress()
    {
        // The same reason MsspHost canonicalises: an import writes one form, a referral the other,
        // and an uncanonicalised pair is two endpoints for one machine.
        await Assert.That(HostName.Normalize("2001:0DB8:0000:0000:0000:0000:0000:0001"))
            .IsEqualTo("2001:db8::1");
        await Assert.That(HostName.Normalize("2001:DB8::1")).IsEqualTo("2001:db8::1");
        await Assert.That(HostName.Normalize("[2001:db8::1]")).IsEqualTo("2001:db8::1");
    }

    [Test]
    public async Task AlreadyCanonicalIsLeftExactlyAlone()
    {
        // Idempotence is what lets a CHECK constraint be written as `host = normalised host`.
        foreach (var host in new[] { "mud.example.org", "2001:db8::1", "192.168.0.1" })
        {
            await Assert.That(HostName.Normalize(host)).IsEqualTo(host);
            await Assert.That(HostName.Normalize(HostName.Normalize(host))).IsEqualTo(host);
        }
    }

    [Test]
    public async Task ItNormalisesRatherThanValidates()
    {
        // Whether a host is real, routable or worth crawling is MsspHost.IsCrawlable's question and
        // DNS's. This one only decides how a string is spelled, so nonsense passes through unharmed
        // rather than becoming an exception a repository would have to catch.
        await Assert.That(HostName.Normalize("NOT A HOST")).IsEqualTo("not a host");
        await Assert.That(HostName.Normalize("")).IsEqualTo("");
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0246: The type or namespace name 'HostName' could not be found`, and the same for
`NpgsqlEndpointRepository`.

- [ ] **Step 4: Write `HostName`**

`src/MUI.Catalog/HostName.cs`:

```csharp
using System.Net;

namespace MUI.Catalog;

/// <summary>
/// One canonical spelling of a host, so that two ways of writing one address are one row.
/// </summary>
/// <remarks>
/// <para>
/// This exists for spec §7.3. A previously-seen endpoint is the strongest identity signal there is,
/// and it is asked as <c>ByAddressAsync(host, port)</c> — so if <c>MUD.Example.ORG</c> and
/// <c>mud.example.org</c> are different strings, the second one does not match a game we already
/// have, a second endpoint row appears beside the first, and a duplicate listing is created by
/// nothing worse than a directory that shouts. Normalising on the way in is what makes the unique
/// index on <c>(host, port)</c> mean what it says.
/// </para>
/// <para>
/// <b>It deliberately mirrors <c>MUI.Crawl.Mssp.MsspHost.Create</c>'s normalisation</b> — lower-case,
/// trailing root dot removed, IP literals in <see cref="IPAddress"/>'s canonical form, brackets
/// stripped — so an endpoint and a referral agree about what one host is. It is a second
/// implementation of one rule by necessity: <c>MUI.Catalog</c> may never reference <c>MUI.Crawl</c>,
/// and a referral arrives as an <c>MsspHost</c> while an import arrives as a bare string. The two are
/// held together by <c>HostNormalisationAgreementTests</c> in MUI.Discovery.Tests, which is the one
/// project that sees both — not by anybody remembering this paragraph.
/// </para>
/// <para>
/// It normalises and does not validate. Whether a host is plausible, routable or worth a crawler's
/// time is <c>MsspHost</c>'s question; answering it here would make a repository responsible for
/// rejecting addresses, which is not its job.
/// </para>
/// </remarks>
public static class HostName
{
    /// <summary>The one spelling of <paramref name="host"/> that is stored and compared.</summary>
    public static string Normalize(string host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var trimmed = host.Trim();

        // A bracketed IPv6 literal, [2001:db8::1], as a URL spells it. The brackets are punctuation
        // for a colon problem a host column does not have.
        if (trimmed.Length > 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            trimmed = trimmed[1..^1].Trim();
        }

        // ToString() is the canonical form: it compresses IPv6 zero runs and strips leading zeroes,
        // so two spellings of one address become one key.
        return IPAddress.TryParse(trimmed, out var address)
            ? address.ToString().ToLowerInvariant()
            : trimmed.TrimEnd('.').ToLowerInvariant();
    }
}
```

- [ ] **Step 5: Write the migration**

`src/MUI.Storage/Migrations/0005_game_endpoint.sql`:

```sql
-- spec §5.5 — plural and historical. A game that moves does not become unfindable: old endpoints are
-- still probed at the §7.4 floor, and a referral or DNS record pointing at an old address re-links to
-- the existing game rather than minting a duplicate.
CREATE TABLE game_endpoint (
    game_id       uuid NOT NULL REFERENCES game (id),
    host          text NOT NULL,
    port          integer NOT NULL,
    kind          text NOT NULL,
    first_seen_at timestamptz NOT NULL,
    last_seen_at  timestamptz NOT NULL,
    state         text NOT NULL,

    PRIMARY KEY (game_id, host, port),

    CONSTRAINT game_endpoint_kind_vocabulary CHECK (kind IN ('telnet', 'tls', 'websocket', 'http')),
    CONSTRAINT game_endpoint_state_vocabulary CHECK (state IN ('active', 'stale', 'gone')),
    CONSTRAINT game_endpoint_port_is_a_port CHECK (port BETWEEN 1 AND 65535),
    CONSTRAINT game_endpoint_seen_after_first_seen CHECK (last_seen_at >= first_seen_at),

    -- Teeth for HostName.Normalize, in the same spirit as §5.7's vocabulary constraints. The unique
    -- index below is only an identity guarantee if one host has one spelling: 'MUD.Example.ORG' and
    -- 'mud.example.org' are different strings and would be two rows for one machine. Postgres cannot
    -- check the IP-literal half of the rule, so this covers the two textual rules and the repository
    -- covers the rest — but a write path that forgets to normalise at all now fails here, loudly,
    -- rather than quietly minting the duplicate listing §7.3 exists to prevent.
    CONSTRAINT game_endpoint_host_is_canonical CHECK (
        host = lower(host) AND host = btrim(host) AND host NOT LIKE '%.')
);

-- §7.3 calls a previously-seen endpoint the strongest identity signal, and asks it of an address with
-- no game in hand. UNIQUE rather than merely indexed, because that is only a signal if one address
-- cannot be claimed by two games — which is exactly the duplicate-listing failure §7.3 exists to stop.
-- Plain equality on a canonical column, so the lookup uses this index; there is no lower(host)
-- functional index because there is nothing left for it to fold.
CREATE UNIQUE INDEX game_endpoint_address_idx ON game_endpoint (host, port);
```

- [ ] **Step 6: Add the interface**

Append to `src/MUI.Storage/Repositories.cs`:

```csharp
/// <summary>The addresses a game answers on (spec §5.5).</summary>
/// <remarks>
/// Hosts are canonicalised by every implementation, on both ends: <c>UpsertAsync</c> stores
/// <c>HostName.Normalize(endpoint.Host)</c> and <c>ByAddressAsync</c> looks up
/// <c>HostName.Normalize(host)</c>. That is part of this interface's contract rather than one
/// repository's private habit, because a fake that compared more leniently than the real thing would
/// pass every test while production minted a duplicate endpoint for a host spelled in capitals.
/// </remarks>
public interface IEndpointRepository
{
    Task<IReadOnlyList<GameEndpoint>> ForGameAsync(Guid gameId, CancellationToken ct);

    /// <summary>§7.3's strongest identity signal, asked of an address with no game in hand.</summary>
    Task<GameEndpoint?> ByAddressAsync(string host, int port, CancellationToken ct);

    Task UpsertAsync(GameEndpoint endpoint, CancellationToken ct);
}
```

- [ ] **Step 7: Write the repository**

`src/MUI.Storage/NpgsqlEndpointRepository.cs`:

```csharp
namespace MUI.Storage;

using Dapper;

using MUI.Catalog;

using Npgsql;

public sealed class NpgsqlEndpointRepository(NpgsqlDataSource source) : IEndpointRepository
{
    private const string Columns =
        """
        game_id AS GameId, host AS Host, port AS Port, kind AS Kind,
        first_seen_at AS FirstSeenAt, last_seen_at AS LastSeenAt, state AS State
        """;

    public async Task<IReadOnlyList<GameEndpoint>> ForGameAsync(Guid gameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<EndpointRow>(new CommandDefinition(
            $"SELECT {Columns} FROM game_endpoint WHERE game_id = @gameId ORDER BY host, port",
            new { gameId },
            cancellationToken: ct));

        return rows.Select(Map).ToList();
    }

    public async Task<GameEndpoint?> ByAddressAsync(string host, int port, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // Ordinal equality against a canonical column, which is what lets this use
        // game_endpoint_address_idx. It can be ordinal only because the parameter is normalised here
        // and the stored value was normalised on the way in — the comparison is strict precisely
        // because the spelling was settled earlier.
        var row = await connection.QuerySingleOrDefaultAsync<EndpointRow>(new CommandDefinition(
            $"SELECT {Columns} FROM game_endpoint WHERE host = @host AND port = @port",
            new { host = HostName.Normalize(host), port },
            cancellationToken: ct));

        return row is null ? null : Map(row);
    }

    public async Task UpsertAsync(GameEndpoint endpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        await using var connection = await source.OpenConnectionAsync(ct);

        // first_seen_at is when WE first saw this address, so a later sighting must never move it
        // forward — the endpoint history is part of the record §7.4 refuses to throw away.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game_endpoint (game_id, host, port, kind, first_seen_at, last_seen_at, state)
            VALUES (@gameId, @host, @port, @kind, @firstSeenAt, @lastSeenAt, @state)
            ON CONFLICT (game_id, host, port) DO UPDATE SET
                kind = EXCLUDED.kind,
                last_seen_at = GREATEST(game_endpoint.last_seen_at, EXCLUDED.last_seen_at),
                state = EXCLUDED.state
            """,
            new
            {
                gameId = endpoint.GameId,
                // The one place a host becomes canonical on the way into the catalogue. The CHECK
                // constraint would reject anything else, which is the point: this is not a courtesy.
                host = HostName.Normalize(endpoint.Host),
                port = endpoint.Port,
                kind = SqlEnums.ToDb(endpoint.Kind),
                firstSeenAt = endpoint.FirstSeenAt,
                lastSeenAt = endpoint.LastSeenAt,
                state = SqlEnums.ToDb(endpoint.State),
            },
            cancellationToken: ct));
    }

    private static GameEndpoint Map(EndpointRow row) => new(
        row.GameId,
        row.Host,
        row.Port,
        SqlEnums.Parse<EndpointKind>(row.Kind),
        row.FirstSeenAt,
        row.LastSeenAt,
        SqlEnums.Parse<EndpointState>(row.State));

    private sealed record EndpointRow(
        Guid GameId, string Host, int Port, string Kind,
        DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, string State);
}
```

- [ ] **Step 8: Run both suites to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests </dev/null
```
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/MUI.Catalog src/MUI.Storage tests/MUI.Catalog.Tests tests/MUI.Storage.Tests
git commit -m "feat(storage): game_endpoint, with one address belonging to at most one game"
```

---

### Task 11: `NpgsqlGameRepository` and `GameQuery` (spec §5, §7.5, §9)

**Files:**
- Modify: `src/MUI.Storage/Repositories.cs`
- Create: `src/MUI.Storage/NpgsqlGameRepository.cs`
- Create: `tests/MUI.Storage.Tests/GameRepositoryTests.cs`

**Interfaces:**
- Consumes: `Game`, `LifecycleState` (Task 3); `SqlEnums` (Task 6); `CapabilityFields` (Task 2).
- Produces:
  - `sealed record MUI.Storage.GameQuery` with `bool IncludeArchived`, `int Limit = 50`, `int Offset`,
    `string? Codebase`, `string? Capability`
  - `interface MUI.Storage.IGameRepository` with `ByIdAsync`, `BySlugAsync`, `InsertAsync`,
    `SetStateAsync`, `ListAsync` (signatures below)
  - `sealed class MUI.Storage.NpgsqlGameRepository(NpgsqlDataSource source) : IGameRepository`

- [ ] **Step 1: Write the failing test**

`tests/MUI.Storage.Tests/GameRepositoryTests.cs`:

```csharp
using Dapper;

using MUI.Catalog;
using MUI.Storage.Tests.Support;

namespace MUI.Storage.Tests;

/// <summary>
/// Spec §9: archived games are excluded by default and reachable via an explicit include-archived
/// filter. Spec §7.5: archiving changes presentation and nothing else, so the row survives.
/// </summary>
public class GameRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    private static Game NewGame(string slug, string name, LifecycleState state = LifecycleState.Active) =>
        new(Guid.NewGuid(), slug, name, state, IsClaimed: false, Now.AddYears(-2), Now, ArchivedAt: null);

    [Test]
    public async Task AnInsertedGameIsFoundByIdAndBySlug()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlGameRepository(db.DataSource);
        var game = NewGame("corvid", "Corvid");

        var id = await repository.InsertAsync(game, CancellationToken.None);

        var byId = await repository.ByIdAsync(id, CancellationToken.None);
        var bySlug = await repository.BySlugAsync("corvid", CancellationToken.None);

        await Assert.That(byId!.Name).IsEqualTo("Corvid");
        await Assert.That(bySlug!.Id).IsEqualTo(id);
        await Assert.That(bySlug.State).IsEqualTo(LifecycleState.Active);
    }

    [Test]
    public async Task AGameNobodyInsertedIsNullRatherThanAThrow()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlGameRepository(db.DataSource);

        await Assert.That(await repository.ByIdAsync(Guid.NewGuid(), CancellationToken.None)).IsNull();
        await Assert.That(await repository.BySlugAsync("nobody", CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task ArchivedGamesAreExcludedByDefaultAndIncludedOnRequest()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlGameRepository(db.DataSource);

        var live = await repository.InsertAsync(NewGame("live", "Live"), CancellationToken.None);
        var gone = await repository.InsertAsync(NewGame("gone", "Gone"), CancellationToken.None);
        await repository.SetStateAsync(gone, LifecycleState.Archived, Now, CancellationToken.None);

        var byDefault = await repository.ListAsync(new GameQuery(), CancellationToken.None);
        var withArchive = await repository.ListAsync(new GameQuery { IncludeArchived = true }, CancellationToken.None);

        await Assert.That(byDefault.Select(g => g.Id).ToList()).IsEquivalentTo(new[] { live });
        await Assert.That(withArchive).HasCount(2);
    }

    [Test]
    public async Task ArchivingRecordsWhenAndUnarchivingClearsIt()
    {
        // §7.5: one successful probe restores the game, and nothing about it was deleted meanwhile.
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlGameRepository(db.DataSource);
        var id = await repository.InsertAsync(NewGame("corvid", "Corvid"), CancellationToken.None);

        await repository.SetStateAsync(id, LifecycleState.Archived, Now, CancellationToken.None);
        var archived = await repository.ByIdAsync(id, CancellationToken.None);

        await repository.SetStateAsync(id, LifecycleState.Active, null, CancellationToken.None);
        var restored = await repository.ByIdAsync(id, CancellationToken.None);

        await Assert.That(archived!.State).IsEqualTo(LifecycleState.Archived);
        await Assert.That(archived.ArchivedAt).IsEqualTo(Now);
        await Assert.That(restored!.State).IsEqualTo(LifecycleState.Active);
        await Assert.That(restored.ArchivedAt).IsNull();
        await Assert.That(restored.Name).IsEqualTo("Corvid");
    }

    [Test]
    public async Task TheListingCanBeFilteredByCodebase()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlGameRepository(db.DataSource);
        var penn = await repository.InsertAsync(NewGame("penn", "Penn Game"), CancellationToken.None);
        var evennia = await repository.InsertAsync(NewGame("evennia", "Evennia Game"), CancellationToken.None);

        await SetField(db, penn, "CODEBASE", "PennMUSH");
        await SetField(db, evennia, "CODEBASE", "Evennia");

        var results = await repository.ListAsync(new GameQuery { Codebase = "PennMUSH" }, CancellationToken.None);

        await Assert.That(results.Select(g => g.Id).ToList()).IsEquivalentTo(new[] { penn });
    }

    [Test]
    public async Task TheListingCanBeFilteredByMeasuredCapabilityAndNotByADeclaredOne()
    {
        // §9's facets are "*measured* protocol support". A game claiming GMCP it does not offer must
        // not appear in the GMCP facet — that disagreement is the interesting fact, not a match.
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlGameRepository(db.DataSource);
        var offers = await repository.InsertAsync(NewGame("offers", "Offers It"), CancellationToken.None);
        var claims = await repository.InsertAsync(NewGame("claims", "Only Claims It"), CancellationToken.None);

        await SetField(db, offers, CapabilityFields.Measured("GMCP"), "true", "handshake", "observed");
        await SetField(db, claims, CapabilityFields.Declared("GMCP"), "true");

        var results = await repository.ListAsync(new GameQuery { Capability = "GMCP" }, CancellationToken.None);

        await Assert.That(results.Select(g => g.Id).ToList()).IsEquivalentTo(new[] { offers });
    }

    [Test]
    public async Task TheListingPagesWithLimitAndOffset()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var repository = new NpgsqlGameRepository(db.DataSource);

        foreach (var name in new[] { "Alpha", "Bravo", "Charlie" })
        {
            await repository.InsertAsync(NewGame(name.ToLowerInvariant(), name), CancellationToken.None);
        }

        var page = await repository.ListAsync(new GameQuery { Limit = 1, Offset = 1 }, CancellationToken.None);

        await Assert.That(page).HasCount(1);
        await Assert.That(page[0].Name).IsEqualTo("Bravo");
    }

    private static async Task SetField(
        TestDatabase db, Guid gameId, string field, string value,
        string source = "mssp", string confidence = "reported")
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO game_field (game_id, field, value, source, confidence, first_seen_at, last_confirmed_at)
            VALUES (@gameId, @field, @value, @source, @confidence, now(), now())
            """,
            new { gameId, field, value, source, confidence });
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0246: The type or namespace name 'NpgsqlGameRepository' could not be found`.

- [ ] **Step 3: Add the interface and the query**

Append to `src/MUI.Storage/Repositories.cs`:

```csharp
/// <summary>
/// What a collection endpoint is asking for (spec §9). Archived games are out by default and in on
/// request — a toggle, never a deletion.
/// </summary>
public sealed record GameQuery
{
    public bool IncludeArchived { get; init; }

    public int Limit { get; init; } = 50;

    public int Offset { get; init; }

    public string? Codebase { get; init; }

    /// <summary>
    /// A capability name such as <c>GMCP</c>. Filters on what we <em>measured</em> in the handshake,
    /// never on what the game declared — §9's facets are measured protocol support, and a game
    /// claiming an option it does not offer must not appear in that facet.
    /// </summary>
    public string? Capability { get; init; }
}

public interface IGameRepository
{
    Task<Game?> ByIdAsync(Guid id, CancellationToken ct);

    Task<Game?> BySlugAsync(string slug, CancellationToken ct);

    Task<Guid> InsertAsync(Game game, CancellationToken ct);

    Task SetStateAsync(Guid id, LifecycleState state, DateTimeOffset? archivedAt, CancellationToken ct);

    Task<IReadOnlyList<Game>> ListAsync(GameQuery query, CancellationToken ct);
}
```

- [ ] **Step 4: Write the repository**

`src/MUI.Storage/NpgsqlGameRepository.cs`:

```csharp
namespace MUI.Storage;

using System.Text;

using Dapper;

using MUI.Catalog;

using Npgsql;

public sealed class NpgsqlGameRepository(NpgsqlDataSource source) : IGameRepository
{
    private const string Columns =
        """
        id AS Id, slug AS Slug, name AS Name, state AS State, is_claimed AS IsClaimed,
        first_seen_at AS FirstSeenAt, last_reachable_at AS LastReachableAt, archived_at AS ArchivedAt
        """;

    public async Task<Game?> ByIdAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var row = await connection.QuerySingleOrDefaultAsync<GameRow>(new CommandDefinition(
            $"SELECT {Columns} FROM game WHERE id = @id", new { id }, cancellationToken: ct));

        return row is null ? null : Map(row);
    }

    public async Task<Game?> BySlugAsync(string slug, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var row = await connection.QuerySingleOrDefaultAsync<GameRow>(new CommandDefinition(
            $"SELECT {Columns} FROM game WHERE slug = @slug", new { slug }, cancellationToken: ct));

        return row is null ? null : Map(row);
    }

    public async Task<Guid> InsertAsync(Game game, CancellationToken ct)
    {
        var id = game.Id == Guid.Empty ? Guid.NewGuid() : game.Id;

        await using var connection = await source.OpenConnectionAsync(ct);

        // Nullable timestamps carry an explicit ::timestamptz because Npgsql cannot infer a type
        // from a null parameter.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game (id, slug, name, state, is_claimed, first_seen_at, last_reachable_at, archived_at)
            VALUES (@id, @slug, @name, @state, @isClaimed, @firstSeenAt,
                    @lastReachableAt::timestamptz, @archivedAt::timestamptz)
            """,
            new
            {
                id,
                slug = game.Slug,
                name = game.Name,
                state = SqlEnums.ToDb(game.State),
                isClaimed = game.IsClaimed,
                firstSeenAt = game.FirstSeenAt,
                lastReachableAt = game.LastReachableAt,
                archivedAt = game.ArchivedAt,
            },
            cancellationToken: ct));

        return id;
    }

    public async Task SetStateAsync(Guid id, LifecycleState state, DateTimeOffset? archivedAt, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE game SET state = @state, archived_at = @archivedAt::timestamptz WHERE id = @id",
            new { id, state = SqlEnums.ToDb(state), archivedAt },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Game>> ListAsync(GameQuery query, CancellationToken ct)
    {
        // The WHERE is built rather than written with `@x IS NULL OR …` guards, because Npgsql
        // cannot infer a parameter's type from a null and the guards would need casts on every one.
        var sql = new StringBuilder($"SELECT {Columns} FROM game g WHERE true");

        if (!query.IncludeArchived)
        {
            sql.Append(" AND g.state <> 'archived'");
        }

        if (query.Codebase is not null)
        {
            sql.Append(
                """
                 AND EXISTS (SELECT 1 FROM game_field f
                             WHERE f.game_id = g.id AND f.field = 'CODEBASE' AND f.value = @codebase)
                """);
        }

        if (query.Capability is not null)
        {
            sql.Append(
                """
                 AND EXISTS (SELECT 1 FROM game_field f
                             WHERE f.game_id = g.id AND f.field = @capabilityField AND f.value = 'true')
                """);
        }

        sql.Append(" ORDER BY g.name, g.slug LIMIT @limit OFFSET @offset");

        await using var connection = await source.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<GameRow>(new CommandDefinition(
            sql.ToString(),
            new
            {
                codebase = query.Codebase,
                capabilityField = query.Capability is null ? null : CapabilityFields.Measured(query.Capability),
                limit = query.Limit,
                offset = query.Offset,
            },
            cancellationToken: ct));

        return rows.Select(Map).ToList();
    }

    private static Game Map(GameRow row) => new(
        row.Id,
        row.Slug,
        row.Name,
        SqlEnums.Parse<LifecycleState>(row.State),
        row.IsClaimed,
        row.FirstSeenAt,
        row.LastReachableAt,
        row.ArchivedAt);

    private sealed record GameRow(
        Guid Id, string Slug, string Name, string State, bool IsClaimed,
        DateTimeOffset FirstSeenAt, DateTimeOffset? LastReachableAt, DateTimeOffset? ArchivedAt);
}
```

- [ ] **Step 5: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests </dev/null
```
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Storage tests/MUI.Storage.Tests
git commit -m "feat(storage): the game repository, with archived games excluded by default"
```

---

### Task 12: `NpgsqlGameFieldRepository` (spec §5.1)

**Files:**
- Modify: `src/MUI.Storage/Repositories.cs`
- Create: `src/MUI.Storage/NpgsqlGameFieldRepository.cs`
- Create: `tests/MUI.Storage.Tests/GameFieldRepositoryTests.cs`

**Interfaces:**
- Consumes: `GameField`, `FieldChange`, `FieldSource`, `FieldConfidence` (Task 3); `SqlEnums` (Task 6); `GameSeed` (Task 8).
- Produces:
  - `interface MUI.Storage.IGameFieldRepository` with
    `Task<IReadOnlyList<GameField>> ForGameAsync(Guid gameId, CancellationToken ct)`,
    `Task UpsertAsync(GameField field, CancellationToken ct)`,
    `Task ConfirmAsync(Guid gameId, string field, DateTimeOffset at, CancellationToken ct)`,
    `Task AppendChangeAsync(FieldChange change, CancellationToken ct)`,
    `Task<IReadOnlyList<FieldChange>> ChangesAsync(Guid gameId, int limit, CancellationToken ct)`
  - `sealed class MUI.Storage.NpgsqlGameFieldRepository(NpgsqlDataSource source) : IGameFieldRepository`

- [ ] **Step 1: Write the failing test**

`tests/MUI.Storage.Tests/GameFieldRepositoryTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Storage.Tests.Support;

namespace MUI.Storage.Tests;

/// <summary>
/// Spec §5.1's two operations, at the storage layer: confirm bumps a timestamp and writes nothing
/// else, change rewrites the row. Which of the two happens is the reconciler's decision (Task 14);
/// this is only about them being possible and cheap.
/// </summary>
public class GameFieldRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    private static GameField Genre(Guid gameId, string value, DateTimeOffset confirmedAt) =>
        new(gameId, "GENRE", value, FieldSource.Mssp, FieldConfidence.Reported, Now.AddYears(-3), confirmedAt);

    [Test]
    public async Task AFieldRoundTripsWithItsSourceAndConfidence()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlGameFieldRepository(db.DataSource);

        await repository.UpsertAsync(Genre(gameId, "Fantasy", Now), CancellationToken.None);
        var fields = await repository.ForGameAsync(gameId, CancellationToken.None);

        await Assert.That(fields).HasCount(1);
        await Assert.That(fields[0].Value).IsEqualTo("Fantasy");
        await Assert.That(fields[0].Source).IsEqualTo(FieldSource.Mssp);
        await Assert.That(fields[0].Confidence).IsEqualTo(FieldConfidence.Reported);
        await Assert.That(fields[0].FirstSeenAt).IsEqualTo(Now.AddYears(-3));
    }

    [Test]
    public async Task ConfirmingMovesTheConfirmationTimeAndNothingElse()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlGameFieldRepository(db.DataSource);

        await repository.UpsertAsync(Genre(gameId, "Fantasy", Now.AddDays(-30)), CancellationToken.None);
        await repository.ConfirmAsync(gameId, "GENRE", Now, CancellationToken.None);

        var fields = await repository.ForGameAsync(gameId, CancellationToken.None);

        await Assert.That(fields[0].LastConfirmedAt).IsEqualTo(Now);
        await Assert.That(fields[0].Value).IsEqualTo("Fantasy");
        await Assert.That(fields[0].FirstSeenAt).IsEqualTo(Now.AddYears(-3));
    }

    [Test]
    public async Task UpsertingTheSameFieldTwiceKeepsOneRowAndTheOriginalFirstSeen()
    {
        // "One row for ever, not one per hour" — and first_seen_at is when the FIELD appeared, not
        // when this value did, so a later write must not move it forward.
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlGameFieldRepository(db.DataSource);

        await repository.UpsertAsync(Genre(gameId, "Fantasy", Now.AddDays(-30)), CancellationToken.None);
        await repository.UpsertAsync(
            new GameField(gameId, "GENRE", "Science Fiction", FieldSource.Mssp, FieldConfidence.Reported, Now, Now),
            CancellationToken.None);

        var fields = await repository.ForGameAsync(gameId, CancellationToken.None);

        await Assert.That(fields).HasCount(1);
        await Assert.That(fields[0].Value).IsEqualTo("Science Fiction");
        await Assert.That(fields[0].FirstSeenAt).IsEqualTo(Now.AddYears(-3));
    }

    [Test]
    public async Task ConfirmingAFieldNobodyStoredDoesNothingRatherThanThrowing()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlGameFieldRepository(db.DataSource);

        await repository.ConfirmAsync(gameId, "GENRE", Now, CancellationToken.None);

        await Assert.That(await repository.ForGameAsync(gameId, CancellationToken.None)).IsEmpty();
    }

    [Test]
    public async Task ChangesComeBackNewestFirstAndBounded()
    {
        // §9's change feed is "the most recent N changes for this game".
        await using var db = await PostgresFixture.MigratedAsync();
        var gameId = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlGameFieldRepository(db.DataSource);

        await repository.AppendChangeAsync(
            new FieldChange(0, gameId, "GENRE", null, "Fantasy", FieldSource.Mssp, Now.AddDays(-10)),
            CancellationToken.None);
        await repository.AppendChangeAsync(
            new FieldChange(0, gameId, "GENRE", "Fantasy", "Science Fiction", FieldSource.Mssp, Now.AddDays(-1)),
            CancellationToken.None);

        var all = await repository.ChangesAsync(gameId, 10, CancellationToken.None);
        var latest = await repository.ChangesAsync(gameId, 1, CancellationToken.None);

        await Assert.That(all).HasCount(2);
        await Assert.That(all[0].NewValue).IsEqualTo("Science Fiction");
        await Assert.That(all[0].OldValue).IsEqualTo("Fantasy");
        await Assert.That(all[1].OldValue).IsNull();
        await Assert.That(latest).HasCount(1);
        await Assert.That(latest[0].NewValue).IsEqualTo("Science Fiction");
        await Assert.That(latest[0].Id).IsGreaterThan(0);
    }

    [Test]
    public async Task OneGamesFieldsAreNotAnothers()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var mine = await GameSeed.InsertAsync(db.DataSource);
        var theirs = await GameSeed.InsertAsync(db.DataSource);
        var repository = new NpgsqlGameFieldRepository(db.DataSource);

        await repository.UpsertAsync(Genre(mine, "Fantasy", Now), CancellationToken.None);

        await Assert.That(await repository.ForGameAsync(theirs, CancellationToken.None)).IsEmpty();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0246: The type or namespace name 'NpgsqlGameFieldRepository' could not be found`.

- [ ] **Step 3: Add the interface**

Append to `src/MUI.Storage/Repositories.cs`:

```csharp
/// <summary>
/// One game's descriptive fields and its change feed (spec §5.1).
/// </summary>
public interface IGameFieldRepository
{
    Task<IReadOnlyList<GameField>> ForGameAsync(Guid gameId, CancellationToken ct);

    Task UpsertAsync(GameField field, CancellationToken ct);

    /// <summary>
    /// The cheap half of §5.1: bump <c>last_confirmed_at</c> and write nothing else. This is what
    /// most probes do to most fields, for ever.
    /// </summary>
    Task ConfirmAsync(Guid gameId, string field, DateTimeOffset at, CancellationToken ct);

    Task AppendChangeAsync(FieldChange change, CancellationToken ct);

    Task<IReadOnlyList<FieldChange>> ChangesAsync(Guid gameId, int limit, CancellationToken ct);
}
```

- [ ] **Step 4: Write the repository**

`src/MUI.Storage/NpgsqlGameFieldRepository.cs`:

```csharp
namespace MUI.Storage;

using Dapper;

using MUI.Catalog;

using Npgsql;

public sealed class NpgsqlGameFieldRepository(NpgsqlDataSource source) : IGameFieldRepository
{
    public async Task<IReadOnlyList<GameField>> ForGameAsync(Guid gameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<FieldRow>(new CommandDefinition(
            """
            SELECT game_id AS GameId, field AS Field, value AS Value, source AS Source,
                   confidence AS Confidence, first_seen_at AS FirstSeenAt, last_confirmed_at AS LastConfirmedAt
            FROM game_field
            WHERE game_id = @gameId
            ORDER BY field
            """,
            new { gameId },
            cancellationToken: ct));

        return rows.Select(row => new GameField(
            row.GameId, row.Field, row.Value,
            SqlEnums.Parse<FieldSource>(row.Source),
            SqlEnums.Parse<FieldConfidence>(row.Confidence),
            row.FirstSeenAt, row.LastConfirmedAt)).ToList();
    }

    public async Task UpsertAsync(GameField field, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // first_seen_at is when this FIELD first appeared on this game, not when the current value
        // did, so a later write leaves it alone: it is what §5.6's age is measured against.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game_field (game_id, field, value, source, confidence, first_seen_at, last_confirmed_at)
            VALUES (@gameId, @field, @value, @source, @confidence, @firstSeenAt, @lastConfirmedAt)
            ON CONFLICT (game_id, field) DO UPDATE SET
                value = EXCLUDED.value,
                source = EXCLUDED.source,
                confidence = EXCLUDED.confidence,
                last_confirmed_at = EXCLUDED.last_confirmed_at
            """,
            new
            {
                gameId = field.GameId,
                field = field.Field,
                value = field.Value,
                source = SqlEnums.ToDb(field.Source),
                confidence = SqlEnums.ToDb(field.Confidence),
                firstSeenAt = field.FirstSeenAt,
                lastConfirmedAt = field.LastConfirmedAt,
            },
            cancellationToken: ct));
    }

    public async Task ConfirmAsync(Guid gameId, string field, DateTimeOffset at, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE game_field SET last_confirmed_at = @at WHERE game_id = @gameId AND field = @field",
            new { gameId, field, at },
            cancellationToken: ct));
    }

    public async Task AppendChangeAsync(FieldChange change, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // The id is assigned by the database; the caller's is ignored, which is why FieldChange is
        // constructed with 0 at every call site.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO field_change (game_id, field, old_value, new_value, source, at)
            VALUES (@gameId, @field, @oldValue::text, @newValue, @source, @at)
            """,
            new
            {
                gameId = change.GameId,
                field = change.Field,
                oldValue = change.OldValue,
                newValue = change.NewValue,
                source = SqlEnums.ToDb(change.Source),
                at = change.At,
            },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<FieldChange>> ChangesAsync(Guid gameId, int limit, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<ChangeRow>(new CommandDefinition(
            """
            SELECT id AS Id, game_id AS GameId, field AS Field, old_value AS OldValue,
                   new_value AS NewValue, source AS Source, at AS At
            FROM field_change
            WHERE game_id = @gameId
            ORDER BY at DESC, id DESC
            LIMIT @limit
            """,
            new { gameId, limit },
            cancellationToken: ct));

        return rows.Select(row => new FieldChange(
            row.Id, row.GameId, row.Field, row.OldValue, row.NewValue,
            SqlEnums.Parse<FieldSource>(row.Source), row.At)).ToList();
    }

    private sealed record FieldRow(
        Guid GameId, string Field, string Value, string Source, string Confidence,
        DateTimeOffset FirstSeenAt, DateTimeOffset LastConfirmedAt);

    private sealed record ChangeRow(
        long Id, Guid GameId, string Field, string? OldValue, string NewValue, string Source, DateTimeOffset At);
}
```

- [ ] **Step 5: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests </dev/null
```
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Storage tests/MUI.Storage.Tests
git commit -m "feat(storage): the field repository, with confirm as the cheap path"
```

---

### Task 13: `FailureCauseMap`, in-memory repositories and `ProbeResult` fixtures

Everything from here on is **`MUI.Discovery`**, and everything from here on is tested **against
in-memory fakes and hand-built `ProbeResult` fixtures — no container and no socket anywhere**. That
is not a shortcut: §6.5 says none of the three writers knows a socket exists, and a suite that needs
one would be evidence the arrow had been broken.

**Files:**
- Modify: `src/MUI.Discovery/MUI.Discovery.csproj`
- Create: `src/MUI.Discovery/Writers/FailureCauseMap.cs`
- Create: `tests/MUI.Discovery.Tests/Support/InMemoryRepositories.cs`
- Create: `tests/MUI.Discovery.Tests/Support/ProbeFixtures.cs`
- Create: `tests/MUI.Discovery.Tests/Writers/FailureCauseMapTests.cs`
- Create: `tests/MUI.Discovery.Tests/InMemoryEndpointRepositoryTests.cs`
- Create: `tests/MUI.Discovery.Tests/HostNormalisationAgreementTests.cs`

**Interfaces:**
- Consumes: `ProbeFailureCauses`, `ProbeResult`, `ProbeOutcome`, `FailureDetail`, `WhoReading`,
  `WhoConfidence`, `MsspTransport`, `PresenceAggregates` (Plan 1); `MUI.Crawl.Mssp.MsspData`,
  `MUI.Crawl.Mssp.MsspVariables`, `MUI.Crawl.Mssp.MsspHost.Create` (Plan 1); `FailureCause` (Task 3);
  `HostName.Normalize` (Task 10); the six repository interfaces (Tasks 8–12).
- Produces:
  - `static class MUI.Discovery.Writers.FailureCauseMap` with
    `FailureCause From(string probeCause)` and `string To(FailureCause cause)`
  - `MUI.Discovery.Tests.Support.InMemoryGameFieldRepository`, `InMemoryPresenceRepository`,
    `InMemoryAvailabilityRepository`, `InMemoryGameRepository`, `InMemoryEndpointRepository` — five
    fakes, each implementing its interface with public `List<T>` state the tests read directly
  - `static class MUI.Discovery.Tests.Support.ProbeFixtures` with
    `ProbeResult Answered(...)` and `ProbeResult Failed(string cause, DateTimeOffset at)`

- [ ] **Step 1: Reference `MUI.Storage` from `MUI.Discovery`**

In `src/MUI.Discovery/MUI.Discovery.csproj`, add inside the existing `<ItemGroup>`:

```xml
    <ProjectReference Include="..\MUI.Storage\MUI.Storage.csproj" />
```

and replace the trailing comment block with:

```xml
  <!--
    The only project that legitimately sees both MUI.Crawl and MUI.Catalog. A ProbeResult is raw
    observation and MUI.Catalog may never reference MUI.Crawl, so turning one into catalogue state
    is Discovery's job — which is why the three writers of spec §6.5 live here and not there.
  -->
```

- [ ] **Step 2: Write the failing test**

`tests/MUI.Discovery.Tests/Writers/FailureCauseMapTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Discovery.Writers;

namespace MUI.Discovery.Tests.Writers;

/// <summary>
/// MUI.Crawl cannot use MUI.Catalog.FailureCause — the reference is forbidden — so §5.3's cause
/// vocabulary crosses the boundary as strings and is mapped here. A mapping with a hole in it is a
/// cause silently becoming something else, which §5.3 would then read as a real transition.
/// </summary>
public class FailureCauseMapTests
{
    [Test]
    public async Task EveryProbeCauseMapsToADistinctFailureCause()
    {
        await Assert.That(FailureCauseMap.From(ProbeFailureCauses.Dns)).IsEqualTo(FailureCause.Dns);
        await Assert.That(FailureCauseMap.From(ProbeFailureCauses.Refused)).IsEqualTo(FailureCause.Refused);
        await Assert.That(FailureCauseMap.From(ProbeFailureCauses.Tls)).IsEqualTo(FailureCause.Tls);
        await Assert.That(FailureCauseMap.From(ProbeFailureCauses.Timeout)).IsEqualTo(FailureCause.Timeout);
        await Assert.That(FailureCauseMap.From(ProbeFailureCauses.HandshakeStalled)).IsEqualTo(FailureCause.HandshakeStalled);
        await Assert.That(FailureCauseMap.From(ProbeFailureCauses.Unknown)).IsEqualTo(FailureCause.Unknown);
    }

    [Test]
    public async Task TheMappingIsTotalInBothDirections()
    {
        // Every FailureCause except None round-trips. None is the cause a REACHABLE interval carries
        // and is never a probe's answer, so there is deliberately no probe string for it.
        foreach (var cause in Enum.GetValues<FailureCause>().Where(c => c is not FailureCause.None))
        {
            await Assert.That(FailureCauseMap.From(FailureCauseMap.To(cause))).IsEqualTo(cause);
        }

        var probeCauses = new[]
        {
            ProbeFailureCauses.Dns, ProbeFailureCauses.Refused, ProbeFailureCauses.Tls,
            ProbeFailureCauses.Timeout, ProbeFailureCauses.HandshakeStalled, ProbeFailureCauses.Unknown,
        };

        foreach (var probeCause in probeCauses)
        {
            await Assert.That(FailureCauseMap.To(FailureCauseMap.From(probeCause))).IsEqualTo(probeCause);
        }
    }

    [Test]
    public async Task ACauseNobodyDeclaredBecomesUnknownRatherThanBeingGuessedAt()
    {
        // §12: parser failures degrade to unknown and are logged. They do not become a wrong cause,
        // because a wrong cause writes a real-looking availability transition.
        await Assert.That(FailureCauseMap.From("something_new_upstream")).IsEqualTo(FailureCause.Unknown);
        await Assert.That(FailureCauseMap.From("")).IsEqualTo(FailureCause.Unknown);
    }

    [Test]
    public async Task ThereIsNoProbeStringForTheReachableCause()
    {
        await Assert.That(() => FailureCauseMap.To(FailureCause.None)).Throws<ArgumentOutOfRangeException>();
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0246: The type or namespace name 'FailureCauseMap' could not be found`.

- [ ] **Step 4: Write `FailureCauseMap`**

`src/MUI.Discovery/Writers/FailureCauseMap.cs`:

```csharp
namespace MUI.Discovery.Writers;

using MUI.Catalog;
using MUI.Crawl;

/// <summary>
/// The failure vocabulary crossing from <c>MUI.Crawl</c> into <c>MUI.Catalog</c>.
/// </summary>
/// <remarks>
/// <c>MUI.Crawl</c> cannot use <see cref="FailureCause"/> — that reference is forbidden — so §5.3's
/// causes leave the probe as the string constants in <see cref="ProbeFailureCauses"/> and are mapped
/// here. Anything unrecognised becomes <see cref="FailureCause.Unknown"/> rather than a guess: only a
/// cause change writes an availability transition (§5.3), so a wrong cause manufactures an event that
/// never happened.
/// </remarks>
public static class FailureCauseMap
{
    public static FailureCause From(string probeCause) => probeCause switch
    {
        ProbeFailureCauses.Dns => FailureCause.Dns,
        ProbeFailureCauses.Refused => FailureCause.Refused,
        ProbeFailureCauses.Tls => FailureCause.Tls,
        ProbeFailureCauses.Timeout => FailureCause.Timeout,
        ProbeFailureCauses.HandshakeStalled => FailureCause.HandshakeStalled,
        _ => FailureCause.Unknown,
    };

    /// <summary>
    /// The probe string for a cause. <see cref="FailureCause.None"/> has none, deliberately: it is
    /// what a <em>reachable</em> interval carries and never a probe's answer.
    /// </summary>
    public static string To(FailureCause cause) => cause switch
    {
        FailureCause.Dns => ProbeFailureCauses.Dns,
        FailureCause.Refused => ProbeFailureCauses.Refused,
        FailureCause.Tls => ProbeFailureCauses.Tls,
        FailureCause.Timeout => ProbeFailureCauses.Timeout,
        FailureCause.HandshakeStalled => ProbeFailureCauses.HandshakeStalled,
        FailureCause.Unknown => ProbeFailureCauses.Unknown,
        _ => throw new ArgumentOutOfRangeException(
            nameof(cause), cause, "FailureCause.None is the cause of a reachable interval, not of a probe failure."),
    };
}
```

- [ ] **Step 5: Write the in-memory repositories**

`tests/MUI.Discovery.Tests/Support/InMemoryRepositories.cs`:

```csharp
using MUI.Catalog;
using MUI.Storage;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// In-memory stand-ins for the storage layer. The writers are tested against these rather than
/// against a database because what is under test is the decision — confirm or change, sample or
/// gap, extend or transition — and a container would only slow that down while proving the same
/// thing. The schema's own constraints are proved in MUI.Storage.Tests.
/// </summary>
public sealed class InMemoryGameFieldRepository : IGameFieldRepository
{
    public List<GameField> Fields { get; } = [];

    public List<FieldChange> Changes { get; } = [];

    public Task<IReadOnlyList<GameField>> ForGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GameField>>(Fields.Where(f => f.GameId == gameId).ToList());

    public Task UpsertAsync(GameField field, CancellationToken ct)
    {
        var index = Fields.FindIndex(f => f.GameId == field.GameId && f.Field == field.Field);

        if (index < 0)
        {
            Fields.Add(field);
        }
        else
        {
            // first_seen_at belongs to the field, not to the value, exactly as the real upsert has it.
            Fields[index] = field with { FirstSeenAt = Fields[index].FirstSeenAt };
        }

        return Task.CompletedTask;
    }

    public Task ConfirmAsync(Guid gameId, string field, DateTimeOffset at, CancellationToken ct)
    {
        var index = Fields.FindIndex(f => f.GameId == gameId && f.Field == field);

        if (index >= 0)
        {
            Fields[index] = Fields[index] with { LastConfirmedAt = at };
        }

        return Task.CompletedTask;
    }

    public Task AppendChangeAsync(FieldChange change, CancellationToken ct)
    {
        Changes.Add(change with { Id = Changes.Count + 1 });

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FieldChange>> ChangesAsync(Guid gameId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FieldChange>>(
            Changes.Where(c => c.GameId == gameId).OrderByDescending(c => c.At).ThenByDescending(c => c.Id)
                .Take(limit).ToList());
}

public sealed class InMemoryPresenceRepository : IPresenceRepository
{
    public List<PresenceSample> Samples { get; } = [];

    public List<DateTimeOffset> EnsuredPartitions { get; } = [];

    public Task AppendAsync(PresenceSample sample, CancellationToken ct)
    {
        Samples.Add(sample);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PresenceSample>> RangeAsync(
        Guid gameId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PresenceSample>>(
            Samples.Where(s => s.GameId == gameId && s.At >= from && s.At < to).OrderBy(s => s.At).ToList());

    public Task EnsurePartitionAsync(DateTimeOffset month, CancellationToken ct)
    {
        EnsuredPartitions.Add(month);

        return Task.CompletedTask;
    }
}

public sealed class InMemoryAvailabilityRepository : IAvailabilityRepository
{
    // AvailabilityInterval carries no origin property — the column is written by the repository and
    // never round-trips through the record — so the fake keeps the tier beside the row, which is the
    // only way it can answer the two cumulative questions apart.
    private readonly HashSet<long> _imported = [];

    public List<AvailabilityInterval> Intervals { get; } = [];

    /// <summary>
    /// Imported reachable time a test wants to assert against without writing intervals for it. It is
    /// added to whatever <see cref="InsertImportedAsync"/> actually wrote.
    /// </summary>
    public TimeSpan ImportedMeasuredReachable { get; set; } = TimeSpan.Zero;

    /// <summary>Whether the interval with this id was written by the imported path.</summary>
    public bool IsImported(long intervalId) => _imported.Contains(intervalId);

    public Task<AvailabilityInterval?> OpenIntervalAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult(Intervals.SingleOrDefault(i => i.GameId == gameId && i.ToAt is null));

    public Task<long> OpenAsync(
        Guid gameId, AvailabilityState state, FailureCause cause, DateTimeOffset from, CancellationToken ct)
    {
        var id = Intervals.Count + 1;
        Intervals.Add(new AvailabilityInterval(id, gameId, state, from, null, cause));

        return Task.FromResult((long)id);
    }

    public Task CloseAsync(long intervalId, DateTimeOffset at, CancellationToken ct)
    {
        var index = Intervals.FindIndex(i => i.Id == intervalId);

        if (index >= 0)
        {
            Intervals[index] = Intervals[index] with { ToAt = at };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AvailabilityInterval>> RangeAsync(
        Guid gameId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AvailabilityInterval>>(
            Intervals.Where(i => i.GameId == gameId && i.FromAt < to && (i.ToAt is null || i.ToAt > from))
                .OrderBy(i => i.FromAt).ToList());

    public Task<long> InsertImportedAsync(
        Guid gameId,
        AvailabilityState state,
        FailureCause cause,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var id = Intervals.Count + 1;
        Intervals.Add(new AvailabilityInterval(id, gameId, state, from, to, cause));
        _imported.Add(id);

        return Task.FromResult((long)id);
    }

    public Task<TimeSpan> CumulativeReachableAsync(Guid gameId, DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult(AvailabilityArithmetic.CumulativeReachable(
            Intervals.Where(i => i.GameId == gameId && !_imported.Contains(i.Id)), now));

    public Task<TimeSpan> CumulativeImportedMeasuredReachableAsync(
        Guid gameId, DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult(ImportedMeasuredReachable + AvailabilityArithmetic.CumulativeReachable(
            Intervals.Where(i => i.GameId == gameId && _imported.Contains(i.Id)), now));
}

public sealed class InMemoryGameRepository : IGameRepository
{
    public List<Game> Games { get; } = [];

    public Task<Game?> ByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Games.SingleOrDefault(g => g.Id == id));

    public Task<Game?> BySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(Games.SingleOrDefault(g => g.Slug == slug));

    public Task<Guid> InsertAsync(Game game, CancellationToken ct)
    {
        var id = game.Id == Guid.Empty ? Guid.NewGuid() : game.Id;
        Games.Add(game with { Id = id });

        return Task.FromResult(id);
    }

    public Task SetStateAsync(Guid id, LifecycleState state, DateTimeOffset? archivedAt, CancellationToken ct)
    {
        var index = Games.FindIndex(g => g.Id == id);

        if (index >= 0)
        {
            Games[index] = Games[index] with { State = state, ArchivedAt = archivedAt };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Game>> ListAsync(GameQuery query, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Game>>(
            Games.Where(g => query.IncludeArchived || g.State is not LifecycleState.Archived)
                .OrderBy(g => g.Name, StringComparer.Ordinal)
                .Skip(query.Offset)
                .Take(query.Limit)
                .ToList());
}

/// <summary>
/// Endpoints, in memory. <see cref="ByAddressAsync"/> is the one query identity turns on — Plans 03
/// and 04 both ask "do we already know this address" to decide whether a host merges into a game we
/// have or becomes a fresh crawl target — so this fake mirrors the real repository exactly on all
/// three of the rules that matter: hosts are canonicalised by <see cref="HostName.Normalize"/> and
/// then compared <b>ordinally</b>, <c>first_seen_at</c> never moves forward, and <c>last_seen_at</c>
/// never moves back.
/// </summary>
/// <remarks>
/// The ordinal comparison is deliberate and is the whole point. A fake that compared
/// case-insensitively would be <em>kinder</em> than the database, which is the worst direction for a
/// disagreement to run: every test would pass while production stored a second row for
/// <c>MUD.Example.ORG</c> and lost §7.3's endpoint signal. Leniency belongs in the normalisation, which
/// both sides share, and nowhere else. Pinned by
/// <c>InMemoryEndpointRepositoryTests</c>, whose assertions are those of
/// <c>MUI.Storage.Tests.EndpointRepositoryTests</c>.
/// </remarks>
public sealed class InMemoryEndpointRepository : IEndpointRepository
{
    public List<GameEndpoint> Endpoints { get; } = [];

    public Task<IReadOnlyList<GameEndpoint>> ForGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GameEndpoint>>(
            Endpoints.Where(e => e.GameId == gameId)
                .OrderBy(e => e.Host, StringComparer.Ordinal).ThenBy(e => e.Port).ToList());

    public Task<GameEndpoint?> ByAddressAsync(string host, int port, CancellationToken ct)
    {
        var canonical = HostName.Normalize(host);

        return Task.FromResult(Endpoints.SingleOrDefault(e =>
            string.Equals(e.Host, canonical, StringComparison.Ordinal) && e.Port == port));
    }

    public Task UpsertAsync(GameEndpoint endpoint, CancellationToken ct)
    {
        var canonical = endpoint with { Host = HostName.Normalize(endpoint.Host) };

        var index = Endpoints.FindIndex(e =>
            e.GameId == canonical.GameId
            && string.Equals(e.Host, canonical.Host, StringComparison.Ordinal)
            && e.Port == canonical.Port);

        if (index < 0)
        {
            Endpoints.Add(canonical);
        }
        else
        {
            // first_seen_at is when WE first saw the address and a later sighting must not move it;
            // last_seen_at is GREATEST of the two, exactly as 0005_game_endpoint.sql's ON CONFLICT has it.
            var incumbent = Endpoints[index];

            Endpoints[index] = canonical with
            {
                FirstSeenAt = incumbent.FirstSeenAt,
                LastSeenAt = canonical.LastSeenAt > incumbent.LastSeenAt ? canonical.LastSeenAt : incumbent.LastSeenAt,
            };
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 6: Pin the fake against the real repository, and both against `MsspHost`**

Two small files. Neither can be folded into the other's suite: the real repository needs a container
and `MUI.Discovery.Tests` must never start one, and `MUI.Storage.Tests` cannot see `MUI.Crawl` at all.
So the agreement is asserted from both ends, and each test names its counterpart.

`tests/MUI.Discovery.Tests/InMemoryEndpointRepositoryTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// The fake must not be kinder than the database. These are the assertions of
/// <c>MUI.Storage.Tests.EndpointRepositoryTests.EverySpellingOfOneHostIsOneEndpointAndNotSeveral</c>
/// and <c>…WhatIsStoredIsCharacterForCharacterWhatHostNameProduces</c>, over the same table of
/// spellings, run against <see cref="InMemoryEndpointRepository"/> instead. If the two ever disagree,
/// the one that is wrong is whichever one is more forgiving.
/// </summary>
public class InMemoryEndpointRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The same table as <c>EndpointRepositoryTests.OneHostManySpellings</c>, in the same order.</summary>
    private static readonly string[] OneHostManySpellings =
    [
        "corvid.example",
        "Corvid.Example",
        "CORVID.EXAMPLE",
        "corvid.example.",
        "  corvid.example  ",
    ];

    private static GameEndpoint Endpoint(Guid gameId, string host, int port) =>
        new(gameId, host, port, EndpointKind.Telnet, Now.AddYears(-1), Now, EndpointState.Active);

    [Test]
    public async Task EverySpellingOfOneHostIsOneEndpointAndNotSeveral()
    {
        var gameId = Guid.NewGuid();
        var endpoints = new InMemoryEndpointRepository();

        foreach (var spelling in OneHostManySpellings)
        {
            await endpoints.UpsertAsync(Endpoint(gameId, spelling, 4201), CancellationToken.None);
        }

        await Assert.That(endpoints.Endpoints).HasCount(1);

        foreach (var spelling in OneHostManySpellings)
        {
            var found = await endpoints.ByAddressAsync(spelling, 4201, CancellationToken.None);

            await Assert.That(found).IsNotNull();
            await Assert.That(found!.GameId).IsEqualTo(gameId);
        }
    }

    [Test]
    public async Task WhatIsStoredIsCharacterForCharacterWhatHostNameProduces()
    {
        var endpoints = new InMemoryEndpointRepository();

        await endpoints.UpsertAsync(Endpoint(Guid.NewGuid(), "MUD.Example.ORG.", 4000), CancellationToken.None);

        await Assert.That(endpoints.Endpoints[0].Host).IsEqualTo(HostName.Normalize("MUD.Example.ORG."));
        await Assert.That(endpoints.Endpoints[0].Host).IsEqualTo("mud.example.org");
    }

    [Test]
    public async Task ADifferentHostIsStillADifferentEndpoint()
    {
        // The correction is one canonical form, not a lenient comparison — so once the spelling is
        // settled, two genuinely different names stay two endpoints.
        var gameId = Guid.NewGuid();
        var endpoints = new InMemoryEndpointRepository();

        await endpoints.UpsertAsync(Endpoint(gameId, "corvid.example", 4201), CancellationToken.None);
        await endpoints.UpsertAsync(Endpoint(gameId, "corvid.example.org", 4201), CancellationToken.None);

        await Assert.That(endpoints.Endpoints).HasCount(2);
    }
}
```

`tests/MUI.Discovery.Tests/HostNormalisationAgreementTests.cs` — the pin `HostName`'s doc comment
promises. `MUI.Discovery` is the one project that sees both `MUI.Catalog` and `MUI.Crawl`, so it is
the only place the two implementations of one rule can be compared at all:

```csharp
using MUI.Catalog;
using MUI.Crawl.Mssp;

namespace MUI.Discovery.Tests;

/// <summary>
/// <see cref="HostName.Normalize"/> and <see cref="MsspHost.Create"/> canonicalise a host twice,
/// because <c>MUI.Catalog</c> may never reference <c>MUI.Crawl</c> and both layers need the answer.
/// Two implementations of one rule drift unless something compares them, and if they drift then a
/// referral and an import disagree about what one host is — which is a duplicate endpoint, and then a
/// duplicate game (spec §7.3).
/// </summary>
public class HostNormalisationAgreementTests
{
    [Test]
    public async Task TheTwoImplementationsAgreeOnEveryHostEitherWillSee()
    {
        string[] hosts =
        [
            "mud.example.org",
            "MUD.Example.ORG",
            "MUD.EXAMPLE.ORG.",
            "  mud.example.org.  ",
            "2001:0DB8:0000:0000:0000:0000:0000:0001",
            "2001:DB8::1",
            "[2001:db8::1]",
            "203.0.113.7",
        ];

        foreach (var host in hosts)
        {
            var referral = MsspHost.Create(host, 4201);

            await Assert.That(referral).IsNotNull();
            await Assert.That(HostName.Normalize(host)).IsEqualTo(referral!.Host);
        }
    }
}
```

- [ ] **Step 7: Write the fixture builder**

`tests/MUI.Discovery.Tests/Support/ProbeFixtures.cs`:

```csharp
using MUI.Crawl;

using MUI.Crawl.Mssp;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// Hand-built <see cref="ProbeResult"/>s. Spec §6.5 and §13: these fixtures exercise every downstream
/// behaviour with no network anywhere, which is the property the whole layering exists to buy.
/// Plan 1's captured JSON deserialises into this same type, so a fixture here and a real capture are
/// interchangeable inputs.
/// </summary>
/// <remarks>
/// The <c>who</c> default is <see cref="WhoReading.NotAttempted"/> — the same default
/// <see cref="ProbeResult.Who"/> itself carries — so a fixture that says nothing about WHO stands for
/// a probe that never asked, and not for one that asked and failed. Task 15 turns on exactly that
/// difference, so a fixture meaning "we asked and could not read the answer" must say
/// <c>who: WhoReading.Unreadable</c> out loud.
/// </remarks>
public static class ProbeFixtures
{
    public static readonly DateTimeOffset At = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    public static MsspData Mssp(params (string Variable, string Value)[] variables) =>
        MsspData.From(variables.Select(pair =>
            new KeyValuePair<string, IReadOnlyList<string>>(pair.Variable, new[] { pair.Value })));

    public static ProbeResult Answered(
        MsspData? mssp = null,
        WhoReading? who = null,
        IReadOnlySet<string>? offered = null,
        string? banner = null,
        MsspTransport msspVia = MsspTransport.TelnetOption70,
        bool tlsObserved = false,
        PresenceAggregates? aggregates = null,
        DateTimeOffset? at = null) =>
        new()
        {
            Host = "corvid.example",
            Port = 4201,
            ObservedAt = at ?? At,
            Outcome = ProbeOutcome.Answered,
            OfferedOptions = offered ?? new HashSet<string>(StringComparer.Ordinal),
            Banner = banner,
            Who = who ?? WhoReading.NotAttempted,
            Mssp = mssp ?? MsspData.Empty,
            MsspVia = msspVia,
            TlsObserved = tlsObserved,
            Aggregates = aggregates,
            Elapsed = TimeSpan.FromSeconds(2),
        };

    public static ProbeResult Failed(string cause, DateTimeOffset? at = null) =>
        new()
        {
            Host = "corvid.example",
            Port = 4201,
            ObservedAt = at ?? At,
            Outcome = ProbeOutcome.Failed,
            Failure = new FailureDetail(cause),
            Elapsed = TimeSpan.FromSeconds(15),
        };
}
```

- [ ] **Step 8: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
```
Expected: PASS — 8 new tests (4 `FailureCauseMapTests`, 3 `InMemoryEndpointRepositoryTests`, 1
`HostNormalisationAgreementTests`) plus the existing `ReferralEdgeTests`.

- [ ] **Step 9: Commit**

```bash
git add src/MUI.Discovery tests/MUI.Discovery.Tests
git commit -m "feat(discovery): the failure-cause map, plus fakes and ProbeResult fixtures for the writers"
```

---

### Task 14: `FieldReconciler` — confirm, change, reject (spec §5.1)

**Files:**
- Create: `src/MUI.Discovery/Writers/FieldReconciler.cs`
- Create: `tests/MUI.Discovery.Tests/Support/ManualTimeProvider.cs`
- Create: `tests/MUI.Discovery.Tests/Writers/FieldReconcilerTests.cs`

**Interfaces:**
- Consumes: `IGameFieldRepository` (Task 12), `SourcePrecedence` (Task 4), `FieldRegistry`,
  `CapabilityFields` (Task 2), `FieldConfidences`, `GameField`, `FieldChange` (Task 3),
  `ProbeResult`, `MsspData`, `MsspVariables` (Plan 1, `MUI.Crawl.Mssp`), `ProbeFixtures`,
  `InMemoryGameFieldRepository` (Task 13).
- Produces:
  - `sealed record MUI.Discovery.Writers.FieldReconciliation(int Confirmed, int Changed, int Rejected)`
  - `sealed class MUI.Discovery.Writers.FieldReconciler(IGameFieldRepository fields, TimeProvider time)`
    with `Task<FieldReconciliation> ApplyAsync(Guid gameId, ProbeResult result, CancellationToken ct)`
  - `sealed class MUI.Discovery.Tests.Support.ManualTimeProvider(DateTimeOffset? start = null) : TimeProvider`
    with `void Advance(TimeSpan by)`

- [ ] **Step 1: Write the failing test**

`tests/MUI.Discovery.Tests/Writers/FieldReconcilerTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Discovery.Tests.Support;
using MUI.Discovery.Writers;

using MUI.Crawl.Mssp;

namespace MUI.Discovery.Tests.Writers;

/// <summary>
/// Spec §5.1: every probe does exactly one of two things to each field — confirm, or change — and a
/// third, reject, when the incoming source loses the precedence ladder to the incumbent.
/// </summary>
public class FieldReconcilerTests
{
    private static readonly Guid AGame = Guid.Parse("6f1d5b1e-0c4a-4a4e-9b7a-6a1d5c2f8b31");

    private static (FieldReconciler Reconciler, InMemoryGameFieldRepository Fields, ManualTimeProvider Time) Subject()
    {
        var time = new ManualTimeProvider(ProbeFixtures.At);
        var fields = new InMemoryGameFieldRepository();

        return (new FieldReconciler(fields, time), fields, time);
    }

    [Test]
    public async Task AGameWhoseGenreNeverMovesCostsOneRowForeverAndNotOnePerHour()
    {
        // The economy the whole §5.1 design is built on, asserted directly: twenty probes, one row,
        // and no change events, because nothing ever changed.
        var (reconciler, fields, time) = Subject();
        var result = ProbeFixtures.Answered(ProbeFixtures.Mssp(("GENRE", "Fantasy")));

        for (var probe = 0; probe < 20; probe++)
        {
            time.Advance(TimeSpan.FromHours(1));
            await reconciler.ApplyAsync(AGame, result, CancellationToken.None);
        }

        await Assert.That(fields.Fields.Count(f => f.Field == "GENRE")).IsEqualTo(1);
        await Assert.That(fields.Changes).IsEmpty();
    }

    [Test]
    public async Task AFirstSightingWritesTheRowAndNoChangeEvent()
    {
        // A change feed is "a table of events that actually happened" (§5.1). A field appearing for
        // the first time is the "newly discovered" feed's event, not this one's.
        var (reconciler, fields, _) = Subject();

        var outcome = await reconciler.ApplyAsync(
            AGame, ProbeFixtures.Answered(ProbeFixtures.Mssp(("GENRE", "Fantasy"))), CancellationToken.None);

        await Assert.That(outcome.Changed).IsEqualTo(1);
        await Assert.That(outcome.Confirmed).IsEqualTo(0);
        await Assert.That(fields.Changes).IsEmpty();
        await Assert.That(fields.Fields.Single().Value).IsEqualTo("Fantasy");
    }

    [Test]
    public async Task AConfirmationBumpsTheTimestampAndWritesNothingElse()
    {
        var (reconciler, fields, time) = Subject();
        var result = ProbeFixtures.Answered(ProbeFixtures.Mssp(("GENRE", "Fantasy")));

        await reconciler.ApplyAsync(AGame, result, CancellationToken.None);
        var firstSeen = fields.Fields.Single().FirstSeenAt;

        time.Advance(TimeSpan.FromDays(30));
        var outcome = await reconciler.ApplyAsync(AGame, result, CancellationToken.None);

        await Assert.That(outcome.Confirmed).IsEqualTo(1);
        await Assert.That(fields.Fields.Single().LastConfirmedAt).IsEqualTo(time.GetUtcNow());
        await Assert.That(fields.Fields.Single().FirstSeenAt).IsEqualTo(firstSeen);
        await Assert.That(fields.Changes).IsEmpty();
    }

    [Test]
    public async Task AChangeRewritesTheRowAndAppendsExactlyOneChangeEvent()
    {
        var (reconciler, fields, time) = Subject();

        await reconciler.ApplyAsync(
            AGame, ProbeFixtures.Answered(ProbeFixtures.Mssp(("GENRE", "Fantasy"))), CancellationToken.None);

        time.Advance(TimeSpan.FromDays(30));
        var outcome = await reconciler.ApplyAsync(
            AGame, ProbeFixtures.Answered(ProbeFixtures.Mssp(("GENRE", "Science Fiction"))), CancellationToken.None);

        await Assert.That(outcome.Changed).IsEqualTo(1);
        await Assert.That(fields.Fields.Single().Value).IsEqualTo("Science Fiction");
        await Assert.That(fields.Changes).HasCount(1);
        await Assert.That(fields.Changes[0].OldValue).IsEqualTo("Fantasy");
        await Assert.That(fields.Changes[0].NewValue).IsEqualTo("Science Fiction");
        await Assert.That(fields.Changes[0].Source).IsEqualTo(FieldSource.Mssp);
    }

    [Test]
    public async Task ALosingSourceIsRejectedAndLeavesTheIncumbentUntouched()
    {
        var (reconciler, fields, time) = Subject();

        // A staff correction outranks anything (§5.1).
        await fields.UpsertAsync(
            new GameField(AGame, "GENRE", "Social", FieldSource.Staff, FieldConfidence.Reported,
                ProbeFixtures.At, ProbeFixtures.At),
            CancellationToken.None);

        time.Advance(TimeSpan.FromDays(1));
        var outcome = await reconciler.ApplyAsync(
            AGame, ProbeFixtures.Answered(ProbeFixtures.Mssp(("GENRE", "Fantasy"))), CancellationToken.None);

        await Assert.That(outcome.Rejected).IsEqualTo(1);
        await Assert.That(fields.Fields.Single().Value).IsEqualTo("Social");
        await Assert.That(fields.Fields.Single().LastConfirmedAt).IsEqualTo(ProbeFixtures.At);
        await Assert.That(fields.Changes).IsEmpty();
    }

    [Test]
    public async Task ThePlayerCountIsNeverStoredAsAField()
    {
        // §5.1 says so outright: the count is not a GameField. It lives in §5.2's presence series,
        // and storing it here would write a change row every hour, for every game, for ever.
        var (reconciler, fields, _) = Subject();

        await reconciler.ApplyAsync(
            AGame, ProbeFixtures.Answered(ProbeFixtures.Mssp((MsspVariables.Players, "37"))), CancellationToken.None);

        await Assert.That(fields.Fields.Any(f => f.Field == MsspVariables.Players)).IsFalse();
    }

    [Test]
    public async Task UptimeIsNeverStoredAsAFieldEither()
    {
        // Same reason and a sharper edge: UPTIME moves on every single probe, so one stored row
        // becomes one field_change row per hour — precisely the cost §5.1 exists to avoid.
        var (reconciler, fields, _) = Subject();

        await reconciler.ApplyAsync(
            AGame, ProbeFixtures.Answered(ProbeFixtures.Mssp((MsspVariables.Uptime, "1750000000"))),
            CancellationToken.None);

        await Assert.That(fields.Fields.Any(f => f.Field == MsspVariables.Uptime)).IsFalse();
    }

    [Test]
    public async Task WhatTheHandshakeOfferedIsStoredSeparatelyFromWhatMsspClaimed()
    {
        // §3.1's whole argument: `GMCP 1` in MSSP is an assertion and the handshake is an
        // observation, and when they disagree that disagreement is the interesting fact.
        var (reconciler, fields, _) = Subject();

        await reconciler.ApplyAsync(
            AGame,
            ProbeFixtures.Answered(
                ProbeFixtures.Mssp(("GMCP", "1"), ("MXP", "1")),
                offered: new HashSet<string>(StringComparer.Ordinal) { "GMCP" }),
            CancellationToken.None);

        var measuredGmcp = fields.Fields.Single(f => f.Field == CapabilityFields.Measured("GMCP"));
        var declaredMxp = fields.Fields.Single(f => f.Field == CapabilityFields.Declared("MXP"));

        await Assert.That(measuredGmcp.Value).IsEqualTo("true");
        await Assert.That(measuredGmcp.Source).IsEqualTo(FieldSource.Handshake);
        await Assert.That(measuredGmcp.Confidence).IsEqualTo(FieldConfidence.Observed);

        await Assert.That(declaredMxp.Value).IsEqualTo("true");
        await Assert.That(declaredMxp.Source).IsEqualTo(FieldSource.Mssp);
        await Assert.That(declaredMxp.Confidence).IsEqualTo(FieldConfidence.Reported);

        // MXP was claimed and not offered, so there is no measured row for it and the page can say so.
        await Assert.That(fields.Fields.Any(f => f.Field == CapabilityFields.Measured("MXP"))).IsFalse();
    }

    [Test]
    public async Task ObservedTlsIsACapabilityLikeAnyOther()
    {
        var (reconciler, fields, _) = Subject();

        await reconciler.ApplyAsync(AGame, ProbeFixtures.Answered(tlsObserved: true), CancellationToken.None);

        await Assert.That(fields.Fields.Single(f => f.Field == CapabilityFields.Measured("TLS")).Value)
            .IsEqualTo("true");
    }

    [Test]
    public async Task TheConnectScreenIsStoredWithBannerProvenance()
    {
        // §6.2: display asset and fingerprint both, and §5.1 wants every displayed fact to carry how
        // it was obtained.
        var (reconciler, fields, _) = Subject();

        await reconciler.ApplyAsync(
            AGame, ProbeFixtures.Answered(banner: "Welcome to Corvid"), CancellationToken.None);

        var screen = fields.Fields.Single(f => f.Field == "connect_screen");

        await Assert.That(screen.Value).IsEqualTo("Welcome to Corvid");
        await Assert.That(screen.Source).IsEqualTo(FieldSource.Banner);
        await Assert.That(screen.Confidence).IsEqualTo(FieldConfidence.Inferred);
    }

    [Test]
    public async Task AFailedProbeReconcilesNothing()
    {
        // A probe that never got in has no opinion about any field, and confirming a field on the
        // strength of a timeout would make §5.6's ages lies.
        var (reconciler, fields, _) = Subject();

        var outcome = await reconciler.ApplyAsync(
            AGame, ProbeFixtures.Failed(ProbeFailureCauses.Timeout), CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(new FieldReconciliation(0, 0, 0));
        await Assert.That(fields.Fields).IsEmpty();
    }
}
```

Add the small clock the tests use, `tests/MUI.Discovery.Tests/Support/ManualTimeProvider.cs`:

```csharp
namespace MUI.Discovery.Tests.Support;

/// <summary>
/// A clock the test moves by hand, so "twenty probes an hour apart" is a loop and not a wait.
/// </summary>
/// <remarks>
/// Deliberately not named <c>FakeTimeProvider</c>, to avoid being mistaken for
/// <c>Microsoft.Extensions.Time.Testing.FakeTimeProvider</c>, which is a real type this project does
/// not reference. Plan 03 builds on the same file and the same name, extending it with a working
/// <c>CreateTimer</c>; whichever plan lands second extends this file rather than declaring a second
/// clock beside it.
/// </remarks>
public sealed class ManualTimeProvider(DateTimeOffset? start = null) : TimeProvider
{
    private DateTimeOffset _now = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0246: The type or namespace name 'FieldReconciler' could not be found`.

- [ ] **Step 3: Write the implementation**

`src/MUI.Discovery/Writers/FieldReconciler.cs`:

```csharp
namespace MUI.Discovery.Writers;

using MUI.Catalog;
using MUI.Crawl;
using MUI.Storage;

using MUI.Crawl.Mssp;

/// <summary>What one probe did to one game's fields.</summary>
public sealed record FieldReconciliation(int Confirmed, int Changed, int Rejected);

/// <summary>
/// Spec §5.1's writer. For each field a probe yields, exactly one of: <em>confirm</em> — bump
/// <c>last_confirmed_at</c> and write nothing else; <em>change</em> — update the row and append one
/// <see cref="FieldChange"/>; or <em>reject</em> — the incoming source lost the precedence ladder to
/// the incumbent, so nothing is written at all.
/// </summary>
/// <remarks>
/// The point of the confirm arm is cost: a game whose <c>GENRE</c> never moves costs one row for
/// ever, not one per hour. That is only true if the volatile MSSP variables stay out — see
/// <see cref="VolatileVariables"/>.
/// </remarks>
public sealed class FieldReconciler(IGameFieldRepository fields, TimeProvider time)
{
    /// <summary>
    /// MSSP variables that are <em>not</em> descriptive fields and must never be stored as one.
    /// </summary>
    /// <remarks>
    /// <c>PLAYERS</c> because §5.1 says outright that the player count is not a <c>GameField</c> — it
    /// lives in §5.2's presence series, where <c>who</c> outranks <c>mssp</c>. <c>UPTIME</c> because it
    /// moves on every probe: stored here it would append one <c>field_change</c> row per game per
    /// hour, for ever, which is exactly the cost §5.1's confirm/change split exists to avoid. Both are
    /// in <see cref="FieldRegistry"/> — <c>PLAYERS</c> is the anchor §5.6 calibrates from — and
    /// neither is stored.
    /// </remarks>
    public static IReadOnlySet<string> VolatileVariables { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MsspVariables.Players,
            MsspVariables.Uptime,
        };

    public async Task<FieldReconciliation> ApplyAsync(Guid gameId, ProbeResult result, CancellationToken ct)
    {
        // A probe that never got in has no opinion about any field. Confirming on the strength of a
        // timeout would make every age in §5.6 a lie.
        if (result.Outcome is not ProbeOutcome.Answered)
        {
            return new FieldReconciliation(0, 0, 0);
        }

        var now = time.GetUtcNow();
        var incumbents = (await fields.ForGameAsync(gameId, ct))
            .ToDictionary(field => field.Field, StringComparer.Ordinal);

        var confirmed = 0;
        var changed = 0;
        var rejected = 0;

        foreach (var (field, value, candidateSource) in Collect(result))
        {
            var confidence = FieldConfidences.For(candidateSource);

            if (!incumbents.TryGetValue(field, out var incumbent))
            {
                // A first sighting is not a change: there was no old value, and §9's change feed is
                // a table of events that actually happened. "Newly discovered" is a different feed.
                await fields.UpsertAsync(
                    new GameField(gameId, field, value, candidateSource, confidence, now, now), ct);
                changed++;
                continue;
            }

            if (!SourcePrecedence.Wins(candidateSource, incumbent.Source, field))
            {
                rejected++;
                continue;
            }

            if (incumbent.Source == candidateSource
                && string.Equals(incumbent.Value, value, StringComparison.Ordinal))
            {
                await fields.ConfirmAsync(gameId, field, now, ct);
                confirmed++;
                continue;
            }

            await fields.UpsertAsync(
                incumbent with
                {
                    Value = value,
                    Source = candidateSource,
                    Confidence = confidence,
                    LastConfirmedAt = now,
                },
                ct);
            await fields.AppendChangeAsync(
                new FieldChange(0, gameId, field, incumbent.Value, value, candidateSource, now), ct);
            changed++;
        }

        return new FieldReconciliation(confirmed, changed, rejected);
    }

    /// <summary>Every field this probe has an opinion about, with where that opinion came from.</summary>
    private static IEnumerable<(string Field, string Value, FieldSource Source)> Collect(ProbeResult result)
    {
        // Layer 1 (§6.1) — what the server actually offered. Observed, so it is stored under the
        // measured name and can never be overwritten by the game's own claim.
        foreach (var option in result.OfferedOptions)
        {
            yield return (CapabilityFields.Measured(option), "true", FieldSource.Handshake);
        }

        if (result.TlsObserved)
        {
            yield return (CapabilityFields.Measured("TLS"), "true", FieldSource.Handshake);
        }

        // Layer 2 (§6.2) — the connect screen, ANSI intact.
        if (!string.IsNullOrEmpty(result.Banner))
        {
            yield return ("connect_screen", result.Banner, FieldSource.Banner);
        }

        // Layer 4 (§6.4) — MSSP, official and unofficial variables alike. A game may emit anything;
        // the registry describes what it knows and does not gate what it does not.
        var capabilities = CapabilityFields.Names.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var variable in result.Mssp.OfficialNames.Concat(result.Mssp.UnofficialNames))
        {
            if (VolatileVariables.Contains(variable))
            {
                continue;
            }

            var value = result.Mssp.Default(variable);

            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var field = capabilities.Contains(variable)
                ? CapabilityFields.Declared(variable)
                : variable;

            // A capability flag is a claim about a boolean, so it is normalised to one; everything
            // else is stored as the game wrote it.
            var stored = capabilities.Contains(variable)
                ? (result.Mssp.Flag(variable) ?? false) ? "true" : "false"
                : value;

            yield return (field, stored, FieldSource.Mssp);
        }
    }
}
```

- [ ] **Step 4: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Discovery/Writers/FieldReconciler.cs tests/MUI.Discovery.Tests
git commit -m "feat(discovery): the field reconciler — confirm, change or reject, once per field"
```

---

### Task 15: `PresenceWriter` — the three states an hour can be in (spec §5.2, §5.4)

**The single most important correctness property in the system.** Conflating a measured zero, an
unmeasurable probe and a failed probe is, per `CLAUDE.md`, "the worst single bug this codebase could
ship".

**The unmeasurable row's `unmeasurable_reason` is derived from `WhoReading.Confidence`, never from
`MsspVia`.** An earlier draft of this task inferred "did we ask WHO?" from whether MSSP had answered,
because `WhoReading.Unread` was `new(WhoConfidence.Unknown)` and record equality made *never asked*
and *asked and could not read the answer* the same value. Plan 01 fixed that at source (contract
addendum §3): `WhoConfidence.NotAttempted` is now the enum's zero, `WhoReading` exposes the
`NotAttempted` and `Unreadable` statics and a `WasAttempted` predicate, and `WhoReading.Unread` is
gone. So this writer asks the reading what happened and takes the answer. That matters because the
old inference was **wrong on a common shape**: a game that answers MSSP without a `PLAYERS` variable
*and* has a customised `DOING` header got `no_mssp_players` when §5.4 calls it `who_unparseable`.

**Files:**
- Create: `src/MUI.Discovery/Writers/PresenceWriter.cs`
- Create: `tests/MUI.Discovery.Tests/Writers/PresenceWriterTests.cs`

**Interfaces:**
- Consumes: `IPresenceRepository` (Task 8), `PresenceSample`, `PresenceSource`, `UnmeasurableReasons`
  (Task 3), `ProbeResult`, `WhoReading` (its `NotAttempted`/`Unreadable` statics, `WasAttempted` and
  `HasCount`), `WhoConfidence` (four members, `NotAttempted` first), `PresenceAggregates` (Plan 1),
  `ProbeFixtures`, `InMemoryPresenceRepository` (Task 13). **Not `MsspTransport`** — see below.
- Produces:
  - `enum MUI.Discovery.Writers.PresenceOutcome { Counted, Unmeasurable, NoSample }`
  - `sealed class MUI.Discovery.Writers.PresenceWriter(IPresenceRepository presence)` with
    `Task<PresenceOutcome> ApplyAsync(Guid gameId, ProbeResult result, CancellationToken ct)`

- [ ] **Step 1: Write the failing test**

`tests/MUI.Discovery.Tests/Writers/PresenceWriterTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Discovery.Tests.Support;
using MUI.Discovery.Writers;

using MUI.Crawl.Mssp;

namespace MUI.Discovery.Tests.Writers;

/// <summary>
/// Spec §5.4's table, one test per row, plus the bug it names. Zero players is not the same fact as
/// unreachable, and neither is "we got in but could not count".
/// </summary>
public class PresenceWriterTests
{
    private static readonly Guid AGame = Guid.Parse("6f1d5b1e-0c4a-4a4e-9b7a-6a1d5c2f8b31");

    private static (PresenceWriter Writer, InMemoryPresenceRepository Presence) Subject()
    {
        var presence = new InMemoryPresenceRepository();

        return (new PresenceWriter(presence), presence);
    }

    [Test]
    public async Task RowOne_ProbeSucceededAndACountWasObtained()
    {
        var (writer, presence) = Subject();

        var outcome = await writer.ApplyAsync(
            AGame,
            ProbeFixtures.Answered(who: new WhoReading(WhoConfidence.Count, Count: 12)),
            CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(PresenceOutcome.Counted);
        await Assert.That(presence.Samples).HasCount(1);
        await Assert.That(presence.Samples[0].Count).IsEqualTo(12);
        await Assert.That(presence.Samples[0].Source).IsEqualTo(PresenceSource.Who);
        await Assert.That(presence.Samples[0].UnmeasurableReason).IsNull();
    }

    [Test]
    public async Task RowOne_AMeasuredZeroIsAFilledCellAndNotAnAbsence()
    {
        // "It means we got in and nobody was there, which is a real and useful fact about a game."
        var (writer, presence) = Subject();

        var outcome = await writer.ApplyAsync(
            AGame,
            ProbeFixtures.Answered(who: new WhoReading(WhoConfidence.Count, Count: 0)),
            CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(PresenceOutcome.Counted);
        await Assert.That(presence.Samples).HasCount(1);
        await Assert.That(presence.Samples[0].Count).IsEqualTo(0);
        await Assert.That(presence.Samples[0].UnmeasurableReason).IsNull();
    }

    [Test]
    public async Task RowTwo_ProbeSucceededAndNoCountWasObtainable()
    {
        // We never asked WHO — an owner override, or ProbeOptions.SendWho off — and MSSP answered
        // without a PLAYERS variable. There is nothing to reproach the WHO parser with, so the
        // reason names the thing that was actually missing.
        var (writer, presence) = Subject();

        var outcome = await writer.ApplyAsync(
            AGame,
            ProbeFixtures.Answered(
                ProbeFixtures.Mssp(("GENRE", "Fantasy")), who: WhoReading.NotAttempted),
            CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(PresenceOutcome.Unmeasurable);
        await Assert.That(presence.Samples).HasCount(1);
        await Assert.That(presence.Samples[0].Count).IsNull();
        await Assert.That(presence.Samples[0].UnmeasurableReason).IsEqualTo(UnmeasurableReasons.NoMsspPlayers);
    }

    [Test]
    public async Task RowThree_AFailedProbeWritesNoPresenceRowAtAll()
    {
        var (writer, presence) = Subject();

        var outcome = await writer.ApplyAsync(
            AGame, ProbeFixtures.Failed(ProbeFailureCauses.Timeout), CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(PresenceOutcome.NoSample);
        await Assert.That(presence.Samples).IsEmpty();
    }

    [Test]
    public async Task AGameWhoseDoingHeaderIsCustomisedPastOurParserIsNotIndistinguishableFromDowntime()
    {
        // The bug §5.4 names. A running game with a softcode-rewritten DOING header yields
        // WhoConfidence.Unknown and no MSSP PLAYERS — and if that wrote nothing, the heatmap would
        // draw it permanently dark while it ran fine. It must write a row, and that row must be
        // distinguishable from the failed-probe case, which writes none.
        //
        // The fixture keeps the default MsspVia (TelnetOption70) deliberately: MSSP answered, and it
        // simply had no PLAYERS. The reason must still be who_unparseable, because it is the WHO
        // reading's own confidence that says what went wrong — not which transport MSSP arrived on.
        var (writer, presence) = Subject();

        var running = await writer.ApplyAsync(
            AGame,
            ProbeFixtures.Answered(who: WhoReading.Unreadable),
            CancellationToken.None);
        var dark = await writer.ApplyAsync(
            AGame, ProbeFixtures.Failed(ProbeFailureCauses.Refused), CancellationToken.None);

        await Assert.That(running).IsEqualTo(PresenceOutcome.Unmeasurable);
        await Assert.That(dark).IsEqualTo(PresenceOutcome.NoSample);
        await Assert.That(presence.Samples).HasCount(1);
        await Assert.That(presence.Samples[0].UnmeasurableReason).IsEqualTo(UnmeasurableReasons.WhoUnparseable);
        await Assert.That(presence.Samples[0].Source).IsEqualTo(PresenceSource.Who);
    }

    [Test]
    public async Task NeverHavingAskedAndAskingAndFailingAreDifferentReasons()
    {
        // The pin for the fix Plan 01 made at source (gap 10). Both probes are identical except for
        // the WHO reading: same MSSP transport, same MSSP variables, no PLAYERS in either. Before
        // WhoConfidence.NotAttempted existed these two were the same value and this test could not
        // have been written; if the two reasons ever coincide again, the distinction has been lost.
        var (writer, presence) = Subject();

        var neverAsked = await writer.ApplyAsync(
            AGame,
            ProbeFixtures.Answered(
                ProbeFixtures.Mssp(("GENRE", "Fantasy")), who: WhoReading.NotAttempted),
            CancellationToken.None);
        var askedAndFailed = await writer.ApplyAsync(
            AGame,
            ProbeFixtures.Answered(
                ProbeFixtures.Mssp(("GENRE", "Fantasy")), who: WhoReading.Unreadable),
            CancellationToken.None);

        await Assert.That(neverAsked).IsEqualTo(PresenceOutcome.Unmeasurable);
        await Assert.That(askedAndFailed).IsEqualTo(PresenceOutcome.Unmeasurable);

        await Assert.That(WhoReading.NotAttempted.WasAttempted).IsFalse();
        await Assert.That(WhoReading.Unreadable.WasAttempted).IsTrue();
        await Assert.That(WhoReading.NotAttempted).IsNotEqualTo(WhoReading.Unreadable);

        await Assert.That(presence.Samples).HasCount(2);
        await Assert.That(presence.Samples[0].UnmeasurableReason)
            .IsEqualTo(UnmeasurableReasons.NoMsspPlayers);
        await Assert.That(presence.Samples[1].UnmeasurableReason)
            .IsEqualTo(UnmeasurableReasons.WhoUnparseable);
        await Assert.That(presence.Samples[0].UnmeasurableReason)
            .IsNotEqualTo(presence.Samples[1].UnmeasurableReason);
    }

    [Test]
    public async Task AWhoWeReadButCouldNotCountIsAnAttemptedWhoAndNotAMissingOne()
    {
        // The third arm: the parser got far enough to classify the answer but produced no number,
        // so HasCount is false while WasAttempted is true. That is a WHO failure, not an absent WHO.
        var (writer, presence) = Subject();

        var outcome = await writer.ApplyAsync(
            AGame,
            ProbeFixtures.Answered(who: new WhoReading(WhoConfidence.Count)),
            CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(PresenceOutcome.Unmeasurable);
        await Assert.That(presence.Samples[0].Count).IsNull();
        await Assert.That(presence.Samples[0].UnmeasurableReason).IsEqualTo(UnmeasurableReasons.WhoUnparseable);
    }

    [Test]
    public async Task WhoOutranksMsspForTheCount()
    {
        // §5.1: the player count does not use the field precedence ladder. It lives in §5.2, where
        // WHO wins because it is live rather than whatever the codebase last cached (§6.3).
        var (writer, presence) = Subject();

        await writer.ApplyAsync(
            AGame,
            ProbeFixtures.Answered(
                ProbeFixtures.Mssp((MsspVariables.Players, "99")),
                who: new WhoReading(WhoConfidence.Count, Count: 12)),
            CancellationToken.None);

        await Assert.That(presence.Samples[0].Count).IsEqualTo(12);
        await Assert.That(presence.Samples[0].Source).IsEqualTo(PresenceSource.Who);
    }

    [Test]
    public async Task MsspPlayersIsUsedWhenWhoCouldNotBeRead()
    {
        // A declared count is still a count. The WHO reading having failed changes where the number
        // came from and therefore its label — it does not turn a filled cell into a hatched one.
        var (writer, presence) = Subject();

        await writer.ApplyAsync(
            AGame,
            ProbeFixtures.Answered(
                ProbeFixtures.Mssp((MsspVariables.Players, "99")), who: WhoReading.Unreadable),
            CancellationToken.None);

        await Assert.That(presence.Samples[0].Count).IsEqualTo(99);
        await Assert.That(presence.Samples[0].Source).IsEqualTo(PresenceSource.Mssp);
        await Assert.That(presence.Samples[0].UnmeasurableReason).IsNull();
    }

    [Test]
    public async Task AggregatesAreStoredOnlyWhenTheParserReachedPerPlayerConfidence()
    {
        // §5.2/§6.3, and §11: hashes, never names.
        var (writer, presence) = Subject();
        var aggregates = new PresenceAggregates("epoch-1", ["aGFzaA"], [1, 0, 0, 0, 0, 0], [0, 1, 0, 0, 0, 0]);

        await writer.ApplyAsync(
            AGame,
            ProbeFixtures.Answered(who: new WhoReading(WhoConfidence.PerPlayer, 1, 1), aggregates: aggregates),
            CancellationToken.None);
        await writer.ApplyAsync(
            AGame,
            ProbeFixtures.Answered(who: new WhoReading(WhoConfidence.Count, Count: 1)),
            CancellationToken.None);

        await Assert.That(presence.Samples[0].AggregatesJson).IsNotNull();
        await Assert.That(presence.Samples[0].AggregatesJson!).Contains("epoch-1");
        await Assert.That(presence.Samples[1].AggregatesJson).IsNull();
    }

    [Test]
    public async Task ThePartitionForTheSamplesMonthIsEnsuredBeforeItIsWritten()
    {
        // A missing partition is an insert error, and a crawler is not entitled to lose a
        // measurement to a calendar rollover.
        var (writer, presence) = Subject();

        await writer.ApplyAsync(
            AGame,
            ProbeFixtures.Answered(who: new WhoReading(WhoConfidence.Count, Count: 3)),
            CancellationToken.None);

        await Assert.That(presence.EnsuredPartitions).IsEquivalentTo(new[] { ProbeFixtures.At });
    }

    [Test]
    public async Task AFailedProbeDoesNotEvenTouchThePartitions()
    {
        var (writer, presence) = Subject();

        await writer.ApplyAsync(AGame, ProbeFixtures.Failed(ProbeFailureCauses.Dns), CancellationToken.None);

        await Assert.That(presence.EnsuredPartitions).IsEmpty();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0246: The type or namespace name 'PresenceWriter' could not be found`.

- [ ] **Step 3: Write the implementation**

`src/MUI.Discovery/Writers/PresenceWriter.cs`:

```csharp
namespace MUI.Discovery.Writers;

using System.Text.Json;

using MUI.Catalog;
using MUI.Crawl;
using MUI.Storage;

/// <summary>Which of §5.4's three rows this probe was.</summary>
public enum PresenceOutcome
{
    /// <summary>Filled cell. Includes a measured zero.</summary>
    Counted,

    /// <summary>Hatched cell — probed, unmeasurable.</summary>
    Unmeasurable,

    /// <summary>Empty cell — not reachable. The availability writer records the transition instead.</summary>
    NoSample,
}

/// <summary>
/// Spec §5.4, and the single most important correctness property in this system.
/// </summary>
/// <remarks>
/// <para>Three states, three renderings, and they must never collapse into two:</para>
/// <list type="bullet">
/// <item>probe succeeded and a count was obtained — a sample with a number, <em>including a measured
/// zero</em>, which is a filled cell meaning we got in and nobody was there;</item>
/// <item>probe succeeded and no count was obtainable — a sample with a null count and a reason, which
/// is a hatched cell;</item>
/// <item>probe failed — <em>no presence row at all</em>, and an availability transition instead.</item>
/// </list>
/// <para>
/// The middle case is the one the first cut of the spec missed. A successful probe whose <c>DOING</c>
/// header has been customised past our parser writes nothing under that reading, which is identical
/// on screen to downtime: the game renders as permanently dark while running perfectly.
/// </para>
/// <para>
/// Which <em>reason</em> the middle case carries is read off <see cref="WhoReading.Confidence"/> and
/// nothing else. That is only possible because Plan 01 gave <see cref="WhoConfidence"/> a
/// <c>NotAttempted</c> member: while "we never asked" and "we asked and could not read the answer"
/// were the same value, this writer had to guess the difference from <c>MsspVia</c> — which is a fact
/// about a different protocol and was wrong whenever MSSP answered but omitted <c>PLAYERS</c>.
/// </para>
/// </remarks>
public sealed class PresenceWriter(IPresenceRepository presence)
{
    public async Task<PresenceOutcome> ApplyAsync(Guid gameId, ProbeResult result, CancellationToken ct)
    {
        // Row three. Nothing is written here, and nothing may be: a failed probe's gap is what makes
        // downtime legible in the heatmap.
        if (result.Outcome is not ProbeOutcome.Answered)
        {
            return PresenceOutcome.NoSample;
        }

        await presence.EnsurePartitionAsync(result.ObservedAt, ct);

        // Row one, WHO. §5.1 is explicit that the count does not use the field precedence ladder:
        // it lives here, and `who` outranks `mssp` because it is live rather than whatever the
        // codebase last cached (§6.3).
        if (result.Who.HasCount)
        {
            await presence.AppendAsync(
                new PresenceSample(
                    gameId, result.ObservedAt, result.Who.Count, PresenceSource.Who, null, AggregatesJson(result)),
                ct);

            return PresenceOutcome.Counted;
        }

        // Row one, MSSP. Labelled as such, because the site says where every number came from.
        if (result.Mssp.Players is { } declared)
        {
            await presence.AppendAsync(
                new PresenceSample(gameId, result.ObservedAt, declared, PresenceSource.Mssp, null, null), ct);

            return PresenceOutcome.Counted;
        }

        // Row two, and the reason is *derived* rather than guessed. Neither measurement produced a
        // number, so the question is which one we are entitled to blame — and the WHO reading says
        // so itself:
        //
        //   attempted (Unknown, or a confidence with no count) => we asked and could not read the
        //     answer. That is §5.4's middle row by name: who_unparseable.
        //   not attempted                                      => there is nothing to blame the WHO
        //     parser for. What was missing is a declared count: no_mssp_players.
        //
        // MsspVia deliberately plays no part. It reports which transport MSSP arrived on, which is a
        // fact about a different protocol; reading intent out of it was this writer's workaround
        // while WhoConfidence had no NotAttempted member, and it mislabelled every probe where MSSP
        // answered without a PLAYERS variable. See the plan's gap table, entry 10.
        var attemptedWho = result.Who.WasAttempted;
        var reason = attemptedWho ? UnmeasurableReasons.WhoUnparseable : UnmeasurableReasons.NoMsspPlayers;
        var source = attemptedWho ? PresenceSource.Who : PresenceSource.Mssp;

        await presence.AppendAsync(
            new PresenceSample(gameId, result.ObservedAt, null, source, reason, null), ct);

        return PresenceOutcome.Unmeasurable;
    }

    /// <summary>
    /// §5.2 and §11: idle buckets, session lengths and a unique-player estimate from salted rotating
    /// hashes — populated only when the WHO parser reached per-player confidence, and never a name.
    /// </summary>
    private static string? AggregatesJson(ProbeResult result) =>
        result.Aggregates is null ? null : JsonSerializer.Serialize(result.Aggregates);
}
```

- [ ] **Step 4: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Discovery/Writers/PresenceWriter.cs tests/MUI.Discovery.Tests/Writers/PresenceWriterTests.cs
git commit -m "feat(discovery): the presence writer, keeping the three states an hour can be in apart"
```

---

### Task 16: `AvailabilityWriter` — intervals, and only a cause change writes a transition (spec §5.3)

**Files:**
- Create: `src/MUI.Discovery/Writers/AvailabilityWriter.cs`
- Create: `tests/MUI.Discovery.Tests/Writers/AvailabilityWriterTests.cs`

**Interfaces:**
- Consumes: `IAvailabilityRepository` (Task 9), `AvailabilityState`, `FailureCause` (Task 3),
  `FailureCauseMap` (Task 13), `ProbeResult`, `ProbeFailureCauses` (Plan 1), `ProbeFixtures`,
  `InMemoryAvailabilityRepository` (Task 13).
- Produces:
  - `sealed record MUI.Discovery.Writers.AvailabilityOutcome(AvailabilityState State, FailureCause Cause, bool OpenedNewInterval)`
  - `sealed class MUI.Discovery.Writers.AvailabilityWriter(IAvailabilityRepository availability)` with
    `Task<AvailabilityOutcome> ApplyAsync(Guid gameId, ProbeResult result, CancellationToken ct)`

- [ ] **Step 1: Write the failing test**

`tests/MUI.Discovery.Tests/Writers/AvailabilityWriterTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Discovery.Tests.Support;
using MUI.Discovery.Writers;

namespace MUI.Discovery.Tests.Writers;

/// <summary>
/// Spec §5.3: each probe either extends the open interval or closes it and opens a new one, and only
/// a cause change writes a transition.
/// </summary>
public class AvailabilityWriterTests
{
    private static readonly Guid AGame = Guid.Parse("6f1d5b1e-0c4a-4a4e-9b7a-6a1d5c2f8b31");

    private static (AvailabilityWriter Writer, InMemoryAvailabilityRepository Availability) Subject()
    {
        var availability = new InMemoryAvailabilityRepository();

        return (new AvailabilityWriter(availability), availability);
    }

    [Test]
    public async Task AHundredConsecutiveTimeoutsAreOneInterval()
    {
        // The whole reason §5.3 stores intervals rather than samples, asserted directly.
        var (writer, availability) = Subject();

        for (var probe = 0; probe < 100; probe++)
        {
            await writer.ApplyAsync(
                AGame,
                ProbeFixtures.Failed(ProbeFailureCauses.Timeout, ProbeFixtures.At.AddHours(probe)),
                CancellationToken.None);
        }

        await Assert.That(availability.Intervals).HasCount(1);
        await Assert.That(availability.Intervals[0].State).IsEqualTo(AvailabilityState.Unreachable);
        await Assert.That(availability.Intervals[0].Cause).IsEqualTo(FailureCause.Timeout);
        await Assert.That(availability.Intervals[0].ToAt).IsNull();
    }

    [Test]
    public async Task OnlyTheFirstOfARunOfIdenticalProbesReportsANewInterval()
    {
        var (writer, _) = Subject();

        var first = await writer.ApplyAsync(
            AGame, ProbeFixtures.Failed(ProbeFailureCauses.Timeout), CancellationToken.None);
        var second = await writer.ApplyAsync(
            AGame, ProbeFixtures.Failed(ProbeFailureCauses.Timeout, ProbeFixtures.At.AddHours(6)),
            CancellationToken.None);

        await Assert.That(first.OpenedNewInterval).IsTrue();
        await Assert.That(second.OpenedNewInterval).IsFalse();
    }

    [Test]
    public async Task AChangeOfCauseClosesTheOldIntervalAndOpensANewOne()
    {
        // A game that stops resolving and starts refusing has told us something, and §9's feeds and
        // §5.3's history both want to know when.
        var (writer, availability) = Subject();

        await writer.ApplyAsync(AGame, ProbeFixtures.Failed(ProbeFailureCauses.Dns), CancellationToken.None);
        var transition = ProbeFixtures.At.AddDays(2);
        var outcome = await writer.ApplyAsync(
            AGame, ProbeFixtures.Failed(ProbeFailureCauses.Refused, transition), CancellationToken.None);

        await Assert.That(outcome.OpenedNewInterval).IsTrue();
        await Assert.That(availability.Intervals).HasCount(2);
        await Assert.That(availability.Intervals[0].ToAt).IsEqualTo(transition);
        await Assert.That(availability.Intervals[0].Cause).IsEqualTo(FailureCause.Dns);
        await Assert.That(availability.Intervals[1].FromAt).IsEqualTo(transition);
        await Assert.That(availability.Intervals[1].Cause).IsEqualTo(FailureCause.Refused);
        await Assert.That(availability.Intervals[1].ToAt).IsNull();
    }

    [Test]
    public async Task AnAnsweringProbeOpensAReachableIntervalWithNoCause()
    {
        var (writer, availability) = Subject();

        var outcome = await writer.ApplyAsync(AGame, ProbeFixtures.Answered(), CancellationToken.None);

        await Assert.That(outcome.State).IsEqualTo(AvailabilityState.Reachable);
        await Assert.That(outcome.Cause).IsEqualTo(FailureCause.None);
        await Assert.That(availability.Intervals).HasCount(1);
    }

    [Test]
    public async Task AGameComingBackClosesTheOutageAtTheMomentItAnswered()
    {
        var (writer, availability) = Subject();

        await writer.ApplyAsync(AGame, ProbeFixtures.Failed(ProbeFailureCauses.Timeout), CancellationToken.None);
        var back = ProbeFixtures.At.AddDays(40);
        await writer.ApplyAsync(AGame, ProbeFixtures.Answered(at: back), CancellationToken.None);

        await Assert.That(availability.Intervals).HasCount(2);
        await Assert.That(availability.Intervals[0].ToAt).IsEqualTo(back);
        await Assert.That(availability.Intervals[1].State).IsEqualTo(AvailabilityState.Reachable);
        await Assert.That(AvailabilityArithmetic.LongestOutage(availability.Intervals, back).TotalDays)
            .IsEqualTo(40).Within(0.01);
    }

    [Test]
    public async Task AStalledHandshakeIsDegradedRatherThanUnreachable()
    {
        // §5.3's third state. handshake_stalled is the one failure where the socket answered: we
        // reached the game and the session did not complete, which is a different fact from "we
        // never got a connection".
        var (writer, _) = Subject();

        var outcome = await writer.ApplyAsync(
            AGame, ProbeFixtures.Failed(ProbeFailureCauses.HandshakeStalled), CancellationToken.None);

        await Assert.That(outcome.State).IsEqualTo(AvailabilityState.Degraded);
        await Assert.That(outcome.Cause).IsEqualTo(FailureCause.HandshakeStalled);
    }

    [Test]
    public async Task EveryOtherFailureIsUnreachable()
    {
        foreach (var cause in new[]
                 {
                     ProbeFailureCauses.Dns, ProbeFailureCauses.Refused,
                     ProbeFailureCauses.Tls, ProbeFailureCauses.Timeout, ProbeFailureCauses.Unknown,
                 })
        {
            var (writer, _) = Subject();

            var outcome = await writer.ApplyAsync(AGame, ProbeFixtures.Failed(cause), CancellationToken.None);

            await Assert.That(outcome.State).IsEqualTo(AvailabilityState.Unreachable);
        }
    }

    [Test]
    public async Task AFailureWithNoDetailIsUnknownRatherThanAGuess()
    {
        var (writer, _) = Subject();
        var result = ProbeFixtures.Failed(ProbeFailureCauses.Timeout) with { Failure = null };

        var outcome = await writer.ApplyAsync(AGame, result, CancellationToken.None);

        await Assert.That(outcome.Cause).IsEqualTo(FailureCause.Unknown);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0246: The type or namespace name 'AvailabilityWriter' could not be found`.

- [ ] **Step 3: Write the implementation**

`src/MUI.Discovery/Writers/AvailabilityWriter.cs`:

```csharp
namespace MUI.Discovery.Writers;

using MUI.Catalog;
using MUI.Crawl;
using MUI.Storage;

/// <summary>What one probe did to a game's availability history.</summary>
public sealed record AvailabilityOutcome(AvailabilityState State, FailureCause Cause, bool OpenedNewInterval);

/// <summary>
/// Spec §5.3's writer. Each probe either extends the open interval — by doing nothing at all — or
/// closes it and opens a new one. <strong>Only a cause change writes a transition</strong>: a hundred
/// consecutive timeouts are one interval, which is what makes "reachable over 90 days" and "longest
/// outage" arithmetic over a handful of rows rather than over twenty-six thousand samples.
/// </summary>
public sealed class AvailabilityWriter(IAvailabilityRepository availability)
{
    public async Task<AvailabilityOutcome> ApplyAsync(Guid gameId, ProbeResult result, CancellationToken ct)
    {
        var (state, cause) = Classify(result);
        var open = await availability.OpenIntervalAsync(gameId, ct);

        if (open is not null && open.State == state && open.Cause == cause)
        {
            // Nothing happened that the history has not already recorded. Extending an open interval
            // costs no write, which is the point.
            return new AvailabilityOutcome(state, cause, OpenedNewInterval: false);
        }

        if (open is not null)
        {
            await availability.CloseAsync(open.Id, result.ObservedAt, ct);
        }

        await availability.OpenAsync(gameId, state, cause, result.ObservedAt, ct);

        return new AvailabilityOutcome(state, cause, OpenedNewInterval: true);
    }

    private static (AvailabilityState State, FailureCause Cause) Classify(ProbeResult result)
    {
        if (result.Outcome is ProbeOutcome.Answered)
        {
            return (AvailabilityState.Reachable, FailureCause.None);
        }

        var cause = FailureCauseMap.From(result.Failure?.Cause ?? ProbeFailureCauses.Unknown);

        // §5.3 names three states and never says what produces the middle one. This is the reading
        // taken: handshake_stalled is the single failure where the socket answered — we reached the
        // game and the session did not complete — so it is degraded. Every other cause means we
        // never got a usable connection at all, which is unreachable.
        return cause is FailureCause.HandshakeStalled
            ? (AvailabilityState.Degraded, cause)
            : (AvailabilityState.Unreachable, cause);
    }
}
```

- [ ] **Step 4: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Discovery/Writers/AvailabilityWriter.cs tests/MUI.Discovery.Tests/Writers/AvailabilityWriterTests.cs
git commit -m "feat(discovery): availability as intervals, with only a cause change writing a transition"
```

---

### Task 17: `ProbeIngestor` — the §6.5 seam

**Files:**
- Create: `src/MUI.Discovery/Writers/ProbeIngestor.cs`
- Create: `tests/MUI.Discovery.Tests/Writers/ProbeIngestorTests.cs`

**Interfaces:**
- Consumes: `FieldReconciler`, `FieldReconciliation` (Task 14), `PresenceWriter`, `PresenceOutcome`
  (Task 15), `AvailabilityWriter`, `AvailabilityOutcome` (Task 16), the fakes and fixtures (Task 13).
- Produces:
  - `sealed record MUI.Discovery.Writers.IngestOutcome(FieldReconciliation Fields, PresenceOutcome Presence, AvailabilityOutcome Availability)`
  - `sealed class MUI.Discovery.Writers.ProbeIngestor(FieldReconciler fields, PresenceWriter presence, AvailabilityWriter availability)`
    with `Task<IngestOutcome> IngestAsync(Guid gameId, ProbeResult result, CancellationToken ct)`

**What may be handed to this seam, and what may not.** §7.2 gained a subsection on `main` (`dfff339`,
not yet on this branch) putting the SSRF gate on the *resolved address* rather than the name, and one
of its bullets is a fact about **this** class's input: **a refusal writes no availability sample.** We
declined to dial; we did not measure; recording it as downtime would put our own security policy into
a game's public reachability history — §5.4's unparseable-WHO-as-zero-players lie, one table over.

Nothing in this plan changes as a result, and that is the finding rather than an omission.
`ProbeOutcome` has exactly two members and both of them mean the socket was opened, so a refusal has
no representation a `ProbeResult` can carry and cannot reach these writers by any honest route. The
one way it *could* arrive is dressed as `ProbeResult.Failed(ProbeFailureCauses.Refused, …)` — note
that `FailureCause.Refused` means the far end sent an RST, and reusing it for a policy refusal would
be a second lie on top of the first. This class cannot tell the two apart after the fact, so the guard
belongs to whatever owns the dial (Plans 01 and 03), and the invariant is written into
`ProbeIngestor`'s own doc comment so an implementer meets it at the seam it constrains.

- [ ] **Step 1: Write the failing test**

`tests/MUI.Discovery.Tests/Writers/ProbeIngestorTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Crawl;
using MUI.Discovery.Tests.Support;
using MUI.Discovery.Writers;

using MUI.Crawl.Mssp;

namespace MUI.Discovery.Tests.Writers;

/// <summary>
/// Spec §6.5's seam: one ProbeResult fans out to the three writers, and none of them knows a socket
/// exists. This whole file runs on hand-built fixtures with no network and no database.
/// </summary>
public class ProbeIngestorTests
{
    private static readonly Guid AGame = Guid.Parse("6f1d5b1e-0c4a-4a4e-9b7a-6a1d5c2f8b31");

    private sealed record Rig(
        ProbeIngestor Ingestor,
        InMemoryGameFieldRepository Fields,
        InMemoryPresenceRepository Presence,
        InMemoryAvailabilityRepository Availability);

    private static Rig Subject()
    {
        var fields = new InMemoryGameFieldRepository();
        var presence = new InMemoryPresenceRepository();
        var availability = new InMemoryAvailabilityRepository();
        var time = new ManualTimeProvider(ProbeFixtures.At);

        return new Rig(
            new ProbeIngestor(
                new FieldReconciler(fields, time),
                new PresenceWriter(presence),
                new AvailabilityWriter(availability)),
            fields,
            presence,
            availability);
    }

    [Test]
    public async Task AGoodProbeReachesAllThreeWriters()
    {
        var rig = Subject();

        var outcome = await rig.Ingestor.IngestAsync(
            AGame,
            ProbeFixtures.Answered(
                ProbeFixtures.Mssp(("GENRE", "Fantasy")),
                who: new WhoReading(WhoConfidence.Count, Count: 7)),
            CancellationToken.None);

        await Assert.That(outcome.Fields.Changed).IsEqualTo(1);
        await Assert.That(outcome.Presence).IsEqualTo(PresenceOutcome.Counted);
        await Assert.That(outcome.Availability.State).IsEqualTo(AvailabilityState.Reachable);

        await Assert.That(rig.Fields.Fields).HasCount(1);
        await Assert.That(rig.Presence.Samples).HasCount(1);
        await Assert.That(rig.Availability.Intervals).HasCount(1);
    }

    [Test]
    public async Task AFailedProbeWritesAnAvailabilityTransitionAndNothingElse()
    {
        // The three rules of §5.4 in one assertion: no field touched, no presence row, one interval.
        var rig = Subject();

        var outcome = await rig.Ingestor.IngestAsync(
            AGame, ProbeFixtures.Failed(ProbeFailureCauses.Refused), CancellationToken.None);

        await Assert.That(outcome.Fields).IsEqualTo(new FieldReconciliation(0, 0, 0));
        await Assert.That(outcome.Presence).IsEqualTo(PresenceOutcome.NoSample);
        await Assert.That(outcome.Availability.State).IsEqualTo(AvailabilityState.Unreachable);

        await Assert.That(rig.Fields.Fields).IsEmpty();
        await Assert.That(rig.Presence.Samples).IsEmpty();
        await Assert.That(rig.Availability.Intervals).HasCount(1);
    }

    [Test]
    public async Task RepeatedIdenticalProbesAccumulatePresenceButNotFieldsOrIntervals()
    {
        // Presence is a series and grows; fields and availability are current state and do not.
        var rig = Subject();
        var result = ProbeFixtures.Answered(
            ProbeFixtures.Mssp(("GENRE", "Fantasy")),
            who: new WhoReading(WhoConfidence.Count, Count: 7));

        for (var probe = 0; probe < 5; probe++)
        {
            await rig.Ingestor.IngestAsync(AGame, result, CancellationToken.None);
        }

        await Assert.That(rig.Fields.Fields).HasCount(1);
        await Assert.That(rig.Fields.Changes).IsEmpty();
        await Assert.That(rig.Availability.Intervals).HasCount(1);

        // Five samples share one timestamp here only because the fixture does; the real store keys
        // on (game_id, at) and the writer is called once per probe.
        await Assert.That(rig.Presence.Samples).HasCount(5);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0246: The type or namespace name 'ProbeIngestor' could not be found`.

- [ ] **Step 3: Write the implementation**

`src/MUI.Discovery/Writers/ProbeIngestor.cs`:

```csharp
namespace MUI.Discovery.Writers;

using MUI.Crawl;

/// <summary>What one probe did to everything.</summary>
public sealed record IngestOutcome(
    FieldReconciliation Fields,
    PresenceOutcome Presence,
    AvailabilityOutcome Availability);

/// <summary>
/// Spec §6.5's seam, made concrete: one immutable <see cref="ProbeResult"/> fans out to the three
/// writers, none of which knows a socket exists.
/// </summary>
/// <remarks>
/// <para>
/// The order is deliberate but not load-bearing — the three touch disjoint tables. Fields first
/// because it is the only one that reads before it writes, presence second, availability last so
/// that a transition is recorded after the evidence for it has been stored.
/// </para>
/// <para>
/// <b>Everything reaching this method is a measurement, and a caller must not hand it anything else.</b>
/// <see cref="ProbeOutcome"/> has two members and both of them mean we dialled: <c>Answered</c>, and
/// <c>Failed</c>, which is a socket that was opened and did not work. A dial the crawler <em>declined
/// to make</em> — §7.2's refusal, when the address a name resolved to is not globally routable — is
/// neither, and there is deliberately no third member for it. Manufacturing a
/// <c>ProbeResult.Failed(ProbeFailureCauses.Refused, …)</c> for one would write an unreachable
/// interval and put our own security policy into a game's public reachability history: the same class
/// of lie as recording an unparseable WHO as zero players (§5.4). This class cannot defend against it
/// — a refusal that has already been dressed as a failure is indistinguishable here — so the guard
/// lives in whatever owns the dial, and this paragraph is the contract it is guarding.
/// </para>
/// </remarks>
public sealed class ProbeIngestor(
    FieldReconciler fields,
    PresenceWriter presence,
    AvailabilityWriter availability)
{
    public async Task<IngestOutcome> IngestAsync(Guid gameId, ProbeResult result, CancellationToken ct)
    {
        var reconciliation = await fields.ApplyAsync(gameId, result, ct);
        var presenceOutcome = await presence.ApplyAsync(gameId, result, ct);
        var availabilityOutcome = await availability.ApplyAsync(gameId, result, ct);

        return new IngestOutcome(reconciliation, presenceOutcome, availabilityOutcome);
    }
}
```

- [ ] **Step 4: Run the suite to verify it passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Discovery/Writers/ProbeIngestor.cs tests/MUI.Discovery.Tests/Writers/ProbeIngestorTests.cs
git commit -m "feat(discovery): the probe ingestor, fanning one result out to the three writers"
```

---

### Task 18: `ArchiveSweeper` — tiered archiving and automatic un-archiving (spec §7.4, §7.5, §7.6)

**Files:**
- Create: `src/MUI.Discovery/Writers/ArchiveSweeper.cs`
- Create: `tests/MUI.Discovery.Tests/Writers/ArchiveSweeperTests.cs`

**Interfaces:**
- Consumes: `IGameRepository`, `GameQuery` (Task 11), `IAvailabilityRepository` (Task 9),
  `ArchivePolicy` (existing), `Game`, `LifecycleState`, `AvailabilityState` (Task 3),
  `InMemoryGameRepository`, `InMemoryAvailabilityRepository`, `ManualTimeProvider` (Tasks 13–14).
- Produces:
  - `sealed class MUI.Discovery.Writers.ArchiveSweeper(IGameRepository games, IAvailabilityRepository availability, TimeProvider time)`
    with `Task<int> SweepAsync(CancellationToken ct)`

**Note for the implementer:** the grace formula is `MUI.Catalog.ArchivePolicy` and is **already
written and already pinned by `ArchivePolicyTests`**. Call it. Do not reimplement, re-derive or
re-clamp it here — this task's job is to feed it the right three inputs and act on the answer.

- [ ] **Step 1: Write the failing test**

`tests/MUI.Discovery.Tests/Writers/ArchiveSweeperTests.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery.Tests.Support;
using MUI.Discovery.Writers;

namespace MUI.Discovery.Tests.Writers;

/// <summary>
/// Spec §7.5. The formula itself is ArchivePolicy's and is pinned by ArchivePolicyTests; what is
/// under test here is that the sweeper feeds it cumulative reachable time, imported time separately,
/// and the claimed flag — and that un-archiving happens without a human on either side.
/// </summary>
public class ArchiveSweeperTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    private sealed record Rig(ArchiveSweeper Sweeper, InMemoryGameRepository Games, InMemoryAvailabilityRepository Availability);

    private static Rig Subject()
    {
        var games = new InMemoryGameRepository();
        var availability = new InMemoryAvailabilityRepository();

        return new Rig(new ArchiveSweeper(games, availability, new ManualTimeProvider(Now)), games, availability);
    }

    private static Game Dark(Guid id, double reachableDays, double darkDays, bool isClaimed = false) =>
        new(id, id.ToString("N"), "Corvid", LifecycleState.Dark, isClaimed,
            Now.AddDays(-(reachableDays + darkDays)), Now.AddDays(-darkDays), ArchivedAt: null);

    private static async Task GiveHistory(
        InMemoryAvailabilityRepository availability, Guid id, double reachableDays, double darkDays)
    {
        var start = Now.AddDays(-(reachableDays + darkDays));
        var wentDark = Now.AddDays(-darkDays);

        var up = await availability.OpenAsync(id, AvailabilityState.Reachable, FailureCause.None, start, CancellationToken.None);
        await availability.CloseAsync(up, wentDark, CancellationToken.None);
        await availability.OpenAsync(id, AvailabilityState.Unreachable, FailureCause.Timeout, wentDark, CancellationToken.None);
    }

    [Test]
    public async Task AYoungGameDarkPastItsFloorIsArchived()
    {
        var rig = Subject();
        var id = Guid.NewGuid();
        await rig.Games.InsertAsync(Dark(id, reachableDays: 30, darkDays: 100), CancellationToken.None);
        await GiveHistory(rig.Availability, id, 30, 100);

        var swept = await rig.Sweeper.SweepAsync(CancellationToken.None);

        var game = await rig.Games.ByIdAsync(id, CancellationToken.None);

        await Assert.That(swept).IsEqualTo(1);
        await Assert.That(game!.State).IsEqualTo(LifecycleState.Archived);
        await Assert.That(game.ArchivedAt).IsEqualTo(Now);
    }

    [Test]
    public async Task AnInstitutionSurvivesTheSameOutage()
    {
        // 100 days dark is fatal to a newcomer and survivable for a decade-old game. That is the
        // whole reason the threshold is tiered.
        var rig = Subject();
        var id = Guid.NewGuid();
        await rig.Games.InsertAsync(Dark(id, reachableDays: 3650, darkDays: 100), CancellationToken.None);
        await GiveHistory(rig.Availability, id, 3650, 100);

        var swept = await rig.Sweeper.SweepAsync(CancellationToken.None);

        await Assert.That(swept).IsEqualTo(0);
        await Assert.That((await rig.Games.ByIdAsync(id, CancellationToken.None))!.State)
            .IsEqualTo(LifecycleState.Dark);
    }

    [Test]
    public async Task GraceIsCumulativeReachableTimeAndNotTheSpanSinceWeMetTheGame()
    {
        // §7.5: a game reachable for two years out of five is credited with two, and a history of
        // flapping accrues nothing for the gaps. Here: 40 days up inside a 700-day acquaintance,
        // which earns only the floor and is therefore archived after 100 days dark.
        var rig = Subject();
        var id = Guid.NewGuid();
        await rig.Games.InsertAsync(
            new Game(id, "flapper", "Flapper", LifecycleState.Dark, IsClaimed: false,
                Now.AddDays(-700), Now.AddDays(-100), ArchivedAt: null),
            CancellationToken.None);

        var up = await rig.Availability.OpenAsync(
            id, AvailabilityState.Reachable, FailureCause.None, Now.AddDays(-140), CancellationToken.None);
        await rig.Availability.CloseAsync(up, Now.AddDays(-100), CancellationToken.None);
        await rig.Availability.OpenAsync(
            id, AvailabilityState.Unreachable, FailureCause.Dns, Now.AddDays(-100), CancellationToken.None);

        await rig.Sweeper.SweepAsync(CancellationToken.None);

        await Assert.That((await rig.Games.ByIdAsync(id, CancellationToken.None))!.State)
            .IsEqualTo(LifecycleState.Archived);
    }

    [Test]
    public async Task ImportedMeasuredTimeCountsAtHalfWeight()
    {
        // Four years of somebody else's probing is credited as two of ours (§7.6), which is 182 days
        // of grace — so 150 days dark is survivable and 200 is not.
        var rig = Subject();
        var id = Guid.NewGuid();
        await rig.Games.InsertAsync(Dark(id, reachableDays: 1, darkDays: 150), CancellationToken.None);
        await GiveHistory(rig.Availability, id, 1, 150);
        rig.Availability.ImportedMeasuredReachable = TimeSpan.FromDays(1460);

        var swept = await rig.Sweeper.SweepAsync(CancellationToken.None);

        await Assert.That(swept).IsEqualTo(0);
        await Assert.That((await rig.Games.ByIdAsync(id, CancellationToken.None))!.State)
            .IsEqualTo(LifecycleState.Dark);
    }

    [Test]
    public async Task AClaimedGameGetsTheCeilingRegardlessOfHowLongWeHaveBeenWatching()
    {
        var rig = Subject();
        var id = Guid.NewGuid();
        await rig.Games.InsertAsync(Dark(id, reachableDays: 1, darkDays: 300, isClaimed: true), CancellationToken.None);
        await GiveHistory(rig.Availability, id, 1, 300);

        var swept = await rig.Sweeper.SweepAsync(CancellationToken.None);

        await Assert.That(swept).IsEqualTo(0);
    }

    [Test]
    public async Task OneSuccessfulProbeRestoresAnArchivedGame()
    {
        // §7.5: un-archiving is automatic and immediate, with no human on either side.
        var rig = Subject();
        var id = Guid.NewGuid();
        await rig.Games.InsertAsync(
            new Game(id, "returned", "Returned", LifecycleState.Archived, IsClaimed: false,
                Now.AddYears(-5), Now, Now.AddDays(-1)),
            CancellationToken.None);
        await rig.Availability.OpenAsync(
            id, AvailabilityState.Reachable, FailureCause.None, Now, CancellationToken.None);

        var swept = await rig.Sweeper.SweepAsync(CancellationToken.None);

        var game = await rig.Games.ByIdAsync(id, CancellationToken.None);

        await Assert.That(swept).IsEqualTo(1);
        await Assert.That(game!.State).IsEqualTo(LifecycleState.Active);
        await Assert.That(game.ArchivedAt).IsNull();
    }

    [Test]
    public async Task NothingIsEverDeleted()
    {
        var rig = Subject();
        var id = Guid.NewGuid();
        await rig.Games.InsertAsync(Dark(id, reachableDays: 30, darkDays: 400), CancellationToken.None);
        await GiveHistory(rig.Availability, id, 30, 400);

        await rig.Sweeper.SweepAsync(CancellationToken.None);

        await Assert.That(rig.Games.Games).HasCount(1);
        await Assert.That((await rig.Games.ByIdAsync(id, CancellationToken.None))!.Slug).IsEqualTo(id.ToString("N"));
        await Assert.That(rig.Availability.Intervals).HasCount(2);
    }

    [Test]
    public async Task AReachableGameIsLeftAloneAndSweepingTwiceChangesNothingTheSecondTime()
    {
        var rig = Subject();
        var reachable = Guid.NewGuid();
        await rig.Games.InsertAsync(
            new Game(reachable, "live", "Live", LifecycleState.Active, IsClaimed: false, Now.AddYears(-1), Now, null),
            CancellationToken.None);
        await rig.Availability.OpenAsync(
            reachable, AvailabilityState.Reachable, FailureCause.None, Now.AddYears(-1), CancellationToken.None);

        var doomed = Guid.NewGuid();
        await rig.Games.InsertAsync(Dark(doomed, reachableDays: 30, darkDays: 400), CancellationToken.None);
        await GiveHistory(rig.Availability, doomed, 30, 400);

        var first = await rig.Sweeper.SweepAsync(CancellationToken.None);
        var second = await rig.Sweeper.SweepAsync(CancellationToken.None);

        await Assert.That(first).IsEqualTo(1);
        await Assert.That(second).IsEqualTo(0);
        await Assert.That((await rig.Games.ByIdAsync(reachable, CancellationToken.None))!.State)
            .IsEqualTo(LifecycleState.Active);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `CS0246: The type or namespace name 'ArchiveSweeper' could not be found`.

- [ ] **Step 3: Write the implementation**

`src/MUI.Discovery/Writers/ArchiveSweeper.cs`:

```csharp
namespace MUI.Discovery.Writers;

using MUI.Catalog;
using MUI.Storage;

/// <summary>
/// Moves games into and out of the archive (spec §7.5).
/// </summary>
/// <remarks>
/// <para>
/// The threshold itself is <see cref="ArchivePolicy"/>'s and is not reimplemented here. This class
/// exists to feed it the three inputs the spec names — <em>cumulative</em> reachable time we measured,
/// imported-measured time separately so §7.6 can weight it at half, and whether the game is claimed —
/// and to act on the answer.
/// </para>
/// <para>
/// <strong>Nothing is ever deleted.</strong> Archiving removes a game from the default listing, the
/// rankings and the "active today" figure, and from nothing else: it keeps its page, its URL, its
/// history and its change feed, it keeps being probed at the §7.4 weekly floor for ever, and one
/// successful probe brings it straight back with no human on either side of the transition.
/// </para>
/// </remarks>
public sealed class ArchiveSweeper(IGameRepository games, IAvailabilityRepository availability, TimeProvider time)
{
    private const int PageSize = 500;

    /// <summary>Sweeps every game and returns how many changed state.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var changed = 0;
        var offset = 0;

        while (true)
        {
            // IncludeArchived, because the sweep is also what brings games back.
            var page = await games.ListAsync(
                new GameQuery { IncludeArchived = true, Limit = PageSize, Offset = offset }, ct);

            if (page.Count == 0)
            {
                break;
            }

            foreach (var game in page)
            {
                if (await SweepOneAsync(game, now, ct))
                {
                    changed++;
                }
            }

            offset += page.Count;
        }

        return changed;
    }

    private async Task<bool> SweepOneAsync(Game game, DateTimeOffset now, CancellationToken ct)
    {
        var open = await availability.OpenIntervalAsync(game.Id, ct);

        if (open is { State: AvailabilityState.Reachable })
        {
            // §7.5: un-archiving is automatic and immediate. One successful probe restores the game
            // to the default listing and fires the "came back" feed.
            if (game.State is not LifecycleState.Archived)
            {
                return false;
            }

            await games.SetStateAsync(game.Id, LifecycleState.Active, null, ct);

            return true;
        }

        if (game.State is LifecycleState.Archived)
        {
            return false;
        }

        // A game we have never seen reachable has been dark since we first met it.
        var darkSince = game.LastReachableAt ?? game.FirstSeenAt;

        var firstParty = await availability.CumulativeReachableAsync(game.Id, now, ct);
        var imported = await availability.CumulativeImportedMeasuredReachableAsync(game.Id, now, ct);

        if (!ArchivePolicy.ShouldArchive(now - darkSince, firstParty, imported, game.IsClaimed))
        {
            return false;
        }

        await games.SetStateAsync(game.Id, LifecycleState.Archived, now, ct);

        return true;
    }
}
```

- [ ] **Step 4: Run the whole solution to verify everything passes**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Web.Tests </dev/null
```
Expected: PASS on all five, with no build warnings.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Discovery/Writers/ArchiveSweeper.cs tests/MUI.Discovery.Tests/Writers/ArchiveSweeperTests.cs
git commit -m "feat(discovery): tiered archiving, fed cumulative reachable time, with automatic return"
```

---

## Self-review

**1. Spec coverage.** Every section this plan claims is traced to a task:

| Spec | Task |
|---|---|
| §5 (storage splits three ways) | 6–12 |
| §5.1 descriptive fields, confirm/change, precedence | 4, 7, 12, 14 |
| §5.2 presence, partitioned, nullable count, aggregates | 8, 15 |
| §5.3 availability as intervals, cause changes only | 9, 16 |
| §5.4 the three states an hour can be in | 8 (CHECK constraints), 15 (all three, plus the named bug) |
| §5.5 endpoints, plural and historical | 10 |
| §5.6 field registry and stored staleness | 2, 3 |
| §5.7 reachable, never uptime | 7 (schema grep), 5, 9 |
| §7.4 unreachable never means removed | 18 (nothing deleted, restore) |
| §7.5 tiered archiving, automatic un-archiving | 18 |
| §7.6 imported tiers, half weight | 9 (`origin` column), 18 |
| §12 bounded, classified failures | 13 (`FailureCauseMap`), 16 |
| §13 fixtures without a socket, availability arithmetic | 5, 13, and every writer test |

**Not covered, deliberately, and stated in the gap table:** §5.2's hourly/daily rollups (no
interface, table or plan owns them; the monthly partitioning is the groundwork), §7.4's
`active → quiet → dark` bands (the spec gives no thresholds; Plan 5's) and slug minting (Plan 3's
identity matcher). The imported-interval *write* path is **not** in that list any more: Task 9
declares `IAvailabilityRepository.InsertImportedAsync` and implements it, because `AvailabilityInterval`
carries no `origin` property and the column has to be written from somewhere that knows about it.
Plan 4's `MeasuredHistorySink` calls it instead of `OpenAsync`, which is what keeps a third party's
history at half weight rather than full.

**2. Placeholder scan.** No "TBD", no "similar to Task N", no "add error handling". Every code step
carries the full text of the file or the exact fragment to insert. The one instruction that names
another file's content — Task 18's "call `ArchivePolicy`, do not reimplement it" — points at a file
that already exists in the repository and is already pinned by its own tests.

**3. Type consistency.** Checked across tasks: `FieldReconciliation(Confirmed, Changed, Rejected)`
constructed in Task 14 and destructured in Task 17; `AvailabilityOutcome(State, Cause, OpenedNewInterval)`
likewise; `PresenceOutcome` values `Counted`/`Unmeasurable`/`NoSample` used identically in Tasks 15
and 17; `CapabilityFields.Measured`/`Declared` used in Tasks 2, 3, 4, 11 and 14 with the same
spelling; `SqlEnums.ToDb`/`Parse` used in Tasks 6, 8, 9, 10, 11 and 12; `HostName.Normalize`
introduced in Task 10 and called by both implementations of `IEndpointRepository` (Tasks 10 and 13) and
by Plan 04's; `ManualTimeProvider` introduced
in Task 14 and reused in Tasks 17 and 18; `GameSeed.InsertAsync` introduced in Task 8 and reused in
Tasks 9, 10 and 12; `PostgresFixture.MigratedAsync` introduced in Task 6 and used from Task 7 on.
`FieldSource`'s declared order is read as the ladder in exactly one place (`SourcePrecedence.RankOf`)
and asserted in exactly one place (`SourcePrecedenceTests.TheDeclaredEnumOrderIsTheLadder`).

One consistency hazard is worth naming for the implementer: **`SqlEnums.ToDb` and the CHECK
constraints are two spellings of the same vocabulary and nothing but a test holds them together.**
`MigrationRunnerTests.EveryStoredEnumMemberRoundTripsBothWays` covers the C# side and the repository
round-trip tests cover the pairing; if a new enum member is added later, both halves move.

**4. Addendum sweep — the MSSP package, and the `WhoReading` workaround.** Re-read after the contract
addendum reversed two earlier decisions.

- *No shared package.* `SharpMU.Mssp` was never published and is not coming, so `MsspData`,
  `MsspHost`, `MsspHostScope` and `MsspVariables` are Plan 01's own types in `MUI.Crawl.Mssp`. For
  this plan that is a `using` and a sentence: the type *names* are unchanged, this plan adds no
  package reference and consumes them through the `MUI.Discovery` → `MUI.Crawl` arrow it already has.
  `MsspSubnegotiationParser` is gone from the design — `TelnetNegotiationCore` 2.7.0 parses telnet
  option 70 itself — and nothing in this plan ever named it outside the retired constraint bullet.
- *The workaround is withdrawn.* Gap-table entry 10 no longer recommends a Plan 01 follow-up; it
  records the fix. Task 15's `PresenceWriter` derives `unmeasurable_reason` from
  `WhoReading.WasAttempted` and nothing else, `MsspVia` has left the decision, and three fixtures
  that used to read `WhoReading.Unread` now say which of the two states they mean. Two tests are new:
  `NeverHavingAskedAndAskingAndFailingAreDifferentReasons`, which asserts the two states are unequal
  *and* produce different reasons, and `AWhoWeReadButCouldNotCountIsAnAttemptedWhoAndNotAMissingOne`,
  which covers the attempted-but-countless arm that neither static value names.

**5. Two naming corrections folded in, and one fake that was missing.**

- *The repository doubles.* The Task 13 test doubles were spelled `Fake<Thing>Repository` while this
  plan's own convention table (and every one of Plan 03's ~60 uses of them) says
  `InMemory<Thing>Repository`. They are renamed here, along with `Support/FakeRepositories.cs` →
  `Support/InMemoryRepositories.cs`.
- *The clock.* Task 14's clock was `Support/FakeTimeProvider.cs`, and Plan 03's is
  `Support/ManualTimeProvider.cs` in the **same** `Support/` directory doing the same job — two names
  for one helper, and the losing one collides with `Microsoft.Extensions.Time.Testing.FakeTimeProvider`,
  a real type someone would reasonably assume was in play. This plan now spells it
  `ManualTimeProvider`, at Plan 03's path and with Plan 03's constructor
  (`DateTimeOffset? start = null`), and its doc comment says why it is not called `FakeTimeProvider`.
  Renamed at its declaration in Task 14 and at all three uses (Tasks 14, 17, 18). Plan 03's version is
  a superset — it also implements `CreateTimer` — so whichever plan lands second extends this file
  rather than declaring a second clock beside it.
- *The fifth fake.* Task 13 built four in-memory repositories and Plan 03 constructs five: it also
  wants `InMemoryEndpointRepository`. It is added here, in `Support/InMemoryRepositories.cs` with its
  neighbours, mirroring `NpgsqlEndpointRepository`'s upsert rules (`first_seen_at` never moves
  forward, `last_seen_at` never moves back). Task 13's `InMemoryAvailabilityRepository` also gained
  `InsertImportedAsync` — without it the fake does not implement the interface Task 9 declares — and
  with it the two cumulative sums answer apart, imported time counting only toward the imported one.

**6. A host now has exactly one spelling (Task 10).** Writing the fifth fake surfaced a real defect in
the fourth-oldest part of this plan: `NpgsqlEndpointRepository.ByAddressAsync` compared `host = @host`,
ordinally, while the natural in-memory spelling of the same lookup is `OrdinalIgnoreCase`. **A fake
kinder than the database is the worst direction for a disagreement to run** — every test passes and
production quietly writes a second `game_endpoint` row the first time a host arrives as
`MUD.Example.ORG`. The unique index on `(host, port)` does not stop it, because the two rows are two
different strings; §7.3's endpoint signal then fails to find a game we already have, and Plan 03's
identity matcher mints a duplicate *listing*. That is the specific failure §7.3 exists to prevent.

It is corrected by making one canonical form rather than by making the comparison lenient:

- `MUI.Catalog.HostName.Normalize` — trim, strip IPv6 brackets, canonicalise an IP literal through
  `IPAddress.ToString()`, otherwise drop the DNS root dot and lower-case. It deliberately mirrors
  `MsspHost.Create`, which `MUI.Catalog` may never reference, so it is a second implementation of one
  rule; `HostNormalisationAgreementTests` in MUI.Discovery.Tests — the one project that sees both —
  compares them over a table of spellings rather than trusting the comment.
- `NpgsqlEndpointRepository` normalises at both ends, `UpsertAsync` and `ByAddressAsync`'s parameter,
  so the comparison can stay ordinal and keep using `game_endpoint_address_idx`.
- `0005_game_endpoint.sql` gains `game_endpoint_host_is_canonical`, which refuses a host that is not
  lower-cased, trimmed and dotless. Postgres cannot check the IP-literal half, but a write path that
  forgets to normalise at all now fails loudly instead of duplicating silently.
- The fake normalises identically and compares `Ordinal`, and `InMemoryEndpointRepositoryTests` runs
  the same table of spellings as `EndpointRepositoryTests`. The two suites cannot be one test — one
  needs a container and the other must never touch one — so each names the other in its doc comment.

Plan 04's own `InMemoryEndpointRepository` and its `ImportPipeline` were brought into line at the same
time: an import is where hosts arrive spelled by somebody else, so it is where this bug would have been
reached first.

**7. §7.2's new resolved-address gate, checked against this plan (`dfff339` on `main`, not yet on this
branch).** It adds four rules to the crawler's dial and one of them names a write: **a refusal writes
no availability sample.** Every rule but that one is Plan 01's and Plan 03's — resolving, refusing a
mixed answer, keeping "could not resolve" distinct, and the operator-seed exemption all happen before
a `ProbeResult` exists. The write rule is checked here and needs **no change**: `ProbeOutcome` is
`Answered` or `Failed` and both mean the socket was opened, so a refusal has no representation these
writers can receive. The reachable hazard is a caller dressing one as
`ProbeResult.Failed(ProbeFailureCauses.Refused, …)` — which is doubly wrong, since `FailureCause.Refused`
means the far end sent an RST — and `ProbeIngestor` cannot detect that after the fact. So the invariant
is stated at the seam, in Task 17's prose and in `ProbeIngestor`'s doc comment, where an implementer of
the dial will meet it; the guard itself belongs to whoever writes the dial. Flagged for Plans 01 and 03
rather than fixed here.

