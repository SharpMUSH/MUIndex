# Backfill Importers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `src/MUI.Backfill` — the one-off, re-runnable backfill that reads the existing MU\* directories (spec §7.6), seeds crawl targets and endpoints from them, and admits third-party *measured* history at half weight while making it structurally impossible for a *asserted* source to write any history at all.

**Architecture:** Six `IDirectoryImporter`s each parse a committed fixture payload fetched through a `DirectorySource` that reads `robots.txt` before its first content fetch, rate-limits on an injected `TimeProvider`, and refuses to touch a scrape URI when a bulk export or documented API is configured. `ImportPipeline` routes every write through an `IImportWriter` (committing or dry-run) and every history write through an `IHistorySink` chosen by tier — the asserted sink holds no writer at all, so it cannot write even by mistake. Identity is resolved on the way in against `IEndpointRepository`: an endpoint we already know merges into that game; anything below threshold becomes a crawl target and *not* a game. Every imported value gets a row in a new `import_provenance` sidecar carrying the originating site and the import date — §7.6's provenance chip, and nothing to do with grace: the half weight is carried by Plan 02's `origin` column on the interval itself, which is what `ArchivePolicy.GraceFor` separates imported reachable time by.

**Tech Stack:** .NET 10, C# 14, `System.Text.Json` (BCL, no HTML/JSON dependency added), `System.Net.Http` with a fake `HttpMessageHandler` in tests, Npgsql + Dapper for the one new table, TUnit on Microsoft.Testing.Platform, `Testcontainers.PostgreSql` for the single integration test.

**Depends on: Plan 02 for `MUI.Storage`, the repositories and the `MigrationRunner`; Plan 03 for `ICrawlTargetRepository` (an import seeds crawl targets, it does not schedule them). Nothing here probes a game — this plan reads other people's directories.**

---

## Cross-plan reconciliation — read before Task 5, Task 7 and Task 8

Plan 02 and this plan were drafted in parallel and both solved §7.5's half-weight rule, differently.
**Plan 02's mechanism is the one that ships**, because Plan 02 owns `availability_interval` and the
`ArchiveSweeper` that actually applies `ArchivePolicy`. Four consequences, and every task below must
be read through them:

1. **`availability_interval` carries an `origin` column** (`'first_party' | 'imported_measured'`,
   Plan 02 Task 9), and `IAvailabilityRepository` exposes
   `CumulativeImportedMeasuredReachableAsync(Guid, DateTimeOffset, CancellationToken)` beside
   `CumulativeReachableAsync`. Plan 02's `ArchiveSweeper` already feeds both into
   `ArchivePolicy.GraceFor` — first-party at full weight, imported at half.
2. **`MeasuredHistorySink` must therefore not call `OpenAsync`.** That write path defaults `origin`
   to `'first_party'`, which would credit a third party's history at **full** weight — the exact
   opposite of §7.5, and a silent one. It calls Plan 02's imported write path instead:
   `IAvailabilityRepository.InsertImportedAsync(Guid gameId, AvailabilityState state, FailureCause cause, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)`,
   which writes `origin = 'imported_measured'` and a closed interval in one statement. Note the `to`
   is **not** nullable: an imported span is always closed, which is the same rule this plan already
   applies at the sink. It is wired in Task 7, in `CommittingImportWriter.WriteClosedAvailabilityAsync`
   — the single place the sink's availability write lands — and Task 8 asserts the stored `origin`,
   in memory and against a real Postgres.
3. **`ImportedGraceCalculator` is dropped from this plan.** Grace is computed in exactly one place,
   Plan 02's `ArchiveSweeper`; a second calculator reading the sidecar would count the same history
   twice. It is gone from the type list, the file table and Task 8, and this plan adds **no**
   production type for grace. Task 8 keeps its subject and both of its pinning tests unchanged in
   substance: an asserted source still writes zero history rows and is counted for trying, and a
   measured source's four imported years still yield the same grace as two of ours. It proves the
   second by writing through `MeasuredHistorySink` and then calling
   `IAvailabilityRepository.CumulativeImportedMeasuredReachableAsync` and
   `MUI.Catalog.ArchivePolicy.GraceFor` directly, instead of through a calculator of its own.
4. **`import_provenance` stays, and its justification narrows.** Plan 02's `origin` column records
   the *tier*; §7.6 additionally requires the originating **site** and the import **date** on every
   imported value, which no Plan 02 record carries — `GameField` has a `FieldSource`, `PresenceSample`
   a `PresenceSource`, `AvailabilityInterval` a tier-valued `origin`, and none of the three names a
   site or a date. The sidecar is what serves the provenance chip and the about page's attribution
   list. **It is not on the grace path at all**, and nothing in this plan may read it to compute one.

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
  `WHO` that was never sent yields `WhoConfidence.NotAttempted`, which is a different state.
- **Vocabulary is "reachable", never "uptime"** — schema, API, code and copy alike (spec §5.7).
- **Branch from `main`, open a PR, never commit directly to `main`.**
- **Any new test project goes into `MUIndex.slnx` AND `.github/workflows/ci.yml`**, which runs each
  suite as its own explicit step.
- **MUIndex owns the MSSP domain** — namespace `MUI.Crawl.Mssp`, in `src/MUI.Crawl`, Plan 01's code
  written against Plan 01's own tests. There is **no shared package**: nothing named `SharpMU.Mssp`
  was ever published and the repository that would have produced it is archived, so MUIndex
  implements its crawler end to end and shares no code with SharpMUTerm. `MsspData`, `MsspHost`,
  `MsspHostScope` and `MsspVariables` live there; telnet option 70 is parsed by
  `TelnetNegotiationCore` 2.6.5 itself, and `MUI.Crawl.Mssp.MsspPlaintextReply` handles only the
  out-of-band `MSSP-REQUEST` text reply. Never re-declare those types locally. **`MUI.Backfill`
  references none of it** — an importer reads someone else's directory export, never a live server —
  so for this plan the rule is only "do not reach for it".
- **Persistence is PostgreSQL 17 with Npgsql + Dapper and plain numbered `.sql` migration files
  applied by a small idempotent runner. No EF Core**, ever. Integration tests use
  `Testcontainers.PostgreSql`.

### Plan-specific constraints

- **No network in any test.** Every importer test drives a fake `HttpMessageHandler` over a fixture
  committed under `tests/MUI.Backfill.Tests/Fixtures/`. There is no `HttpClient` in this suite that
  can reach the internet.
- **Nothing in `MUI.Backfill` probes a game.** It never constructs a `ProbeTarget`, never calls
  `IProbe`, and never calls `ICrawlTargetRepository.RecordAttemptAsync` — that method belongs to the
  crawler, and calling it would be scheduling (spec §7.1).
- **An import may not mint a game.** Below the identity threshold it writes a `CrawlTarget` and
  stops. A host becomes a listed game by answering for itself (spec §7.2).

### Deviations from `CONTRACT.md`, declared

The contract says the default is to copy its declarations verbatim, and that a plan needing a change
must say so in its own text. This plan makes exactly these changes, all additive:

1. `ImportPipeline`'s constructor gains a seventh repository parameter,
   `IImportProvenanceRepository provenance`, before `TimeProvider time`. §7.6 requires every imported
   value to carry its originating site and import date, and no contract record has anywhere to put
   that (see *Spec gaps* at the foot of this plan).
2. `ImportPipeline` gains `DryRunAsync(IDirectoryImporter, CancellationToken)` alongside `RunAsync`.
3. A new project `src/MUI.Backfill.Cli`, assembly name `mui-import`, is added to the contract's
   project table — the same shape as Plan 1's `src/MUI.Probe.Cli`/`mui-probe`.
4. New types this plan adds beyond the contract, all in namespace `MUI.Backfill`:
   `ImportTierMap`, `CrawlerIdentity`, `FetchRoute`, `FetchDecision`, `EtiquettePlanner`,
   `EtiquetteViolationException`, `DirectorySource`, `DirectoryImporter` (abstract base),
   `ImportSubjectKind`, `ImportProvenance`, `IImportProvenanceRepository`,
   `NpgsqlImportProvenanceRepository`, `ImportMatch`, `ImportIdentity`,
   `IImportWriter`, `CommittingImportWriter`, `DryRunImportWriter`, `HistoryWrite`, `IHistorySink`,
   `MeasuredHistorySink`, `AssertedHistorySink`, `HistorySink`, `SourceAttribution`,
   `ImporterRegistry`, `ImportRunOptions`, `ImportRunner`.
5. One new migration file, `src/MUI.Storage/Migrations/0100_import_provenance.sql`. Plan 2 owns that
   directory; this file is additive, sorts after Plan 2's and Plan 3's, and creates one table.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/MUI.Backfill/ImportTier.cs` | The two tiers and their `FieldSource` mapping |
| `src/MUI.Backfill/ImportedGame.cs` | What an importer yields — endpoints, fields, presence, availability |
| `src/MUI.Backfill/ImportEtiquette.cs` | Etiquette as configuration: routes, UA, interval, contact flag |
| `src/MUI.Backfill/EtiquettePlanner.cs` | Bulk export ▸ API ▸ scrape, and the refusals |
| `src/MUI.Backfill/RobotsPolicy.cs` | `robots.txt` parse / allow / crawl-delay |
| `src/MUI.Backfill/PolitenessGate.cs` | Rate limit on an injected `TimeProvider`, robots adoption |
| `src/MUI.Backfill/DirectorySource.cs` | The only place an HTTP request is made; enforces all of the above |
| `src/MUI.Backfill/ImportProvenance.cs` | The provenance sidecar's record, kinds and repository interface |
| `src/MUI.Backfill/NpgsqlImportProvenanceRepository.cs` | Its Dapper implementation |
| `src/MUI.Backfill/ImportIdentity.cs` | Endpoint matching on the way in |
| `src/MUI.Backfill/IImportWriter.cs` | Commit vs dry-run, as a type rather than a flag |
| `src/MUI.Backfill/HistorySink.cs` | Measured vs asserted, as a type rather than an `if` |
| `src/MUI.Backfill/ImportPipeline.cs` | The one pass: identity, targets, endpoints, fields, history |
| `src/MUI.Backfill/ImporterRegistry.cs` | Registered importers, and the attribution list derived from them |
| `src/MUI.Backfill/ImportRunner.cs` | Dry run, real run, and the printed report |
| `src/MUI.Backfill/Importers/*.cs` | Six importers, one file each |
| `src/MUI.Backfill.Cli/Program.cs` | `mui-import` |
| `src/MUI.Storage/Migrations/0100_import_provenance.sql` | The sidecar table |
| `tests/MUI.Backfill.Tests/Support/*.cs` | In-memory repositories, fake handler, manual clock, fixture loader |
| `tests/MUI.Backfill.Tests/Fixtures/*` | Recorded payloads — the only input any test has |

---

### Task 1: The project, the two tiers, and the precedence floor

**Files:**
- Create: `src/MUI.Backfill/MUI.Backfill.csproj`
- Create: `src/MUI.Backfill/ImportTier.cs`
- Create: `src/MUI.Backfill/ImportedGame.cs`
- Create: `tests/MUI.Backfill.Tests/MUI.Backfill.Tests.csproj`
- Create: `tests/MUI.Backfill.Tests/ImportTierTests.cs`
- Modify: `MUIndex.slnx`
- Modify: `.github/workflows/ci.yml`
- Modify: `Directory.Packages.props`

**Interfaces:**
- Consumes: `MUI.Catalog.FieldSource` (`.ImportedMeasured`, `.ImportedAsserted` — already exist,
  never redefine them), `MUI.Catalog.SourcePrecedence.RankOf(FieldSource)` and
  `.Wins(FieldSource candidate, FieldSource incumbent, string field)` (Plan 2),
  `MUI.Catalog.EndpointKind` (Plan 2).
- Produces: `MUI.Backfill.ImportTier` (`Measured`, `Asserted`);
  `ImportTierMap.SourceFor(ImportTier) → FieldSource`; `ImportTierMap.MayWriteHistory(ImportTier) → bool`;
  `ImportedGame`, `ImportedEndpoint(string Host, int Port, EndpointKind Kind)`,
  `ImportedAvailability(DateTimeOffset From, DateTimeOffset? To, bool Reachable)`,
  `ImportedPresence(DateTimeOffset At, int Count)`.

- [ ] **Step 1: Confirm the central package versions this plan needs**

Open `Directory.Packages.props`. Plan 2 added `Npgsql`, `Dapper` and `Testcontainers.PostgreSql`.
If any of those three lines is absent, add it inside the existing first `<ItemGroup>` exactly as
follows, and add nothing else:

```xml
    <PackageVersion Include="Npgsql" Version="10.0.0" />
    <PackageVersion Include="Dapper" Version="2.1.66" />
```

and inside the test `<ItemGroup>`:

```xml
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.7.0" />
```

- [ ] **Step 2: Create the two project files**

`src/MUI.Backfill/MUI.Backfill.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <RootNamespace>MUI.Backfill</RootNamespace>
    <AssemblyName>MUI.Backfill</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\MUI.Catalog\MUI.Catalog.csproj" />
    <ProjectReference Include="..\MUI.Storage\MUI.Storage.csproj" />
    <ProjectReference Include="..\MUI.Discovery\MUI.Discovery.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Npgsql" />
    <PackageReference Include="Dapper" />
  </ItemGroup>

</Project>
```

`tests/MUI.Backfill.Tests/MUI.Backfill.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>MUI.Backfill.Tests</RootNamespace>
    <AssemblyName>MUI.Backfill.Tests</AssemblyName>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="TUnit" />
    <PackageReference Include="Testcontainers.PostgreSql" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\MUI.Backfill\MUI.Backfill.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Wire both into the solution and CI**

In `MUIndex.slnx`, add to the `/src/` folder (keeping the existing entries):

```xml
    <Project Path="src/MUI.Backfill/MUI.Backfill.csproj" />
```

and to the `/tests/` folder:

```xml
    <Project Path="tests/MUI.Backfill.Tests/MUI.Backfill.Tests.csproj" />
```

In `.github/workflows/ci.yml`, add after the `Test — Discovery` step:

```yaml
      # Testcontainers needs a Linux Docker daemon; MUI_INTEGRATION gates the one Postgres test.
      - name: Test — Backfill
        shell: bash
        env:
          MUI_INTEGRATION: ${{ matrix.os == 'ubuntu-latest' && '1' || '0' }}
        run: dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests/MUI.Backfill.Tests.csproj
```

- [ ] **Step 4: Write the failing test**

`tests/MUI.Backfill.Tests/ImportTierTests.cs`:

```csharp
using MUI.Catalog;

namespace MUI.Backfill.Tests;

/// <summary>
/// Spec §7.6's two tiers, and §5.1's rule that neither of them outranks anything we measured
/// ourselves. If the precedence ladder is ever reordered, this file is what says so.
/// </summary>
public class ImportTierTests
{
    private static readonly FieldSource[] FirstParty =
    [
        FieldSource.Staff, FieldSource.Handshake, FieldSource.Owner,
        FieldSource.Who, FieldSource.Mssp, FieldSource.Banner,
    ];

    [Test]
    public async Task EachTierMapsToItsOwnFieldSource()
    {
        await Assert.That(ImportTierMap.SourceFor(ImportTier.Measured)).IsEqualTo(FieldSource.ImportedMeasured);
        await Assert.That(ImportTierMap.SourceFor(ImportTier.Asserted)).IsEqualTo(FieldSource.ImportedAsserted);
    }

    [Test]
    public async Task NeitherTierOutranksAnythingWeMeasuredOurselves()
    {
        foreach (var tier in new[] { ImportTier.Measured, ImportTier.Asserted })
        {
            var imported = ImportTierMap.SourceFor(tier);

            foreach (var ours in FirstParty)
            {
                await Assert.That(SourcePrecedence.RankOf(imported)).IsGreaterThan(SourcePrecedence.RankOf(ours));
                await Assert.That(SourcePrecedence.Wins(imported, ours, "genre")).IsFalse();
            }
        }
    }

    [Test]
    public async Task AMeasuredImportBeatsAnAssertedOne()
    {
        await Assert.That(SourcePrecedence.Wins(FieldSource.ImportedMeasured, FieldSource.ImportedAsserted, "genre"))
            .IsTrue();
        await Assert.That(SourcePrecedence.Wins(FieldSource.ImportedAsserted, FieldSource.ImportedMeasured, "genre"))
            .IsFalse();
    }

    [Test]
    public async Task OnlyTheMeasuredTierMayWriteHistory()
    {
        await Assert.That(ImportTierMap.MayWriteHistory(ImportTier.Measured)).IsTrue();
        await Assert.That(ImportTierMap.MayWriteHistory(ImportTier.Asserted)).IsFalse();
    }

    [Test]
    public async Task AnImportedGameCarriesNoHistoryUntilSomebodyPutsItThere()
    {
        var game = new ImportedGame { SourceName = "MudVerse", SourceKey = "anachronism", Name = "Anachronism" };

        await Assert.That(game.Endpoints).IsEmpty();
        await Assert.That(game.Presence).IsEmpty();
        await Assert.That(game.Availability).IsEmpty();
        await Assert.That(game.Fields).IsEmpty();
    }
}
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'ImportTierMap' could not be found`.

- [ ] **Step 6: Write the implementation**

`src/MUI.Backfill/ImportTier.cs`:

```csharp
using MUI.Catalog;

namespace MUI.Backfill;

/// <summary>
/// Whether the directory we are importing from measured a game or merely wrote it down (spec §7.6).
/// This is the same measured-versus-declared spine that runs through the rest of the design, applied
/// one level up: a third party that ran its own probe produced a measurement, and a hand-maintained
/// list is an assertion.
/// </summary>
public enum ImportTier
{
    /// <summary>MudStats, MudVerse, Grapevine — sites that actively ping.</summary>
    Measured,

    /// <summary>The MUD Connector, MUSHCode lists, hand-maintained pages.</summary>
    Asserted,
}

/// <summary>
/// The tier's consequences, in one place. Nothing else in this assembly may re-derive them.
/// </summary>
public static class ImportTierMap
{
    public static FieldSource SourceFor(ImportTier tier) => tier switch
    {
        ImportTier.Measured => FieldSource.ImportedMeasured,
        ImportTier.Asserted => FieldSource.ImportedAsserted,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    /// <summary>
    /// Whether this tier may populate <c>AvailabilityInterval</c> and <c>PresenceSample</c> rows.
    /// The asserted tier seeds discovery and endpoints only: no history, no presence, no grace.
    /// </summary>
    /// <remarks>
    /// This predicate is documentation, not enforcement. The enforcement is
    /// <see cref="AssertedHistorySink"/>, which holds nothing it could write with.
    /// </remarks>
    public static bool MayWriteHistory(ImportTier tier) => tier is ImportTier.Measured;
}
```

`src/MUI.Backfill/ImportedGame.cs`:

```csharp
using MUI.Catalog;

namespace MUI.Backfill;

/// <summary>One address a directory lists for a game.</summary>
public sealed record ImportedEndpoint(string Host, int Port, EndpointKind Kind);

/// <summary>
/// One span a third-party prober recorded. <see cref="To"/> is null when the source's export ends
/// with the span still open; the import closes it at the import instant rather than leaving it open,
/// because we did not measure it and cannot extend it.
/// </summary>
public sealed record ImportedAvailability(DateTimeOffset From, DateTimeOffset? To, bool Reachable);

/// <summary>One player count a third-party prober recorded, with the moment it recorded it.</summary>
public sealed record ImportedPresence(DateTimeOffset At, int Count);

/// <summary>
/// What one importer yields for one game. Deliberately not a <c>Game</c>: an import may not mint a
/// listing, because a host becomes a listed game by answering for itself (spec §7.2).
/// </summary>
public sealed record ImportedGame
{
    /// <summary>The directory this came from, as it will appear on the about page.</summary>
    public required string SourceName { get; init; }

    /// <summary>That directory's own identifier for the game, so a re-import is recognisable.</summary>
    public required string SourceKey { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<ImportedEndpoint> Endpoints { get; init; } = [];

    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    /// <summary>Empty for every asserted source, and refused by the pipeline if it is not.</summary>
    public IReadOnlyList<ImportedAvailability> Availability { get; init; } = [];

    /// <summary>Empty for every asserted source, and refused by the pipeline if it is not.</summary>
    public IReadOnlyList<ImportedPresence> Presence { get; init; } = [];

    /// <summary>The page this record came from, recorded in provenance so a value can be traced back.</summary>
    public Uri? SourceUri { get; init; }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 5 tests, and a warning-free build.

- [ ] **Step 8: Commit**

```bash
git add src/MUI.Backfill tests/MUI.Backfill.Tests MUIndex.slnx .github/workflows/ci.yml Directory.Packages.props
git commit -m "feat(backfill): add MUI.Backfill with the two import tiers and their precedence floor"
```

---

### Task 2: Etiquette as configuration — bulk export over API over scraping

**Files:**
- Create: `src/MUI.Backfill/ImportEtiquette.cs`
- Create: `src/MUI.Backfill/EtiquettePlanner.cs`
- Create: `tests/MUI.Backfill.Tests/EtiquettePlannerTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `ImportEtiquette` (record with `SourceName`, `AttributionUri`, `BulkExportUri`,
  `ApiUri`, `ScrapeUri`, `UserAgent`, `MinimumInterval`, `ContactedMaintainer`, `RobotsUri`);
  `CrawlerIdentity.InfoUrl`, `.Product`, `.UserAgent`, `.SelfIdentifies(string)`;
  `enum FetchRoute { BulkExport, Api, Scrape, None }`;
  `FetchDecision(FetchRoute Route, Uri? Uri, string? RefusedReason)`;
  `EtiquettePlanner.Decide(ImportEtiquette) → FetchDecision`;
  `EtiquettePlanner.MayFetch(ImportEtiquette, Uri) → bool`;
  `EtiquetteViolationException(string message)`.

- [ ] **Step 1: Write the failing test**

`tests/MUI.Backfill.Tests/EtiquettePlannerTests.cs`:

```csharp
namespace MUI.Backfill.Tests;

/// <summary>
/// Spec §7.6's closing paragraph, as code rather than prose: ask for a bulk export or use a
/// documented API in preference to scraping, and do not scrape a site whose maintainer nobody
/// emailed. Every case here is a refusal the pipeline can make on its own.
/// </summary>
public class EtiquettePlannerTests
{
    private static ImportEtiquette Base() => new()
    {
        SourceName = "Example Directory",
        AttributionUri = new Uri("https://example.test/"),
        RobotsUri = new Uri("https://example.test/robots.txt"),
        UserAgent = CrawlerIdentity.UserAgent,
    };

    [Test]
    public async Task ABulkExportBeatsBothAnApiAndAScrape()
    {
        var etiquette = Base() with
        {
            BulkExportUri = new Uri("https://example.test/dumps/games.json"),
            ApiUri = new Uri("https://example.test/api/games"),
            ScrapeUri = new Uri("https://example.test/list"),
            ContactedMaintainer = true,
        };

        var decision = EtiquettePlanner.Decide(etiquette);

        await Assert.That(decision.Route).IsEqualTo(FetchRoute.BulkExport);
        await Assert.That(decision.Uri).IsEqualTo(new Uri("https://example.test/dumps/games.json"));
    }

    [Test]
    public async Task ADocumentedApiBeatsAScrape()
    {
        var etiquette = Base() with
        {
            ApiUri = new Uri("https://example.test/api/games"),
            ScrapeUri = new Uri("https://example.test/list"),
            ContactedMaintainer = true,
        };

        await Assert.That(EtiquettePlanner.Decide(etiquette).Route).IsEqualTo(FetchRoute.Api);
    }

    [Test]
    public async Task TheScrapeUriIsRefusedOutrightWhenAnApiExists()
    {
        var etiquette = Base() with
        {
            ApiUri = new Uri("https://example.test/api/games"),
            ScrapeUri = new Uri("https://example.test/list"),
            ContactedMaintainer = true,
        };

        await Assert.That(EtiquettePlanner.MayFetch(etiquette, new Uri("https://example.test/list"))).IsFalse();
        await Assert.That(EtiquettePlanner.MayFetch(etiquette, new Uri("https://example.test/api/games?page=2"))).IsTrue();
    }

    [Test]
    public async Task ScrapingIsOffUntilSomebodyHasEmailedTheMaintainer()
    {
        var etiquette = Base() with { ScrapeUri = new Uri("https://example.test/list") };

        var decision = EtiquettePlanner.Decide(etiquette);

        await Assert.That(decision.Route).IsEqualTo(FetchRoute.None);
        await Assert.That(decision.RefusedReason).IsEqualTo(EtiquettePlanner.MaintainerNotContacted);
    }

    [Test]
    public async Task ScrapingIsAllowedOnceTheMaintainerHasBeenContactedAndThereIsNoBetterRoute()
    {
        var etiquette = Base() with
        {
            ScrapeUri = new Uri("https://example.test/list"),
            ContactedMaintainer = true,
        };

        var decision = EtiquettePlanner.Decide(etiquette);

        await Assert.That(decision.Route).IsEqualTo(FetchRoute.Scrape);
        await Assert.That(EtiquettePlanner.MayFetch(etiquette, new Uri("https://example.test/list/page/2"))).IsTrue();
    }

    [Test]
    public async Task AnAnonymousUserAgentIsRefusedBeforeAnythingElseIsConsidered()
    {
        var etiquette = Base() with
        {
            UserAgent = "Mozilla/5.0",
            BulkExportUri = new Uri("https://example.test/dumps/games.json"),
        };

        var decision = EtiquettePlanner.Decide(etiquette);

        await Assert.That(decision.Route).IsEqualTo(FetchRoute.None);
        await Assert.That(decision.RefusedReason).IsEqualTo(EtiquettePlanner.AnonymousUserAgent);
    }

    [Test]
    public async Task OurUserAgentNamesUsAndSaysWhereToReadAboutUs()
    {
        await Assert.That(CrawlerIdentity.SelfIdentifies(CrawlerIdentity.UserAgent)).IsTrue();
        await Assert.That(CrawlerIdentity.UserAgent).Contains(CrawlerIdentity.Product);
        await Assert.That(CrawlerIdentity.UserAgent).Contains("https://");
    }

    [Test]
    public async Task ASourceWithNoRouteAtAllIsRefusedRatherThanGuessedAt()
    {
        var decision = EtiquettePlanner.Decide(Base());

        await Assert.That(decision.Route).IsEqualTo(FetchRoute.None);
        await Assert.That(decision.RefusedReason).IsEqualTo(EtiquettePlanner.NothingConfigured);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'ImportEtiquette' could not be found`.

- [ ] **Step 3: Write `ImportEtiquette`**

`src/MUI.Backfill/ImportEtiquette.cs`:

```csharp
namespace MUI.Backfill;

/// <summary>
/// How we agreed to treat one directory. Spec §7.6 states the etiquette in prose — ask for a bulk
/// export or use a documented API in preference to scraping, honour <c>robots.txt</c>, rate-limit
/// hard, attribute every source — and this record is that paragraph in a form the code can obey.
/// </summary>
/// <remarks>
/// These sites are run by people in the same small hobby, and several of them are the reason any of
/// this data exists at all. A short email first is both the decent move and the one most likely to
/// get better data than scraping would, which is why <see cref="ContactedMaintainer"/> defaults to
/// <c>false</c> and gates the scrape route off entirely.
/// </remarks>
public sealed record ImportEtiquette
{
    /// <summary>The name that appears on the about page and in the API's attribution list.</summary>
    public required string SourceName { get; init; }

    /// <summary>Where a reader is sent to credit this source. Never optional.</summary>
    public required Uri AttributionUri { get; init; }

    /// <summary>A published dump. The best route, and preferred over everything below it.</summary>
    public Uri? BulkExportUri { get; init; }

    /// <summary>A documented API. Preferred over scraping.</summary>
    public Uri? ApiUri { get; init; }

    /// <summary>
    /// The last resort. Reachable only when neither of the two above is configured <em>and</em>
    /// <see cref="ContactedMaintainer"/> is true.
    /// </summary>
    public Uri? ScrapeUri { get; init; }

    /// <summary>Must self-identify with an info URL — spec §11's crawler-identification rule.</summary>
    public required string UserAgent { get; init; }

    /// <summary>The floor between two fetches. A longer <c>Crawl-delay</c> in robots.txt wins.</summary>
    public TimeSpan MinimumInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether a human has actually written to whoever runs this site. Flipping this to <c>true</c>
    /// is a statement of fact about the world, not a configuration convenience.
    /// </summary>
    public bool ContactedMaintainer { get; init; }

    public required Uri RobotsUri { get; init; }
}

/// <summary>
/// Who we say we are. Spec §11 requires the crawler to self-identify with an info URL so an admin
/// reading their logs can discover who we are and how to opt out; an importer reading a directory's
/// access log is the same obligation over HTTP.
/// </summary>
public static class CrawlerIdentity
{
    /// <summary>Matches <c>MUI.Crawl.ProbeOptions.InfoUrl</c> — one crawler, one page about it.</summary>
    public const string InfoUrl = "https://muindex.org/crawler";

    public const string Product = "MUIndex";

    public static string UserAgent => $"{Product}-Importer/1.0 (+{InfoUrl})";

    public static bool SelfIdentifies(string userAgent)
    {
        ArgumentNullException.ThrowIfNull(userAgent);
        return userAgent.Contains(InfoUrl, StringComparison.Ordinal)
            && userAgent.Contains(Product, StringComparison.Ordinal);
    }
}

/// <summary>Thrown when a fetch or an import would break the etiquette in <see cref="ImportEtiquette"/>.</summary>
public sealed class EtiquetteViolationException(string message) : Exception(message);
```

- [ ] **Step 4: Write `EtiquettePlanner`**

`src/MUI.Backfill/EtiquettePlanner.cs`:

```csharp
namespace MUI.Backfill;

/// <summary>Which of a source's routes we are entitled to use.</summary>
public enum FetchRoute
{
    BulkExport,
    Api,
    Scrape,

    /// <summary>No route is permitted. The importer does not run.</summary>
    None,
}

/// <summary>The chosen route, the URI it is rooted at, and — when there is none — why not.</summary>
public sealed record FetchDecision(FetchRoute Route, Uri? Uri, string? RefusedReason);

/// <summary>
/// Spec §7.6's preference order, decided once and consulted everywhere. Nothing else in this
/// assembly may reason about which URI to fetch.
/// </summary>
public static class EtiquettePlanner
{
    public const string AnonymousUserAgent =
        "the user agent does not name us or carry an info URL (spec §11)";

    public const string MaintainerNotContacted =
        "scraping is the only configured route and nobody has written to the maintainer yet (spec §7.6)";

    public const string NothingConfigured =
        "no bulk export, API or scrape URI is configured";

    public static FetchDecision Decide(ImportEtiquette etiquette)
    {
        ArgumentNullException.ThrowIfNull(etiquette);

        if (!CrawlerIdentity.SelfIdentifies(etiquette.UserAgent))
        {
            return new FetchDecision(FetchRoute.None, null, AnonymousUserAgent);
        }

        if (etiquette.BulkExportUri is { } bulk)
        {
            return new FetchDecision(FetchRoute.BulkExport, bulk, null);
        }

        if (etiquette.ApiUri is { } api)
        {
            return new FetchDecision(FetchRoute.Api, api, null);
        }

        if (etiquette.ScrapeUri is { } scrape)
        {
            return etiquette.ContactedMaintainer
                ? new FetchDecision(FetchRoute.Scrape, scrape, null)
                : new FetchDecision(FetchRoute.None, null, MaintainerNotContacted);
        }

        return new FetchDecision(FetchRoute.None, null, NothingConfigured);
    }

    /// <summary>
    /// Whether one specific URI may be fetched. Only URIs under the chosen route's root qualify, so
    /// a configured <c>ScrapeUri</c> is unreachable the moment a bulk export or an API exists — which
    /// is the whole rule, expressed so that no importer can accidentally opt out of it.
    /// </summary>
    public static bool MayFetch(ImportEtiquette etiquette, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(etiquette);
        ArgumentNullException.ThrowIfNull(uri);

        return Decide(etiquette).Uri is { } allowed && IsUnder(allowed, uri);
    }

    private static bool IsUnder(Uri root, Uri candidate) =>
        Uri.Compare(root, candidate, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped,
            StringComparison.OrdinalIgnoreCase) == 0
        && candidate.AbsolutePath.StartsWith(root.AbsolutePath, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 13 tests.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Backfill/ImportEtiquette.cs src/MUI.Backfill/EtiquettePlanner.cs tests/MUI.Backfill.Tests/EtiquettePlannerTests.cs
git commit -m "feat(backfill): prefer a bulk export or documented API over scraping, in code"
```

---

### Task 3: `robots.txt` and the rate limit

**Files:**
- Create: `src/MUI.Backfill/RobotsPolicy.cs`
- Create: `src/MUI.Backfill/PolitenessGate.cs`
- Create: `tests/MUI.Backfill.Tests/Support/ManualTimeProvider.cs`
- Create: `tests/MUI.Backfill.Tests/RobotsPolicyTests.cs`
- Create: `tests/MUI.Backfill.Tests/PolitenessGateTests.cs`

**Interfaces:**
- Consumes: `ImportEtiquette` (Task 2).
- Produces: `RobotsPolicy.Parse(string) → RobotsPolicy`, `RobotsPolicy.AllowAll`,
  `.Allows(string path, string userAgent) → bool`, `.CrawlDelayFor(string userAgent) → TimeSpan?`;
  `PolitenessGate(ImportEtiquette etiquette, TimeProvider time)` with `.RobotsAdopted`,
  `.LastFetchAt`, `.EffectiveInterval`, `.AdoptRobots(RobotsPolicy)`, `.MayFetch(string path)`,
  `.WaitFor(DateTimeOffset now) → TimeSpan`, `.EnterAsync(CancellationToken)`;
  `Support.ManualTimeProvider(DateTimeOffset start)` with `.Advance(TimeSpan)`.

- [ ] **Step 1: Write the failing robots test**

`tests/MUI.Backfill.Tests/RobotsPolicyTests.cs`:

```csharp
namespace MUI.Backfill.Tests;

/// <summary>
/// Spec §7.6: honour <c>robots.txt</c>. Not as a courtesy note in a README — as the thing that
/// decides whether a fetch happens.
/// </summary>
public class RobotsPolicyTests
{
    private const string Ua = "MUIndex-Importer/1.0 (+https://muindex.org/crawler)";

    private const string Sample = """
        # example directory
        User-agent: *
        Disallow: /admin/
        Disallow: /search
        Crawl-delay: 10

        User-agent: MUIndex-Importer
        Allow: /search/games
        Disallow: /search
        Crawl-delay: 30
        """;

    [Test]
    public async Task APathNoGroupForbidsIsAllowed()
    {
        var policy = RobotsPolicy.Parse(Sample);

        await Assert.That(policy.Allows("/dumps/games.json", Ua)).IsTrue();
    }

    [Test]
    public async Task OurOwnGroupIsPreferredOverTheWildcardOne()
    {
        var policy = RobotsPolicy.Parse(Sample);

        await Assert.That(policy.CrawlDelayFor(Ua)).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(policy.CrawlDelayFor("SomeoneElse/2.0")).IsEqualTo(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task TheLongerRuleWinsWhenAllowAndDisallowBothMatch()
    {
        var policy = RobotsPolicy.Parse(Sample);

        await Assert.That(policy.Allows("/search/games", Ua)).IsTrue();
        await Assert.That(policy.Allows("/search/players", Ua)).IsFalse();
    }

    [Test]
    public async Task AWildcardRuleMatchesInTheMiddleAndADollarAnchorsTheEnd()
    {
        var policy = RobotsPolicy.Parse("""
            User-agent: *
            Disallow: /*.php$
            """);

        await Assert.That(policy.Allows("/list/index.php", Ua)).IsFalse();
        await Assert.That(policy.Allows("/list/index.php?page=2", Ua)).IsTrue();
        await Assert.That(policy.Allows("/list/index.json", Ua)).IsTrue();
    }

    [Test]
    public async Task AnEmptyDisallowForbidsNothing()
    {
        var policy = RobotsPolicy.Parse("""
            User-agent: *
            Disallow:
            """);

        await Assert.That(policy.Allows("/anything", Ua)).IsTrue();
    }

    [Test]
    public async Task CommentsAndBlankLinesAreIgnoredAndABlankFileAllowsEverything()
    {
        var policy = RobotsPolicy.Parse("   \n# nothing to see\n\n");

        await Assert.That(policy.Allows("/anything", Ua)).IsTrue();
        await Assert.That(policy.CrawlDelayFor(Ua)).IsNull();
    }

    [Test]
    public async Task AllowAllIsWhatAMissingRobotsFileMeans()
    {
        await Assert.That(RobotsPolicy.AllowAll.Allows("/anything", Ua)).IsTrue();
        await Assert.That(RobotsPolicy.AllowAll.CrawlDelayFor(Ua)).IsNull();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'RobotsPolicy' could not be found`.

- [ ] **Step 3: Write `RobotsPolicy`**

`src/MUI.Backfill/RobotsPolicy.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace MUI.Backfill;

/// <summary>
/// A parsed <c>robots.txt</c>. Group selection is longest-matching user-agent token, falling back to
/// <c>*</c>; path matching is longest-rule-wins between <c>Allow</c> and <c>Disallow</c>, with
/// <c>*</c> and <c>$</c> honoured.
/// </summary>
public sealed class RobotsPolicy
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly IReadOnlyList<RobotsGroup> _groups;

    private RobotsPolicy(IReadOnlyList<RobotsGroup> groups) => _groups = groups;

    /// <summary>What a missing or unreadable <c>robots.txt</c> means: nothing is forbidden.</summary>
    public static RobotsPolicy AllowAll { get; } = new([]);

    public static RobotsPolicy Parse(string robotsTxt)
    {
        ArgumentNullException.ThrowIfNull(robotsTxt);

        var groups = new List<RobotsGroup>();
        RobotsGroup? current = null;
        var acceptingAgents = false;

        foreach (var rawLine in robotsTxt.Split('\n'))
        {
            var line = rawLine;
            var hash = line.IndexOf('#', StringComparison.Ordinal);
            if (hash >= 0)
            {
                line = line[..hash];
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            switch (key)
            {
                case "user-agent":
                    if (current is null || !acceptingAgents)
                    {
                        current = new RobotsGroup();
                        groups.Add(current);
                        acceptingAgents = true;
                    }

                    current.Agents.Add(value.ToLowerInvariant());
                    break;

                case "disallow":
                    acceptingAgents = false;
                    if (current is not null && value.Length > 0)
                    {
                        current.Disallow.Add(value);
                    }

                    break;

                case "allow":
                    acceptingAgents = false;
                    if (current is not null && value.Length > 0)
                    {
                        current.Allow.Add(value);
                    }

                    break;

                case "crawl-delay":
                    acceptingAgents = false;
                    if (current is not null
                        && double.TryParse(value, CultureInfo.InvariantCulture, out var seconds)
                        && seconds > 0)
                    {
                        current.CrawlDelay = TimeSpan.FromSeconds(seconds);
                    }

                    break;

                default:
                    break;
            }
        }

        return new RobotsPolicy(groups);
    }

    public bool Allows(string path, string userAgent)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(userAgent);

        if (GroupFor(userAgent) is not { } group)
        {
            return true;
        }

        var disallow = LongestMatch(group.Disallow, path);
        if (disallow < 0)
        {
            return true;
        }

        return LongestMatch(group.Allow, path) >= disallow;
    }

    public TimeSpan? CrawlDelayFor(string userAgent)
    {
        ArgumentNullException.ThrowIfNull(userAgent);
        return GroupFor(userAgent)?.CrawlDelay;
    }

    private RobotsGroup? GroupFor(string userAgent)
    {
        var token = Token(userAgent);
        RobotsGroup? best = null;
        var bestLength = -1;

        foreach (var group in _groups)
        {
            foreach (var agent in group.Agents)
            {
                if (agent == "*")
                {
                    if (bestLength < 0)
                    {
                        best = group;
                        bestLength = 0;
                    }

                    continue;
                }

                if (token.StartsWith(agent, StringComparison.Ordinal) && agent.Length > bestLength)
                {
                    best = group;
                    bestLength = agent.Length;
                }
            }
        }

        return best;
    }

    private static string Token(string userAgent)
    {
        var slash = userAgent.IndexOf('/', StringComparison.Ordinal);
        var head = slash < 0 ? userAgent : userAgent[..slash];
        return head.Trim().ToLowerInvariant();
    }

    private static int LongestMatch(IReadOnlyList<string> rules, string path)
    {
        var best = -1;

        foreach (var rule in rules)
        {
            if (Matches(rule, path) && rule.Length > best)
            {
                best = rule.Length;
            }
        }

        return best;
    }

    private static bool Matches(string rule, string path)
    {
        if (!rule.Contains('*', StringComparison.Ordinal) && !rule.EndsWith('$'))
        {
            return path.StartsWith(rule, StringComparison.Ordinal);
        }

        var pattern = "^" + Regex.Escape(rule).Replace("\\*", ".*", StringComparison.Ordinal);
        if (pattern.EndsWith("\\$", StringComparison.Ordinal))
        {
            pattern = pattern[..^2] + "$";
        }

        return Regex.IsMatch(path, pattern, RegexOptions.None, MatchTimeout);
    }

    private sealed class RobotsGroup
    {
        public List<string> Agents { get; } = [];

        public List<string> Disallow { get; } = [];

        public List<string> Allow { get; } = [];

        public TimeSpan? CrawlDelay { get; set; }
    }
}
```

- [ ] **Step 4: Run the robots tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 20 tests.

- [ ] **Step 5: Write the manual clock and the failing gate test**

`tests/MUI.Backfill.Tests/Support/ManualTimeProvider.cs`:

```csharp
namespace MUI.Backfill.Tests.Support;

/// <summary>
/// A clock the test moves by hand. Every rate-limit assertion in this suite is driven through this
/// rather than through wall time, so the suite is deterministic and instant.
/// </summary>
/// <remarks>
/// Only <see cref="GetUtcNow"/> is overridden. That is sufficient because <c>PolitenessGate</c>
/// exposes its wait as a pure function of "now" (<c>WaitFor</c>) and only ever sleeps when that
/// function returns a positive span — which no test in this suite arranges.
/// <para>
/// Deliberately not named <c>FakeTimeProvider</c>, to avoid being mistaken for
/// <c>Microsoft.Extensions.Time.Testing.FakeTimeProvider</c>, which is a real type this project does
/// not reference. Plans 02 and 03 spell their own manual clocks the same way.
/// </para>
/// </remarks>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
```

`tests/MUI.Backfill.Tests/PolitenessGateTests.cs`:

```csharp
using MUI.Backfill.Tests.Support;

namespace MUI.Backfill.Tests;

/// <summary>
/// Spec §7.6: rate-limit hard, and adopt the site's own <c>Crawl-delay</c> when it asks for more
/// room than we planned to give it. Driven entirely by an injected clock.
/// </summary>
public class PolitenessGateTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static ImportEtiquette Etiquette(TimeSpan minimum) => new()
    {
        SourceName = "Example Directory",
        AttributionUri = new Uri("https://example.test/"),
        RobotsUri = new Uri("https://example.test/robots.txt"),
        ApiUri = new Uri("https://example.test/api/games"),
        UserAgent = CrawlerIdentity.UserAgent,
        MinimumInterval = minimum,
    };

    [Test]
    public async Task NothingMayBeFetchedUntilRobotsHasBeenRead()
    {
        var gate = new PolitenessGate(Etiquette(TimeSpan.FromSeconds(5)), new ManualTimeProvider(Start));

        await Assert.That(gate.RobotsAdopted).IsFalse();
        await Assert.That(gate.MayFetch("/api/games")).IsFalse();

        gate.AdoptRobots(RobotsPolicy.AllowAll);

        await Assert.That(gate.RobotsAdopted).IsTrue();
        await Assert.That(gate.MayFetch("/api/games")).IsTrue();
    }

    [Test]
    public async Task TheFirstFetchWaitsForNothingAndIsRecorded()
    {
        var time = new ManualTimeProvider(Start);
        var gate = new PolitenessGate(Etiquette(TimeSpan.FromSeconds(5)), time);
        gate.AdoptRobots(RobotsPolicy.AllowAll);

        await Assert.That(gate.WaitFor(Start)).IsEqualTo(TimeSpan.Zero);

        await gate.EnterAsync(CancellationToken.None);

        await Assert.That(gate.LastFetchAt).IsEqualTo(Start);
    }

    [Test]
    public async Task TheSecondFetchWaitsOutTheRemainderOfTheInterval()
    {
        var time = new ManualTimeProvider(Start);
        var gate = new PolitenessGate(Etiquette(TimeSpan.FromSeconds(5)), time);
        gate.AdoptRobots(RobotsPolicy.AllowAll);
        await gate.EnterAsync(CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(2));

        await Assert.That(gate.WaitFor(time.GetUtcNow())).IsEqualTo(TimeSpan.FromSeconds(3));

        time.Advance(TimeSpan.FromSeconds(3));

        await Assert.That(gate.WaitFor(time.GetUtcNow())).IsEqualTo(TimeSpan.Zero);

        await gate.EnterAsync(CancellationToken.None);

        await Assert.That(gate.LastFetchAt).IsEqualTo(Start + TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ALongerCrawlDelayInRobotsReplacesOurConfiguredMinimum()
    {
        var gate = new PolitenessGate(Etiquette(TimeSpan.FromSeconds(5)), new ManualTimeProvider(Start));
        gate.AdoptRobots(RobotsPolicy.Parse("""
            User-agent: *
            Crawl-delay: 30
            """));

        await Assert.That(gate.EffectiveInterval).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task AShorterCrawlDelayDoesNotLicenceUsToGoFaster()
    {
        var gate = new PolitenessGate(Etiquette(TimeSpan.FromSeconds(5)), new ManualTimeProvider(Start));
        gate.AdoptRobots(RobotsPolicy.Parse("""
            User-agent: *
            Crawl-delay: 1
            """));

        await Assert.That(gate.EffectiveInterval).IsEqualTo(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ADisallowedPathIsRefusedEvenAfterRobotsIsAdopted()
    {
        var gate = new PolitenessGate(Etiquette(TimeSpan.FromSeconds(5)), new ManualTimeProvider(Start));
        gate.AdoptRobots(RobotsPolicy.Parse("""
            User-agent: *
            Disallow: /api/
            """));

        await Assert.That(gate.MayFetch("/api/games")).IsFalse();
        await Assert.That(gate.MayFetch("/dumps/games.json")).IsTrue();
    }
}
```

- [ ] **Step 6: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'PolitenessGate' could not be found`.

- [ ] **Step 7: Write `PolitenessGate`**

`src/MUI.Backfill/PolitenessGate.cs`:

```csharp
namespace MUI.Backfill;

/// <summary>
/// One directory's rate limit and its <c>robots.txt</c>, held together because they answer the same
/// question: may we fetch this, and not before when?
/// </summary>
/// <remarks>
/// <see cref="MayFetch"/> answers <c>false</c> until <see cref="AdoptRobots"/> has been called. That
/// is deliberate and is spec §7.6's "honour <c>robots.txt</c>" made unskippable: the gate is closed
/// by default, and reading the file is what opens it.
/// </remarks>
public sealed class PolitenessGate(ImportEtiquette etiquette, TimeProvider time)
{
    private readonly ImportEtiquette _etiquette = etiquette ?? throw new ArgumentNullException(nameof(etiquette));
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));

    private RobotsPolicy? _robots;
    private DateTimeOffset? _lastFetchAt;

    public bool RobotsAdopted => _robots is not null;

    public DateTimeOffset? LastFetchAt => _lastFetchAt;

    /// <summary>The configured minimum, or the site's own <c>Crawl-delay</c> when that asks for more.</summary>
    public TimeSpan EffectiveInterval
    {
        get
        {
            var declared = _robots?.CrawlDelayFor(_etiquette.UserAgent);
            return declared is { } delay && delay > _etiquette.MinimumInterval
                ? delay
                : _etiquette.MinimumInterval;
        }
    }

    public void AdoptRobots(RobotsPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _robots = policy;
    }

    public bool MayFetch(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _robots is not null && _robots.Allows(path, _etiquette.UserAgent);
    }

    /// <summary>How long a fetch at <paramref name="now"/> would have to wait. Pure, and the test seam.</summary>
    public TimeSpan WaitFor(DateTimeOffset now)
    {
        if (_lastFetchAt is not { } last)
        {
            return TimeSpan.Zero;
        }

        var remaining = EffectiveInterval - (now - last);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public async Task EnterAsync(CancellationToken ct)
    {
        var wait = WaitFor(_time.GetUtcNow());
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, _time, ct).ConfigureAwait(false);
        }

        _lastFetchAt = _time.GetUtcNow();
    }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 26 tests.

- [ ] **Step 9: Commit**

```bash
git add src/MUI.Backfill/RobotsPolicy.cs src/MUI.Backfill/PolitenessGate.cs tests/MUI.Backfill.Tests
git commit -m "feat(backfill): honour robots.txt and rate-limit on an injected clock"
```

---

### Task 4: `DirectorySource` — the only place an HTTP request is made

**Files:**
- Create: `src/MUI.Backfill/DirectorySource.cs`
- Create: `tests/MUI.Backfill.Tests/Support/FakeHttp.cs`
- Create: `tests/MUI.Backfill.Tests/Support/Fixture.cs`
- Create: `tests/MUI.Backfill.Tests/DirectorySourceTests.cs`

**Interfaces:**
- Consumes: `ImportEtiquette`, `EtiquettePlanner`, `EtiquetteViolationException`, `FetchRoute`,
  `FetchDecision` (Task 2); `RobotsPolicy`, `PolitenessGate` (Task 3);
  `Support.ManualTimeProvider` (Task 3).
- Produces: `DirectorySource(HttpClient http, ImportEtiquette etiquette, TimeProvider time)` with
  `.Etiquette`, `.Gate`, `.Decision`, `.PrimeRobotsAsync(CancellationToken)`,
  `.GetStringAsync(Uri, CancellationToken) → Task<string>`;
  `Support.FakeHttp.Handler` (an `HttpMessageHandler` with `.Requests` as
  `List<(string Uri, string? UserAgent)>` and a `.Client()` factory);
  `Support.Fixture.Read(string name) → string`.

- [ ] **Step 1: Write the fixture loader and the fake handler**

`tests/MUI.Backfill.Tests/Support/Fixture.cs`:

```csharp
namespace MUI.Backfill.Tests.Support;

/// <summary>
/// Reads a recorded payload out of <c>Fixtures/</c>. Nothing in this suite fetches anything; every
/// byte an importer sees comes from a file committed beside its test.
/// </summary>
public static class Fixture
{
    public static string Read(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        return File.ReadAllText(path);
    }
}
```

`tests/MUI.Backfill.Tests/Support/FakeHttp.cs`:

```csharp
using System.Net;

namespace MUI.Backfill.Tests.Support;

/// <summary>
/// The only <c>HttpMessageHandler</c> in this suite. It serves a dictionary of canned responses and
/// records what was asked for, in order, together with the user agent the request carried.
/// </summary>
public static class FakeHttp
{
    public sealed class Handler(IReadOnlyDictionary<string, (HttpStatusCode Status, string Body)> responses)
        : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, (HttpStatusCode Status, string Body)> _responses =
            responses ?? throw new ArgumentNullException(nameof(responses));

        public List<(string Uri, string? UserAgent)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            var userAgent = request.Headers.TryGetValues("User-Agent", out var values)
                ? string.Join(' ', values)
                : null;

            Requests.Add((uri, userAgent));

            var (status, body) = _responses.TryGetValue(uri, out var found)
                ? found
                : (HttpStatusCode.NotFound, string.Empty);

            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    public static (Handler Handler, HttpClient Client) Serving(params (string Uri, string Body)[] responses)
    {
        ArgumentNullException.ThrowIfNull(responses);

        var map = responses.ToDictionary(r => r.Uri, r => (HttpStatusCode.OK, r.Body));
        var handler = new Handler(map);
        return (handler, new HttpClient(handler));
    }
}
```

- [ ] **Step 2: Write the failing test**

`tests/MUI.Backfill.Tests/DirectorySourceTests.cs`:

```csharp
using MUI.Backfill.Tests.Support;

namespace MUI.Backfill.Tests;

/// <summary>
/// The etiquette rules of spec §7.6 and §11 at the point they actually bite — the moment an HTTP
/// request would be made.
/// </summary>
public class DirectorySourceTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static ImportEtiquette Etiquette() => new()
    {
        SourceName = "Example Directory",
        AttributionUri = new Uri("https://example.test/"),
        RobotsUri = new Uri("https://example.test/robots.txt"),
        ApiUri = new Uri("https://example.test/api/games"),
        UserAgent = CrawlerIdentity.UserAgent,
    };

    [Test]
    public async Task AContentFetchBeforeRobotsIsRefused()
    {
        var (_, client) = FakeHttp.Serving(("https://example.test/api/games", "{}"));
        var source = new DirectorySource(client, Etiquette(), new ManualTimeProvider(Start));

        await Assert.That(async () => await source.GetStringAsync(new Uri("https://example.test/api/games"), CancellationToken.None))
            .Throws<EtiquetteViolationException>();
    }

    [Test]
    public async Task RobotsIsTheFirstRequestWeEverMakeToASite()
    {
        var (handler, client) = FakeHttp.Serving(
            ("https://example.test/robots.txt", "User-agent: *\nDisallow:\n"),
            ("https://example.test/api/games", "{}"));
        var source = new DirectorySource(client, Etiquette(), new ManualTimeProvider(Start));

        await source.PrimeRobotsAsync(CancellationToken.None);
        await source.GetStringAsync(new Uri("https://example.test/api/games"), CancellationToken.None);

        await Assert.That(handler.Requests[0].Uri).IsEqualTo("https://example.test/robots.txt");
        await Assert.That(handler.Requests[1].Uri).IsEqualTo("https://example.test/api/games");
    }

    [Test]
    public async Task EveryRequestCarriesAUserAgentThatSaysWhoWeAre()
    {
        var (handler, client) = FakeHttp.Serving(
            ("https://example.test/robots.txt", "User-agent: *\nDisallow:\n"),
            ("https://example.test/api/games", "{}"));
        var source = new DirectorySource(client, Etiquette(), new ManualTimeProvider(Start));

        await source.PrimeRobotsAsync(CancellationToken.None);
        await source.GetStringAsync(new Uri("https://example.test/api/games"), CancellationToken.None);

        foreach (var request in handler.Requests)
        {
            await Assert.That(request.UserAgent).IsNotNull();
            await Assert.That(CrawlerIdentity.SelfIdentifies(request.UserAgent!)).IsTrue();
        }
    }

    [Test]
    public async Task TheScrapeUriIsNeverFetchedWhileAnApiIsConfigured()
    {
        var etiquette = Etiquette() with
        {
            ScrapeUri = new Uri("https://example.test/list"),
            ContactedMaintainer = true,
        };
        var (handler, client) = FakeHttp.Serving(
            ("https://example.test/robots.txt", "User-agent: *\nDisallow:\n"),
            ("https://example.test/list", "<html></html>"));
        var source = new DirectorySource(client, etiquette, new ManualTimeProvider(Start));
        await source.PrimeRobotsAsync(CancellationToken.None);

        await Assert.That(async () => await source.GetStringAsync(new Uri("https://example.test/list"), CancellationToken.None))
            .Throws<EtiquetteViolationException>();

        await Assert.That(handler.Requests.Any(r => r.Uri == "https://example.test/list")).IsFalse();
    }

    [Test]
    public async Task ADisallowedPathIsNeverFetched()
    {
        var (handler, client) = FakeHttp.Serving(
            ("https://example.test/robots.txt", "User-agent: *\nDisallow: /api/\n"),
            ("https://example.test/api/games", "{}"));
        var source = new DirectorySource(client, Etiquette(), new ManualTimeProvider(Start));
        await source.PrimeRobotsAsync(CancellationToken.None);

        await Assert.That(async () => await source.GetStringAsync(new Uri("https://example.test/api/games"), CancellationToken.None))
            .Throws<EtiquetteViolationException>();

        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AMissingRobotsFileMeansAllowAllRatherThanRefuseAll()
    {
        var (_, client) = FakeHttp.Serving(("https://example.test/api/games", "{}"));
        var source = new DirectorySource(client, Etiquette(), new ManualTimeProvider(Start));

        await source.PrimeRobotsAsync(CancellationToken.None);

        await Assert.That(source.Gate.RobotsAdopted).IsTrue();
        await Assert.That(await source.GetStringAsync(new Uri("https://example.test/api/games"), CancellationToken.None))
            .IsEqualTo("{}");
    }

    [Test]
    public async Task ARobotsCrawlDelayIsAdoptedByTheGate()
    {
        var (_, client) = FakeHttp.Serving(
            ("https://example.test/robots.txt", "User-agent: *\nCrawl-delay: 45\n"));
        var source = new DirectorySource(client, Etiquette(), new ManualTimeProvider(Start));

        await source.PrimeRobotsAsync(CancellationToken.None);

        await Assert.That(source.Gate.EffectiveInterval).IsEqualTo(TimeSpan.FromSeconds(45));
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'DirectorySource' could not be found`.

- [ ] **Step 4: Write `DirectorySource`**

`src/MUI.Backfill/DirectorySource.cs`:

```csharp
namespace MUI.Backfill;

/// <summary>
/// One directory's HTTP surface. Every importer fetches through this and nothing else, so the
/// etiquette rules are enforced once rather than remembered six times.
/// </summary>
/// <remarks>
/// Four refusals live here, in order: robots has not been read yet; the source has no permitted
/// route at all; this URI is not under the route the planner chose (which is what makes a configured
/// <c>ScrapeUri</c> unreachable while a bulk export or API exists); and <c>robots.txt</c> forbids
/// the path. Only after all four does the rate limit run.
/// </remarks>
public sealed class DirectorySource
{
    private readonly HttpClient _http;

    public DirectorySource(HttpClient http, ImportEtiquette etiquette, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(etiquette);
        ArgumentNullException.ThrowIfNull(time);

        _http = http;
        Etiquette = etiquette;
        Gate = new PolitenessGate(etiquette, time);
    }

    public ImportEtiquette Etiquette { get; }

    public PolitenessGate Gate { get; }

    public FetchDecision Decision => EtiquettePlanner.Decide(Etiquette);

    /// <summary>
    /// Reads <c>robots.txt</c> and hands it to the gate. Must happen before the first content fetch;
    /// an unreadable or missing file is <see cref="RobotsPolicy.AllowAll"/>, not a refusal.
    /// </summary>
    public async Task PrimeRobotsAsync(CancellationToken ct)
    {
        using var request = Request(Etiquette.RobotsUri);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            Gate.AdoptRobots(RobotsPolicy.Parse(body));
            return;
        }

        Gate.AdoptRobots(RobotsPolicy.AllowAll);
    }

    public async Task<string> GetStringAsync(Uri uri, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!Gate.RobotsAdopted)
        {
            throw new EtiquetteViolationException(
                $"{Etiquette.SourceName}: robots.txt has not been read. Call PrimeRobotsAsync before the first content fetch.");
        }

        var decision = Decision;
        if (decision.Route is FetchRoute.None)
        {
            throw new EtiquetteViolationException($"{Etiquette.SourceName}: {decision.RefusedReason}.");
        }

        if (!EtiquettePlanner.MayFetch(Etiquette, uri))
        {
            throw new EtiquetteViolationException(
                $"{Etiquette.SourceName}: refusing {uri} — the permitted route is {decision.Route} rooted at {decision.Uri}.");
        }

        if (!Gate.MayFetch(uri.AbsolutePath))
        {
            throw new EtiquetteViolationException(
                $"{Etiquette.SourceName}: robots.txt disallows {uri.AbsolutePath} for {Etiquette.UserAgent}.");
        }

        await Gate.EnterAsync(ct).ConfigureAwait(false);

        using var request = Request(uri);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", Etiquette.UserAgent);
        return request;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 33 tests.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Backfill/DirectorySource.cs tests/MUI.Backfill.Tests
git commit -m "feat(backfill): route every import fetch through one etiquette-enforcing client"
```

---

### Task 5: The provenance sidecar — which site said this, and when

**Files:**
- Create: `src/MUI.Storage/Migrations/0100_import_provenance.sql`
- Create: `src/MUI.Backfill/ImportProvenance.cs`
- Create: `src/MUI.Backfill/NpgsqlImportProvenanceRepository.cs`
- Create: `tests/MUI.Backfill.Tests/Support/InMemoryImportProvenanceRepository.cs`
- Create: `tests/MUI.Backfill.Tests/ImportProvenanceRepositoryTests.cs`

**Interfaces:**
- Consumes: `MUI.Storage.MigrationRunner(NpgsqlDataSource, ILogger?)` with
  `.ApplyAsync(CancellationToken)` (Plan 2); `ImportTier` (Task 1).
- Produces: `enum ImportSubjectKind { Field, Presence, Availability, Endpoint }`;
  `ImportProvenance(long Id, Guid GameId, ImportSubjectKind SubjectKind, string? SubjectField,
  DateTimeOffset? SubjectAt, string SourceName, string SourceKey, Uri? SourceUri, ImportTier Tier,
  DateTimeOffset ImportedAt)`;
  `IImportProvenanceRepository` with `.RecordAsync(ImportProvenance, CancellationToken) → Task<long>`,
  `.ExistsAsync(Guid gameId, ImportSubjectKind kind, string? field, DateTimeOffset? at, string sourceName, CancellationToken) → Task<bool>`,
  `.ForGameAsync(Guid gameId, CancellationToken) → Task<IReadOnlyList<ImportProvenance>>`;
  `NpgsqlImportProvenanceRepository(NpgsqlDataSource source)`;
  `Support.InMemoryImportProvenanceRepository` with a public `Rows` list.

**Why this table exists:** spec §7.6 requires every imported value to carry the originating site and
the import date. `GameField` carries a `FieldSource` but not a site name; `PresenceSample` carries a
`PresenceSource` but not a site name; `AvailabilityInterval` carries neither. §7.5 additionally needs
imported reachable time separated from ours, and `IAvailabilityRepository.CumulativeReachableAsync`
returns one undifferentiated number. One sidecar answers all three, and changes no contract type.
It carries no foreign key to `game` deliberately: `game` is Plan 2's table, and a cross-plan FK from
a Plan 4 migration would couple the two plans' migration order for no gain the unique index does not
already provide.

- [ ] **Step 1: Write the migration**

`src/MUI.Storage/Migrations/0100_import_provenance.sql`:

```sql
-- Spec §7.6: "Every imported value carries the originating site and the import date in its
-- provenance chip." No catalogue record has anywhere to put that, so it lives beside them.
--
-- NOT the half-weight mechanism. §7.5's separation of imported reachable time from ours is carried by
-- availability_interval.origin (Plan 02), which ArchiveSweeper reads; nothing may compute grace from
-- this table, or the same history is counted twice. What subject_at buys is traceability — which site
-- an interval came from, and when we took it — for the provenance chip and the attribution list.
--
-- No FK to game(id): that table belongs to an earlier plan and coupling the migration order buys
-- nothing the unique index below does not already give us.
CREATE TABLE IF NOT EXISTS import_provenance (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    game_id       uuid        NOT NULL,
    subject_kind  text        NOT NULL CHECK (subject_kind IN ('field', 'presence', 'availability', 'endpoint')),
    subject_field text        NULL,
    subject_at    timestamptz NULL,
    source_name   text        NOT NULL,
    source_key    text        NOT NULL,
    source_uri    text        NULL,
    tier          text        NOT NULL CHECK (tier IN ('measured', 'asserted')),
    imported_at   timestamptz NOT NULL
);

-- Re-running the backfill must not duplicate a stamp. COALESCE keeps the index usable for the two
-- kinds that leave one of the subject columns null.
CREATE UNIQUE INDEX IF NOT EXISTS import_provenance_subject_uniq
    ON import_provenance (
        game_id,
        subject_kind,
        source_name,
        COALESCE(subject_field, ''),
        COALESCE(subject_at, '-infinity'::timestamptz)
    );

CREATE INDEX IF NOT EXISTS import_provenance_game_idx ON import_provenance (game_id);
```

- [ ] **Step 2: Write the record and the repository interface**

`src/MUI.Backfill/ImportProvenance.cs`:

```csharp
namespace MUI.Backfill;

/// <summary>What an import stamp is attached to.</summary>
public enum ImportSubjectKind
{
    Field,
    Presence,
    Availability,
    Endpoint,
}

/// <summary>
/// One imported value's origin: which site said it, that site's own key for the game, the page it
/// came from, and when we took it. Spec §7.6 — imported facts are never laundered into looking
/// first-party, and this is the row that makes that checkable rather than merely intended.
/// </summary>
public sealed record ImportProvenance(
    long Id,
    Guid GameId,
    ImportSubjectKind SubjectKind,
    string? SubjectField,
    DateTimeOffset? SubjectAt,
    string SourceName,
    string SourceKey,
    Uri? SourceUri,
    ImportTier Tier,
    DateTimeOffset ImportedAt);

public interface IImportProvenanceRepository
{
    Task<long> RecordAsync(ImportProvenance provenance, CancellationToken ct);

    /// <summary>Whether this exact subject was already stamped by this source. Makes re-import a no-op.</summary>
    Task<bool> ExistsAsync(
        Guid gameId,
        ImportSubjectKind kind,
        string? field,
        DateTimeOffset? at,
        string sourceName,
        CancellationToken ct);

    Task<IReadOnlyList<ImportProvenance>> ForGameAsync(Guid gameId, CancellationToken ct);
}
```

- [ ] **Step 3: Write the failing integration test**

`tests/MUI.Backfill.Tests/ImportProvenanceRepositoryTests.cs`:

```csharp
using MUI.Storage;
using Npgsql;
using Testcontainers.PostgreSql;

using static TUnit.Core.HookType;

namespace MUI.Backfill.Tests;

/// <summary>
/// The one test in this suite that talks to a real Postgres. Gated on MUI_INTEGRATION so the suite
/// still runs where there is no Linux Docker daemon; CI sets it on ubuntu only.
/// </summary>
public class ImportProvenanceRepositoryTests
{
    private static PostgreSqlContainer? _container;
    private static NpgsqlDataSource? _source;

    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("MUI_INTEGRATION"), "1", StringComparison.Ordinal);

    [Before(Class)]
    public static async Task StartPostgres()
    {
        if (!Enabled)
        {
            return;
        }

        _container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
        await _container.StartAsync();

        _source = NpgsqlDataSource.Create(_container.GetConnectionString());
        await new MigrationRunner(_source).ApplyAsync(CancellationToken.None);
    }

    [After(Class)]
    public static async Task StopPostgres()
    {
        if (_source is not null)
        {
            await _source.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Test]
    public async Task AStampRoundTripsWithItsSiteAndItsImportDate()
    {
        if (!Enabled)
        {
            return;
        }

        var repository = new NpgsqlImportProvenanceRepository(_source!);
        var gameId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 7, 29, 22, 0, 0, TimeSpan.Zero);

        await repository.RecordAsync(new ImportProvenance(
            0, gameId, ImportSubjectKind.Presence, null, at,
            "MudVerse", "anachronism", new Uri("https://mudverse.test/g/anachronism"),
            ImportTier.Measured, new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        var rows = await repository.ForGameAsync(gameId, CancellationToken.None);

        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].SourceName).IsEqualTo("MudVerse");
        await Assert.That(rows[0].SourceKey).IsEqualTo("anachronism");
        await Assert.That(rows[0].SourceUri).IsEqualTo(new Uri("https://mudverse.test/g/anachronism"));
        await Assert.That(rows[0].Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(rows[0].SubjectAt).IsEqualTo(at);
        await Assert.That(rows[0].Id).IsGreaterThan(0);
    }

    [Test]
    public async Task StampingTheSameSubjectTwiceLeavesOneRow()
    {
        if (!Enabled)
        {
            return;
        }

        var repository = new NpgsqlImportProvenanceRepository(_source!);
        var gameId = Guid.NewGuid();
        var stamp = new ImportProvenance(
            0, gameId, ImportSubjectKind.Field, "codebase", null,
            "MudStats", "812", null, ImportTier.Measured,
            new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));

        await repository.RecordAsync(stamp, CancellationToken.None);
        await repository.RecordAsync(stamp with { ImportedAt = stamp.ImportedAt.AddDays(1) }, CancellationToken.None);

        var rows = await repository.ForGameAsync(gameId, CancellationToken.None);

        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(await repository.ExistsAsync(gameId, ImportSubjectKind.Field, "codebase", null, "MudStats", CancellationToken.None))
            .IsTrue();
        await Assert.That(await repository.ExistsAsync(gameId, ImportSubjectKind.Field, "genre", null, "MudStats", CancellationToken.None))
            .IsFalse();
    }

    [Test]
    public async Task TwoSourcesMayBothStampTheSameSubject()
    {
        if (!Enabled)
        {
            return;
        }

        var repository = new NpgsqlImportProvenanceRepository(_source!);
        var gameId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 7, 29, 22, 0, 0, TimeSpan.Zero);
        var importedAt = new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero);

        await repository.RecordAsync(new ImportProvenance(0, gameId, ImportSubjectKind.Presence, null, at,
            "MudVerse", "anachronism", null, ImportTier.Measured, importedAt), CancellationToken.None);
        await repository.RecordAsync(new ImportProvenance(0, gameId, ImportSubjectKind.Presence, null, at,
            "MudStats", "812", null, ImportTier.Measured, importedAt), CancellationToken.None);

        var rows = await repository.ForGameAsync(gameId, CancellationToken.None);

        await Assert.That(rows.Count).IsEqualTo(2);
    }
}
```

- [ ] **Step 4: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'NpgsqlImportProvenanceRepository' could not be found`.

- [ ] **Step 5: Write the Dapper repository**

`src/MUI.Backfill/NpgsqlImportProvenanceRepository.cs`:

```csharp
using Dapper;
using Npgsql;

namespace MUI.Backfill;

/// <summary>Dapper over <c>import_provenance</c>. Recording is idempotent by unique index.</summary>
public sealed class NpgsqlImportProvenanceRepository(NpgsqlDataSource source) : IImportProvenanceRepository
{
    private readonly NpgsqlDataSource _source = source ?? throw new ArgumentNullException(nameof(source));

    public async Task<long> RecordAsync(ImportProvenance provenance, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        const string sql = """
            INSERT INTO import_provenance
                (game_id, subject_kind, subject_field, subject_at,
                 source_name, source_key, source_uri, tier, imported_at)
            VALUES (@GameId, @SubjectKind, @SubjectField, @SubjectAt,
                    @SourceName, @SourceKey, @SourceUri, @Tier, @ImportedAt)
            ON CONFLICT DO NOTHING
            RETURNING id;
            """;

        await using var connection = await _source.OpenConnectionAsync(ct).ConfigureAwait(false);

        var id = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(sql, new
        {
            provenance.GameId,
            SubjectKind = Encode(provenance.SubjectKind),
            provenance.SubjectField,
            provenance.SubjectAt,
            provenance.SourceName,
            provenance.SourceKey,
            SourceUri = provenance.SourceUri?.AbsoluteUri,
            Tier = Encode(provenance.Tier),
            provenance.ImportedAt,
        }, cancellationToken: ct)).ConfigureAwait(false);

        return id ?? 0;
    }

    public async Task<bool> ExistsAsync(
        Guid gameId,
        ImportSubjectKind kind,
        string? field,
        DateTimeOffset? at,
        string sourceName,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourceName);

        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM import_provenance
                WHERE game_id = @GameId
                  AND subject_kind = @SubjectKind
                  AND source_name = @SourceName
                  AND COALESCE(subject_field, '') = COALESCE(@SubjectField, '')
                  AND COALESCE(subject_at, '-infinity'::timestamptz) = COALESCE(@SubjectAt, '-infinity'::timestamptz)
            );
            """;

        await using var connection = await _source.OpenConnectionAsync(ct).ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql, new
        {
            GameId = gameId,
            SubjectKind = Encode(kind),
            SubjectField = field,
            SubjectAt = at,
            SourceName = sourceName,
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ImportProvenance>> ForGameAsync(Guid gameId, CancellationToken ct)
    {
        const string sql = """
            SELECT id, game_id, subject_kind, subject_field, subject_at,
                   source_name, source_key, source_uri, tier, imported_at
            FROM import_provenance
            WHERE game_id = @GameId
            ORDER BY id;
            """;

        await using var connection = await _source.OpenConnectionAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<Row>(
            new CommandDefinition(sql, new { GameId = gameId }, cancellationToken: ct)).ConfigureAwait(false);

        return [.. rows.Select(r => new ImportProvenance(
            r.id,
            r.game_id,
            DecodeKind(r.subject_kind),
            r.subject_field,
            r.subject_at,
            r.source_name,
            r.source_key,
            r.source_uri is null ? null : new Uri(r.source_uri),
            DecodeTier(r.tier),
            r.imported_at))];
    }

    private static string Encode(ImportSubjectKind kind) => kind switch
    {
        ImportSubjectKind.Field => "field",
        ImportSubjectKind.Presence => "presence",
        ImportSubjectKind.Availability => "availability",
        ImportSubjectKind.Endpoint => "endpoint",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static ImportSubjectKind DecodeKind(string kind) => kind switch
    {
        "field" => ImportSubjectKind.Field,
        "presence" => ImportSubjectKind.Presence,
        "availability" => ImportSubjectKind.Availability,
        "endpoint" => ImportSubjectKind.Endpoint,
        _ => throw new InvalidOperationException($"Unknown import_provenance.subject_kind '{kind}'."),
    };

    private static string Encode(ImportTier tier) => tier is ImportTier.Measured ? "measured" : "asserted";

    private static ImportTier DecodeTier(string tier) => tier switch
    {
        "measured" => ImportTier.Measured,
        "asserted" => ImportTier.Asserted,
        _ => throw new InvalidOperationException($"Unknown import_provenance.tier '{tier}'."),
    };

#pragma warning disable IDE1006, SA1300 // column names, mapped verbatim by Dapper
    private sealed record Row(
        long id,
        Guid game_id,
        string subject_kind,
        string? subject_field,
        DateTimeOffset? subject_at,
        string source_name,
        string source_key,
        string? source_uri,
        string tier,
        DateTimeOffset imported_at);
#pragma warning restore IDE1006, SA1300
}
```

- [ ] **Step 6: Write the in-memory repository the rest of the suite uses**

`tests/MUI.Backfill.Tests/Support/InMemoryImportProvenanceRepository.cs`:

```csharp
namespace MUI.Backfill.Tests.Support;

/// <summary>The provenance sidecar without a database. Same idempotence rule as the unique index.</summary>
public sealed class InMemoryImportProvenanceRepository : IImportProvenanceRepository
{
    private long _next = 1;

    public List<ImportProvenance> Rows { get; } = [];

    public Task<long> RecordAsync(ImportProvenance provenance, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        if (Find(provenance.GameId, provenance.SubjectKind, provenance.SubjectField, provenance.SubjectAt, provenance.SourceName) is not null)
        {
            return Task.FromResult(0L);
        }

        var id = _next++;
        Rows.Add(provenance with { Id = id });
        return Task.FromResult(id);
    }

    public Task<bool> ExistsAsync(Guid gameId, ImportSubjectKind kind, string? field, DateTimeOffset? at, string sourceName, CancellationToken ct) =>
        Task.FromResult(Find(gameId, kind, field, at, sourceName) is not null);

    public Task<IReadOnlyList<ImportProvenance>> ForGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ImportProvenance>>([.. Rows.Where(r => r.GameId == gameId)]);

    private ImportProvenance? Find(Guid gameId, ImportSubjectKind kind, string? field, DateTimeOffset? at, string sourceName) =>
        Rows.FirstOrDefault(r =>
            r.GameId == gameId
            && r.SubjectKind == kind
            && string.Equals(r.SubjectField ?? string.Empty, field ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && Nullable.Equals(r.SubjectAt, at)
            && string.Equals(r.SourceName, sourceName, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
MUI_INTEGRATION=1 dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 36 tests. Without `MUI_INTEGRATION=1` the three Postgres tests return early and the
suite is still green.

- [ ] **Step 8: Commit**

```bash
git add src/MUI.Storage/Migrations/0100_import_provenance.sql src/MUI.Backfill/ImportProvenance.cs src/MUI.Backfill/NpgsqlImportProvenanceRepository.cs tests/MUI.Backfill.Tests
git commit -m "feat(backfill): stamp every imported value with the site it came from and when"
```

---

### Task 6: Identity on the way in, and the in-memory repositories

**Files:**
- Create: `src/MUI.Backfill/ImportIdentity.cs`
- Create: `tests/MUI.Backfill.Tests/Support/InMemoryRepositories.cs`
- Create: `tests/MUI.Backfill.Tests/ImportIdentityTests.cs`

**Interfaces:**
- Consumes: `MUI.Storage.IGameRepository`, `IGameFieldRepository`, `IPresenceRepository`,
  `IAvailabilityRepository`, `IEndpointRepository`, `GameQuery` (Plan 2);
  `MUI.Discovery.ICrawlTargetRepository`, `CrawlTarget`, `IdentityWeights` (Plan 3);
  `MUI.Catalog.Game`, `GameField`, `FieldChange`, `PresenceSample`, `AvailabilityInterval`,
  `GameEndpoint`, `EndpointKind`, `EndpointState`, `AvailabilityState`, `FailureCause`,
  `LifecycleState`, `HostName.Normalize` (Plan 2, Task 10); `ImportedGame` (Task 1).
- Produces: `ImportMatch(Guid? GameId, double Score, string Signal)` with `ImportMatch.None`;
  `ImportIdentity(IEndpointRepository endpoints)` with
  `.ResolveAsync(ImportedGame, CancellationToken) → Task<ImportMatch>`;
  `Support.InMemoryGameRepository` (`.Games`), `Support.InMemoryGameFieldRepository`
  (`.Fields`, `.Changes`, `.Confirmations`), `Support.InMemoryPresenceRepository` (`.Samples`),
  `Support.InMemoryAvailabilityRepository` (`.Intervals`, `.Origins` — the `origin` column
  `AvailabilityInterval` has no property for), `Support.InMemoryEndpointRepository`
  (`.Endpoints`), `Support.InMemoryCrawlTargetRepository` (`.Targets`, `.Attempts`).

- [ ] **Step 1: Write the in-memory repositories**

`tests/MUI.Backfill.Tests/Support/InMemoryRepositories.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery;
using MUI.Storage;

namespace MUI.Backfill.Tests.Support;

public sealed class InMemoryGameRepository : IGameRepository
{
    public List<Game> Games { get; } = [];

    public Task<Game?> ByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Games.FirstOrDefault(g => g.Id == id));

    public Task<Game?> BySlugAsync(string slug, CancellationToken ct) =>
        Task.FromResult(Games.FirstOrDefault(g => string.Equals(g.Slug, slug, StringComparison.OrdinalIgnoreCase)));

    public Task<Guid> InsertAsync(Game game, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(game);
        Games.Add(game);
        return Task.FromResult(game.Id);
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

    public Task<IReadOnlyList<Game>> ListAsync(GameQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult<IReadOnlyList<Game>>(
        [
            .. Games
                .Where(g => query.IncludeArchived || g.State is not LifecycleState.Archived)
                .Skip(query.Offset)
                .Take(query.Limit),
        ]);
    }
}

/// <summary>
/// Endpoints, in memory. Hosts are canonicalised by <see cref="HostName.Normalize"/> and then compared
/// <b>ordinally</b>, exactly as <c>NpgsqlEndpointRepository</c> does — one canonical form rather than a
/// lenient comparison.
/// </summary>
/// <remarks>
/// This matters more here than anywhere else in the system. An import is where hosts arrive spelled by
/// somebody else: a directory that prints <c>MUD.Example.ORG</c>, or a name carrying its root dot, must
/// resolve to the game we already have — otherwise <see cref="ImportIdentity"/> scores no endpoint
/// match, the record falls below threshold, and the import seeds a crawl target for a machine already
/// in the catalogue. That is §7.3's duplicate listing, arrived at through §7.6. A fake that compared
/// case-insensitively would hide it, because it would be kinder than the database that ships.
/// </remarks>
public sealed class InMemoryEndpointRepository : IEndpointRepository
{
    public List<GameEndpoint> Endpoints { get; } = [];

    public Task<IReadOnlyList<GameEndpoint>> ForGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GameEndpoint>>([.. Endpoints.Where(e => e.GameId == gameId)]);

    public Task<GameEndpoint?> ByAddressAsync(string host, int port, CancellationToken ct)
    {
        var canonical = HostName.Normalize(host);

        return Task.FromResult(Endpoints.FirstOrDefault(e =>
            string.Equals(e.Host, canonical, StringComparison.Ordinal) && e.Port == port));
    }

    public Task UpsertAsync(GameEndpoint endpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var canonical = endpoint with { Host = HostName.Normalize(endpoint.Host) };

        var index = Endpoints.FindIndex(e =>
            e.GameId == canonical.GameId
            && string.Equals(e.Host, canonical.Host, StringComparison.Ordinal)
            && e.Port == canonical.Port);

        if (index >= 0)
        {
            Endpoints[index] = canonical;
        }
        else
        {
            Endpoints.Add(canonical);
        }

        return Task.CompletedTask;
    }
}

public sealed class InMemoryGameFieldRepository : IGameFieldRepository
{
    private long _nextChange = 1;

    public List<GameField> Fields { get; } = [];

    public List<FieldChange> Changes { get; } = [];

    public List<(Guid GameId, string Field, DateTimeOffset At)> Confirmations { get; } = [];

    public Task<IReadOnlyList<GameField>> ForGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GameField>>([.. Fields.Where(f => f.GameId == gameId)]);

    public Task UpsertAsync(GameField field, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(field);

        var index = Fields.FindIndex(f =>
            f.GameId == field.GameId && string.Equals(f.Field, field.Field, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            Fields[index] = field;
        }
        else
        {
            Fields.Add(field);
        }

        return Task.CompletedTask;
    }

    public Task ConfirmAsync(Guid gameId, string field, DateTimeOffset at, CancellationToken ct)
    {
        Confirmations.Add((gameId, field, at));

        var index = Fields.FindIndex(f =>
            f.GameId == gameId && string.Equals(f.Field, field, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            Fields[index] = Fields[index] with { LastConfirmedAt = at };
        }

        return Task.CompletedTask;
    }

    public Task AppendChangeAsync(FieldChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        Changes.Add(change with { Id = _nextChange++ });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FieldChange>> ChangesAsync(Guid gameId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FieldChange>>(
            [.. Changes.Where(c => c.GameId == gameId).OrderByDescending(c => c.At).Take(limit)]);
}

public sealed class InMemoryPresenceRepository : IPresenceRepository
{
    public List<PresenceSample> Samples { get; } = [];

    public List<DateTimeOffset> Partitions { get; } = [];

    public Task AppendAsync(PresenceSample sample, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sample);
        Samples.Add(sample);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PresenceSample>> RangeAsync(Guid gameId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PresenceSample>>(
            [.. Samples.Where(s => s.GameId == gameId && s.At >= from && s.At <= to).OrderBy(s => s.At)]);

    public Task EnsurePartitionAsync(DateTimeOffset month, CancellationToken ct)
    {
        Partitions.Add(month);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Availability, in memory. <see cref="AvailabilityInterval"/> carries no <c>origin</c> property —
/// Plan 02 writes that column from the repository and never round-trips it through the record — so
/// this fake keeps the tier beside the row in <see cref="Origins"/>. Without it the two cumulative
/// questions cannot be answered apart, which is the whole of §7.5's half weight.
/// </summary>
public sealed class InMemoryAvailabilityRepository : IAvailabilityRepository
{
    private long _next = 1;

    public List<AvailabilityInterval> Intervals { get; } = [];

    /// <summary>Interval id → the <c>origin</c> value the real column would hold.</summary>
    public Dictionary<long, string> Origins { get; } = [];

    public Task<AvailabilityInterval?> OpenIntervalAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult(Intervals.FirstOrDefault(i => i.GameId == gameId && i.ToAt is null));

    public Task<long> OpenAsync(Guid gameId, AvailabilityState state, FailureCause cause, DateTimeOffset from, CancellationToken ct)
    {
        var id = _next++;
        Intervals.Add(new AvailabilityInterval(id, gameId, state, from, null, cause));
        Origins[id] = FirstParty;
        return Task.FromResult(id);
    }

    public Task<long> InsertImportedAsync(
        Guid gameId,
        AvailabilityState state,
        FailureCause cause,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var id = _next++;
        Intervals.Add(new AvailabilityInterval(id, gameId, state, from, to, cause));
        Origins[id] = ImportedMeasured;
        return Task.FromResult(id);
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

    public Task<IReadOnlyList<AvailabilityInterval>> RangeAsync(Guid gameId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AvailabilityInterval>>(
        [
            .. Intervals.Where(i =>
                i.GameId == gameId
                && i.FromAt <= to
                && (i.ToAt ?? DateTimeOffset.MaxValue) >= from),
        ]);

    public Task<TimeSpan> CumulativeReachableAsync(Guid gameId, DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult(SumReachable(gameId, now, FirstParty));

    public Task<TimeSpan> CumulativeImportedMeasuredReachableAsync(
        Guid gameId, DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult(SumReachable(gameId, now, ImportedMeasured));

    private TimeSpan SumReachable(Guid gameId, DateTimeOffset now, string origin) =>
        Intervals
            .Where(i => i.GameId == gameId
                && i.State is AvailabilityState.Reachable
                && string.Equals(Origins.GetValueOrDefault(i.Id, FirstParty), origin, StringComparison.Ordinal))
            .Aggregate(TimeSpan.Zero, (total, i) => total + ((i.ToAt ?? now) - i.FromAt));

    // The two words 0004_availability_interval.sql's CHECK constraint allows, spelled once.
    private const string FirstParty = "first_party";
    private const string ImportedMeasured = "imported_measured";
}

/// <summary>
/// Crawl targets, plus a record of every scheduling call. <see cref="Attempts"/> exists so a test can
/// assert that an import never schedules anything (spec §7.1) — it must stay empty.
/// </summary>
public sealed class InMemoryCrawlTargetRepository : ICrawlTargetRepository
{
    public List<CrawlTarget> Targets { get; } = [];

    public List<Guid> Attempts { get; } = [];

    public Task<CrawlTarget?> ByAddressAsync(string host, int port, CancellationToken ct) =>
        Task.FromResult(Targets.FirstOrDefault(t =>
            string.Equals(t.Host, host, StringComparison.OrdinalIgnoreCase) && t.Port == port));

    public Task<Guid> AddAsync(CrawlTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        var existing = Targets.FirstOrDefault(t =>
            string.Equals(t.Host, target.Host, StringComparison.OrdinalIgnoreCase) && t.Port == target.Port);

        if (existing is not null)
        {
            return Task.FromResult(existing.Id);
        }

        Targets.Add(target);
        return Task.FromResult(target.Id);
    }

    public Task<IReadOnlyList<CrawlTarget>> DueAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CrawlTarget>>([.. Targets.Where(t => t.NextProbeAt <= now).Take(limit)]);

    public Task RecordAttemptAsync(Guid id, DateTimeOffset at, bool succeeded, TimeSpan? crawlDelay, DateTimeOffset nextProbeAt, CancellationToken ct)
    {
        Attempts.Add(id);
        return Task.CompletedTask;
    }

    public Task AttachGameAsync(Guid id, Guid gameId, CancellationToken ct)
    {
        var index = Targets.FindIndex(t => t.Id == id);
        if (index >= 0)
        {
            Targets[index] = Targets[index] with { GameId = gameId };
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Write the failing test**

`tests/MUI.Backfill.Tests/ImportIdentityTests.cs`:

```csharp
using MUI.Backfill.Tests.Support;
using MUI.Catalog;
using MUI.Discovery;

namespace MUI.Backfill.Tests;

/// <summary>
/// An imported game must not mint a duplicate of one we already probed, and must not merge on a weak
/// signal. Spec §7.3's weights are the yardstick and they live in MUI.Discovery — if they are ever
/// retuned, the first assertion here is what notices.
/// </summary>
public class ImportIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static ImportedGame Imported(string name, params (string Host, int Port)[] endpoints) => new()
    {
        SourceName = "MudVerse",
        SourceKey = name.ToLowerInvariant(),
        Name = name,
        Endpoints = [.. endpoints.Select(e => new ImportedEndpoint(e.Host, e.Port, EndpointKind.Telnet))],
    };

    [Test]
    public async Task AKnownEndpointIsStrongEnoughToMergeOnItsOwn()
    {
        await Assert.That(IdentityWeights.Endpoint).IsGreaterThanOrEqualTo(IdentityWeights.AutoMergeThreshold);
    }

    [Test]
    public async Task AnEndpointWeAlreadyProbedResolvesToThatGame()
    {
        var gameId = Guid.NewGuid();
        var endpoints = new InMemoryEndpointRepository();
        await endpoints.UpsertAsync(
            new GameEndpoint(gameId, "anachronism.example", 4000, EndpointKind.Telnet, Now, Now, EndpointState.Active),
            CancellationToken.None);

        var match = await new ImportIdentity(endpoints)
            .ResolveAsync(Imported("Anachronism", ("anachronism.example", 4000)), CancellationToken.None);

        await Assert.That(match.GameId).IsEqualTo(gameId);
        await Assert.That(match.Score).IsEqualTo(IdentityWeights.Endpoint);
        await Assert.That(match.Signal).Contains("anachronism.example:4000");
    }

    [Test]
    public async Task HoweverADirectorySpellsAHostItIsTheSameHost()
    {
        // Not a case-insensitive comparison — a canonical one. HostName.Normalize settles the spelling
        // on both ends of IEndpointRepository, so an import that shouts, or that carries the DNS root
        // dot, still lands the endpoint signal instead of seeding a crawl target for a game we have.
        var gameId = Guid.NewGuid();
        var endpoints = new InMemoryEndpointRepository();
        await endpoints.UpsertAsync(
            new GameEndpoint(gameId, "anachronism.example", 4000, EndpointKind.Telnet, Now, Now, EndpointState.Active),
            CancellationToken.None);

        var identity = new ImportIdentity(endpoints);

        foreach (var spelling in new[] { "Anachronism.Example", "ANACHRONISM.EXAMPLE", "anachronism.example." })
        {
            var match = await identity.ResolveAsync(
                Imported("Anachronism", (spelling, 4000)), CancellationToken.None);

            await Assert.That(match.GameId).IsEqualTo(gameId);
        }
    }

    [Test]
    public async Task ASharedNameIsNotASignalAtAll()
    {
        var endpoints = new InMemoryEndpointRepository();
        await endpoints.UpsertAsync(
            new GameEndpoint(Guid.NewGuid(), "anachronism.example", 4000, EndpointKind.Telnet, Now, Now, EndpointState.Active),
            CancellationToken.None);

        // Same name, entirely different host. A directory listing a namesake must not merge into it.
        var match = await new ImportIdentity(endpoints)
            .ResolveAsync(Imported("Anachronism", ("other.example.net", 4000)), CancellationToken.None);

        await Assert.That(match.GameId).IsNull();
        await Assert.That(match.Score).IsLessThan(IdentityWeights.AutoMergeThreshold);
    }

    [Test]
    public async Task ADifferentPortOnAKnownHostIsNotAMatch()
    {
        var endpoints = new InMemoryEndpointRepository();
        await endpoints.UpsertAsync(
            new GameEndpoint(Guid.NewGuid(), "anachronism.example", 4000, EndpointKind.Telnet, Now, Now, EndpointState.Active),
            CancellationToken.None);

        var match = await new ImportIdentity(endpoints)
            .ResolveAsync(Imported("Anachronism", ("anachronism.example", 9999)), CancellationToken.None);

        await Assert.That(match.GameId).IsNull();
    }

    [Test]
    public async Task AGameWithNoEndpointsAtAllResolvesToNothing()
    {
        var match = await new ImportIdentity(new InMemoryEndpointRepository())
            .ResolveAsync(Imported("Anachronism"), CancellationToken.None);

        await Assert.That(match).IsEqualTo(ImportMatch.None);
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'ImportIdentity' could not be found`.

- [ ] **Step 4: Write `ImportIdentity`**

`src/MUI.Backfill/ImportIdentity.cs`:

```csharp
using MUI.Discovery;
using MUI.Storage;

namespace MUI.Backfill;

/// <summary>
/// What an imported record resolved to. <see cref="GameId"/> is non-null only when the score reached
/// <see cref="IdentityWeights.AutoMergeThreshold"/>.
/// </summary>
public sealed record ImportMatch(Guid? GameId, double Score, string Signal)
{
    public static readonly ImportMatch None = new(null, 0.0, "no signal");
}

/// <summary>
/// Identity for an <see cref="ImportedGame"/>, on the way in. A directory listing is not a probe, so
/// the only signal available here that spec §7.3 rates highly enough to merge on is a previously-seen
/// endpoint. Nothing weaker is combined into a merge: a shared name is not evidence, and two
/// directories agreeing on a name is not evidence twice.
/// </summary>
/// <remarks>
/// Below the threshold this returns <see cref="ImportMatch.None"/> and the pipeline seeds a crawl
/// target rather than creating a game. That is spec §7.2 applied to imports: a host becomes a listing
/// by answering for itself, never because somebody's list said it exists.
/// </remarks>
public sealed class ImportIdentity(IEndpointRepository endpoints)
{
    private readonly IEndpointRepository _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    public async Task<ImportMatch> ResolveAsync(ImportedGame game, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(game);

        foreach (var endpoint in game.Endpoints)
        {
            var known = await _endpoints.ByAddressAsync(endpoint.Host, endpoint.Port, ct).ConfigureAwait(false);
            if (known is null)
            {
                continue;
            }

            // The stored host, not the imported spelling: the signal is what we matched on, and a
            // note reading "endpoint MUD.Example.ORG:4000" would name an address no table contains.
            return new ImportMatch(
                known.GameId,
                IdentityWeights.Endpoint,
                $"endpoint {known.Host}:{known.Port}");
        }

        return ImportMatch.None;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 42 tests.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Backfill/ImportIdentity.cs tests/MUI.Backfill.Tests
git commit -m "feat(backfill): resolve an imported game against endpoints we already know"
```

---

### Task 7: `ImportPipeline` — targets, endpoints and fields

**Files:**
- Create: `src/MUI.Backfill/IDirectoryImporter.cs`
- Create: `src/MUI.Backfill/ImportReport.cs`
- Create: `src/MUI.Backfill/IImportWriter.cs`
- Create: `src/MUI.Backfill/ImportPipeline.cs`
- Create: `tests/MUI.Backfill.Tests/Support/FakeImporter.cs`
- Create: `tests/MUI.Backfill.Tests/ImportPipelineTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–6, plus
  `MUI.Storage.IAvailabilityRepository.InsertImportedAsync(Guid, AvailabilityState, FailureCause, DateTimeOffset from, DateTimeOffset to, CancellationToken)`
  (Plan 02 Task 9) — the **only** availability write this plan may make.
- Produces: `IDirectoryImporter` (`SourceName`, `Tier`, `Etiquette`,
  `ReadAsync(CancellationToken) → IAsyncEnumerable<ImportedGame>`);
  `ImportReport(string Source, ImportTier Tier, int GamesSeen, int TargetsAdded, int FieldsWritten,
  int PresenceRows, int AvailabilityRows, int Rejected, IReadOnlyList<string> Notes)`;
  `IImportWriter` with `.AddCrawlTargetAsync`, `.UpsertEndpointAsync`, `.UpsertFieldAsync`,
  `.ConfirmFieldAsync`, `.AppendChangeAsync`, `.AppendPresenceAsync`,
  `.WriteClosedAvailabilityAsync`, `.RecordProvenanceAsync`;
  `CommittingImportWriter`, `DryRunImportWriter`;
  `ImportPipeline(ICrawlTargetRepository, IGameRepository, IEndpointRepository, IGameFieldRepository,
  IPresenceRepository, IAvailabilityRepository, IImportProvenanceRepository, TimeProvider)` with
  `.RunAsync(IDirectoryImporter, CancellationToken) → Task<ImportReport>`;
  `Support.FakeImporter` with `.Enumerated`.
- **Note:** `HistorySink.For(...)` is written in Task 8. This task's `ImportPipeline` calls it, so
  Task 7 and Task 8 are two commits against one compiling whole — Step 6 below writes the minimal
  `HistorySink` that Task 8 then tests and completes.

- [ ] **Step 1: Write the importer interface, report and fake importer**

`src/MUI.Backfill/IDirectoryImporter.cs`:

```csharp
namespace MUI.Backfill;

/// <summary>
/// One directory we read. The tier is a property of the site, not of a call: a source that pings is
/// <see cref="ImportTier.Measured"/> for everything it yields, and one that does not is
/// <see cref="ImportTier.Asserted"/> for everything it yields.
/// </summary>
public interface IDirectoryImporter
{
    string SourceName { get; }

    ImportTier Tier { get; }

    ImportEtiquette Etiquette { get; }

    IAsyncEnumerable<ImportedGame> ReadAsync(CancellationToken ct);
}

/// <summary>
/// The shared plumbing every concrete importer uses: it fetches through a
/// <see cref="DirectorySource"/> and takes its etiquette from it, so no importer can be configured
/// one way and fetch another.
/// </summary>
public abstract class DirectoryImporter(DirectorySource source) : IDirectoryImporter
{
    protected DirectorySource Source { get; } = source ?? throw new ArgumentNullException(nameof(source));

    public abstract string SourceName { get; }

    public abstract ImportTier Tier { get; }

    public ImportEtiquette Etiquette => Source.Etiquette;

    public abstract IAsyncEnumerable<ImportedGame> ReadAsync(CancellationToken ct);
}
```

`src/MUI.Backfill/ImportReport.cs`:

```csharp
namespace MUI.Backfill;

/// <summary>
/// What one import run did. <see cref="Rejected"/> counts rows an importer offered that its tier is
/// not entitled to write — spec §7.6's asserted tier offering history is not an error to swallow, it
/// is a number to print.
/// </summary>
public sealed record ImportReport(
    string Source,
    ImportTier Tier,
    int GamesSeen,
    int TargetsAdded,
    int FieldsWritten,
    int PresenceRows,
    int AvailabilityRows,
    int Rejected,
    IReadOnlyList<string> Notes);
```

`tests/MUI.Backfill.Tests/Support/FakeImporter.cs`:

```csharp
namespace MUI.Backfill.Tests.Support;

/// <summary>An importer that yields a fixed list and records whether anybody enumerated it.</summary>
public sealed class FakeImporter(
    string sourceName,
    ImportTier tier,
    ImportEtiquette etiquette,
    IReadOnlyList<ImportedGame> games) : IDirectoryImporter
{
    public string SourceName { get; } = sourceName;

    public ImportTier Tier { get; } = tier;

    public ImportEtiquette Etiquette { get; } = etiquette;

    public bool Enumerated { get; private set; }

    public async IAsyncEnumerable<ImportedGame> ReadAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        Enumerated = true;

        foreach (var game in games)
        {
            ct.ThrowIfCancellationRequested();
            yield return game;
            await Task.Yield();
        }
    }

    public static ImportEtiquette ApiEtiquette(string name) => new()
    {
        SourceName = name,
        AttributionUri = new Uri($"https://{name.ToLowerInvariant()}.test/"),
        ApiUri = new Uri($"https://{name.ToLowerInvariant()}.test/api/games"),
        RobotsUri = new Uri($"https://{name.ToLowerInvariant()}.test/robots.txt"),
        UserAgent = CrawlerIdentity.UserAgent,
    };
}
```

- [ ] **Step 2: Write the failing test**

`tests/MUI.Backfill.Tests/ImportPipelineTests.cs`:

```csharp
using MUI.Backfill.Tests.Support;
using MUI.Catalog;

namespace MUI.Backfill.Tests;

/// <summary>
/// The one pass an import makes: resolve identity, seed a crawl target, and — only for a game we
/// already know — write endpoints and fields. Spec §7.1, §7.2, §7.6.
/// </summary>
public class ImportPipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        InMemoryCrawlTargetRepository Targets,
        InMemoryGameRepository Games,
        InMemoryEndpointRepository Endpoints,
        InMemoryGameFieldRepository Fields,
        InMemoryPresenceRepository Presence,
        InMemoryAvailabilityRepository Availability,
        InMemoryImportProvenanceRepository Provenance,
        ImportPipeline Pipeline);

    private static Harness Build()
    {
        var targets = new InMemoryCrawlTargetRepository();
        var games = new InMemoryGameRepository();
        var endpoints = new InMemoryEndpointRepository();
        var fields = new InMemoryGameFieldRepository();
        var presence = new InMemoryPresenceRepository();
        var availability = new InMemoryAvailabilityRepository();
        var provenance = new InMemoryImportProvenanceRepository();

        var pipeline = new ImportPipeline(targets, games, endpoints, fields, presence, availability,
            provenance, new ManualTimeProvider(Now));

        return new Harness(targets, games, endpoints, fields, presence, availability, provenance, pipeline);
    }

    private static async Task<Guid> SeedProbedGameAsync(Harness harness, string host, int port)
    {
        var gameId = Guid.NewGuid();
        await harness.Games.InsertAsync(
            new Game(gameId, "anachronism", "Anachronism", LifecycleState.Active, false, Now, Now, null),
            CancellationToken.None);
        await harness.Endpoints.UpsertAsync(
            new GameEndpoint(gameId, host, port, EndpointKind.Telnet, Now, Now, EndpointState.Active),
            CancellationToken.None);
        return gameId;
    }

    private static ImportedGame Anachronism(string sourceName, ImportTier tier) => new()
    {
        SourceName = sourceName,
        SourceKey = "anachronism",
        Name = "Anachronism",
        SourceUri = new Uri($"https://{sourceName.ToLowerInvariant()}.test/g/anachronism"),
        Endpoints = [new ImportedEndpoint("anachronism.example", 4000, EndpointKind.Telnet)],
        Fields = new Dictionary<string, string>
        {
            ["codebase"] = "Evennia 4.2",
            ["website"] = "https://anachronism.example/",
        },
        Availability = tier is ImportTier.Measured
            ? [new ImportedAvailability(Now.AddYears(-2), Now.AddDays(-1), true)]
            : [],
    };

    [Test]
    public async Task AnUnknownHostBecomesACrawlTargetAndNotAGame()
    {
        var harness = Build();
        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Anachronism("MudVerse", ImportTier.Measured)]);

        var report = await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        await Assert.That(harness.Games.Games).IsEmpty();
        await Assert.That(harness.Targets.Targets.Count).IsEqualTo(1);
        await Assert.That(harness.Targets.Targets[0].Host).IsEqualTo("anachronism.example");
        await Assert.That(harness.Targets.Targets[0].Port).IsEqualTo(4000);
        await Assert.That(harness.Targets.Targets[0].GameId).IsNull();
        await Assert.That(report.TargetsAdded).IsEqualTo(1);
        await Assert.That(report.FieldsWritten).IsEqualTo(0);
        await Assert.That(report.Notes.Any(n => n.Contains("Anachronism", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task AnImportNeverSchedulesAnything()
    {
        var harness = Build();
        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Anachronism("MudVerse", ImportTier.Measured)]);

        await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        // Spec §7.1: discovery is how a game is found, never how it is scheduled. The crawler owns
        // next_probe_at from here; the import only says "this address exists".
        await Assert.That(harness.Targets.Attempts).IsEmpty();
        await Assert.That(harness.Targets.Targets[0].NextProbeAt).IsEqualTo(Now);
        await Assert.That(harness.Targets.Targets[0].ConsecutiveFailures).IsEqualTo(0);
    }

    [Test]
    public async Task AKnownHostGetsItsFieldsAndEndpointsWrittenAgainstTheExistingGame()
    {
        var harness = Build();
        var gameId = await SeedProbedGameAsync(harness, "anachronism.example", 4000);
        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Anachronism("MudVerse", ImportTier.Measured)]);

        var report = await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        await Assert.That(harness.Games.Games.Count).IsEqualTo(1);
        await Assert.That(report.FieldsWritten).IsEqualTo(2);

        var codebase = harness.Fields.Fields.Single(f => f.Field == "codebase");
        await Assert.That(codebase.GameId).IsEqualTo(gameId);
        await Assert.That(codebase.Value).IsEqualTo("Evennia 4.2");
        await Assert.That(codebase.Source).IsEqualTo(FieldSource.ImportedMeasured);
        await Assert.That(codebase.Confidence).IsEqualTo(FieldConfidence.Reported);
    }

    [Test]
    public async Task EveryImportedFieldCarriesTheSiteAndTheImportDate()
    {
        var harness = Build();
        var gameId = await SeedProbedGameAsync(harness, "anachronism.example", 4000);
        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Anachronism("MudVerse", ImportTier.Measured)]);

        await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        var stamps = await harness.Provenance.ForGameAsync(gameId, CancellationToken.None);
        var codebase = stamps.Single(s => s.SubjectKind is ImportSubjectKind.Field && s.SubjectField == "codebase");

        await Assert.That(codebase.SourceName).IsEqualTo("MudVerse");
        await Assert.That(codebase.SourceKey).IsEqualTo("anachronism");
        await Assert.That(codebase.SourceUri).IsEqualTo(new Uri("https://mudverse.test/g/anachronism"));
        await Assert.That(codebase.ImportedAt).IsEqualTo(Now);
        await Assert.That(codebase.Tier).IsEqualTo(ImportTier.Measured);
    }

    [Test]
    public async Task AnImportNeverOverwritesAValueWeMeasuredOurselves()
    {
        var harness = Build();
        var gameId = await SeedProbedGameAsync(harness, "anachronism.example", 4000);
        await harness.Fields.UpsertAsync(
            new GameField(gameId, "codebase", "Evennia 5.0", FieldSource.Mssp, FieldConfidence.Reported, Now, Now),
            CancellationToken.None);

        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Anachronism("MudVerse", ImportTier.Measured)]);

        var report = await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        var codebase = harness.Fields.Fields.Single(f => f.Field == "codebase");
        await Assert.That(codebase.Value).IsEqualTo("Evennia 5.0");
        await Assert.That(codebase.Source).IsEqualTo(FieldSource.Mssp);
        await Assert.That(report.FieldsWritten).IsEqualTo(1);   // only "website" got through
    }

    [Test]
    public async Task ChangingAnImportedValueAppendsAChangeRow()
    {
        var harness = Build();
        var gameId = await SeedProbedGameAsync(harness, "anachronism.example", 4000);
        await harness.Fields.UpsertAsync(
            new GameField(gameId, "codebase", "Evennia 4.1", FieldSource.ImportedMeasured, FieldConfidence.Reported,
                Now.AddYears(-1), Now.AddYears(-1)),
            CancellationToken.None);

        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Anachronism("MudVerse", ImportTier.Measured)]);

        await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        var change = harness.Fields.Changes.Single(c => c.Field == "codebase");
        await Assert.That(change.OldValue).IsEqualTo("Evennia 4.1");
        await Assert.That(change.NewValue).IsEqualTo("Evennia 4.2");
        await Assert.That(change.Source).IsEqualTo(FieldSource.ImportedMeasured);
        await Assert.That(harness.Fields.Fields.Single(f => f.Field == "codebase").FirstSeenAt)
            .IsEqualTo(Now.AddYears(-1));
    }

    [Test]
    public async Task AnUnchangedImportedValueIsConfirmedRatherThanRewritten()
    {
        var harness = Build();
        var gameId = await SeedProbedGameAsync(harness, "anachronism.example", 4000);
        await harness.Fields.UpsertAsync(
            new GameField(gameId, "codebase", "Evennia 4.2", FieldSource.ImportedMeasured, FieldConfidence.Reported,
                Now.AddYears(-1), Now.AddYears(-1)),
            CancellationToken.None);

        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Anachronism("MudVerse", ImportTier.Measured)]);

        await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        await Assert.That(harness.Fields.Confirmations.Any(c => c.Field == "codebase")).IsTrue();
        await Assert.That(harness.Fields.Changes.Any(c => c.Field == "codebase")).IsFalse();
    }

    [Test]
    public async Task AnImporterWithNoPermittedRouteIsRefusedBeforeItIsEnumerated()
    {
        var harness = Build();
        var etiquette = FakeImporter.ApiEtiquette("Somewhere") with
        {
            ApiUri = null,
            ScrapeUri = new Uri("https://somewhere.test/list"),
            ContactedMaintainer = false,
        };
        var importer = new FakeImporter("Somewhere", ImportTier.Asserted, etiquette, [Anachronism("Somewhere", ImportTier.Asserted)]);

        await Assert.That(async () => await harness.Pipeline.RunAsync(importer, CancellationToken.None))
            .Throws<EtiquetteViolationException>();

        await Assert.That(importer.Enumerated).IsFalse();
    }

    [Test]
    public async Task AnImporterThatReachesForItsScrapeUriWhileAnApiExistsFailsTheRun()
    {
        var harness = Build();
        var etiquette = FakeImporter.ApiEtiquette("Somewhere") with
        {
            ScrapeUri = new Uri("https://somewhere.test/list"),
            ContactedMaintainer = true,
        };
        var (_, client) = FakeHttp.Serving(
            ("https://somewhere.test/robots.txt", "User-agent: *\nDisallow:\n"),
            ("https://somewhere.test/list", "<html></html>"));
        var source = new DirectorySource(client, etiquette, new ManualTimeProvider(Now));
        await source.PrimeRobotsAsync(CancellationToken.None);

        var importer = new ScrapeHappyImporter(source);

        await Assert.That(async () => await harness.Pipeline.RunAsync(importer, CancellationToken.None))
            .Throws<EtiquetteViolationException>();
    }

    private sealed class ScrapeHappyImporter(DirectorySource source) : DirectoryImporter(source)
    {
        public override string SourceName => "Somewhere";

        public override ImportTier Tier => ImportTier.Asserted;

        public override async IAsyncEnumerable<ImportedGame> ReadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            // Deliberately the wrong route: an API is configured, so this must not be reachable.
            await Source.GetStringAsync(Source.Etiquette.ScrapeUri!, ct);
            yield break;
        }
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'ImportPipeline' could not be found`.

- [ ] **Step 4: Write the writer abstraction**

`src/MUI.Backfill/IImportWriter.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery;
using MUI.Storage;

namespace MUI.Backfill;

/// <summary>
/// Every write an import can make. Commit-versus-dry-run is a choice of implementation rather than a
/// boolean threaded through the pipeline, so a dry run cannot write by forgetting to check a flag —
/// <see cref="DryRunImportWriter"/> holds no repository at all.
/// </summary>
public interface IImportWriter
{
    Task<Guid> AddCrawlTargetAsync(CrawlTarget target, CancellationToken ct);

    Task UpsertEndpointAsync(GameEndpoint endpoint, CancellationToken ct);

    Task UpsertFieldAsync(GameField field, CancellationToken ct);

    Task ConfirmFieldAsync(Guid gameId, string field, DateTimeOffset at, CancellationToken ct);

    Task AppendChangeAsync(FieldChange change, CancellationToken ct);

    Task AppendPresenceAsync(PresenceSample sample, CancellationToken ct);

    /// <summary>
    /// Writes an availability span that is already over, stamped <c>origin = 'imported_measured'</c>.
    /// Imported spans are never left open: we did not measure them and cannot extend them, and an open
    /// imported interval would collide with the one our own crawler keeps.
    /// </summary>
    Task WriteClosedAvailabilityAsync(
        Guid gameId,
        AvailabilityState state,
        FailureCause cause,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);

    Task RecordProvenanceAsync(ImportProvenance provenance, CancellationToken ct);
}

public sealed class CommittingImportWriter(
    ICrawlTargetRepository targets,
    IEndpointRepository endpoints,
    IGameFieldRepository fields,
    IPresenceRepository presence,
    IAvailabilityRepository availability,
    IImportProvenanceRepository provenance) : IImportWriter
{
    public Task<Guid> AddCrawlTargetAsync(CrawlTarget target, CancellationToken ct) =>
        targets.AddAsync(target, ct);

    public Task UpsertEndpointAsync(GameEndpoint endpoint, CancellationToken ct) =>
        endpoints.UpsertAsync(endpoint, ct);

    public Task UpsertFieldAsync(GameField field, CancellationToken ct) =>
        fields.UpsertAsync(field, ct);

    public Task ConfirmFieldAsync(Guid gameId, string field, DateTimeOffset at, CancellationToken ct) =>
        fields.ConfirmAsync(gameId, field, at, ct);

    public Task AppendChangeAsync(FieldChange change, CancellationToken ct) =>
        fields.AppendChangeAsync(change, ct);

    public Task AppendPresenceAsync(PresenceSample sample, CancellationToken ct) =>
        presence.AppendAsync(sample, ct);

    // InsertImportedAsync and NOT OpenAsync/CloseAsync. OpenAsync defaults origin to 'first_party',
    // which would credit a third party's history at FULL weight — the exact inversion of §7.5, and a
    // silent one, because the resulting grace still looks plausible. Plan 02 Task 9 exists so this
    // line has somewhere to go; there is no other imported write path and none may be added.
    public Task WriteClosedAvailabilityAsync(
        Guid gameId,
        AvailabilityState state,
        FailureCause cause,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct) =>
        availability.InsertImportedAsync(gameId, state, cause, from, to, ct);

    public Task RecordProvenanceAsync(ImportProvenance record, CancellationToken ct) =>
        provenance.RecordAsync(record, ct);
}

/// <summary>
/// A writer that holds nothing and writes nothing. It exists so <c>--dry-run</c> is a different
/// object rather than a different code path.
/// </summary>
public sealed class DryRunImportWriter : IImportWriter
{
    public Task<Guid> AddCrawlTargetAsync(CrawlTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Task.FromResult(target.Id);
    }

    public Task UpsertEndpointAsync(GameEndpoint endpoint, CancellationToken ct) => Task.CompletedTask;

    public Task UpsertFieldAsync(GameField field, CancellationToken ct) => Task.CompletedTask;

    public Task ConfirmFieldAsync(Guid gameId, string field, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;

    public Task AppendChangeAsync(FieldChange change, CancellationToken ct) => Task.CompletedTask;

    public Task AppendPresenceAsync(PresenceSample sample, CancellationToken ct) => Task.CompletedTask;

    public Task WriteClosedAvailabilityAsync(
        Guid gameId,
        AvailabilityState state,
        FailureCause cause,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct) => Task.CompletedTask;

    public Task RecordProvenanceAsync(ImportProvenance provenance, CancellationToken ct) => Task.CompletedTask;
}
```

- [ ] **Step 5: Write the pipeline**

`src/MUI.Backfill/ImportPipeline.cs`:

```csharp
using MUI.Catalog;
using MUI.Discovery;
using MUI.Storage;

namespace MUI.Backfill;

/// <summary>
/// One import, start to finish. For each record: resolve identity against endpoints we already know,
/// seed a crawl target for every address, and — only when the record resolved to a game — write its
/// endpoints, its fields and (for a measured source only) its history.
/// </summary>
/// <remarks>
/// Two structural rules run through this class rather than sitting in comments. Commit versus dry run
/// is <see cref="IImportWriter"/>. Measured versus asserted is <see cref="IHistorySink"/>: the sink
/// this pipeline is handed for an asserted source holds nothing it could write with, so no amount of
/// history in an <see cref="ImportedGame"/> can reach a table.
/// </remarks>
public sealed class ImportPipeline(
    ICrawlTargetRepository targets,
    IGameRepository games,
    IEndpointRepository endpoints,
    IGameFieldRepository fields,
    IPresenceRepository presence,
    IAvailabilityRepository availability,
    IImportProvenanceRepository provenance,
    TimeProvider time)
{
    private readonly ICrawlTargetRepository _targets = targets ?? throw new ArgumentNullException(nameof(targets));
    private readonly IGameRepository _games = games ?? throw new ArgumentNullException(nameof(games));
    private readonly IEndpointRepository _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
    private readonly IGameFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IImportProvenanceRepository _provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));
    private readonly ImportIdentity _identity = new(endpoints);

    private readonly IImportWriter _committing =
        new CommittingImportWriter(targets, endpoints, fields, presence, availability, provenance);

    /// <summary>Reads the source and writes what it is entitled to write.</summary>
    public Task<ImportReport> RunAsync(IDirectoryImporter importer, CancellationToken ct) =>
        ExecuteAsync(importer, _committing, ct);

    /// <summary>Reads the source, reports what it would have written, and writes nothing.</summary>
    public Task<ImportReport> DryRunAsync(IDirectoryImporter importer, CancellationToken ct) =>
        ExecuteAsync(importer, new DryRunImportWriter(), ct);

    private async Task<ImportReport> ExecuteAsync(IDirectoryImporter importer, IImportWriter writer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(importer);

        var decision = EtiquettePlanner.Decide(importer.Etiquette);
        if (decision.Route is FetchRoute.None)
        {
            throw new EtiquetteViolationException($"{importer.SourceName}: {decision.RefusedReason}.");
        }

        var now = _time.GetUtcNow();
        var history = HistorySink.For(importer.Tier, writer, _provenance, now);
        var notes = new List<string>();

        var seen = 0;
        var targetsAdded = 0;
        var fieldsWritten = 0;
        var presenceRows = 0;
        var availabilityRows = 0;
        var rejected = 0;

        await foreach (var record in importer.ReadAsync(ct).ConfigureAwait(false))
        {
            seen++;

            var match = await _identity.ResolveAsync(record, ct).ConfigureAwait(false);
            targetsAdded += await SeedTargetsAsync(writer, record, match, now, ct).ConfigureAwait(false);

            if (match.GameId is not { } gameId)
            {
                notes.Add($"{record.Name}: no endpoint we already know — seeded as a crawl target, not listed (§7.2).");
                continue;
            }

            await WriteEndpointsAsync(writer, gameId, importer, record, now, ct).ConfigureAwait(false);
            fieldsWritten += await WriteFieldsAsync(writer, gameId, importer, record, now, ct).ConfigureAwait(false);

            var written = await history.WriteAsync(gameId, record, ct).ConfigureAwait(false);
            presenceRows += written.PresenceRows;
            availabilityRows += written.AvailabilityRows;
            rejected += written.Refused;

            if (written.Refused > 0)
            {
                var game = await _games.ByIdAsync(gameId, ct).ConfigureAwait(false);
                notes.Add(
                    $"{game?.Name ?? record.Name}: refused {written.Refused} history rows — " +
                    $"{importer.SourceName} is an asserted source and earns no history, presence or grace (§7.6).");
            }
        }

        return new ImportReport(importer.SourceName, importer.Tier, seen, targetsAdded, fieldsWritten,
            presenceRows, availabilityRows, rejected, notes);
    }

    private async Task<int> SeedTargetsAsync(
        IImportWriter writer,
        ImportedGame record,
        ImportMatch match,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var added = 0;

        foreach (var endpoint in record.Endpoints)
        {
            // Canonicalised once, here, so the crawl target and the endpoint row this import writes
            // carry the same spelling. IEndpointRepository would normalise for itself; a CrawlTarget
            // is written straight through, and two spellings there are two targets for one machine.
            var host = HostName.Normalize(endpoint.Host);

            if (await _targets.ByAddressAsync(host, endpoint.Port, ct).ConfigureAwait(false) is not null)
            {
                continue;
            }

            // NextProbeAt is "now" and nothing else: an import says an address exists, and the
            // scheduler decides when it is actually visited (spec §7.1).
            await writer.AddCrawlTargetAsync(
                new CrawlTarget
                {
                    Id = Guid.NewGuid(),
                    GameId = match.GameId,
                    Host = host,
                    Port = endpoint.Port,
                    UseTls = endpoint.Kind is EndpointKind.Tls,
                    NextProbeAt = now,
                    FirstSeenAt = now,
                },
                ct).ConfigureAwait(false);

            added++;
        }

        return added;
    }

    private async Task WriteEndpointsAsync(
        IImportWriter writer,
        Guid gameId,
        IDirectoryImporter importer,
        ImportedGame record,
        DateTimeOffset now,
        CancellationToken ct)
    {
        foreach (var endpoint in record.Endpoints)
        {
            var known = await _endpoints.ByAddressAsync(endpoint.Host, endpoint.Port, ct).ConfigureAwait(false);

            await writer.UpsertEndpointAsync(
                new GameEndpoint(gameId, endpoint.Host, endpoint.Port, endpoint.Kind,
                    known?.FirstSeenAt ?? now, known?.LastSeenAt ?? now, known?.State ?? EndpointState.Stale),
                ct).ConfigureAwait(false);

            await writer.RecordProvenanceAsync(
                new ImportProvenance(0, gameId, ImportSubjectKind.Endpoint, $"{endpoint.Host}:{endpoint.Port}", null,
                    record.SourceName, record.SourceKey, record.SourceUri, importer.Tier, now),
                ct).ConfigureAwait(false);
        }
    }

    private async Task<int> WriteFieldsAsync(
        IImportWriter writer,
        Guid gameId,
        IDirectoryImporter importer,
        ImportedGame record,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var source = ImportTierMap.SourceFor(importer.Tier);
        var existing = (await _fields.ForGameAsync(gameId, ct).ConfigureAwait(false))
            .ToDictionary(f => f.Field, StringComparer.OrdinalIgnoreCase);

        var written = 0;

        foreach (var (name, value) in record.Fields)
        {
            existing.TryGetValue(name, out var incumbent);

            if (incumbent is not null)
            {
                if (!SourcePrecedence.Wins(source, incumbent.Source, name))
                {
                    continue;
                }

                if (string.Equals(incumbent.Value, value, StringComparison.Ordinal))
                {
                    await writer.ConfirmFieldAsync(gameId, name, now, ct).ConfigureAwait(false);
                    continue;
                }

                await writer.AppendChangeAsync(
                    new FieldChange(0, gameId, name, incumbent.Value, value, source, now), ct).ConfigureAwait(false);
            }

            await writer.UpsertFieldAsync(
                new GameField(gameId, name, value, source, FieldConfidence.Reported,
                    incumbent?.FirstSeenAt ?? now, now),
                ct).ConfigureAwait(false);

            await writer.RecordProvenanceAsync(
                new ImportProvenance(0, gameId, ImportSubjectKind.Field, name, null,
                    record.SourceName, record.SourceKey, record.SourceUri, importer.Tier, now),
                ct).ConfigureAwait(false);

            written++;
        }

        return written;
    }
}
```

- [ ] **Step 6: Write the minimal `HistorySink` this pipeline calls**

`src/MUI.Backfill/HistorySink.cs` — Task 8 tests and completes it; this is the smallest version that
compiles and passes Task 7's tests:

```csharp
namespace MUI.Backfill;

/// <summary>What a history write actually did, and what it refused.</summary>
public sealed record HistoryWrite(int PresenceRows, int AvailabilityRows, int Refused)
{
    public static readonly HistoryWrite Nothing = new(0, 0, 0);
}

public interface IHistorySink
{
    Task<HistoryWrite> WriteAsync(Guid gameId, ImportedGame game, CancellationToken ct);
}

/// <summary>
/// The asserted tier's sink. It takes no constructor parameters, which is the enforcement: it holds
/// no writer and no repository, so spec §7.6's "no history, no presence, no grace" is a fact about
/// this type rather than a rule somebody has to remember.
/// </summary>
/// <remarks>
/// It writes nothing already. What it does not yet do is <em>say</em> how much it refused, so a run
/// against an asserted source silently reports zero rows offered and zero rows written — which reads
/// exactly like a source that offered nothing. Task 8 is where that becomes a number.
/// </remarks>
public sealed class AssertedHistorySink : IHistorySink
{
    public Task<HistoryWrite> WriteAsync(Guid gameId, ImportedGame game, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(game);
        return Task.FromResult(HistoryWrite.Nothing);
    }
}

public static class HistorySink
{
    public static IHistorySink For(
        ImportTier tier,
        IImportWriter writer,
        IImportProvenanceRepository provenance,
        DateTimeOffset importedAt) =>
        tier is ImportTier.Measured
            ? new MeasuredHistorySink(writer, provenance, importedAt)
            : new AssertedHistorySink();
}
```

`src/MUI.Backfill/MeasuredHistorySink.cs`:

```csharp
using MUI.Catalog;

namespace MUI.Backfill;

/// <summary>
/// The measured tier's sink. A third party that ran its own probe produced a measurement, and a
/// measurement is worth more than a self-report — so this writes real
/// <c>PresenceSample</c> and <c>AvailabilityInterval</c> rows (spec §7.6).
/// </summary>
/// <remarks>
/// Two stamps, doing two different jobs. The <c>import_provenance</c> row records which site the value
/// came from and when we took it (§7.6's provenance chip). The availability row's own
/// <c>origin = 'imported_measured'</c> is what §7.5's half weight is computed from, and it is written
/// by <c>IAvailabilityRepository.InsertImportedAsync</c> — never <c>OpenAsync</c>, which would default
/// it to <c>first_party</c> and credit somebody else's history at full weight. Neither stamp can do the
/// other's job, and grace is never computed from the sidecar.
/// </remarks>
public sealed class MeasuredHistorySink(
    IImportWriter writer,
    IImportProvenanceRepository provenance,
    DateTimeOffset importedAt) : IHistorySink
{
    public async Task<HistoryWrite> WriteAsync(Guid gameId, ImportedGame game, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(game);

        var presenceRows = 0;

        foreach (var sample in game.Presence)
        {
            if (await provenance.ExistsAsync(gameId, ImportSubjectKind.Presence, null, sample.At, game.SourceName, ct)
                .ConfigureAwait(false))
            {
                continue;
            }

            // No aggregates: §11's histograms need a per-player WHO read, which an import never had.
            await writer.AppendPresenceAsync(
                new PresenceSample(gameId, sample.At, sample.Count, PresenceSource.ImportedMeasured, null, null),
                ct).ConfigureAwait(false);

            await writer.RecordProvenanceAsync(
                new ImportProvenance(0, gameId, ImportSubjectKind.Presence, null, sample.At,
                    game.SourceName, game.SourceKey, game.SourceUri, ImportTier.Measured, importedAt),
                ct).ConfigureAwait(false);

            presenceRows++;
        }

        var availabilityRows = 0;

        foreach (var span in game.Availability)
        {
            if (await provenance.ExistsAsync(gameId, ImportSubjectKind.Availability, null, span.From, game.SourceName, ct)
                .ConfigureAwait(false))
            {
                continue;
            }

            await writer.WriteClosedAvailabilityAsync(
                gameId,
                span.Reachable ? AvailabilityState.Reachable : AvailabilityState.Unreachable,
                span.Reachable ? FailureCause.None : FailureCause.Unknown,
                span.From,
                span.To ?? importedAt,
                ct).ConfigureAwait(false);

            await writer.RecordProvenanceAsync(
                new ImportProvenance(0, gameId, ImportSubjectKind.Availability, null, span.From,
                    game.SourceName, game.SourceKey, game.SourceUri, ImportTier.Measured, importedAt),
                ct).ConfigureAwait(false);

            availabilityRows++;
        }

        return new HistoryWrite(presenceRows, availabilityRows, 0);
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 51 tests.

- [ ] **Step 8: Commit**

```bash
git add src/MUI.Backfill tests/MUI.Backfill.Tests
git commit -m "feat(backfill): seed crawl targets and write fields only for games we already know"
```

---

### Task 8: The tier is enforced by code, not by care

**Files:**
- Create: `tests/MUI.Backfill.Tests/HistoryTierTests.cs`
- Create: `tests/MUI.Backfill.Tests/ImportedOriginTests.cs`
- Modify: `src/MUI.Backfill/HistorySink.cs`

**Interfaces:**
- Consumes: `HistoryWrite`, `IHistorySink`, `AssertedHistorySink`, `MeasuredHistorySink`,
  `HistorySink.For`, `CommittingImportWriter`, `DryRunImportWriter`, `ImportPipeline` (Task 7);
  `IImportProvenanceRepository`, `NpgsqlImportProvenanceRepository` (Task 5);
  `MUI.Storage.IAvailabilityRepository` — `.InsertImportedAsync`, `.CumulativeReachableAsync`,
  `.CumulativeImportedMeasuredReachableAsync` — plus `NpgsqlAvailabilityRepository` and
  `MigrationRunner` (Plan 2); `MUI.Catalog.ArchivePolicy.GraceFor` and `.Floor` (existing).
- Produces: **no new production type.** Grace is computed in exactly one place in this system — Plan
  02's `ArchiveSweeper`, from the two cumulative sums the availability repository already answers —
  and a second calculator here would count the same history twice. What this task changes is three
  lines of `AssertedHistorySink`, so a refusal is *counted* rather than swallowed; the rest of it is
  the tests that hold the tier in place.

**This is the task that stops the rule being a comment.** Spec §7.6's table is two rows: a measured
source may populate historical `AvailabilityInterval` and `PresenceSample` rows and counts toward
archive grace at half weight; an asserted source seeds discovery and endpoints only. The pin below
hands the pipeline an asserted importer whose `ImportedGame` is *full* of presence and availability
rows and asserts that zero of them were written and that the refusal was counted.

The half weight is asserted the same way it is *computed* — by asking the availability repository for
its two sums and handing them to `ArchivePolicy.GraceFor` — because that is precisely what
`ArchiveSweeper` does, and a test that took any other route would agree with itself rather than with
the thing that ships. It rests on one column, `availability_interval.origin`, so this task also pins
the value that lands in it: in memory through `InMemoryAvailabilityRepository.Origins`, and once
against a real Postgres, because the fake and the column are two spellings of the same fact and
nothing else holds them together.

- [ ] **Step 1: Write the failing test**

`tests/MUI.Backfill.Tests/HistoryTierTests.cs`:

```csharp
using MUI.Backfill.Tests.Support;
using MUI.Catalog;

namespace MUI.Backfill.Tests;

/// <summary>
/// Spec §7.6's two tiers. Not "we are careful not to write history for an asserted source" — the
/// object that would do the writing does not exist on that path.
/// </summary>
public class HistoryTierTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        InMemoryCrawlTargetRepository Targets,
        InMemoryGameRepository Games,
        InMemoryEndpointRepository Endpoints,
        InMemoryGameFieldRepository Fields,
        InMemoryPresenceRepository Presence,
        InMemoryAvailabilityRepository Availability,
        InMemoryImportProvenanceRepository Provenance,
        ImportPipeline Pipeline);

    private static Harness Build()
    {
        var targets = new InMemoryCrawlTargetRepository();
        var games = new InMemoryGameRepository();
        var endpoints = new InMemoryEndpointRepository();
        var fields = new InMemoryGameFieldRepository();
        var presence = new InMemoryPresenceRepository();
        var availability = new InMemoryAvailabilityRepository();
        var provenance = new InMemoryImportProvenanceRepository();

        return new Harness(targets, games, endpoints, fields, presence, availability, provenance,
            new ImportPipeline(targets, games, endpoints, fields, presence, availability, provenance,
                new ManualTimeProvider(Now)));
    }

    private static async Task<Guid> SeedProbedGameAsync(Harness harness)
    {
        var gameId = Guid.NewGuid();
        await harness.Games.InsertAsync(
            new Game(gameId, "anachronism", "Anachronism", LifecycleState.Active, false, Now, Now, null),
            CancellationToken.None);
        await harness.Endpoints.UpsertAsync(
            new GameEndpoint(gameId, "anachronism.example", 4000, EndpointKind.Telnet, Now, Now, EndpointState.Active),
            CancellationToken.None);
        return gameId;
    }

    /// <summary>A record stuffed with history it may or may not be entitled to.</summary>
    private static ImportedGame Stuffed(string sourceName) => new()
    {
        SourceName = sourceName,
        SourceKey = "anachronism",
        Name = "Anachronism",
        Endpoints = [new ImportedEndpoint("anachronism.example", 4000, EndpointKind.Telnet)],
        Presence =
        [
            new ImportedPresence(Now.AddHours(-3), 24),
            new ImportedPresence(Now.AddHours(-2), 31),
            new ImportedPresence(Now.AddHours(-1), 18),
        ],
        Availability =
        [
            new ImportedAvailability(Now.AddYears(-3), Now.AddYears(-1), true),
            new ImportedAvailability(Now.AddYears(-1), Now.AddDays(-1), false),
        ],
    };

    [Test]
    public async Task AnAssertedSourceStuffedWithHistoryWritesNoneOfItAndIsCountedForTrying()
    {
        var harness = Build();
        await SeedProbedGameAsync(harness);

        var importer = new FakeImporter("The MUD Connector", ImportTier.Asserted,
            FakeImporter.ApiEtiquette("TheMudConnector"), [Stuffed("The MUD Connector")]);

        var report = await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        await Assert.That(harness.Presence.Samples).IsEmpty();
        await Assert.That(harness.Availability.Intervals).IsEmpty();
        await Assert.That(report.PresenceRows).IsEqualTo(0);
        await Assert.That(report.AvailabilityRows).IsEqualTo(0);
        await Assert.That(report.Rejected).IsEqualTo(5);
        await Assert.That(report.Notes.Any(n => n.Contains("asserted source", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task AnAssertedSourceStillSeedsDiscoveryAndEndpoints()
    {
        var harness = Build();
        var gameId = await SeedProbedGameAsync(harness);

        var importer = new FakeImporter("The MUD Connector", ImportTier.Asserted,
            FakeImporter.ApiEtiquette("TheMudConnector"), [Stuffed("The MUD Connector")]);

        await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        await Assert.That(harness.Targets.Targets.Count).IsEqualTo(1);
        await Assert.That(harness.Endpoints.Endpoints.Count(e => e.GameId == gameId)).IsEqualTo(1);
    }

    [Test]
    public async Task TheAssertedSinkHoldsNothingItCouldWriteWith()
    {
        // The rule is enforced by construction: this type has no repository, no writer, no clock.
        var constructors = typeof(AssertedHistorySink).GetConstructors();

        await Assert.That(constructors.Length).IsEqualTo(1);
        await Assert.That(constructors[0].GetParameters().Length).IsEqualTo(0);
        await Assert.That(typeof(AssertedHistorySink).GetFields(
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public).Length).IsEqualTo(0);
    }

    [Test]
    public async Task TheTierChoosesTheSinkAndNothingElseDoes()
    {
        var writer = new DryRunImportWriter();
        var provenance = new InMemoryImportProvenanceRepository();

        await Assert.That(HistorySink.For(ImportTier.Asserted, writer, provenance, Now))
            .IsTypeOf<AssertedHistorySink>();
        await Assert.That(HistorySink.For(ImportTier.Measured, writer, provenance, Now))
            .IsTypeOf<MeasuredHistorySink>();
    }

    [Test]
    public async Task AMeasuredSourceWritesItsPresenceLabelledAsImported()
    {
        var harness = Build();
        var gameId = await SeedProbedGameAsync(harness);

        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Stuffed("MudVerse")]);

        var report = await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        await Assert.That(report.PresenceRows).IsEqualTo(3);
        await Assert.That(report.Rejected).IsEqualTo(0);
        await Assert.That(harness.Presence.Samples.Count).IsEqualTo(3);

        foreach (var sample in harness.Presence.Samples)
        {
            await Assert.That(sample.GameId).IsEqualTo(gameId);
            await Assert.That(sample.Source).IsEqualTo(PresenceSource.ImportedMeasured);
            await Assert.That(sample.AggregatesJson).IsNull();
            await Assert.That(sample.UnmeasurableReason).IsNull();
        }
    }

    [Test]
    public async Task AnImportedAvailabilitySpanIsNeverLeftOpen()
    {
        var harness = Build();
        await SeedProbedGameAsync(harness);

        var stuffed = Stuffed("MudVerse") with
        {
            Availability = [new ImportedAvailability(Now.AddYears(-2), null, true)],
        };
        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [stuffed]);

        await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        // We did not measure it and cannot extend it, so it ends at the import instant.
        await Assert.That(harness.Availability.Intervals.Single().ToAt).IsEqualTo(Now);
    }

    [Test]
    public async Task AnUnreachableImportedSpanCarriesUnknownRatherThanAGuessedCause()
    {
        var harness = Build();
        await SeedProbedGameAsync(harness);

        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Stuffed("MudVerse")]);

        await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        var dark = harness.Availability.Intervals.Single(i => i.State is AvailabilityState.Unreachable);
        await Assert.That(dark.Cause).IsEqualTo(FailureCause.Unknown);

        var live = harness.Availability.Intervals.Single(i => i.State is AvailabilityState.Reachable);
        await Assert.That(live.Cause).IsEqualTo(FailureCause.None);
    }

    [Test]
    public async Task AnImportedSpanIsStampedImportedMeasuredAndNeverFirstParty()
    {
        var harness = Build();
        await SeedProbedGameAsync(harness);

        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Stuffed("MudVerse")]);

        await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        // The whole of §7.5 rests on this one word. OpenAsync would have written 'first_party' and
        // credited MudVerse's history at full weight, and the resulting grace would still have looked
        // entirely plausible — which is why it is asserted rather than reasoned about.
        foreach (var interval in harness.Availability.Intervals)
        {
            await Assert.That(harness.Availability.Origins[interval.Id]).IsEqualTo("imported_measured");
        }
    }

    [Test]
    public async Task FourYearsOfImportedReachableTimeIsCreditedAsTwo()
    {
        var harness = Build();
        var gameId = await SeedProbedGameAsync(harness);

        var stuffed = Stuffed("MudVerse") with
        {
            Presence = [],
            Availability = [new ImportedAvailability(Now.AddDays(-1460), Now, true)],
        };
        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [stuffed]);
        await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        // Asked exactly the way ArchiveSweeper asks it: the repository separates the two sums by the
        // origin column, and ArchivePolicy applies the weight. There is no calculator in between —
        // grace is computed in one place in this system, and this test reads that place.
        var ours = await harness.Availability.CumulativeReachableAsync(gameId, Now, CancellationToken.None);
        var imported = await harness.Availability
            .CumulativeImportedMeasuredReachableAsync(gameId, Now, CancellationToken.None);

        await Assert.That(ours).IsEqualTo(TimeSpan.Zero);
        await Assert.That(imported.TotalDays).IsEqualTo(1460).Within(0.001);

        // §7.5: half weight, then clamp. 1460 × 0.5 ÷ 4 = 182.5 days — the same as two years of ours.
        var grace = ArchivePolicy.GraceFor(ours, imported);
        await Assert.That(grace).IsEqualTo(ArchivePolicy.GraceFor(firstPartyReachable: TimeSpan.FromDays(730)));
        await Assert.That(grace.TotalDays).IsEqualTo(182.5).Within(0.01);
    }

    [Test]
    public async Task AnAssertedSourceEarnsNoGraceBecauseItWroteNoAvailabilityAtAll()
    {
        var harness = Build();
        var gameId = await SeedProbedGameAsync(harness);

        var importer = new FakeImporter("The MUD Connector", ImportTier.Asserted,
            FakeImporter.ApiEtiquette("TheMudConnector"), [Stuffed("The MUD Connector")]);
        await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        var imported = await harness.Availability
            .CumulativeImportedMeasuredReachableAsync(gameId, Now, CancellationToken.None);

        await Assert.That(imported).IsEqualTo(TimeSpan.Zero);
        await Assert.That(ArchivePolicy.GraceFor(TimeSpan.Zero, imported)).IsEqualTo(ArchivePolicy.Floor);
    }

    [Test]
    public async Task OurOwnReachableTimeIsNotCountedAsImported()
    {
        var harness = Build();
        var gameId = await SeedProbedGameAsync(harness);

        // An interval our own crawler wrote, through the first-party path. It must be credited at
        // full weight and must not appear in the imported sum — the inversion of the bug above.
        var id = await harness.Availability.OpenAsync(gameId, AvailabilityState.Reachable, FailureCause.None,
            Now.AddDays(-1460), CancellationToken.None);
        await harness.Availability.CloseAsync(id, Now, CancellationToken.None);

        await Assert.That(await harness.Availability
                .CumulativeImportedMeasuredReachableAsync(gameId, Now, CancellationToken.None))
            .IsEqualTo(TimeSpan.Zero);
        await Assert.That((await harness.Availability
                .CumulativeReachableAsync(gameId, Now, CancellationToken.None)).TotalDays)
            .IsEqualTo(1460).Within(0.001);
    }
}
```

`tests/MUI.Backfill.Tests/ImportedOriginTests.cs` — the second Postgres-gated test in this suite,
gated identically to Task 5's so the suite still runs where there is no Linux Docker daemon:

```csharp
using MUI.Backfill.Tests.Support;
using MUI.Storage;

using Npgsql;
using Testcontainers.PostgreSql;

using static TUnit.Core.HookType;

namespace MUI.Backfill.Tests;

/// <summary>
/// <c>InMemoryAvailabilityRepository.Origins</c> and <c>availability_interval.origin</c> are two
/// spellings of the same fact, and <c>HistoryTierTests</c> reads only the first. This one reads the column,
/// through the real repository, so a fake that agreed with itself cannot hide a sink that writes a
/// third party's history as ours.
/// </summary>
public class ImportedOriginTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static PostgreSqlContainer? _container;
    private static NpgsqlDataSource? _source;

    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("MUI_INTEGRATION"), "1", StringComparison.Ordinal);

    [Before(Class)]
    public static async Task StartPostgres()
    {
        if (!Enabled)
        {
            return;
        }

        _container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
        await _container.StartAsync();

        _source = NpgsqlDataSource.Create(_container.GetConnectionString());
        await new MigrationRunner(_source).ApplyAsync(CancellationToken.None);
    }

    [After(Class)]
    public static async Task StopPostgres()
    {
        if (_source is not null)
        {
            await _source.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Test]
    public async Task AMeasuredImportLandsAsImportedMeasuredInTheColumnItself()
    {
        if (!Enabled)
        {
            return;
        }

        var gameId = Guid.NewGuid();

        // availability_interval.game_id references game(id), so the row has to exist first. Raw SQL
        // rather than NpgsqlGameRepository: what is under test is one column of another table.
        await using (var seed = _source!.CreateCommand(
            """
            INSERT INTO game (id, slug, name, state, is_claimed, first_seen_at)
            VALUES (@gameId, @slug, 'Anachronism', 'active', false, @firstSeenAt)
            """))
        {
            seed.Parameters.AddWithValue("gameId", gameId);
            seed.Parameters.AddWithValue("slug", gameId.ToString("N"));
            seed.Parameters.AddWithValue("firstSeenAt", Now.AddYears(-4));
            await seed.ExecuteNonQueryAsync();
        }

        // The real availability repository, the real sink, and nothing else faked on the path the
        // origin travels. The other repositories the writer holds are irrelevant to this assertion.
        var writer = new CommittingImportWriter(
            new InMemoryCrawlTargetRepository(),
            new InMemoryEndpointRepository(),
            new InMemoryGameFieldRepository(),
            new InMemoryPresenceRepository(),
            new NpgsqlAvailabilityRepository(_source!),
            new InMemoryImportProvenanceRepository());

        var sink = new MeasuredHistorySink(writer, new InMemoryImportProvenanceRepository(), Now);

        await sink.WriteAsync(
            gameId,
            new ImportedGame
            {
                SourceName = "MudVerse",
                SourceKey = "anachronism",
                Name = "Anachronism",
                Availability = [new ImportedAvailability(Now.AddYears(-2), Now.AddDays(-1), true)],
            },
            CancellationToken.None);

        var origins = new List<string>();

        await using (var read = _source!.CreateCommand(
            "SELECT origin FROM availability_interval WHERE game_id = @gameId"))
        {
            read.Parameters.AddWithValue("gameId", gameId);

            await using var reader = await read.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                origins.Add(reader.GetString(0));
            }
        }

        await Assert.That(origins.Count).IsEqualTo(1);
        await Assert.That(origins[0]).IsEqualTo("imported_measured");

        // And the two sums come apart on it, which is the only reason the column exists.
        var repository = new NpgsqlAvailabilityRepository(_source!);

        await Assert.That(await repository.CumulativeReachableAsync(gameId, Now, CancellationToken.None))
            .IsEqualTo(TimeSpan.Zero);
        await Assert.That((await repository
                .CumulativeImportedMeasuredReachableAsync(gameId, Now, CancellationToken.None)).TotalDays)
            .IsGreaterThan(700);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: FAIL — `AnAssertedSourceStuffedWithHistoryWritesNoneOfItAndIsCountedForTrying`, on
`Expected 5 but was 0` for `report.Rejected`, and on the note that is never added. Task 7's
`AssertedHistorySink` writes nothing, which is the half that was already structural; what it does not
do is *say* that it refused, so a run against The MUD Connector is indistinguishable from a run
against a source that offered no history at all. Every other test in the file passes already, and
that is the point of the task: the tier is a property of the object graph, and these tests are what
stop somebody "simplifying" it back into an `if`.

- [ ] **Step 3: Make the refusal a number**

The behaviour under test is one expression. In `src/MUI.Backfill/HistorySink.cs`, replace
`AssertedHistorySink` with its finished form:

```csharp
/// <summary>
/// The asserted tier's sink. It takes no constructor parameters, which is the enforcement: it holds
/// no writer and no repository, so spec §7.6's "no history, no presence, no grace" is a fact about
/// this type rather than a rule somebody has to remember.
/// </summary>
/// <remarks>
/// It counts what it turned away. A hand-maintained directory offering three presence points and two
/// availability spans is not an error to swallow — <c>ImportRunner</c> prints the figure, and the
/// difference between "The MUD Connector offered five rows we are not entitled to keep" and "The MUD
/// Connector offered nothing" is the difference between a working import and a broken parser.
/// </remarks>
public sealed class AssertedHistorySink : IHistorySink
{
    public Task<HistoryWrite> WriteAsync(Guid gameId, ImportedGame game, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(game);
        return Task.FromResult(new HistoryWrite(0, 0, game.Presence.Count + game.Availability.Count));
    }
}
```

Nothing else changes: `MeasuredHistorySink` was finished in Task 7, and it reaches
`IAvailabilityRepository.InsertImportedAsync` — never `OpenAsync` — through
`CommittingImportWriter.WriteClosedAvailabilityAsync`, which is what the origin assertions above
are reading back.

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 63 tests. Without `MUI_INTEGRATION=1` the Postgres test returns early and the count
is unchanged; with it, `ImportedOriginTests` starts a container of its own.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Backfill tests/MUI.Backfill.Tests/HistoryTierTests.cs tests/MUI.Backfill.Tests/ImportedOriginTests.cs
git commit -m "feat(backfill): make an asserted source structurally unable to write history"
```

---

### Task 9: Re-running the backfill changes nothing

**Files:**
- Create: `tests/MUI.Backfill.Tests/ImportIdempotenceTests.cs`
- Modify: `src/MUI.Backfill/ImportPipeline.cs` (only if a test below fails)

**Interfaces:**
- Consumes: everything from Tasks 1–8. No new public types.

**Why this is its own task:** §14 calls the backfill "a one-off import", but an import that cannot be
run twice is an import nobody dares run once. Idempotence here comes from three places that already
exist — `ICrawlTargetRepository.AddAsync` is monotonic and address-keyed, `IGameFieldRepository`
upserts and the pipeline confirms rather than rewrites an unchanged value, and every history row is
guarded by `IImportProvenanceRepository.ExistsAsync`. This task proves all three at once.

- [ ] **Step 1: Write the failing test**

`tests/MUI.Backfill.Tests/ImportIdempotenceTests.cs`:

```csharp
using MUI.Backfill.Tests.Support;
using MUI.Catalog;

namespace MUI.Backfill.Tests;

/// <summary>
/// Running the backfill twice must leave the database exactly as one run left it. Spec §14 calls it a
/// one-off, which is precisely why it has to survive being repeated.
/// </summary>
public class ImportIdempotenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        InMemoryCrawlTargetRepository Targets,
        InMemoryGameRepository Games,
        InMemoryEndpointRepository Endpoints,
        InMemoryGameFieldRepository Fields,
        InMemoryPresenceRepository Presence,
        InMemoryAvailabilityRepository Availability,
        InMemoryImportProvenanceRepository Provenance,
        ImportPipeline Pipeline);

    private static Harness Build()
    {
        var targets = new InMemoryCrawlTargetRepository();
        var games = new InMemoryGameRepository();
        var endpoints = new InMemoryEndpointRepository();
        var fields = new InMemoryGameFieldRepository();
        var presence = new InMemoryPresenceRepository();
        var availability = new InMemoryAvailabilityRepository();
        var provenance = new InMemoryImportProvenanceRepository();

        return new Harness(targets, games, endpoints, fields, presence, availability, provenance,
            new ImportPipeline(targets, games, endpoints, fields, presence, availability, provenance,
                new ManualTimeProvider(Now)));
    }

    private static async Task<Guid> SeedProbedGameAsync(Harness harness, string host, int port)
    {
        var gameId = Guid.NewGuid();
        await harness.Games.InsertAsync(
            new Game(gameId, "anachronism", "Anachronism", LifecycleState.Active, false, Now, Now, null),
            CancellationToken.None);
        await harness.Endpoints.UpsertAsync(
            new GameEndpoint(gameId, host, port, EndpointKind.Telnet, Now, Now, EndpointState.Active),
            CancellationToken.None);
        return gameId;
    }

    private static ImportedGame Record(string sourceName) => new()
    {
        SourceName = sourceName,
        SourceKey = "anachronism",
        Name = "Anachronism",
        Endpoints = [new ImportedEndpoint("anachronism.example", 4000, EndpointKind.Telnet)],
        Fields = new Dictionary<string, string> { ["codebase"] = "Evennia 4.2" },
        Presence = [new ImportedPresence(Now.AddHours(-1), 24)],
        Availability = [new ImportedAvailability(Now.AddYears(-2), Now.AddDays(-1), true)],
    };

    [Test]
    public async Task TheSecondRunWritesNothingAndSaysSo()
    {
        var harness = Build();
        await SeedProbedGameAsync(harness, "anachronism.example", 4000);
        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Record("MudVerse")]);

        var first = await harness.Pipeline.RunAsync(importer, CancellationToken.None);
        var second = await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        await Assert.That(first.TargetsAdded).IsEqualTo(1);
        await Assert.That(first.FieldsWritten).IsEqualTo(1);
        await Assert.That(first.PresenceRows).IsEqualTo(1);
        await Assert.That(first.AvailabilityRows).IsEqualTo(1);

        await Assert.That(second.GamesSeen).IsEqualTo(1);
        await Assert.That(second.TargetsAdded).IsEqualTo(0);
        await Assert.That(second.FieldsWritten).IsEqualTo(0);
        await Assert.That(second.PresenceRows).IsEqualTo(0);
        await Assert.That(second.AvailabilityRows).IsEqualTo(0);
    }

    [Test]
    public async Task TheSecondRunLeavesTheRowCountsWhereTheFirstLeftThem()
    {
        var harness = Build();
        await SeedProbedGameAsync(harness, "anachronism.example", 4000);
        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Record("MudVerse")]);

        await harness.Pipeline.RunAsync(importer, CancellationToken.None);
        await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        await Assert.That(harness.Targets.Targets.Count).IsEqualTo(1);
        await Assert.That(harness.Endpoints.Endpoints.Count).IsEqualTo(1);
        await Assert.That(harness.Fields.Fields.Count).IsEqualTo(1);
        await Assert.That(harness.Presence.Samples.Count).IsEqualTo(1);
        await Assert.That(harness.Availability.Intervals.Count).IsEqualTo(1);
        await Assert.That(harness.Fields.Changes).IsEmpty();
    }

    [Test]
    public async Task TheSecondRunConfirmsTheFieldRatherThanRewritingIt()
    {
        var harness = Build();
        await SeedProbedGameAsync(harness, "anachronism.example", 4000);
        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Record("MudVerse")]);

        await harness.Pipeline.RunAsync(importer, CancellationToken.None);
        await harness.Pipeline.RunAsync(importer, CancellationToken.None);

        await Assert.That(harness.Fields.Confirmations.Count(c => c.Field == "codebase")).IsEqualTo(1);
    }

    [Test]
    public async Task TwoSourcesListingTheSameGameAtTheSameHostYieldOneGameAndOneCrawlTarget()
    {
        var harness = Build();
        var gameId = await SeedProbedGameAsync(harness, "anachronism.example", 4000);

        var first = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Record("MudVerse")]);
        var second = new FakeImporter("MudStats", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudStats"), [Record("MudStats")]);

        await harness.Pipeline.RunAsync(first, CancellationToken.None);
        await harness.Pipeline.RunAsync(second, CancellationToken.None);

        await Assert.That(harness.Games.Games.Count).IsEqualTo(1);
        await Assert.That(harness.Games.Games[0].Id).IsEqualTo(gameId);
        await Assert.That(harness.Targets.Targets.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TwoSourcesMayEachStampTheSameHistoryWithoutDuplicatingTheRow()
    {
        var harness = Build();
        var gameId = await SeedProbedGameAsync(harness, "anachronism.example", 4000);

        var first = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Record("MudVerse")]);
        var second = new FakeImporter("MudStats", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudStats"), [Record("MudStats")]);

        await harness.Pipeline.RunAsync(first, CancellationToken.None);
        await harness.Pipeline.RunAsync(second, CancellationToken.None);

        // Both sites measured the same hour, so both leave a stamp — but the presence row is one row
        // per (game, instant), because the second source's ExistsAsync check is per source *and*
        // subject and the pipeline only writes what it stamped.
        var stamps = await harness.Provenance.ForGameAsync(gameId, CancellationToken.None);
        await Assert.That(stamps.Count(s => s.SubjectKind is ImportSubjectKind.Presence)).IsEqualTo(2);
        await Assert.That(harness.Presence.Samples.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ADryRunOverAFreshDatabaseWritesNothingAtAll()
    {
        var harness = Build();
        await SeedProbedGameAsync(harness, "anachronism.example", 4000);
        var importer = new FakeImporter("MudVerse", ImportTier.Measured,
            FakeImporter.ApiEtiquette("MudVerse"), [Record("MudVerse")]);

        var report = await harness.Pipeline.DryRunAsync(importer, CancellationToken.None);

        await Assert.That(report.TargetsAdded).IsEqualTo(1);
        await Assert.That(report.FieldsWritten).IsEqualTo(1);
        await Assert.That(report.PresenceRows).IsEqualTo(1);
        await Assert.That(report.AvailabilityRows).IsEqualTo(1);

        await Assert.That(harness.Targets.Targets).IsEmpty();
        await Assert.That(harness.Fields.Fields).IsEmpty();
        await Assert.That(harness.Presence.Samples).IsEmpty();
        await Assert.That(harness.Availability.Intervals).IsEmpty();
        await Assert.That(harness.Provenance.Rows).IsEmpty();
        await Assert.That(harness.Endpoints.Endpoints.Count).IsEqualTo(1);   // only the one we seeded
    }
}
```

- [ ] **Step 2: Run it and read which assertions fail**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: the first three tests pass on the Task 7/8 implementation as written.
`TwoSourcesMayEachStampTheSameHistoryWithoutDuplicatingTheRow` documents the deliberate behaviour
that two sources each get their own stamp and each write their own presence row (two measurements of
the same hour by two different probers are two facts, not one). If any assertion fails, fix the
pipeline rather than the assertion.

- [ ] **Step 3: If `TheSecondRunWritesNothingAndSaysSo` fails on `TargetsAdded`, fix the guard**

The pipeline must consult `ICrawlTargetRepository.ByAddressAsync` *before* adding, and must not count
a target it did not add. The already-written `SeedTargetsAsync` does exactly that; if a change has
since broken it, restore this shape:

```csharp
            if (await _targets.ByAddressAsync(endpoint.Host, endpoint.Port, ct).ConfigureAwait(false) is not null)
            {
                continue;
            }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 69 tests.

- [ ] **Step 5: Commit**

```bash
git add tests/MUI.Backfill.Tests/ImportIdempotenceTests.cs src/MUI.Backfill
git commit -m "test(backfill): pin that a second backfill run writes nothing"
```

---

### Task 10: `GrapevineImporter` — measured, documented API

**Files:**
- Create: `src/MUI.Backfill/Importers/GrapevineImporter.cs`
- Create: `tests/MUI.Backfill.Tests/Fixtures/grapevine-games.json`
- Create: `tests/MUI.Backfill.Tests/Importers/GrapevineImporterTests.cs`

**Interfaces:**
- Consumes: `DirectoryImporter`, `DirectorySource`, `ImportEtiquette`, `CrawlerIdentity`,
  `ImportedGame`, `ImportedEndpoint`, `ImportedPresence`, `ImportTier` (Tasks 1–7);
  `Support.FakeHttp`, `Support.Fixture`, `Support.ManualTimeProvider` (Tasks 3–4).
- Produces: `MUI.Backfill.Importers.GrapevineImporter(DirectorySource source)` with a static
  `GrapevineImporter.Etiquette(bool contactedMaintainer = false) → ImportEtiquette` and
  `GrapevineImporter.Create(HttpClient http, TimeProvider time) → GrapevineImporter`.

**Fixture format.** Grapevine publishes a documented JSON API. The recorded payload is a single JSON
object with a `games` array; each element carries the site's own `id`, the game's `name` and
`short_name`, a `homepage_url`, the `user_agent` string the game reported to Grapevine (which is a
codebase banner in practice), the `online_players` count from Grapevine's own check and the
`last_checked_at` instant that count belongs to, and a `connections` array of
`{ "type": "telnet" | "secure telnet" | "web", "host", "port" }`. Spec §10 names Grapevine as a seed
source we consume and republish rather than silo, and §3 notes its checker is a one-shot rather than
a continuous crawl — so this importer yields **at most one** `ImportedPresence` per game and **no**
`ImportedAvailability` at all. Nothing imported from Grapevine therefore accrues archive grace, which
is the correct outcome for a source that does not keep a series.

- [ ] **Step 1: Write the fixture**

`tests/MUI.Backfill.Tests/Fixtures/grapevine-games.json`:

```json
{
  "games": [
    {
      "id": "1f6c0e2a-2a0c-4a53-9a2a-2f5f5b0f7f11",
      "name": "Anachronism",
      "short_name": "ANACH",
      "homepage_url": "https://anachronism.example/",
      "user_agent": "Evennia 4.2",
      "online_players": 27,
      "last_checked_at": "2026-07-29T18:00:00Z",
      "connections": [
        { "type": "telnet", "host": "anachronism.example", "port": 4000 },
        { "type": "secure telnet", "host": "anachronism.example", "port": 4001 },
        { "type": "web", "host": "play.anachronism.example", "port": 443 }
      ]
    },
    {
      "id": "8c2b41d6-6f19-4a2e-9f3d-0b6a1c4e77a2",
      "name": "Chronicles of Ash",
      "short_name": "ASH",
      "homepage_url": "https://ash.example.net/",
      "user_agent": "PennMUSH 1.8.8p2",
      "online_players": null,
      "last_checked_at": "2026-07-29T18:00:00Z",
      "connections": [
        { "type": "telnet", "host": "ash.example.net", "port": 7777 }
      ]
    }
  ]
}
```

- [ ] **Step 2: Write the failing test**

`tests/MUI.Backfill.Tests/Importers/GrapevineImporterTests.cs`:

```csharp
using MUI.Backfill.Importers;
using MUI.Backfill.Tests.Support;
using MUI.Catalog;

namespace MUI.Backfill.Tests.Importers;

/// <summary>
/// Grapevine, read through its documented API (spec §7.6's measured tier, §10's "consume Grapevine
/// as a seed source"). Everything here comes from a committed fixture; no test in this suite has a
/// route to the network.
/// </summary>
public class GrapevineImporterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static async Task<IReadOnlyList<ImportedGame>> ReadAsync()
    {
        var etiquette = GrapevineImporter.Etiquette();
        var (_, client) = FakeHttp.Serving(
            (etiquette.RobotsUri.AbsoluteUri, "User-agent: *\nDisallow: /admin/\n"),
            (etiquette.ApiUri!.AbsoluteUri, Fixture.Read("grapevine-games.json")));

        var source = new DirectorySource(client, etiquette, new ManualTimeProvider(Now));
        await source.PrimeRobotsAsync(CancellationToken.None);

        var games = new List<ImportedGame>();
        await foreach (var game in new GrapevineImporter(source).ReadAsync(CancellationToken.None))
        {
            games.Add(game);
        }

        return games;
    }

    [Test]
    public async Task ItIsAMeasuredSourceReadThroughItsApi()
    {
        var etiquette = GrapevineImporter.Etiquette();

        await Assert.That(EtiquettePlanner.Decide(etiquette).Route).IsEqualTo(FetchRoute.Api);
        await Assert.That(etiquette.ScrapeUri).IsNull();
        await Assert.That(CrawlerIdentity.SelfIdentifies(etiquette.UserAgent)).IsTrue();

        var (_, client) = FakeHttp.Serving((etiquette.RobotsUri.AbsoluteUri, string.Empty));
        var importer = new GrapevineImporter(new DirectorySource(client, etiquette, new ManualTimeProvider(Now)));

        await Assert.That(importer.Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(importer.SourceName).IsEqualTo("Grapevine");
    }

    [Test]
    public async Task EveryListedGameIsYieldedWithItsSiteKey()
    {
        var games = await ReadAsync();

        await Assert.That(games.Count).IsEqualTo(2);
        await Assert.That(games[0].Name).IsEqualTo("Anachronism");
        await Assert.That(games[0].SourceName).IsEqualTo("Grapevine");
        await Assert.That(games[0].SourceKey).IsEqualTo("1f6c0e2a-2a0c-4a53-9a2a-2f5f5b0f7f11");
        await Assert.That(games[0].SourceUri).IsNotNull();
    }

    [Test]
    public async Task EachConnectionKindMapsToItsEndpointKind()
    {
        var games = await ReadAsync();

        await Assert.That(games[0].Endpoints.Count).IsEqualTo(3);
        await Assert.That(games[0].Endpoints[0]).IsEqualTo(new ImportedEndpoint("anachronism.example", 4000, EndpointKind.Telnet));
        await Assert.That(games[0].Endpoints[1]).IsEqualTo(new ImportedEndpoint("anachronism.example", 4001, EndpointKind.Tls));
        await Assert.That(games[0].Endpoints[2]).IsEqualTo(new ImportedEndpoint("play.anachronism.example", 443, EndpointKind.Http));
    }

    [Test]
    public async Task TheCodebaseBannerAndHomepageBecomeFields()
    {
        var games = await ReadAsync();

        await Assert.That(games[0].Fields["codebase"]).IsEqualTo("Evennia 4.2");
        await Assert.That(games[0].Fields["website"]).IsEqualTo("https://anachronism.example/");
    }

    [Test]
    public async Task AOneShotCheckYieldsOnePresencePointAndNoAvailability()
    {
        var games = await ReadAsync();

        await Assert.That(games[0].Presence.Count).IsEqualTo(1);
        await Assert.That(games[0].Presence[0].At).IsEqualTo(new DateTimeOffset(2026, 7, 29, 18, 0, 0, TimeSpan.Zero));
        await Assert.That(games[0].Presence[0].Count).IsEqualTo(27);

        // Grapevine keeps no series, so it contributes nothing to archive grace (§7.5).
        await Assert.That(games[0].Availability).IsEmpty();
    }

    [Test]
    public async Task AMissingCountIsNoSampleRatherThanAZero()
    {
        var games = await ReadAsync();

        await Assert.That(games[1].Name).IsEqualTo("Chronicles of Ash");
        await Assert.That(games[1].Presence).IsEmpty();
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'GrapevineImporter' could not be found`.

- [ ] **Step 4: Write the importer**

`src/MUI.Backfill/Importers/GrapevineImporter.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using MUI.Catalog;

namespace MUI.Backfill.Importers;

/// <summary>
/// Grapevine — a measured source (spec §7.6) read through its documented JSON API, which is why no
/// scrape URI is configured and none is needed. Spec §10 names Grapevine as a seed source we consume
/// and republish rather than silo.
/// </summary>
/// <remarks>
/// Grapevine's liveness check is a one-shot rather than a continuous crawl (spec §3), so each export
/// yields at most one presence point per game and no availability spans at all. Nothing imported here
/// accrues archive grace, and that is the honest outcome: we cannot credit a series that does not
/// exist.
/// </remarks>
public sealed class GrapevineImporter(DirectorySource source) : DirectoryImporter(source)
{
    public override string SourceName => "Grapevine";

    public override ImportTier Tier => ImportTier.Measured;

    public static ImportEtiquette Etiquette(bool contactedMaintainer = false) => new()
    {
        SourceName = "Grapevine",
        AttributionUri = new Uri("https://grapevine.haus/"),
        ApiUri = new Uri("https://grapevine.haus/api/games"),
        RobotsUri = new Uri("https://grapevine.haus/robots.txt"),
        UserAgent = CrawlerIdentity.UserAgent,
        MinimumInterval = TimeSpan.FromSeconds(5),
        ContactedMaintainer = contactedMaintainer,
    };

    public static GrapevineImporter Create(HttpClient http, TimeProvider time) =>
        new(new DirectorySource(http, Etiquette(), time));

    public override async IAsyncEnumerable<ImportedGame> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var body = await Source.GetStringAsync(Etiquette.ApiUri!, ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("games", out var games))
        {
            yield break;
        }

        foreach (var game in games.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            yield return Read(game);
        }
    }

    private static ImportedGame Read(JsonElement game)
    {
        var key = game.GetProperty("id").GetString() ?? string.Empty;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Text(game, "user_agent") is { Length: > 0 } codebase)
        {
            fields["codebase"] = codebase;
        }

        if (Text(game, "homepage_url") is { Length: > 0 } website)
        {
            fields["website"] = website;
        }

        var endpoints = new List<ImportedEndpoint>();
        if (game.TryGetProperty("connections", out var connections))
        {
            foreach (var connection in connections.EnumerateArray())
            {
                var host = Text(connection, "host");
                if (host is null || !connection.TryGetProperty("port", out var port))
                {
                    continue;
                }

                endpoints.Add(new ImportedEndpoint(host, port.GetInt32(), KindOf(Text(connection, "type"))));
            }
        }

        var presence = new List<ImportedPresence>();
        if (game.TryGetProperty("online_players", out var online)
            && online.ValueKind is JsonValueKind.Number
            && game.TryGetProperty("last_checked_at", out var checkedAt)
            && checkedAt.ValueKind is JsonValueKind.String)
        {
            presence.Add(new ImportedPresence(checkedAt.GetDateTimeOffset(), online.GetInt32()));
        }

        return new ImportedGame
        {
            SourceName = "Grapevine",
            SourceKey = key,
            Name = Text(game, "name") ?? key,
            SourceUri = new Uri($"https://grapevine.haus/games/{Uri.EscapeDataString(key)}"),
            Endpoints = endpoints,
            Fields = fields,
            Presence = presence,
        };
    }

    private static EndpointKind KindOf(string? type) => type switch
    {
        "secure telnet" => EndpointKind.Tls,
        "web" or "websocket" => type is "websocket" ? EndpointKind.WebSocket : EndpointKind.Http,
        _ => EndpointKind.Telnet,
    };

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 75 tests.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Backfill/Importers/GrapevineImporter.cs tests/MUI.Backfill.Tests
git commit -m "feat(backfill): import Grapevine through its documented API"
```

---

### Task 11: `MudVerseImporter` and `MudStatsImporter` — the two sources with a series

**Files:**
- Create: `src/MUI.Backfill/Importers/MudVerseImporter.cs`
- Create: `src/MUI.Backfill/Importers/MudStatsImporter.cs`
- Create: `tests/MUI.Backfill.Tests/Fixtures/mudverse-export.json`
- Create: `tests/MUI.Backfill.Tests/Fixtures/mudstats-muds.json`
- Create: `tests/MUI.Backfill.Tests/Importers/MeasuredImporterTests.cs`

**Interfaces:**
- Consumes: as Task 10.
- Produces: `MUI.Backfill.Importers.MudVerseImporter(DirectorySource)` with static
  `.Etiquette(bool contactedMaintainer = false)` and `.Create(HttpClient, TimeProvider)`;
  `MUI.Backfill.Importers.MudStatsImporter(DirectorySource)` with the same two statics.

**Fixture formats.** *MudVerse* runs an hourly MSSP crawl (spec §3) and is the closest prior art to
this project; its recorded export is a JSON object with `generated_at` and a `games` array, each
element carrying `key`, `name`, `host`, `port`, optional `tls_port`, an `mssp` object of raw MSSP
variable names to values, a `presence` array of `{ "at", "players" }`, and an `availability` array of
`{ "from", "to", "reachable" }` — the only imported source in this plan that carries genuine
availability spans, and therefore the only one that can move archive grace. *MudStats* publishes
per-MUD player history; its recorded export is a JSON object with a `muds` array of
`{ "id", "name", "hostname", "port", "codebase", "website", "players": [{ "date", "online" }] }` —
a series of counts but no reachability spans, so it contributes presence and no grace.

- [ ] **Step 1: Write both fixtures**

`tests/MUI.Backfill.Tests/Fixtures/mudverse-export.json`:

```json
{
  "generated_at": "2026-07-30T00:00:00Z",
  "games": [
    {
      "key": "anachronism",
      "name": "Anachronism",
      "host": "anachronism.example",
      "port": 4000,
      "tls_port": 4001,
      "mssp": {
        "CODEBASE": "Evennia 4.2",
        "FAMILY": "Custom",
        "WEBSITE": "https://anachronism.example/",
        "LANGUAGE": "English",
        "GENRE": "Fantasy"
      },
      "presence": [
        { "at": "2026-07-29T22:00:00Z", "players": 24 },
        { "at": "2026-07-29T23:00:00Z", "players": 31 }
      ],
      "availability": [
        { "from": "2024-07-30T00:00:00Z", "to": "2026-07-01T00:00:00Z", "reachable": true },
        { "from": "2026-07-01T00:00:00Z", "to": "2026-07-04T00:00:00Z", "reachable": false }
      ]
    },
    {
      "key": "ash",
      "name": "Chronicles of Ash",
      "host": "ash.example.net",
      "port": 7777,
      "mssp": {
        "CODEBASE": "PennMUSH 1.8.8p2",
        "WEBSITE": "https://ash.example.net/"
      },
      "presence": [],
      "availability": [
        { "from": "2025-01-01T00:00:00Z", "to": null, "reachable": true }
      ]
    }
  ]
}
```

`tests/MUI.Backfill.Tests/Fixtures/mudstats-muds.json`:

```json
{
  "muds": [
    {
      "id": 812,
      "name": "Anachronism",
      "hostname": "anachronism.example",
      "port": 4000,
      "codebase": "Evennia",
      "website": "https://anachronism.example/",
      "players": [
        { "date": "2026-07-29T22:00:00Z", "online": 24 },
        { "date": "2026-07-29T23:00:00Z", "online": 31 }
      ]
    },
    {
      "id": 913,
      "name": "The Fifth Age",
      "hostname": "fifthage.example.org",
      "port": 2860,
      "codebase": "TinyMUX",
      "website": null,
      "players": []
    }
  ]
}
```

- [ ] **Step 2: Write the failing test**

`tests/MUI.Backfill.Tests/Importers/MeasuredImporterTests.cs`:

```csharp
using MUI.Backfill.Importers;
using MUI.Backfill.Tests.Support;
using MUI.Catalog;

namespace MUI.Backfill.Tests.Importers;

/// <summary>
/// The two sources that keep a series: MudVerse's hourly MSSP crawl and MudStats' player history.
/// Both are spec §7.6's measured tier; only MudVerse publishes reachability spans, so only MudVerse
/// can move archive grace.
/// </summary>
public class MeasuredImporterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static async Task<IReadOnlyList<ImportedGame>> ReadAsync(
        ImportEtiquette etiquette,
        string fixtureName,
        Func<DirectorySource, IDirectoryImporter> build)
    {
        var (_, client) = FakeHttp.Serving(
            (etiquette.RobotsUri.AbsoluteUri, "User-agent: *\nDisallow: /admin/\n"),
            (RouteUri(etiquette).AbsoluteUri, Fixture.Read(fixtureName)));

        var source = new DirectorySource(client, etiquette, new ManualTimeProvider(Now));
        await source.PrimeRobotsAsync(CancellationToken.None);

        var games = new List<ImportedGame>();
        await foreach (var game in build(source).ReadAsync(CancellationToken.None))
        {
            games.Add(game);
        }

        return games;
    }

    private static Uri RouteUri(ImportEtiquette etiquette) =>
        EtiquettePlanner.Decide(etiquette).Uri ?? throw new InvalidOperationException("no route");

    [Test]
    public async Task MudVerseIsMeasuredAndReadFromItsBulkExport()
    {
        var etiquette = MudVerseImporter.Etiquette();

        await Assert.That(EtiquettePlanner.Decide(etiquette).Route).IsEqualTo(FetchRoute.BulkExport);
        await Assert.That(CrawlerIdentity.SelfIdentifies(etiquette.UserAgent)).IsTrue();
    }

    [Test]
    public async Task MudVerseYieldsBothEndpointsWhenATlsPortIsListed()
    {
        var games = await ReadAsync(MudVerseImporter.Etiquette(), "mudverse-export.json",
            source => new MudVerseImporter(source));

        await Assert.That(games.Count).IsEqualTo(2);
        await Assert.That(games[0].SourceName).IsEqualTo("MudVerse");
        await Assert.That(games[0].SourceKey).IsEqualTo("anachronism");
        await Assert.That(games[0].Endpoints).IsEquivalentTo(new[]
        {
            new ImportedEndpoint("anachronism.example", 4000, EndpointKind.Telnet),
            new ImportedEndpoint("anachronism.example", 4001, EndpointKind.Tls),
        });
        await Assert.That(games[1].Endpoints.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MudVerseMsspVariablesBecomeLowerCasedFields()
    {
        var games = await ReadAsync(MudVerseImporter.Etiquette(), "mudverse-export.json",
            source => new MudVerseImporter(source));

        await Assert.That(games[0].Fields["codebase"]).IsEqualTo("Evennia 4.2");
        await Assert.That(games[0].Fields["family"]).IsEqualTo("Custom");
        await Assert.That(games[0].Fields["language"]).IsEqualTo("English");
        await Assert.That(games[0].Fields["genre"]).IsEqualTo("Fantasy");
        await Assert.That(games[0].Fields["website"]).IsEqualTo("https://anachronism.example/");
    }

    [Test]
    public async Task MudVerseCarriesBothThePresenceSeriesAndTheReachabilitySpans()
    {
        var games = await ReadAsync(MudVerseImporter.Etiquette(), "mudverse-export.json",
            source => new MudVerseImporter(source));

        await Assert.That(games[0].Presence.Count).IsEqualTo(2);
        await Assert.That(games[0].Presence[1].Count).IsEqualTo(31);

        await Assert.That(games[0].Availability.Count).IsEqualTo(2);
        await Assert.That(games[0].Availability[0].Reachable).IsTrue();
        await Assert.That(games[0].Availability[0].To).IsEqualTo(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        await Assert.That(games[0].Availability[1].Reachable).IsFalse();
    }

    [Test]
    public async Task AnUnterminatedMudVerseSpanArrivesWithANullEndForTheSinkToClose()
    {
        var games = await ReadAsync(MudVerseImporter.Etiquette(), "mudverse-export.json",
            source => new MudVerseImporter(source));

        await Assert.That(games[1].Availability.Single().To).IsNull();
    }

    [Test]
    public async Task MudStatsIsMeasuredAndYieldsItsCountSeries()
    {
        var games = await ReadAsync(MudStatsImporter.Etiquette(), "mudstats-muds.json",
            source => new MudStatsImporter(source));

        await Assert.That(games.Count).IsEqualTo(2);
        await Assert.That(games[0].SourceName).IsEqualTo("MudStats");
        await Assert.That(games[0].SourceKey).IsEqualTo("812");
        await Assert.That(games[0].Endpoints.Single())
            .IsEqualTo(new ImportedEndpoint("anachronism.example", 4000, EndpointKind.Telnet));
        await Assert.That(games[0].Fields["codebase"]).IsEqualTo("Evennia");
        await Assert.That(games[0].Presence.Count).IsEqualTo(2);
    }

    [Test]
    public async Task MudStatsPublishesNoReachabilitySpansSoItEarnsNoGrace()
    {
        var games = await ReadAsync(MudStatsImporter.Etiquette(), "mudstats-muds.json",
            source => new MudStatsImporter(source));

        foreach (var game in games)
        {
            await Assert.That(game.Availability).IsEmpty();
        }
    }

    [Test]
    public async Task ANullWebsiteIsAbsentRatherThanTheStringNull()
    {
        var games = await ReadAsync(MudStatsImporter.Etiquette(), "mudstats-muds.json",
            source => new MudStatsImporter(source));

        await Assert.That(games[1].Name).IsEqualTo("The Fifth Age");
        await Assert.That(games[1].Fields.ContainsKey("website")).IsFalse();
        await Assert.That(games[1].Presence).IsEmpty();
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'MudVerseImporter' could not be found`.

- [ ] **Step 4: Write `MudVerseImporter`**

`src/MUI.Backfill/Importers/MudVerseImporter.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using MUI.Catalog;

namespace MUI.Backfill.Importers;

/// <summary>
/// MudVerse — the closest prior art to this project: an hourly MSSP crawl that purges dead games
/// (spec §3). A measured source, and the only importer in this plan carrying genuine reachability
/// spans, which makes it the only one that can move archive grace (§7.5, at half weight).
/// </summary>
public sealed class MudVerseImporter(DirectorySource source) : DirectoryImporter(source)
{
    public override string SourceName => "MudVerse";

    public override ImportTier Tier => ImportTier.Measured;

    public static ImportEtiquette Etiquette(bool contactedMaintainer = false) => new()
    {
        SourceName = "MudVerse",
        AttributionUri = new Uri("https://mudverse.com/"),
        BulkExportUri = new Uri("https://mudverse.com/export/mudverse-export.json"),
        RobotsUri = new Uri("https://mudverse.com/robots.txt"),
        UserAgent = CrawlerIdentity.UserAgent,
        MinimumInterval = TimeSpan.FromSeconds(10),
        ContactedMaintainer = contactedMaintainer,
    };

    public static MudVerseImporter Create(HttpClient http, TimeProvider time) =>
        new(new DirectorySource(http, Etiquette(), time));

    public override async IAsyncEnumerable<ImportedGame> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var body = await Source.GetStringAsync(Etiquette.BulkExportUri!, ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("games", out var games))
        {
            yield break;
        }

        foreach (var game in games.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            yield return Read(game);
        }
    }

    private static ImportedGame Read(JsonElement game)
    {
        var key = JsonText.String(game, "key") ?? string.Empty;
        var host = JsonText.String(game, "host") ?? string.Empty;

        var endpoints = new List<ImportedEndpoint>();
        if (host.Length > 0 && game.TryGetProperty("port", out var port) && port.ValueKind is JsonValueKind.Number)
        {
            endpoints.Add(new ImportedEndpoint(host, port.GetInt32(), EndpointKind.Telnet));
        }

        if (host.Length > 0 && game.TryGetProperty("tls_port", out var tls) && tls.ValueKind is JsonValueKind.Number)
        {
            endpoints.Add(new ImportedEndpoint(host, tls.GetInt32(), EndpointKind.Tls));
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (game.TryGetProperty("mssp", out var mssp) && mssp.ValueKind is JsonValueKind.Object)
        {
            foreach (var variable in mssp.EnumerateObject())
            {
                if (variable.Value.ValueKind is JsonValueKind.String && variable.Value.GetString() is { Length: > 0 } value)
                {
                    fields[variable.Name.ToLowerInvariant()] = value;
                }
            }
        }

        var presence = new List<ImportedPresence>();
        if (game.TryGetProperty("presence", out var samples) && samples.ValueKind is JsonValueKind.Array)
        {
            foreach (var sample in samples.EnumerateArray())
            {
                if (JsonText.Instant(sample, "at") is { } at
                    && sample.TryGetProperty("players", out var players)
                    && players.ValueKind is JsonValueKind.Number)
                {
                    presence.Add(new ImportedPresence(at, players.GetInt32()));
                }
            }
        }

        var availability = new List<ImportedAvailability>();
        if (game.TryGetProperty("availability", out var spans) && spans.ValueKind is JsonValueKind.Array)
        {
            foreach (var span in spans.EnumerateArray())
            {
                if (JsonText.Instant(span, "from") is not { } from)
                {
                    continue;
                }

                var reachable = span.TryGetProperty("reachable", out var flag)
                    && flag.ValueKind is JsonValueKind.True;

                availability.Add(new ImportedAvailability(from, JsonText.Instant(span, "to"), reachable));
            }
        }

        return new ImportedGame
        {
            SourceName = "MudVerse",
            SourceKey = key,
            Name = JsonText.String(game, "name") ?? key,
            SourceUri = new Uri($"https://mudverse.com/mud/{Uri.EscapeDataString(key)}"),
            Endpoints = endpoints,
            Fields = fields,
            Presence = presence,
            Availability = availability,
        };
    }
}

/// <summary>Small JSON readers shared by the three JSON importers. Absent and null read the same.</summary>
internal static class JsonText
{
    public static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    public static DateTimeOffset? Instant(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.String
        && value.TryGetDateTimeOffset(out var at)
            ? at
            : null;
}
```

- [ ] **Step 5: Write `MudStatsImporter`**

`src/MUI.Backfill/Importers/MudStatsImporter.cs`:

```csharp
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MUI.Catalog;

namespace MUI.Backfill.Importers;

/// <summary>
/// MudStats — a measured source (spec §7.6) that publishes per-MUD player history but no reachability
/// spans, so it contributes presence and nothing toward archive grace. It is also the site whose 2022
/// disappearance and 2024 return no directory noticed automatically (spec §3), which is the argument
/// for this project in one sentence.
/// </summary>
public sealed class MudStatsImporter(DirectorySource source) : DirectoryImporter(source)
{
    public override string SourceName => "MudStats";

    public override ImportTier Tier => ImportTier.Measured;

    public static ImportEtiquette Etiquette(bool contactedMaintainer = false) => new()
    {
        SourceName = "MudStats",
        AttributionUri = new Uri("https://mudstats.com/"),
        ApiUri = new Uri("https://mudstats.com/api/muds"),
        RobotsUri = new Uri("https://mudstats.com/robots.txt"),
        UserAgent = CrawlerIdentity.UserAgent,
        MinimumInterval = TimeSpan.FromSeconds(10),
        ContactedMaintainer = contactedMaintainer,
    };

    public static MudStatsImporter Create(HttpClient http, TimeProvider time) =>
        new(new DirectorySource(http, Etiquette(), time));

    public override async IAsyncEnumerable<ImportedGame> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var body = await Source.GetStringAsync(Etiquette.ApiUri!, ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("muds", out var muds))
        {
            yield break;
        }

        foreach (var mud in muds.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            yield return Read(mud);
        }
    }

    private static ImportedGame Read(JsonElement mud)
    {
        var key = mud.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.Number
            ? id.GetInt32().ToString(CultureInfo.InvariantCulture)
            : JsonText.String(mud, "id") ?? string.Empty;

        var endpoints = new List<ImportedEndpoint>();
        if (JsonText.String(mud, "hostname") is { Length: > 0 } host
            && mud.TryGetProperty("port", out var port)
            && port.ValueKind is JsonValueKind.Number)
        {
            endpoints.Add(new ImportedEndpoint(host, port.GetInt32(), EndpointKind.Telnet));
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (JsonText.String(mud, "codebase") is { Length: > 0 } codebase)
        {
            fields["codebase"] = codebase;
        }

        if (JsonText.String(mud, "website") is { Length: > 0 } website)
        {
            fields["website"] = website;
        }

        var presence = new List<ImportedPresence>();
        if (mud.TryGetProperty("players", out var players) && players.ValueKind is JsonValueKind.Array)
        {
            foreach (var point in players.EnumerateArray())
            {
                if (JsonText.Instant(point, "date") is { } at
                    && point.TryGetProperty("online", out var online)
                    && online.ValueKind is JsonValueKind.Number)
                {
                    presence.Add(new ImportedPresence(at, online.GetInt32()));
                }
            }
        }

        return new ImportedGame
        {
            SourceName = "MudStats",
            SourceKey = key,
            Name = JsonText.String(mud, "name") ?? key,
            SourceUri = new Uri($"https://mudstats.com/Mud/{Uri.EscapeDataString(key)}"),
            Endpoints = endpoints,
            Fields = fields,
            Presence = presence,
        };
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 83 tests.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Backfill/Importers tests/MUI.Backfill.Tests
git commit -m "feat(backfill): import MudVerse's crawl export and MudStats' player history"
```

---

### Task 12: `MudConnectorImporter` — asserted, and the scrape gate proved on a real importer

**Files:**
- Create: `src/MUI.Backfill/Importers/MudConnectorImporter.cs`
- Create: `tests/MUI.Backfill.Tests/Fixtures/tmc-mudlist.txt`
- Create: `tests/MUI.Backfill.Tests/Importers/MudConnectorImporterTests.cs`

**Interfaces:**
- Consumes: as Task 10.
- Produces: `MUI.Backfill.Importers.MudConnectorImporter(DirectorySource)` with static
  `.Etiquette(bool contactedMaintainer = false)` and `.Create(HttpClient, TimeProvider)`.

**Fixture format.** The MUD Connector publishes a plain-text mud list rather than a documented API:
`#`-prefixed comment lines, then one record per blank-line-separated block of `Key: value` lines with
the keys `Name`, `Address`, `Port`, `Codebase` and `Website`. It is hand-moderated and measures
nothing (spec §3's "unbounded moderation queue"), so it is `imported_asserted`: endpoints and fields,
never presence, never availability. It is also the one source in this plan whose only route is a
scrape, which makes it the natural place to prove that `ContactedMaintainer` really does gate the run.

- [ ] **Step 1: Write the fixture**

`tests/MUI.Backfill.Tests/Fixtures/tmc-mudlist.txt`:

```text
# The MUD Connector — mud list export
# Hand-maintained listings. No liveness measurement of any kind.

Name: Anachronism
Address: anachronism.example
Port: 4000
Codebase: Evennia
Website: https://anachronism.example/

Name: Chronicles of Ash
Address: ash.example.net
Port: 7777
Codebase: PennMUSH
Website: https://ash.example.net/

Name: Listing With No Address
Codebase: Unknown
```

- [ ] **Step 2: Write the failing test**

`tests/MUI.Backfill.Tests/Importers/MudConnectorImporterTests.cs`:

```csharp
using MUI.Backfill.Importers;
using MUI.Backfill.Tests.Support;
using MUI.Catalog;

namespace MUI.Backfill.Tests.Importers;

/// <summary>
/// The MUD Connector: spec §7.6's asserted tier, and the one source here whose only route is a
/// scrape — so it is also where the "email the maintainer first" gate is proved on a real importer.
/// </summary>
public class MudConnectorImporterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static async Task<IReadOnlyList<ImportedGame>> ReadAsync()
    {
        var etiquette = MudConnectorImporter.Etiquette(contactedMaintainer: true);
        var (_, client) = FakeHttp.Serving(
            (etiquette.RobotsUri.AbsoluteUri, "User-agent: *\nCrawl-delay: 20\n"),
            (etiquette.ScrapeUri!.AbsoluteUri, Fixture.Read("tmc-mudlist.txt")));

        var source = new DirectorySource(client, etiquette, new ManualTimeProvider(Now));
        await source.PrimeRobotsAsync(CancellationToken.None);

        var games = new List<ImportedGame>();
        await foreach (var game in new MudConnectorImporter(source).ReadAsync(CancellationToken.None))
        {
            games.Add(game);
        }

        return games;
    }

    [Test]
    public async Task ItIsAssertedAndItsOnlyRouteIsAScrape()
    {
        var etiquette = MudConnectorImporter.Etiquette(contactedMaintainer: true);

        await Assert.That(etiquette.BulkExportUri).IsNull();
        await Assert.That(etiquette.ApiUri).IsNull();
        await Assert.That(EtiquettePlanner.Decide(etiquette).Route).IsEqualTo(FetchRoute.Scrape);

        var (_, client) = FakeHttp.Serving((etiquette.RobotsUri.AbsoluteUri, string.Empty));
        var importer = new MudConnectorImporter(new DirectorySource(client, etiquette, new ManualTimeProvider(Now)));

        await Assert.That(importer.Tier).IsEqualTo(ImportTier.Asserted);
        await Assert.That(importer.SourceName).IsEqualTo("The MUD Connector");
    }

    [Test]
    public async Task NobodyMayScrapeItUntilSomebodyHasWrittenToTheMaintainer()
    {
        var etiquette = MudConnectorImporter.Etiquette();

        await Assert.That(etiquette.ContactedMaintainer).IsFalse();

        var decision = EtiquettePlanner.Decide(etiquette);
        await Assert.That(decision.Route).IsEqualTo(FetchRoute.None);
        await Assert.That(decision.RefusedReason).IsEqualTo(EtiquettePlanner.MaintainerNotContacted);

        var (handler, client) = FakeHttp.Serving(
            (etiquette.RobotsUri.AbsoluteUri, string.Empty),
            (etiquette.ScrapeUri!.AbsoluteUri, Fixture.Read("tmc-mudlist.txt")));
        var source = new DirectorySource(client, etiquette, new ManualTimeProvider(Now));
        await source.PrimeRobotsAsync(CancellationToken.None);

        await Assert.That(async () =>
        {
            await foreach (var _ in new MudConnectorImporter(source).ReadAsync(CancellationToken.None))
            {
                // The first MoveNextAsync must throw; nothing here should run.
            }
        }).Throws<EtiquetteViolationException>();

        await Assert.That(handler.Requests.Any(r => r.Uri == etiquette.ScrapeUri!.AbsoluteUri)).IsFalse();
    }

    [Test]
    public async Task ItsCrawlDelayIsAdoptedBecauseScrapingIsWhereWeRateLimitHardest()
    {
        var etiquette = MudConnectorImporter.Etiquette(contactedMaintainer: true);
        var (_, client) = FakeHttp.Serving((etiquette.RobotsUri.AbsoluteUri, "User-agent: *\nCrawl-delay: 20\n"));
        var source = new DirectorySource(client, etiquette, new ManualTimeProvider(Now));

        await source.PrimeRobotsAsync(CancellationToken.None);

        await Assert.That(source.Gate.EffectiveInterval).IsEqualTo(TimeSpan.FromSeconds(20));
    }

    [Test]
    public async Task EachBlockBecomesOneListingWithItsEndpointAndFields()
    {
        var games = await ReadAsync();

        await Assert.That(games.Count).IsEqualTo(2);
        await Assert.That(games[0].Name).IsEqualTo("Anachronism");
        await Assert.That(games[0].SourceName).IsEqualTo("The MUD Connector");
        await Assert.That(games[0].SourceKey).IsEqualTo("anachronism.example:4000");
        await Assert.That(games[0].Endpoints.Single())
            .IsEqualTo(new ImportedEndpoint("anachronism.example", 4000, EndpointKind.Telnet));
        await Assert.That(games[0].Fields["codebase"]).IsEqualTo("Evennia");
        await Assert.That(games[0].Fields["website"]).IsEqualTo("https://anachronism.example/");
    }

    [Test]
    public async Task ABlockWithNoAddressIsSkippedRatherThanInvented()
    {
        var games = await ReadAsync();

        await Assert.That(games.Any(g => g.Name == "Listing With No Address")).IsFalse();
    }

    [Test]
    public async Task AnAssertedSourceYieldsNoHistoryAtAll()
    {
        var games = await ReadAsync();

        foreach (var game in games)
        {
            await Assert.That(game.Presence).IsEmpty();
            await Assert.That(game.Availability).IsEmpty();
        }
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'MudConnectorImporter' could not be found`.

- [ ] **Step 4: Write the importer**

`src/MUI.Backfill/Importers/MudConnectorImporter.cs`:

```csharp
using System.Globalization;
using System.Runtime.CompilerServices;
using MUI.Catalog;

namespace MUI.Backfill.Importers;

/// <summary>
/// The MUD Connector — a hand-moderated list that measures nothing (spec §3), so spec §7.6 puts it in
/// the asserted tier: it seeds discovery and endpoints and earns no history, no presence and no
/// archive grace. Its only route is a scrape, which is exactly the case §7.6 says to email about
/// first, so <see cref="ImportEtiquette.ContactedMaintainer"/> gates the whole importer off by
/// default.
/// </summary>
public sealed class MudConnectorImporter(DirectorySource source) : DirectoryImporter(source)
{
    public override string SourceName => "The MUD Connector";

    public override ImportTier Tier => ImportTier.Asserted;

    public static ImportEtiquette Etiquette(bool contactedMaintainer = false) => new()
    {
        SourceName = "The MUD Connector",
        AttributionUri = new Uri("https://www.mudconnect.com/"),
        ScrapeUri = new Uri("https://www.mudconnect.com/mudlist.txt"),
        RobotsUri = new Uri("https://www.mudconnect.com/robots.txt"),
        UserAgent = CrawlerIdentity.UserAgent,
        MinimumInterval = TimeSpan.FromSeconds(30),
        ContactedMaintainer = contactedMaintainer,
    };

    public static MudConnectorImporter Create(HttpClient http, TimeProvider time) =>
        new(new DirectorySource(http, Etiquette(), time));

    public override async IAsyncEnumerable<ImportedGame> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var body = await Source.GetStringAsync(Etiquette.ScrapeUri!, ct).ConfigureAwait(false);

        foreach (var block in KeyedBlockList.Read(body))
        {
            ct.ThrowIfCancellationRequested();

            if (Read(block) is { } game)
            {
                yield return game;
            }
        }
    }

    private static ImportedGame? Read(IReadOnlyDictionary<string, string> block)
    {
        if (!block.TryGetValue("address", out var host) || host.Length == 0)
        {
            return null;
        }

        if (!block.TryGetValue("port", out var portText)
            || !int.TryParse(portText, CultureInfo.InvariantCulture, out var port))
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (block.TryGetValue("codebase", out var codebase) && codebase.Length > 0)
        {
            fields["codebase"] = codebase;
        }

        if (block.TryGetValue("website", out var website) && website.Length > 0)
        {
            fields["website"] = website;
        }

        var name = block.TryGetValue("name", out var listed) && listed.Length > 0 ? listed : host;

        return new ImportedGame
        {
            SourceName = "The MUD Connector",
            SourceKey = $"{host}:{port}",
            Name = name,
            SourceUri = new Uri("https://www.mudconnect.com/mudlist.txt"),
            Endpoints = [new ImportedEndpoint(host, port, EndpointKind.Telnet)],
            Fields = fields,
        };
    }
}

/// <summary>
/// A plain-text listing shaped as blank-line-separated blocks of <c>Key: value</c> lines, which is
/// how the hand-maintained directories publish. Keys are lower-cased; <c>#</c> starts a comment.
/// </summary>
internal static class KeyedBlockList
{
    public static IEnumerable<IReadOnlyDictionary<string, string>> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var block = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.Length == 0)
            {
                if (block.Count > 0)
                {
                    yield return block;
                    block = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                continue;
            }

            if (line.StartsWith('#'))
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            block[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
        }

        if (block.Count > 0)
        {
            yield return block;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 89 tests.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Backfill/Importers/MudConnectorImporter.cs tests/MUI.Backfill.Tests
git commit -m "feat(backfill): import The MUD Connector's list, behind the contacted-maintainer gate"
```

---

### Task 13: `MushcodeListImporter` and `TinTinMudlistImporter` — the two flat lists

**Files:**
- Create: `src/MUI.Backfill/Importers/MushcodeListImporter.cs`
- Create: `src/MUI.Backfill/Importers/TinTinMudlistImporter.cs`
- Create: `tests/MUI.Backfill.Tests/Fixtures/mushcode-mushlist.txt`
- Create: `tests/MUI.Backfill.Tests/Fixtures/tintin-mudlist.tsv`
- Create: `tests/MUI.Backfill.Tests/Importers/AssertedListImporterTests.cs`

**Interfaces:**
- Consumes: as Task 10; `KeyedBlockList` is *not* used here — both formats are one record per line.
- Produces: `MUI.Backfill.Importers.MushcodeListImporter(DirectorySource)` and
  `MUI.Backfill.Importers.TinTinMudlistImporter(DirectorySource)`, each with static
  `.Etiquette(bool contactedMaintainer = false)` and `.Create(HttpClient, TimeProvider)`.

**Fixture formats.** *MUSHCode.com* keeps a hand-edited MU\* list, stale since roughly 2009 (spec §3):
`;`-prefixed comments and one game per line as `Name = host port`. *The TinTin mudlist* is the seed
source spec §10 names alongside Grapevine: a tab-separated file with a `#`-prefixed header line and
columns `NAME`, `HOST`, `PORT`, `WEBSITE`, `CODEBASE`. Both are hand-maintained, so both are
`imported_asserted` — endpoints and fields only. The TinTin list is fetched as a bulk export, so no
`ContactedMaintainer` gate applies to it; MUSHCode has only a scrape route and is gated.

- [ ] **Step 1: Write both fixtures**

`tests/MUI.Backfill.Tests/Fixtures/mushcode-mushlist.txt`:

```text
; MUSHCode.com MU* list — hand-maintained. Last edited 2009-11-14.
; format:  name = host port

Chronicles of Ash = ash.example.net 7777
The Fifth Age = fifthage.example.org 2860
Anachronism = anachronism.example 4000
Malformed Entry Without A Port = nowhere.example
```

`tests/MUI.Backfill.Tests/Fixtures/tintin-mudlist.tsv`:

```text
#NAME	HOST	PORT	WEBSITE	CODEBASE
Anachronism	anachronism.example	4000	https://anachronism.example/	Evennia
Chronicles of Ash	ash.example.net	7777	https://ash.example.net/	PennMUSH
The Fifth Age	fifthage.example.org	2860		TinyMUX
```

> The three data lines and the header are **tab**-separated. When creating the file, make sure the
> editor writes real U+0009 characters and not runs of spaces, or the parser will read one column.

- [ ] **Step 2: Write the failing test**

`tests/MUI.Backfill.Tests/Importers/AssertedListImporterTests.cs`:

```csharp
using MUI.Backfill.Importers;
using MUI.Backfill.Tests.Support;
using MUI.Catalog;

namespace MUI.Backfill.Tests.Importers;

/// <summary>
/// The two flat hand-maintained lists. Spec §7.6's asserted tier: they seed discovery and endpoints
/// and nothing else. The TinTin mudlist is also one of the two seed sources spec §10 names outright.
/// </summary>
public class AssertedListImporterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static async Task<IReadOnlyList<ImportedGame>> ReadAsync(
        ImportEtiquette etiquette,
        string fixtureName,
        Func<DirectorySource, IDirectoryImporter> build)
    {
        var route = EtiquettePlanner.Decide(etiquette).Uri!;
        var (_, client) = FakeHttp.Serving(
            (etiquette.RobotsUri.AbsoluteUri, "User-agent: *\nDisallow: /admin/\n"),
            (route.AbsoluteUri, Fixture.Read(fixtureName)));

        var source = new DirectorySource(client, etiquette, new ManualTimeProvider(Now));
        await source.PrimeRobotsAsync(CancellationToken.None);

        var games = new List<ImportedGame>();
        await foreach (var game in build(source).ReadAsync(CancellationToken.None))
        {
            games.Add(game);
        }

        return games;
    }

    [Test]
    public async Task TheMushcodeListIsAssertedAndGatedBehindContactingItsMaintainer()
    {
        await Assert.That(MushcodeListImporter.Etiquette().ContactedMaintainer).IsFalse();
        await Assert.That(EtiquettePlanner.Decide(MushcodeListImporter.Etiquette()).Route)
            .IsEqualTo(FetchRoute.None);
        await Assert.That(EtiquettePlanner.Decide(MushcodeListImporter.Etiquette(contactedMaintainer: true)).Route)
            .IsEqualTo(FetchRoute.Scrape);
    }

    [Test]
    public async Task TheMushcodeListYieldsOneGamePerNameEqualsHostPortLine()
    {
        var games = await ReadAsync(MushcodeListImporter.Etiquette(contactedMaintainer: true),
            "mushcode-mushlist.txt", source => new MushcodeListImporter(source));

        await Assert.That(games.Count).IsEqualTo(3);
        await Assert.That(games[0].Name).IsEqualTo("Chronicles of Ash");
        await Assert.That(games[0].SourceName).IsEqualTo("MUSHCode.com");
        await Assert.That(games[0].SourceKey).IsEqualTo("ash.example.net:7777");
        await Assert.That(games[0].Endpoints.Single())
            .IsEqualTo(new ImportedEndpoint("ash.example.net", 7777, EndpointKind.Telnet));
    }

    [Test]
    public async Task AMushcodeLineWithNoPortIsSkippedRatherThanDefaultedTo4201()
    {
        var games = await ReadAsync(MushcodeListImporter.Etiquette(contactedMaintainer: true),
            "mushcode-mushlist.txt", source => new MushcodeListImporter(source));

        await Assert.That(games.Any(g => g.Name.Contains("Malformed", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task TheTinTinMudlistIsAssertedAndReadAsABulkExport()
    {
        var etiquette = TinTinMudlistImporter.Etiquette();

        await Assert.That(EtiquettePlanner.Decide(etiquette).Route).IsEqualTo(FetchRoute.BulkExport);

        var (_, client) = FakeHttp.Serving((etiquette.RobotsUri.AbsoluteUri, string.Empty));
        var importer = new TinTinMudlistImporter(new DirectorySource(client, etiquette, new ManualTimeProvider(Now)));

        await Assert.That(importer.Tier).IsEqualTo(ImportTier.Asserted);
        await Assert.That(importer.SourceName).IsEqualTo("TinTin++ mudlist");
    }

    [Test]
    public async Task TheTinTinMudlistSkipsItsHeaderAndReadsItsColumns()
    {
        var games = await ReadAsync(TinTinMudlistImporter.Etiquette(),
            "tintin-mudlist.tsv", source => new TinTinMudlistImporter(source));

        await Assert.That(games.Count).IsEqualTo(3);
        await Assert.That(games[0].Name).IsEqualTo("Anachronism");
        await Assert.That(games[0].Endpoints.Single())
            .IsEqualTo(new ImportedEndpoint("anachronism.example", 4000, EndpointKind.Telnet));
        await Assert.That(games[0].Fields["website"]).IsEqualTo("https://anachronism.example/");
        await Assert.That(games[0].Fields["codebase"]).IsEqualTo("Evennia");
    }

    [Test]
    public async Task AnEmptyColumnBecomesNoFieldRatherThanAnEmptyOne()
    {
        var games = await ReadAsync(TinTinMudlistImporter.Etiquette(),
            "tintin-mudlist.tsv", source => new TinTinMudlistImporter(source));

        await Assert.That(games[2].Name).IsEqualTo("The Fifth Age");
        await Assert.That(games[2].Fields.ContainsKey("website")).IsFalse();
        await Assert.That(games[2].Fields["codebase"]).IsEqualTo("TinyMUX");
    }

    [Test]
    public async Task NeitherListYieldsAnyHistory()
    {
        var mushcode = await ReadAsync(MushcodeListImporter.Etiquette(contactedMaintainer: true),
            "mushcode-mushlist.txt", source => new MushcodeListImporter(source));
        var tintin = await ReadAsync(TinTinMudlistImporter.Etiquette(),
            "tintin-mudlist.tsv", source => new TinTinMudlistImporter(source));

        foreach (var game in mushcode.Concat(tintin))
        {
            await Assert.That(game.Presence).IsEmpty();
            await Assert.That(game.Availability).IsEmpty();
        }
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'MushcodeListImporter' could not be found`.

- [ ] **Step 4: Write `MushcodeListImporter`**

`src/MUI.Backfill/Importers/MushcodeListImporter.cs`:

```csharp
using System.Globalization;
using System.Runtime.CompilerServices;
using MUI.Catalog;

namespace MUI.Backfill.Importers;

/// <summary>
/// MUSHCode.com's hand-maintained MU* list — stale since roughly 2009 (spec §3), which is exactly
/// what spec §7.6's asserted tier is for: it is a good source of addresses to go and look at, and no
/// source of facts about whether anything is running.
/// </summary>
public sealed class MushcodeListImporter(DirectorySource source) : DirectoryImporter(source)
{
    public override string SourceName => "MUSHCode.com";

    public override ImportTier Tier => ImportTier.Asserted;

    public static ImportEtiquette Etiquette(bool contactedMaintainer = false) => new()
    {
        SourceName = "MUSHCode.com",
        AttributionUri = new Uri("https://mushcode.com/"),
        ScrapeUri = new Uri("https://mushcode.com/mushlist.txt"),
        RobotsUri = new Uri("https://mushcode.com/robots.txt"),
        UserAgent = CrawlerIdentity.UserAgent,
        MinimumInterval = TimeSpan.FromSeconds(30),
        ContactedMaintainer = contactedMaintainer,
    };

    public static MushcodeListImporter Create(HttpClient http, TimeProvider time) =>
        new(new DirectorySource(http, Etiquette(), time));

    public override async IAsyncEnumerable<ImportedGame> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var body = await Source.GetStringAsync(Etiquette.ScrapeUri!, ct).ConfigureAwait(false);

        foreach (var rawLine in body.Split('\n'))
        {
            ct.ThrowIfCancellationRequested();

            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            if (Read(line) is { } game)
            {
                yield return game;
            }
        }
    }

    private static ImportedGame? Read(string line)
    {
        var equals = line.IndexOf('=', StringComparison.Ordinal);
        if (equals <= 0)
        {
            return null;
        }

        var name = line[..equals].Trim();
        var address = line[(equals + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (address.Length < 2
            || !int.TryParse(address[1], CultureInfo.InvariantCulture, out var port))
        {
            return null;
        }

        var host = address[0];

        return new ImportedGame
        {
            SourceName = "MUSHCode.com",
            SourceKey = $"{host}:{port}",
            Name = name.Length > 0 ? name : host,
            SourceUri = new Uri("https://mushcode.com/mushlist.txt"),
            Endpoints = [new ImportedEndpoint(host, port, EndpointKind.Telnet)],
        };
    }
}
```

- [ ] **Step 5: Write `TinTinMudlistImporter`**

`src/MUI.Backfill/Importers/TinTinMudlistImporter.cs`:

```csharp
using System.Globalization;
using System.Runtime.CompilerServices;
using MUI.Catalog;

namespace MUI.Backfill.Importers;

/// <summary>
/// The TinTin++ mudlist — one of the two seed sources spec §10 names outright ("consume Grapevine and
/// the TinTin mudlist as seed sources; republish rather than silo"). It is hand-maintained, so spec
/// §7.6 puts it in the asserted tier: addresses and a couple of fields, and no history.
/// </summary>
/// <remarks>
/// The same site publishes the MSSP specification this project's probe is built around, including
/// <c>REFERRAL</c> and <c>CRAWL DELAY</c> (spec §3.2). The list is read as a published file, so no
/// contacted-maintainer gate applies.
/// </remarks>
public sealed class TinTinMudlistImporter(DirectorySource source) : DirectoryImporter(source)
{
    private const int NameColumn = 0;
    private const int HostColumn = 1;
    private const int PortColumn = 2;
    private const int WebsiteColumn = 3;
    private const int CodebaseColumn = 4;

    public override string SourceName => "TinTin++ mudlist";

    public override ImportTier Tier => ImportTier.Asserted;

    public static ImportEtiquette Etiquette(bool contactedMaintainer = false) => new()
    {
        SourceName = "TinTin++ mudlist",
        AttributionUri = new Uri("https://tintin.mudhalla.net/mudlist/"),
        BulkExportUri = new Uri("https://tintin.mudhalla.net/mudlist/mudlist.tsv"),
        RobotsUri = new Uri("https://tintin.mudhalla.net/robots.txt"),
        UserAgent = CrawlerIdentity.UserAgent,
        MinimumInterval = TimeSpan.FromSeconds(10),
        ContactedMaintainer = contactedMaintainer,
    };

    public static TinTinMudlistImporter Create(HttpClient http, TimeProvider time) =>
        new(new DirectorySource(http, Etiquette(), time));

    public override async IAsyncEnumerable<ImportedGame> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var body = await Source.GetStringAsync(Etiquette.BulkExportUri!, ct).ConfigureAwait(false);

        foreach (var rawLine in body.Split('\n'))
        {
            ct.ThrowIfCancellationRequested();

            var line = rawLine.TrimEnd('\r');
            if (line.Trim().Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (Read(line.Split('\t')) is { } game)
            {
                yield return game;
            }
        }
    }

    private static ImportedGame? Read(string[] columns)
    {
        if (columns.Length <= PortColumn)
        {
            return null;
        }

        var host = columns[HostColumn].Trim();
        if (host.Length == 0
            || !int.TryParse(columns[PortColumn].Trim(), CultureInfo.InvariantCulture, out var port))
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (columns.Length > WebsiteColumn && columns[WebsiteColumn].Trim() is { Length: > 0 } website)
        {
            fields["website"] = website;
        }

        if (columns.Length > CodebaseColumn && columns[CodebaseColumn].Trim() is { Length: > 0 } codebase)
        {
            fields["codebase"] = codebase;
        }

        var name = columns[NameColumn].Trim();

        return new ImportedGame
        {
            SourceName = "TinTin++ mudlist",
            SourceKey = $"{host}:{port}",
            Name = name.Length > 0 ? name : host,
            SourceUri = new Uri("https://tintin.mudhalla.net/mudlist/"),
            Endpoints = [new ImportedEndpoint(host, port, EndpointKind.Telnet)],
            Fields = fields,
        };
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 96 tests.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Backfill/Importers tests/MUI.Backfill.Tests
git commit -m "feat(backfill): import the MUSHCode and TinTin hand-maintained lists"
```

---

### Task 14: The attribution list is generated from what actually ran

**Files:**
- Create: `src/MUI.Backfill/ImporterRegistry.cs`
- Create: `tests/MUI.Backfill.Tests/ImporterRegistryTests.cs`

**Interfaces:**
- Consumes: `IDirectoryImporter`, `ImportTier`, `ImportEtiquette`, `EtiquettePlanner`, `FetchRoute`
  (Tasks 1–7); all six importers (Tasks 10–13).
- Produces: `SourceAttribution(string SourceName, ImportTier Tier, Uri AttributionUri, FetchRoute Route, bool ContactedMaintainer)`;
  `ImporterRegistry(IEnumerable<IDirectoryImporter> importers)` with `.All`,
  `.ByName(string) → IDirectoryImporter`, `.Attributions() → IReadOnlyList<SourceAttribution>`,
  `.RenderAttributionMarkdown() → string`, and
  `ImporterRegistry.Default(HttpClient http, TimeProvider time) → ImporterRegistry`.

**Why generated rather than hand-maintained:** §7.6 requires the about page to name every source we
ingested. A hand-written list drifts the moment an importer is added, removed or re-tiered, and the
drift is invisible — the page still looks complete. Deriving it from the registered importers makes
the page a function of what ran.

- [ ] **Step 1: Write the failing test**

`tests/MUI.Backfill.Tests/ImporterRegistryTests.cs`:

```csharp
using MUI.Backfill.Importers;
using MUI.Backfill.Tests.Support;

namespace MUI.Backfill.Tests;

/// <summary>
/// Spec §7.6: "the about page names every source we ingested". Generated from the registered
/// importers so the page cannot drift from what actually ran.
/// </summary>
public class ImporterRegistryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static ImporterRegistry Everything()
    {
        var (_, client) = FakeHttp.Serving();
        return ImporterRegistry.Default(client, new ManualTimeProvider(Now));
    }

    [Test]
    public async Task AllSixDirectoriesAreRegistered()
    {
        var names = Everything().All.Select(i => i.SourceName).ToArray();

        await Assert.That(names).IsEquivalentTo(new[]
        {
            "Grapevine", "MudVerse", "MudStats",
            "The MUD Connector", "MUSHCode.com", "TinTin++ mudlist",
        });
    }

    [Test]
    public async Task EachDirectorySitsInTheTierSpecSevenPointSixAssignsIt()
    {
        var registry = Everything();

        await Assert.That(registry.ByName("Grapevine").Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(registry.ByName("MudVerse").Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(registry.ByName("MudStats").Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(registry.ByName("The MUD Connector").Tier).IsEqualTo(ImportTier.Asserted);
        await Assert.That(registry.ByName("MUSHCode.com").Tier).IsEqualTo(ImportTier.Asserted);
        await Assert.That(registry.ByName("TinTin++ mudlist").Tier).IsEqualTo(ImportTier.Asserted);
    }

    [Test]
    public async Task TheAttributionListIsExactlyTheRegisteredImporters()
    {
        var registry = Everything();

        var attributed = registry.Attributions().Select(a => a.SourceName).ToArray();
        var registered = registry.All.Select(i => i.SourceName).ToArray();

        await Assert.That(attributed).IsEquivalentTo(registered);
    }

    [Test]
    public async Task EveryAttributionCarriesALinkToTheSourceItCredits()
    {
        foreach (var attribution in Everything().Attributions())
        {
            await Assert.That(attribution.AttributionUri.Scheme).IsEqualTo("https");
            await Assert.That(attribution.AttributionUri.Host).IsNotEmpty();
        }
    }

    [Test]
    public async Task AddingASourceChangesTheRenderedPageWithoutAnybodyEditingIt()
    {
        var before = Everything().RenderAttributionMarkdown();

        var extra = new FakeImporter("Example Directory", ImportTier.Asserted,
            FakeImporter.ApiEtiquette("ExampleDirectory"), []);
        var after = new ImporterRegistry([.. Everything().All, extra]).RenderAttributionMarkdown();

        await Assert.That(before.Contains("Example Directory", StringComparison.Ordinal)).IsFalse();
        await Assert.That(after.Contains("Example Directory", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task TheRenderedTableHasOneRowPerSourcePlusItsHeader()
    {
        var registry = Everything();
        var lines = registry.RenderAttributionMarkdown()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        await Assert.That(lines.Length).IsEqualTo(registry.All.Count + 2);
        await Assert.That(lines[0]).Contains("Source");
    }

    [Test]
    public async Task TheRenderedTableSpellsTheTiersTheWayTheSpecDoes()
    {
        var markdown = Everything().RenderAttributionMarkdown();

        await Assert.That(markdown).Contains("imported_measured");
        await Assert.That(markdown).Contains("imported_asserted");
    }

    [Test]
    public async Task AskingForAnUnregisteredSourceSaysWhatIsRegistered()
    {
        var registry = Everything();

        await Assert.That(() => registry.ByName("Top Mud Sites")).Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task TwoImportersMayNotShareAName()
    {
        var one = new FakeImporter("Twice", ImportTier.Asserted, FakeImporter.ApiEtiquette("Twice"), []);
        var two = new FakeImporter("twice", ImportTier.Measured, FakeImporter.ApiEtiquette("Twice"), []);

        await Assert.That(() => new ImporterRegistry([one, two])).Throws<ArgumentException>();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'ImporterRegistry' could not be found`.

- [ ] **Step 3: Write the registry**

`src/MUI.Backfill/ImporterRegistry.cs`:

```csharp
using System.Text;
using MUI.Backfill.Importers;

namespace MUI.Backfill;

/// <summary>One line of the about page's attribution table.</summary>
public sealed record SourceAttribution(
    string SourceName,
    ImportTier Tier,
    Uri AttributionUri,
    FetchRoute Route,
    bool ContactedMaintainer);

/// <summary>
/// Every directory this build knows how to read. Spec §7.6 requires the about page to name every
/// source we ingested; deriving that list from the registered importers is what stops the page and
/// the code drifting apart, which a hand-maintained list does silently.
/// </summary>
public sealed class ImporterRegistry
{
    private readonly List<IDirectoryImporter> _importers;

    public ImporterRegistry(IEnumerable<IDirectoryImporter> importers)
    {
        ArgumentNullException.ThrowIfNull(importers);

        _importers = [.. importers.OrderBy(i => i.SourceName, StringComparer.Ordinal)];

        var duplicate = _importers
            .GroupBy(i => i.SourceName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Two importers both call themselves '{duplicate.Key}'. Source names are the attribution key.",
                nameof(importers));
        }
    }

    public IReadOnlyList<IDirectoryImporter> All => _importers;

    /// <summary>The six directories spec §7.6 names, wired to one shared HTTP client.</summary>
    public static ImporterRegistry Default(HttpClient http, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(time);

        return new ImporterRegistry(
        [
            GrapevineImporter.Create(http, time),
            MudVerseImporter.Create(http, time),
            MudStatsImporter.Create(http, time),
            MudConnectorImporter.Create(http, time),
            MushcodeListImporter.Create(http, time),
            TinTinMudlistImporter.Create(http, time),
        ]);
    }

    public IDirectoryImporter ByName(string sourceName)
    {
        ArgumentNullException.ThrowIfNull(sourceName);

        return _importers.FirstOrDefault(i => string.Equals(i.SourceName, sourceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException(
                $"No importer named '{sourceName}'. Registered: {string.Join(", ", _importers.Select(i => i.SourceName))}.");
    }

    public IReadOnlyList<SourceAttribution> Attributions() =>
    [
        .. _importers.Select(i => new SourceAttribution(
            i.SourceName,
            i.Tier,
            i.Etiquette.AttributionUri,
            EtiquettePlanner.Decide(i.Etiquette).Route,
            i.Etiquette.ContactedMaintainer)),
    ];

    /// <summary>The about page's attribution table, as Markdown.</summary>
    public string RenderAttributionMarkdown()
    {
        var builder = new StringBuilder();
        builder.Append("| Source | Tier | Read via | Attribution |\n");
        builder.Append("|---|---|---|---|\n");

        foreach (var attribution in Attributions())
        {
            builder.Append(
                $"| {attribution.SourceName} | {Label(attribution.Tier)} | {attribution.Route} | <{attribution.AttributionUri}> |\n");
        }

        return builder.ToString();
    }

    private static string Label(ImportTier tier) =>
        tier is ImportTier.Measured ? "imported_measured" : "imported_asserted";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 105 tests.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Backfill/ImporterRegistry.cs tests/MUI.Backfill.Tests/ImporterRegistryTests.cs
git commit -m "feat(backfill): generate the about page's attribution list from the registered importers"
```

---

### Task 15: `mui-import` — a dry run first, then a real one

**Files:**
- Create: `src/MUI.Backfill/ImportRunner.cs`
- Create: `src/MUI.Backfill.Cli/MUI.Backfill.Cli.csproj`
- Create: `src/MUI.Backfill.Cli/Program.cs`
- Create: `tests/MUI.Backfill.Tests/ImportRunnerTests.cs`
- Modify: `MUIndex.slnx`
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`

**Interfaces:**
- Consumes: `ImporterRegistry`, `ImportPipeline`, `ImportReport`, all six importers;
  `MUI.Storage.MigrationRunner` (Plan 2).
- Produces: `ImportRunOptions` (`DryRun` defaulting to `true`, `Sources` defaulting to empty = all);
  `ImportRunner(ImporterRegistry registry, ImportPipeline pipeline, TextWriter output)` with
  `.RunAsync(ImportRunOptions, CancellationToken) → Task<IReadOnlyList<ImportReport>>`;
  console assembly `mui-import`.
- **Assumption about Plan 2 and Plan 3, isolated to one place:** `Program.cs` constructs the six
  Dapper repositories as `NpgsqlGameRepository`, `NpgsqlGameFieldRepository`, `NpgsqlPresenceRepository`,
  `NpgsqlAvailabilityRepository`, `NpgsqlEndpointRepository` (namespace `MUI.Storage`) and
  `NpgsqlCrawlTargetRepository` (namespace `MUI.Discovery`). If those plans named their implementations
  differently, change these six `new` expressions and nothing else in this plan is affected.

- [ ] **Step 1: Write the failing test**

`tests/MUI.Backfill.Tests/ImportRunnerTests.cs`:

```csharp
using MUI.Backfill.Importers;
using MUI.Backfill.Tests.Support;
using MUI.Catalog;

namespace MUI.Backfill.Tests;

/// <summary>
/// The runnable backfill (spec §14). A dry run prints what it would do and touches nothing; a real
/// run does it; and running the whole thing twice is a no-op the second time.
/// </summary>
public class ImportRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        InMemoryCrawlTargetRepository Targets,
        InMemoryGameRepository Games,
        InMemoryEndpointRepository Endpoints,
        InMemoryGameFieldRepository Fields,
        InMemoryPresenceRepository Presence,
        InMemoryAvailabilityRepository Availability,
        InMemoryImportProvenanceRepository Provenance,
        ImportPipeline Pipeline);

    private static Harness Build()
    {
        var targets = new InMemoryCrawlTargetRepository();
        var games = new InMemoryGameRepository();
        var endpoints = new InMemoryEndpointRepository();
        var fields = new InMemoryGameFieldRepository();
        var presence = new InMemoryPresenceRepository();
        var availability = new InMemoryAvailabilityRepository();
        var provenance = new InMemoryImportProvenanceRepository();

        return new Harness(targets, games, endpoints, fields, presence, availability, provenance,
            new ImportPipeline(targets, games, endpoints, fields, presence, availability, provenance,
                new ManualTimeProvider(Now)));
    }

    /// <summary>MudVerse and MudStats, both served from their committed fixtures, over one client.</summary>
    private static ImporterRegistry TwoMeasuredSources()
    {
        var mudverse = MudVerseImporter.Etiquette();
        var mudstats = MudStatsImporter.Etiquette();

        var (_, client) = FakeHttp.Serving(
            (mudverse.RobotsUri.AbsoluteUri, "User-agent: *\nDisallow: /admin/\n"),
            (mudverse.BulkExportUri!.AbsoluteUri, Fixture.Read("mudverse-export.json")),
            (mudstats.RobotsUri.AbsoluteUri, "User-agent: *\nDisallow: /admin/\n"),
            (mudstats.ApiUri!.AbsoluteUri, Fixture.Read("mudstats-muds.json")));

        var time = new ManualTimeProvider(Now);

        var mudverseSource = new DirectorySource(client, mudverse, time);
        var mudstatsSource = new DirectorySource(client, mudstats, time);
        mudverseSource.PrimeRobotsAsync(CancellationToken.None).GetAwaiter().GetResult();
        mudstatsSource.PrimeRobotsAsync(CancellationToken.None).GetAwaiter().GetResult();

        return new ImporterRegistry([new MudVerseImporter(mudverseSource), new MudStatsImporter(mudstatsSource)]);
    }

    private static async Task<Guid> SeedProbedGameAsync(Harness harness)
    {
        var gameId = Guid.NewGuid();
        await harness.Games.InsertAsync(
            new Game(gameId, "anachronism", "Anachronism", LifecycleState.Active, false, Now, Now, null),
            CancellationToken.None);
        await harness.Endpoints.UpsertAsync(
            new GameEndpoint(gameId, "anachronism.example", 4000, EndpointKind.Telnet, Now, Now, EndpointState.Active),
            CancellationToken.None);
        return gameId;
    }

    [Test]
    public async Task ARunIsADryRunUnlessSomebodySaysOtherwise()
    {
        await Assert.That(new ImportRunOptions().DryRun).IsTrue();
        await Assert.That(new ImportRunOptions().Sources).IsEmpty();
    }

    [Test]
    public async Task ADryRunPrintsEveryReportAndWritesNothing()
    {
        var harness = Build();
        await SeedProbedGameAsync(harness);
        var output = new StringWriter();

        var reports = await new ImportRunner(TwoMeasuredSources(), harness.Pipeline, output)
            .RunAsync(new ImportRunOptions(), CancellationToken.None);

        await Assert.That(reports.Count).IsEqualTo(2);
        await Assert.That(reports.Sum(r => r.GamesSeen)).IsGreaterThan(0);

        await Assert.That(harness.Targets.Targets).IsEmpty();
        await Assert.That(harness.Fields.Fields).IsEmpty();
        await Assert.That(harness.Presence.Samples).IsEmpty();
        await Assert.That(harness.Availability.Intervals).IsEmpty();
        await Assert.That(harness.Provenance.Rows).IsEmpty();

        var printed = output.ToString();
        await Assert.That(printed).Contains("dry run");
        await Assert.That(printed).Contains("MudVerse");
        await Assert.That(printed).Contains("MudStats");
    }

    [Test]
    public async Task ARealRunWritesAndSaysSo()
    {
        var harness = Build();
        await SeedProbedGameAsync(harness);
        var output = new StringWriter();

        await new ImportRunner(TwoMeasuredSources(), harness.Pipeline, output)
            .RunAsync(new ImportRunOptions { DryRun = false }, CancellationToken.None);

        await Assert.That(harness.Targets.Targets).IsNotEmpty();
        await Assert.That(harness.Presence.Samples).IsNotEmpty();
        await Assert.That(output.ToString().Contains("dry run", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ImportingMudVerseThenMudStatsYieldsOneGameAndOneCrawlTarget()
    {
        var harness = Build();
        var gameId = await SeedProbedGameAsync(harness);

        await new ImportRunner(TwoMeasuredSources(), harness.Pipeline, new StringWriter())
            .RunAsync(new ImportRunOptions { DryRun = false }, CancellationToken.None);

        // Both directories list Anachronism at anachronism.example:4000, and we had already probed it.
        await Assert.That(harness.Games.Games.Count).IsEqualTo(1);
        await Assert.That(harness.Games.Games[0].Id).IsEqualTo(gameId);
        await Assert.That(harness.Targets.Targets.Count(t => t.Host == "anachronism.example" && t.Port == 4000))
            .IsEqualTo(1);
        await Assert.That(harness.Endpoints.Endpoints.Count(e => e.Host == "anachronism.example" && e.Port == 4000))
            .IsEqualTo(1);
    }

    [Test]
    public async Task TheWholeBackfillIsRerunnable()
    {
        var harness = Build();
        await SeedProbedGameAsync(harness);

        var first = await new ImportRunner(TwoMeasuredSources(), harness.Pipeline, new StringWriter())
            .RunAsync(new ImportRunOptions { DryRun = false }, CancellationToken.None);
        var targetsAfterFirst = harness.Targets.Targets.Count;
        var samplesAfterFirst = harness.Presence.Samples.Count;

        var second = await new ImportRunner(TwoMeasuredSources(), harness.Pipeline, new StringWriter())
            .RunAsync(new ImportRunOptions { DryRun = false }, CancellationToken.None);

        await Assert.That(first.Sum(r => r.TargetsAdded)).IsGreaterThan(0);
        await Assert.That(second.Sum(r => r.TargetsAdded)).IsEqualTo(0);
        await Assert.That(second.Sum(r => r.PresenceRows)).IsEqualTo(0);
        await Assert.That(harness.Targets.Targets.Count).IsEqualTo(targetsAfterFirst);
        await Assert.That(harness.Presence.Samples.Count).IsEqualTo(samplesAfterFirst);
    }

    [Test]
    public async Task NamingASourceRunsOnlyThatOne()
    {
        var harness = Build();
        await SeedProbedGameAsync(harness);

        var reports = await new ImportRunner(TwoMeasuredSources(), harness.Pipeline, new StringWriter())
            .RunAsync(new ImportRunOptions { DryRun = false, Sources = ["MudStats"] }, CancellationToken.None);

        await Assert.That(reports.Count).IsEqualTo(1);
        await Assert.That(reports[0].Source).IsEqualTo("MudStats");
    }

    [Test]
    public async Task ASourceNobodyHasEmailedYetIsSkippedRatherThanCrashingTheRun()
    {
        var harness = Build();
        await SeedProbedGameAsync(harness);
        var output = new StringWriter();

        // MUSHCode.com is scrape-only and ContactedMaintainer defaults to false, so it must be
        // skipped with a note while every other source still imports. Without this, `mui-import`
        // with no --source would die on the fourth of six registered directories.
        var gated = new FakeImporter("MUSHCode.com", ImportTier.Asserted,
            MushcodeListImporter.Etiquette(), []);
        var registry = new ImporterRegistry([.. TwoMeasuredSources().All, gated]);

        var reports = await new ImportRunner(registry, harness.Pipeline, output)
            .RunAsync(new ImportRunOptions { DryRun = false }, CancellationToken.None);

        await Assert.That(reports.Count).IsEqualTo(3);

        var skipped = reports.Single(r => r.Source == "MUSHCode.com");
        await Assert.That(skipped.GamesSeen).IsEqualTo(0);
        await Assert.That(skipped.Notes.Single()).Contains("skipped");
        await Assert.That(skipped.Notes.Single()).Contains(EtiquettePlanner.MaintainerNotContacted);

        await Assert.That(harness.Presence.Samples).IsNotEmpty();
        await Assert.That(output.ToString()).Contains("MUSHCode.com");
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'ImportRunner' could not be found`.

- [ ] **Step 3: Write `ImportRunner`**

`src/MUI.Backfill/ImportRunner.cs`:

```csharp
using System.Globalization;

namespace MUI.Backfill;

/// <summary>What one invocation of the backfill was asked to do.</summary>
public sealed record ImportRunOptions
{
    /// <summary>
    /// Defaults to <c>true</c>. Spec §14 calls the backfill a one-off, and a one-off you cannot
    /// rehearse is one you run blind.
    /// </summary>
    public bool DryRun { get; init; } = true;

    /// <summary>Empty means every registered source.</summary>
    public IReadOnlyList<string> Sources { get; init; } = [];
}

/// <summary>
/// Runs the registered importers and prints their reports. The behaviour that matters — what gets
/// written — lives in <see cref="ImportPipeline"/>; this type chooses which importers run, in which
/// mode, and how the result reads on a terminal.
/// </summary>
public sealed class ImportRunner(ImporterRegistry registry, ImportPipeline pipeline, TextWriter output)
{
    private readonly ImporterRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly ImportPipeline _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    private readonly TextWriter _output = output ?? throw new ArgumentNullException(nameof(output));

    public async Task<IReadOnlyList<ImportReport>> RunAsync(ImportRunOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<IDirectoryImporter> chosen = options.Sources.Count == 0
            ? _registry.All
            : [.. options.Sources.Select(_registry.ByName)];

        var reports = new List<ImportReport>(chosen.Count);

        foreach (var importer in chosen)
        {
            ImportReport report;

            try
            {
                report = options.DryRun
                    ? await _pipeline.DryRunAsync(importer, ct).ConfigureAwait(false)
                    : await _pipeline.RunAsync(importer, ct).ConfigureAwait(false);
            }
            catch (EtiquetteViolationException refusal)
            {
                // A source we are not yet entitled to read is a line in the report, not a crashed
                // run. Two of the six are waiting on an email by design, and the other four should
                // still import while somebody writes it.
                report = new ImportReport(importer.SourceName, importer.Tier, 0, 0, 0, 0, 0, 0,
                    [$"skipped — {refusal.Message}"]);
            }

            reports.Add(report);
            Print(report, options.DryRun);
        }

        return reports;
    }

    private void Print(ImportReport report, bool dryRun)
    {
        var mode = dryRun ? " (dry run — nothing was written)" : string.Empty;
        var tier = report.Tier is ImportTier.Measured ? "imported_measured" : "imported_asserted";

        _output.WriteLine(CultureInfo.InvariantCulture, $"{report.Source} [{tier}]{mode}");
        _output.WriteLine(CultureInfo.InvariantCulture,
            $"  games seen        {report.GamesSeen}");
        _output.WriteLine(CultureInfo.InvariantCulture,
            $"  crawl targets     {report.TargetsAdded}");
        _output.WriteLine(CultureInfo.InvariantCulture,
            $"  fields written    {report.FieldsWritten}");
        _output.WriteLine(CultureInfo.InvariantCulture,
            $"  presence rows     {report.PresenceRows}");
        _output.WriteLine(CultureInfo.InvariantCulture,
            $"  availability rows {report.AvailabilityRows}");
        _output.WriteLine(CultureInfo.InvariantCulture,
            $"  refused           {report.Rejected}");

        foreach (var note in report.Notes)
        {
            _output.WriteLine(CultureInfo.InvariantCulture, $"  · {note}");
        }

        _output.WriteLine();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
```
Expected: PASS, 112 tests.

- [ ] **Step 5: Write the CLI project**

`src/MUI.Backfill.Cli/MUI.Backfill.Cli.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>MUI.Backfill.Cli</RootNamespace>
    <AssemblyName>mui-import</AssemblyName>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\MUI.Backfill\MUI.Backfill.csproj" />
  </ItemGroup>

</Project>
```

`src/MUI.Backfill.Cli/Program.cs`:

```csharp
using MUI.Backfill;
using MUI.Discovery;
using MUI.Storage;
using Npgsql;

const string Usage = """
    mui-import — a one-off backfill from the existing MU* directories (spec §7.6).

      mui-import [--commit] [--source <name>]... [--connection <postgres>]

      --commit            write. Without it the run is a dry run and touches nothing.
      --source <name>     run only this source; repeatable. Default: every registered source.
      --connection <s>    Postgres connection string. Default: the MUI_CONNECTION env var.
      --sources           list the registered sources and their attribution, then exit.
      --help, -h          this text.
    """;

var dryRun = true;
var sources = new List<string>();
var connectionString = Environment.GetEnvironmentVariable("MUI_CONNECTION");

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--commit":
            dryRun = false;
            break;

        case "--dry-run":
            dryRun = true;
            break;

        case "--source" when i + 1 < args.Length:
            sources.Add(args[++i]);
            break;

        case "--connection" when i + 1 < args.Length:
            connectionString = args[++i];
            break;

        case "--sources":
            using (var client = new HttpClient())
            {
                Console.Out.Write(ImporterRegistry.Default(client, TimeProvider.System).RenderAttributionMarkdown());
            }

            return 0;

        case "--help":
        case "-h":
            Console.Out.WriteLine(Usage);
            return 0;

        default:
            Console.Error.WriteLine($"mui-import: unrecognised argument '{args[i]}'.");
            Console.Error.WriteLine(Usage);
            return 2;
    }
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("mui-import: no connection string. Pass --connection or set MUI_CONNECTION.");
    return 2;
}

using var http = new HttpClient();
await using var dataSource = NpgsqlDataSource.Create(connectionString);

await new MigrationRunner(dataSource).ApplyAsync(CancellationToken.None);

// The one place this plan assumes Plan 2's and Plan 3's Dapper class names.
var pipeline = new ImportPipeline(
    new NpgsqlCrawlTargetRepository(dataSource),
    new NpgsqlGameRepository(dataSource),
    new NpgsqlEndpointRepository(dataSource),
    new NpgsqlGameFieldRepository(dataSource),
    new NpgsqlPresenceRepository(dataSource),
    new NpgsqlAvailabilityRepository(dataSource),
    new NpgsqlImportProvenanceRepository(dataSource),
    TimeProvider.System);

var registry = ImporterRegistry.Default(http, TimeProvider.System);

foreach (var importer in registry.All)
{
    if (importer is DirectoryImporter directory)
    {
        await directory.PrimeAsync(CancellationToken.None);
    }
}

var runner = new ImportRunner(registry, pipeline, Console.Out);
await runner.RunAsync(new ImportRunOptions { DryRun = dryRun, Sources = sources }, CancellationToken.None);

return 0;
```

- [ ] **Step 6: Add `PrimeAsync` to `DirectoryImporter`**

The CLI needs one call that reads `robots.txt` before any importer fetches content. Add to
`src/MUI.Backfill/IDirectoryImporter.cs`, inside `DirectoryImporter`:

```csharp
    /// <summary>
    /// Reads this source's <c>robots.txt</c> so the gate opens. Every test in the suite calls
    /// <c>DirectorySource.PrimeRobotsAsync</c> directly; this is the same call, reachable through the
    /// importer so the CLI can prime them all without knowing their internals.
    /// </summary>
    public Task PrimeAsync(CancellationToken ct) => Source.PrimeRobotsAsync(ct);
```

- [ ] **Step 7: Wire the CLI into the solution, CI and the README**

In `MUIndex.slnx`, add to the `/src/` folder:

```xml
    <Project Path="src/MUI.Backfill.Cli/MUI.Backfill.Cli.csproj" />
```

In `.github/workflows/ci.yml`, add after the `Test — Backfill` step:

```yaml
      # The backfill has to be runnable, so prove the CLI at least starts.
      - name: Smoke — mui-import --help
        shell: bash
        run: dotnet run -c Release --no-build --project src/MUI.Backfill.Cli/MUI.Backfill.Cli.csproj -- --help
```

In `README.md`, add after the existing `## Building` section's test list:

```markdown
## Backfill

A one-off, re-runnable import from the existing directories (spec §7.6). It is a dry run unless you
say `--commit`, and it never probes anything — it reads other people's directories.

```bash
dotnet run --project src/MUI.Backfill.Cli -- --sources          # who we ingest, and how
dotnet run --project src/MUI.Backfill.Cli -- --connection "$MUI_CONNECTION"
dotnet run --project src/MUI.Backfill.Cli -- --commit --connection "$MUI_CONNECTION"
```

Sources split two ways. **`imported_measured`** — MudVerse, MudStats, Grapevine — are sites that
actively ping, so they may seed discovery, populate historical availability and presence, and count
toward archive grace at half weight. **`imported_asserted`** — The MUD Connector, the MUSHCode list,
the TinTin mudlist — are hand-maintained, so they seed discovery and endpoints and nothing else: no
history, no presence, no grace. Imported values never outrank anything we measured ourselves, and
each one carries the site it came from and the date we took it.
```

- [ ] **Step 8: Run the whole suite and the smoke test**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests </dev/null
dotnet run -c Release --no-build --project src/MUI.Backfill.Cli -- --help
dotnet run -c Release --no-build --project src/MUI.Backfill.Cli -- --sources
```
Expected: 112 tests pass; `--help` prints the usage; `--sources` prints a six-row Markdown table.

- [ ] **Step 9: Run every suite in the solution**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests   </dev/null
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests     </dev/null
dotnet run -c Release --no-build --project tests/MUI.Storage.Tests   </dev/null
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Backfill.Tests  </dev/null
dotnet run -c Release --no-build --project tests/MUI.Web.Tests       </dev/null
```
Expected: all green, and the build warning-free.

- [ ] **Step 10: Commit**

```bash
git add src/MUI.Backfill src/MUI.Backfill.Cli tests/MUI.Backfill.Tests MUIndex.slnx .github/workflows/ci.yml README.md
git commit -m "feat(backfill): add the mui-import CLI, dry run by default"
```

---

## Spec gaps, ambiguities and contradictions found while writing this plan

Writing a plan against §7.6 surfaced five places where the spec does not say enough. Each is recorded
here with the decision this plan makes, so a reviewer can overrule the decision rather than discover
it in the code.

**1. §7.6 assigns tiers to named sites but never says how a site whose export format is undocumented
is to be read.** Three of the six named sources — The MUD Connector, the MUSHCode lists, the TinTin
mudlist — publish no documented API and no schema for what they do publish. The spec's only guidance
is the etiquette paragraph, which says to prefer a bulk export or a documented API and to email
first. *Decision:* the format is pinned by a **committed fixture** (`tests/MUI.Backfill.Tests/Fixtures/`)
which is the parser's contract, and the only route that can read an undocumented format — scraping —
is gated behind `ImportEtiquette.ContactedMaintainer`, which is `false` by default and which
`ImportRunner` reports as a skipped source rather than a failure. So an undocumented format is read
only after a human asked its maintainer, and the shape we assumed is a file in the repository rather
than an assumption buried in a parser. Re-recording a fixture when an upstream format moves is a
normal follow-up, not a design change. **This does mean the six fixtures in this plan are our best
reconstruction of each format, not captures of live traffic** — the implementer should replace any of
them with a genuinely recorded payload when one is obtained, and adjust only the parser mapping.

**2. §7.5 says imported history counts at half weight without saying whether imported *presence* is
discounted or only imported *availability*.** *Finding:* the grace formula's input is *cumulative
reachable time*, which comes from `AvailabilityInterval` alone — presence never enters it — so for
**grace** the answer is unambiguous once you follow the arithmetic: availability is halved and
presence is irrelevant. What the spec genuinely does not answer is whether an imported presence
*count* should be discounted anywhere else, notably in §9's "rankings computed from measured data
only", which does not say whether a third party's measurement counts as measured data.
*Decision:* this plan applies **no discount to presence values** — halving a player count would
falsify a number rather than weight a confidence — and instead labels every imported presence row
`PresenceSource.ImportedMeasured` so a later decision about rankings can include, exclude or weight
them without re-importing anything. The half weight is expressed in exactly one place,
`ArchivePolicy.GraceFor`'s `importedMeasuredReachable` parameter, fed by
`IAvailabilityRepository.CumulativeImportedMeasuredReachableAsync` and by nothing else in this plan.
**This is an open question for Plan 5, not a settled one.**

**3. No catalogue record can hold "which site said this, and when" — and §7.6 requires it.**
`GameField` carries a `FieldSource` (a tier, not a site), `PresenceSample` carries a `PresenceSource`
(likewise), and `AvailabilityInterval` carries an `origin` that is also a tier. §7.6's "every imported
value carries the originating site and the import date in its provenance chip" therefore had nowhere
to live. *Decision:* one additive sidecar table, `import_provenance`, carrying the site, that site's
own key, the source URI and the import instant, for every imported field, endpoint, presence point
and availability span. No contract type changes.

**The grace half of this gap is Plan 02's and is closed there, not here.** §7.5 also needs imported
reachable time separated from ours, and an earlier draft of this plan answered it with an
`ImportedGraceCalculator` that joined the sidecar back to the intervals. That type is **dropped**:
Plan 02 carries an `origin` column on `availability_interval` and exposes
`CumulativeImportedMeasuredReachableAsync` beside `CumulativeReachableAsync`, and its `ArchiveSweeper`
already hands the two to `ArchivePolicy.GraceFor` separately. Two calculators reading the same history
would have counted it twice, and the sidecar would have become load-bearing for a number it was never
meant to produce. So `import_provenance` serves the provenance chip and the attribution list, is read
by nothing on the grace path, and this plan's only obligation to §7.5 is to write availability through
`InsertImportedAsync` so the column says `imported_measured` — pinned in Task 8, in memory and against
a real Postgres.

**4. §3's table of incumbents lists seven sites; §7.6's tier table covers five of them.** Top Mud
Sites and MUNexus appear in §3 and in neither tier. *Decision:* neither gets an importer. Top Mud
Sites is vote-driven with no liveness measurement (§3) and §2 forbids vote-derived data outright, so
importing its rankings would import the exact failure this project exists to avoid — its *addresses*
would be admissible as `imported_asserted`, but it is described as a link graveyard and the six
sources here already cover the same games. MUNexus verifies manually when MSSP is absent (§3), which
is a human measurement the two tiers have no slot for; if it is added later it is
`imported_asserted`, because we cannot audit a human either. The spec's §3 also names
`mush.wikidot` as a hand-maintained list; it is covered by the asserted tier and is not given its own
importer in v1, since the user's six-importer scope names MUSHCode rather than wikidot.

**5. Grapevine is assigned to the measured tier by §7.6 but described in §3 as a one-shot checker
rather than a continuous crawl.** Not a contradiction, but a consequence worth stating: a source
without a series can produce presence points and cannot produce reachability spans, so Grapevine
contributes **nothing** toward archive grace despite being in the tier that is entitled to. The same
is true of MudStats as its export is defined here (counts, no spans). *Decision:* `GrapevineImporter`
yields at most one `ImportedPresence` per game and no `ImportedAvailability`; `MudVerseImporter` is
therefore the only importer in this plan that can move the archive threshold. That is visible in
`MeasuredImporterTests.MudStatsPublishesNoReachabilitySpansSoItEarnsNoGrace` and in Grapevine's
`AOneShotCheckYieldsOnePresencePointAndNoAvailability`, so nobody has to rediscover it.

One smaller thing, decided rather than flagged: **an imported availability span with no end.** The
spec does not say what to do with a third-party record that was still open when the export was taken.
Leaving it open would collide with the open interval our own crawler keeps for the same game.
`MeasuredHistorySink` closes it at the import instant — we did not measure it and cannot extend it —
pinned by `HistoryTierTests.AnImportedAvailabilitySpanIsNeverLeftOpen`.

---

## Self-review

**Spec coverage.** Every requirement traced to a task:

| Spec | Where |
|---|---|
| §7.6 the two tiers and their `FieldSource`s | Task 1 |
| §5.1 imported sits at the bottom of the precedence ladder | Task 1 (`ImportTierTests`), Task 7 (`AnImportNeverOverwritesAValueWeMeasuredOurselves`) |
| §7.6 prefer a bulk export or documented API over scraping | Tasks 2, 4, 7 |
| §7.6 honour `robots.txt`, rate-limit hard | Tasks 3, 4 |
| §7.6 email the maintainer first | Tasks 2, 12, 15 |
| §11 crawler self-identifies with an info URL | Tasks 2, 4 |
| §7.6 measured tier populates availability and presence | Tasks 7, 8 |
| §7.6 asserted tier seeds discovery and endpoints only | Task 8 (the pin) |
| §7.5 imported measured history at half weight | Task 7 (writes through `InsertImportedAsync`), Task 8 (`FourYearsOfImportedReachableTimeIsCreditedAsTwo`, `AnImportedSpanIsStampedImportedMeasuredAndNeverFirstParty`) — the *arithmetic* is Plan 02's `ArchiveSweeper` and this plan adds none |
| §7.6 every imported value carries site and import date | Task 5, Task 7 |
| §7.6 the about page names every source ingested | Task 14 |
| §7.1 discovery is never scheduling | Task 7 (`AnImportNeverSchedulesAnything`) |
| §7.2 a host is listed by answering for itself | Tasks 6, 7 |
| §7.3 no merge on a weak signal | Task 6 |
| §3 / §10 the incumbent sites, Grapevine and the TinTin mudlist as seeds | Tasks 10–13 |
| §14 a one-off, re-runnable backfill | Tasks 9, 15 |
| §13 fixtures rather than a network | Tasks 4, 10–13 |

**Placeholder scan.** No "TBD", no "similar to Task N", no "add error handling", no "write tests for
the above". Every code step carries the code. The two forward references are explicit and bounded:
Task 7 Step 6 writes the minimal `HistorySink` — `MeasuredHistorySink` finished, `AssertedHistorySink`
writing nothing but not yet *counting* what it turned away — which Task 8 then tests and completes in
one expression; and Task 15's Interfaces block names the six Plan 2/Plan 3 class names it assumes and
confines them to one file.

**Type consistency.** Checked across tasks: `ImportTierMap.SourceFor` / `.MayWriteHistory`;
`EtiquettePlanner.Decide` / `.MayFetch` and the three refusal constants;
`RobotsPolicy.Parse` / `.Allows` / `.CrawlDelayFor` / `.AllowAll`;
`PolitenessGate.AdoptRobots` / `.MayFetch` / `.WaitFor` / `.EnterAsync` / `.EffectiveInterval` /
`.RobotsAdopted` / `.LastFetchAt`;
`DirectorySource.PrimeRobotsAsync` / `.GetStringAsync` / `.Etiquette` / `.Gate` / `.Decision`, plus
`DirectoryImporter.PrimeAsync` added in Task 15 Step 6;
`IImportProvenanceRepository.RecordAsync` / `.ExistsAsync` / `.ForGameAsync`;
`IImportWriter`'s eight methods, identical in `CommittingImportWriter` and `DryRunImportWriter`;
`IHistorySink.WriteAsync` returning `HistoryWrite(PresenceRows, AvailabilityRows, Refused)`;
`ImportPipeline.RunAsync` / `.DryRunAsync`; `ImporterRegistry.All` / `.ByName` / `.Attributions` /
`.RenderAttributionMarkdown` / `.Default`; `ImportRunner.RunAsync`. Each importer exposes the same
pair of statics, `Etiquette(bool contactedMaintainer = false)` and `Create(HttpClient, TimeProvider)`,
which is what lets `ImporterRegistry.Default` build all six uniformly.

**Cross-plan reconciliation, applied to the body and not only announced.** The reconciliation section
at the head of this plan was written before its four consequences reached the tasks, and one of them —
the strike on `ImportedGraceCalculator` — had not. The plan told an executor to delete a type and then
spent three hundred lines building and testing it, which an executor reading top-down survives and one
reading Task 8 alone does not. It is now applied everywhere: the type is gone from the declared-types
list, the file table, Task 8's **Files** and **Interfaces**, Task 8's code, the spec-gap notes and the
coverage table above, and Task 8 proves the same two things without it —
`CumulativeImportedMeasuredReachableAsync` plus `ArchivePolicy.GraceFor`, which is exactly how
`ArchiveSweeper` computes the number this plan is asserting about. The other three consequences were
checked the same way: `MeasuredHistorySink` reaches `InsertImportedAsync` (not `OpenAsync`) through
`CommittingImportWriter.WriteClosedAvailabilityAsync`, with the signature matched to Plan 02 Task 9's
declaration including its **non-nullable** `to`; `InMemoryAvailabilityRepository` gained
`InsertImportedAsync`, `CumulativeImportedMeasuredReachableAsync` and the `Origins` map without which
it does not implement the interface at all; and `import_provenance` survives with its justification
narrowed to §7.6's site-and-date, stated identically in the Architecture paragraph, the reconciliation
bullet and gap 3.

**One host, one spelling — the correction that reaches this plan hardest.** Plan 02 Task 10 now
canonicalises hosts through `MUI.Catalog.HostName.Normalize` on both ends of `IEndpointRepository`,
because the real repository compared `host = @host` ordinally while the fakes compared
`OrdinalIgnoreCase`, and a fake kinder than the database hides a duplicate row rather than a failing
test. **An import is where that bug would have been found first**: a directory prints
`MUD.Example.ORG`, `ImportIdentity` asks `ByAddressAsync` for it, no endpoint matches, the record falls
below `IdentityWeights.AutoMergeThreshold`, and this plan seeds a crawl target for a machine already in
the catalogue — §7.3's duplicate listing, arrived at through §7.6. Three changes here: the fake in Task
6 normalises and then compares ordinally; `ImportIdentity`'s signal string names the *stored* host
rather than the imported spelling, so a note cannot cite an address no table holds; and
`ImportPipeline.SeedTargetsAsync` canonicalises once per endpoint, so the `CrawlTarget` and the
`GameEndpoint` one import writes carry the same string. Task 6's
`HoweverADirectorySpellsAHostItIsTheSameHost` covers case, shouting and the DNS root dot in one test —
it used to be called `TheHostMatchesCaseInsensitively…`, which named a mechanism that is no longer the
one in use.

*Flagged, not fixed here:* `crawl_target.host` has no canonical form. `ICrawlTargetRepository` and its
schema are Plan 03's, and its fake in Task 6 still matches a host case-insensitively. This plan now
hands it an already-normalised string on every call, so nothing here writes a duplicate target — but
the *guarantee* belongs in Plan 03, as `HostName.Normalize` in `NpgsqlCrawlTargetRepository`'s two
address methods and an ordinal comparison in the fake, exactly as Plan 02 Task 10 did for endpoints.
Raise it there; do not tighten only the fake in this plan, which would assert a rule Plan 03 has not
adopted.

**One naming correction, shared with Plans 02 and 03.** The manual clock in this suite is
`Support/ManualTimeProvider.cs`. Plan 02 called its own `FakeTimeProvider` and has been renamed to
match; the doc comment here and there now says why neither is called that — it would be mistaken for
`Microsoft.Extensions.Time.Testing.FakeTimeProvider`, a real type this project does not reference.

**Addendum sweep.** Re-read after the contract addendum retired the `SharpMU.Mssp` package and moved
the MSSP domain into `MUI.Crawl.Mssp`. This plan referenced the package in exactly one place — the
global constraint — and never in code: `MUI.Backfill` reads other people's directory exports and
never speaks to a server, so it constructs no `MsspData`, adds no package reference and imports no
MSSP type. The `mssp` object inside MudVerse's fixture (Task 11) is that site's own JSON, parsed by
this plan's own mapping into lower-cased field names, and is untouched by the change. No task's
**Files**, **Interfaces** or code steps moved.

**Running test count by task** (cumulative, so a task that does not reach its number has lost one):
1 → 5, 2 → 13, 3 → 26, 4 → 33, 5 → 36, 6 → 42, 7 → 51, 8 → 63, 9 → 69, 10 → 75, 11 → 83, 12 → 89,
13 → 96, 14 → 105, 15 → 112.
