# AresCentral Discovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Read the AresCentral games API on a schedule, seed the addresses it lists, record the values it holds under a new weak provenance rung, and show on each game page which source first found it.

**Architecture:** A new `MUI.Ares` project holds a typed `HttpClient` and its DTO and knows nothing about the catalogue. `AresCycle` in `MUI.Crawler` turns one fetch into targets, fields and listing rows; `AresService` runs it under its own Postgres advisory lease, and `mui-crawl --ares` forces one pass. Separately, a `discovered_via` column travels from `crawl_target` to `game` and surfaces as one dated line on the game page.

**Tech Stack:** .NET 10, ASP.NET Core, Npgsql + Dapper, raw SQL migrations (no EF Core), TUnit on Microsoft.Testing.Platform, Blazor SSR, RESX message bundles.

**Spec:** `docs/specs/2026-08-22-arescentral-discovery-design.md`

## Global Constraints

- **.NET 10**, `TreatWarningsAsErrors` is `true` solution-wide. A build with a warning is a failed build.
- **Tests are TUnit on Microsoft.Testing.Platform.** `dotnet test` does not work. Run a suite with
  `dotnet run -c Release --no-build --project tests/<Suite> </dev/null`. Keep the `</dev/null`.
- Assertions are `await Assert.That(x).IsEqualTo(y)`. Tests are `[Test]` methods on a plain public class.
- **Never `new HttpClient()`.** Typed clients through `IHttpClientFactory`, `AllowAutoRedirect = false`.
- **`MUI.Catalog` may not reference `MUI.Crawl`.** `MUI.Ares` references neither.
- **Nothing is ever deleted.** A delisting stamps a date; it does not remove a row or end a listing.
- **Parsers never fabricate.** A missing or blank value in the API response writes no field at all.
- Migration numbers collide across worktrees. `mui_migration` keys on **filename**. Before writing a
  migration, run `ls migrations | tail -3` and check production for a clash. This plan writes `0032`,
  `0033` and `0034`; renumber if any is taken.
- New message ids go into **all five** RESX files: `Messages.resx` (en, with a `<comment>`),
  `Messages.de.resx`, `Messages.ja.resx`, `Messages.nl.resx`, `Messages.zh-Hans.resx`.
- Protocol names (`MSSP`, `WHO`, `INFO`, `I3`) and proper nouns (`AresCentral`, `AresMUSH`) are machine
  voice and are not translated. The sentence around them is.

---

### Task 1: The `ares_central` provenance rung

**Files:**
- Modify: `src/MUI.Catalog/Games/Provenance.cs` (insert between `Mssp`/`Info` and `I3Mudlist`)
- Modify: `src/MUI.Catalog/Persistence/SqlEnums.cs:14-40`
- Modify: `src/MUI.Web/Components/Text/Provenance.cs:44-63`
- Create: `migrations/0032_ares_central_field_source.sql`
- Modify: all five `src/MUI.Web/Resources/Messages*.resx`
- Test: `tests/MUI.Catalog.Tests/Fields/FieldPrecedenceTests.cs` (add to existing file if present, else create)

**Interfaces:**
- Consumes: nothing.
- Produces: `FieldSource.AresCentral`; `SqlEnums.ToDb(FieldSource.AresCentral) == "ares_central"`;
  `SqlEnums.ToFieldSource("ares_central") == FieldSource.AresCentral`;
  `Provenance.Via(tag, FieldSource.AresCentral)` returns the message at id `source.aresCentral`.

- [ ] **Step 1: Write the failing test**

Append to `tests/MUI.Catalog.Tests/Fields/FieldPrecedenceTests.cs` (create the file with
`namespace MUI.Catalog.Tests;` and `using MUI.Catalog;` if it does not exist):

```csharp
/// <summary>
/// A hub repeating a claim of unknown age loses to the game speaking to us directly now, and beats
/// the I3 mudlist, which is unauthenticated and carries `test` beside the real entries.
/// </summary>
[Test]
public async Task AresCentralRanksBelowMsspAndAboveTheI3Mudlist()
{
    await Assert.That(FieldPrecedence.RankOf(FieldSource.Mssp))
        .IsLessThan(FieldPrecedence.RankOf(FieldSource.AresCentral));
    await Assert.That(FieldPrecedence.RankOf(FieldSource.AresCentral))
        .IsLessThan(FieldPrecedence.RankOf(FieldSource.I3Mudlist));
}

/// <summary>A human correction outranks any hub, always (§5.1).</summary>
[Test]
public async Task StaffAndOwnerStillOutrankAresCentral()
{
    await Assert.That(FieldPrecedence.RankOf(FieldSource.Staff))
        .IsLessThan(FieldPrecedence.RankOf(FieldSource.AresCentral));
    await Assert.That(FieldPrecedence.RankOf(FieldSource.Owner))
        .IsLessThan(FieldPrecedence.RankOf(FieldSource.AresCentral));
}

/// <summary>
/// The rung is stored as text, so inserting a member mid-enum may not change what is written.
/// </summary>
[Test]
public async Task TheRungRoundTripsThroughItsDatabaseSpelling()
{
    await Assert.That(SqlEnums.ToDb(FieldSource.AresCentral)).IsEqualTo("ares_central");
    await Assert.That(SqlEnums.ToFieldSource("ares_central")).IsEqualTo(FieldSource.AresCentral);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `'FieldSource' does not contain a definition for 'AresCentral'`.

- [ ] **Step 3: Add the enum member**

In `src/MUI.Catalog/Games/Provenance.cs`, immediately **before** `I3Mudlist`:

```csharp
    /// <summary>
    /// A value AresCentral holds: the game told the AresMUSH community hub, and the hub told us
    /// through an API whose maintainer issued us credentials.
    /// </summary>
    /// <remarks>
    /// Declared, not measured — nobody here read this off the game's own socket. Ranks below
    /// <see cref="Mssp"/>, because a game speaking to us directly and now beats a hub repeating a
    /// claim of unknown age, and above <see cref="I3Mudlist"/>, because AresCentral is
    /// authenticated, curated by the codebase's own author, and excludes games marked In Development
    /// and games long offline, where the live I3 mudlist carries <c>test</c> and <c>Your MUD Name</c>
    /// beside the real entries. Never above <see cref="Staff"/> or <see cref="Owner"/>: a hub does
    /// not police what a game calls itself, and a human correction wins.
    /// </remarks>
    AresCentral,
```

- [ ] **Step 4: Add the two SQL mappings**

In `src/MUI.Catalog/Persistence/SqlEnums.cs`, in `ToDb` before the `I3Mudlist` arm:

```csharp
        FieldSource.AresCentral => "ares_central",
```

and in `ToFieldSource` before the `"i3_mudlist"` arm:

```csharp
        "ares_central" => FieldSource.AresCentral,
```

- [ ] **Step 5: Add the display wording**

In `src/MUI.Web/Components/Text/Provenance.cs`, in the `Via` switch before the `I3Mudlist` arm:

```csharp
            FieldSource.AresCentral => "source.aresCentral",
```

- [ ] **Step 6: Add the message id to all five bundles**

`src/MUI.Web/Resources/Messages.resx`:

```xml
  <data name="source.aresCentral" xml:space="preserve">
    <value>AresCentral</value>
    <comment>how a value reached us, one id per source; a proper noun, not translated</comment>
  </data>
```

The same `<data name="source.aresCentral">` block with `<value>AresCentral</value>` in
`Messages.de.resx`, `Messages.ja.resx`, `Messages.nl.resx` and `Messages.zh-Hans.resx`. The name is a
proper noun and stays as-is in every locale; the id exists so the switch has somewhere to point and
so a future locale can wrap it in a phrase.

- [ ] **Step 7: Write the migration**

Create `migrations/0032_ares_central_field_source.sql`:

```sql
-- Lets game_field name AresCentral as a source (§5.1). The AresMUSH community hub answers an
-- authenticated API with the games it lists, and those values are the game's own self-description
-- relayed by a third party — declared, never measured.
--
-- Precedence: below `mssp` (a game's own MSSP NAME is spoken to us directly, now; a hub entry is a
-- claim of unknown age relayed onward) and above `i3_mudlist` (AresCentral is authenticated, curated
-- by the codebase's author, and excludes In Development and long-offline games; the I3 mudlist is
-- none of those things). Does NOT rank above `staff` or `owner`.
--
-- field_change takes the same widening, or a source it cannot spell is not a change it can log.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE game_field
    DROP CONSTRAINT game_field_source_vocabulary,
    ADD CONSTRAINT game_field_source_vocabulary CHECK (source IN (
        'staff', 'handshake', 'owner', 'who', 'mssp', 'ares_central', 'i3_mudlist', 'banner'));

ALTER TABLE field_change
    DROP CONSTRAINT field_change_source_vocabulary,
    ADD CONSTRAINT field_change_source_vocabulary CHECK (source IN (
        'staff', 'handshake', 'owner', 'who', 'mssp', 'ares_central', 'i3_mudlist', 'banner'));
```

Check the existing constraint's member list first with
`grep -rn 'source_vocabulary' migrations/ | tail -4` and copy it forward exactly, adding only
`'ares_central'`. Do not retype it from this plan if it has since changed.

- [ ] **Step 8: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null`
Expected: PASS. A missing `Provenance.Via` case throws `ArgumentOutOfRangeException` by design, so a
`MUI.Web.Tests` run should also pass — run it too:
`dotnet run -c Release --no-build --project tests/MUI.Web.Tests </dev/null`

- [ ] **Step 9: Commit**

```bash
git add src/MUI.Catalog src/MUI.Web migrations/0032_ares_central_field_source.sql tests/MUI.Catalog.Tests
git commit -m "Give AresCentral a rung between MSSP and the I3 mudlist"
```

---

### Task 2: `DiscoverySource` — how an address got here

**Files:**
- Create: `src/MUI.Discovery/Scheduling/DiscoverySource.cs`
- Modify: `src/MUI.Discovery/Scheduling/CrawlTarget.cs` (add property to `CrawlTarget`)
- Modify: `src/MUI.Catalog/Persistence/Games/Records.cs:26-38` (add to `GameRecord`)
- Modify: `src/MUI.Catalog/Persistence/Games/NpgsqlGameStore.cs:52-70` (insert + read)
- Modify: `src/MUI.Crawler/Persistence/NpgsqlCrawlTargetRepository.cs:65-103` (insert) and its SELECT list
- Modify: `src/MUI.Crawler/Crawl/CatalogueBinder.cs:216-225` (copy onto the minted game)
- Modify: `src/MUI.Crawler/Scheduling/CrawlerService.cs:254`, `src/MUI.Discovery/Referral/ReferralGraphWriter.cs:178`,
  `src/MUI.Discovery/Intake/Submission.cs:386`, `src/MUI.Crawler/I3/I3Cycle.cs:79`
- Create: `migrations/0033_discovered_via.sql`
- Test: `tests/MUI.Discovery.Tests/DiscoverySourceTests.cs`,
  `tests/MUI.Crawler.Tests/CrawlRegistryPostgresTests.cs` (append)

**Interfaces:**
- Consumes: nothing.
- Produces: `enum MUI.Discovery.DiscoverySource { OperatorSeed, Submission, Referral, I3Mudlist, AresCentral, Backfill }`;
  `DiscoverySources.ToDb(DiscoverySource)` → `string`; `DiscoverySources.From(string?)` → `DiscoverySource?`;
  `CrawlTarget.DiscoveredVia` (`DiscoverySource?`, `init`);
  `GameRecord.DiscoveredVia` (`DiscoverySource?`, defaulted `null`, positioned last so existing
  positional constructions keep compiling).

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Discovery.Tests/DiscoverySourceTests.cs`:

```csharp
using MUI.Discovery;

namespace MUI.Discovery.Tests;

/// <summary>
/// How an address reached the registry, as a dated fact about our crawl rather than a claim about
/// the game. Every spelling here is written to the database and read back out of it, so a rename is
/// a migration and not an edit.
/// </summary>
public class DiscoverySourceTests
{
    [Test]
    [Arguments(DiscoverySource.OperatorSeed, "operator_seed")]
    [Arguments(DiscoverySource.Submission, "submission")]
    [Arguments(DiscoverySource.Referral, "referral")]
    [Arguments(DiscoverySource.I3Mudlist, "i3_mudlist")]
    [Arguments(DiscoverySource.AresCentral, "ares_central")]
    [Arguments(DiscoverySource.Backfill, "backfill")]
    public async Task EverySourceRoundTripsThroughItsDatabaseSpelling(
        DiscoverySource source, string spelling)
    {
        await Assert.That(DiscoverySources.ToDb(source)).IsEqualTo(spelling);
        await Assert.That(DiscoverySources.From(spelling)).IsEqualTo(source);
    }

    /// <summary>
    /// Every address that existed before this column did has no answer, and a guess would be worse
    /// than silence — the page renders nothing rather than naming a source we never recorded.
    /// </summary>
    [Test]
    public async Task AnUnrecordedSourceStaysUnknown()
    {
        await Assert.That(DiscoverySources.From(null)).IsNull();
        await Assert.That(DiscoverySources.From("")).IsNull();
    }

    /// <summary>
    /// A spelling the database allowed and this build does not know is a deployment mid-rollout, not
    /// a corrupt row. Unknown, never an exception on a page render.
    /// </summary>
    [Test]
    public async Task AnUnrecognisedSpellingIsUnknownRatherThanAThrow()
    {
        await Assert.That(DiscoverySources.From("mudstats")).IsNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'DiscoverySource' could not be found`.

- [ ] **Step 3: Write the enum and its spellings**

Create `src/MUI.Discovery/Scheduling/DiscoverySource.cs`:

```csharp
namespace MUI.Discovery;

/// <summary>
/// Which channel first brought an address into the registry.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a fact about our crawl, not about the game.</b> It answers "how did this site come to
/// know about this address, and when" — never "where did this game come from". Any game worth
/// listing appears in several places at once, so the value here records which channel reached us
/// first and nothing more, and every surface that renders it must say so in those words.
/// </para>
/// <para>
/// Set once, when a <see cref="CrawlTarget"/> row is created, and never afterwards:
/// <c>ICrawlTargetRepository.AddAsync</c> collapses onto an existing row and updates depth alone, so
/// a second channel finding a known address cannot overwrite the first.
/// </para>
/// </remarks>
public enum DiscoverySource
{
    /// <summary>An address a human operator configured into this deployment.</summary>
    OperatorSeed,

    /// <summary>Somebody handed it to us through the public submission form (§8).</summary>
    Submission,

    /// <summary>Another game's own list named it (§7.2).</summary>
    Referral,

    /// <summary>The Intermud-3 router listed it.</summary>
    I3Mudlist,

    /// <summary>The AresCentral games API listed it.</summary>
    AresCentral,

    /// <summary>
    /// The one-time day-one address backfill (§7.6).
    /// </summary>
    /// <remarks>
    /// Deliberately names no directory. §7.6 takes host and port from several lists and records which
    /// one supplied a given address nowhere at all, so this is the honest ceiling on what we can say.
    /// Nothing in this repository writes it — the importer lives on <c>import/one-time</c> — and it
    /// exists so that branch has a spelling to use.
    /// </remarks>
    Backfill,
}

/// <summary>The database spelling of each <see cref="DiscoverySource"/>, in one place.</summary>
/// <remarks>
/// Text rather than the enum's integer, for the same reason <c>FieldSource</c> is text: a column a
/// person can read in <c>psql</c> survives a member being inserted in the middle of the enum.
/// </remarks>
public static class DiscoverySources
{
    public static string ToDb(DiscoverySource source) => source switch
    {
        DiscoverySource.OperatorSeed => "operator_seed",
        DiscoverySource.Submission => "submission",
        DiscoverySource.Referral => "referral",
        DiscoverySource.I3Mudlist => "i3_mudlist",
        DiscoverySource.AresCentral => "ares_central",
        DiscoverySource.Backfill => "backfill",
        _ => throw new ArgumentOutOfRangeException(
            nameof(source), source, "No database spelling for this discovery source. Add one."),
    };

    /// <summary>
    /// The source a stored spelling names, or null.
    /// </summary>
    /// <remarks>
    /// Null for an absent value — every row written before the column existed — and null, rather than
    /// a throw, for a spelling this build does not know: during a rollout an older replica renders
    /// pages written by a newer one, and an unknown channel is a line we omit, not a page we fail.
    /// </remarks>
    public static DiscoverySource? From(string? value) => value switch
    {
        "operator_seed" => DiscoverySource.OperatorSeed,
        "submission" => DiscoverySource.Submission,
        "referral" => DiscoverySource.Referral,
        "i3_mudlist" => DiscoverySource.I3Mudlist,
        "ares_central" => DiscoverySource.AresCentral,
        "backfill" => DiscoverySource.Backfill,
        _ => null,
    };
}
```

- [ ] **Step 4: Run the test**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS.

- [ ] **Step 5: Write the migration**

Create `migrations/0033_discovered_via.sql`:

```sql
-- Records which channel first brought an address into the registry, and carries it onto the game
-- the address is promoted to.
--
-- This is a fact about our crawl, not about the game: "first seen via AresCentral on this date",
-- never "this game came from AresCentral". §7.6 rejected an origin field on the grounds that a
-- game's origin is not one fact — any game worth listing is in several directories — and that
-- objection is answered by what the value is allowed to say rather than by not storing it. Nothing
-- reads this as exclusivity and no surface may render it as a badge.
--
-- Nullable with no default and no backfill. Every row that exists today predates the column, and a
-- guess would be exactly the accident §7.6 warned about; unknown renders nothing.
--
-- crawl_target's ON CONFLICT (host, port) DO UPDATE touches depth alone, so a second channel finding
-- a known address cannot overwrite the first. That is the write-once rule, and it is already
-- enforced by the statement rather than by a trigger.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE crawl_target ADD COLUMN discovered_via text;
ALTER TABLE crawl_target ADD CONSTRAINT crawl_target_discovered_via_vocabulary CHECK (
    discovered_via IS NULL OR discovered_via IN (
        'operator_seed', 'submission', 'referral', 'i3_mudlist', 'ares_central', 'backfill'));

ALTER TABLE game ADD COLUMN discovered_via text;
ALTER TABLE game ADD CONSTRAINT game_discovered_via_vocabulary CHECK (
    discovered_via IS NULL OR discovered_via IN (
        'operator_seed', 'submission', 'referral', 'i3_mudlist', 'ares_central', 'backfill'));
```

- [ ] **Step 6: Thread the column through the records**

`src/MUI.Discovery/Scheduling/CrawlTarget.cs`, on `CrawlTarget`, after `SubmittedAt`:

```csharp
    /// <summary>
    /// Which channel first brought this address here, or null for every row written before the
    /// column existed (migration 0033).
    /// </summary>
    /// <remarks>
    /// Write-once: <see cref="ICrawlTargetRepository.AddAsync"/> collapses onto an existing row and
    /// updates depth alone, so a second channel finding a known address leaves this as the first
    /// channel set it. <c>CatalogueBinder</c> copies it onto the game it mints, the same way
    /// <see cref="SubmittedAt"/> travels.
    /// </remarks>
    public DiscoverySource? DiscoveredVia { get; init; }
```

`src/MUI.Catalog/Persistence/Games/Records.cs`, as the **last** parameter of `GameRecord` so every
existing positional construction still compiles:

```csharp
    IReadOnlyList<string>? CorroboratedBy = null,
    DiscoverySource? DiscoveredVia = null);
```

Add `using MUI.Discovery;` to that file. If `MUI.Catalog` does not already reference `MUI.Discovery`,
**do not add the reference** — instead move `DiscoverySource.cs` into
`src/MUI.Catalog/Games/DiscoverySource.cs` under `namespace MUI.Catalog;`, and have `MUI.Discovery`
consume it from there. Check with
`grep -n 'ProjectReference' src/MUI.Catalog/MUI.Catalog.csproj src/MUI.Discovery/MUI.Discovery.csproj`
before writing either line; the arrow between these two assemblies is load-bearing and this plan must
not invert it.

- [ ] **Step 7: Thread it through both stores**

`NpgsqlCrawlTargetRepository.AddAsync`: add `discovered_via` to the column list, `@discoveredVia` to
the values list, and to the anonymous parameter object:

```csharp
                discoveredVia = target.DiscoveredVia is { } via ? DiscoverySources.ToDb(via) : null,
```

Leave the `ON CONFLICT` clause exactly as it is. Add `discovered_via` to every `SELECT` list in that
file that materialises a `CrawlTarget`, and map it with `DiscoverySources.From(row.DiscoveredVia)`.
Find them with `grep -n 'SELECT' src/MUI.Crawler/Persistence/NpgsqlCrawlTargetRepository.cs`.

`NpgsqlGameStore.InsertAsync`: add `discovered_via` to the column list, `@discoveredVia` to the values
list, and the same ternary to the parameter object. Add it to the game SELECT lists in the same file.

- [ ] **Step 8: Stamp it at all five creation sites**

Each site adds one property to the `CrawlTarget` it constructs. Nothing else changes.

- `src/MUI.Crawler/Scheduling/CrawlerService.cs:254` → `DiscoveredVia = DiscoverySource.OperatorSeed,`
- `src/MUI.Discovery/Intake/Submission.cs:386` → `DiscoveredVia = DiscoverySource.Submission,`
- `src/MUI.Discovery/Referral/ReferralGraphWriter.cs:178` → `DiscoveredVia = DiscoverySource.Referral,`
- `src/MUI.Crawler/I3/I3Cycle.cs:79` → `DiscoveredVia = DiscoverySource.I3Mudlist,`
- `AresCycle` is Task 5 and sets `DiscoverySource.AresCentral` there.

- [ ] **Step 9: Carry it onto the minted game**

`src/MUI.Crawler/Crawl/CatalogueBinder.cs`, in `CreateAsync`'s `new GameRecord(...)`:

```csharp
            SubmittedAt: target.SubmittedAt,
            DiscoveredVia: target.DiscoveredVia);
```

- [ ] **Step 10: Write the persistence test**

Append to `tests/MUI.Crawler.Tests/CrawlRegistryPostgresTests.cs`, following the fixture pattern
already in that file (read the top of it first for how it obtains a data source and skips without
Postgres):

```csharp
/// <summary>
/// Write-once, enforced by the insert rather than by a rule somebody has to remember. A referral
/// naming an address AresCentral already gave us must not relabel where we first found it.
/// </summary>
[Test]
public async Task ASecondChannelFindingAKnownAddressDoesNotRelabelIt()
{
    await using var source = await Fixture.DataSourceAsync();
    var targets = new NpgsqlCrawlTargetRepository(source);
    var now = DateTimeOffset.UtcNow;

    await targets.AddAsync(
        new CrawlTarget
        {
            Id = Guid.CreateVersion7(),
            Host = "ares.example.org",
            Port = 4201,
            NextProbeAt = now,
            FirstSeenAt = now,
            DiscoveredVia = DiscoverySource.AresCentral,
        },
        CancellationToken.None);

    await targets.AddAsync(
        new CrawlTarget
        {
            Id = Guid.CreateVersion7(),
            Host = "ares.example.org",
            Port = 4201,
            NextProbeAt = now,
            FirstSeenAt = now,
            DiscoveredVia = DiscoverySource.Referral,
        },
        CancellationToken.None);

    var stored = await targets.ByAddressAsync("ares.example.org", 4201, CancellationToken.None);

    await Assert.That(stored!.DiscoveredVia).IsEqualTo(DiscoverySource.AresCentral);
}
```

- [ ] **Step 11: Run the suites**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Crawler.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
```
Expected: PASS. The Crawler and Catalog suites need Postgres; Podman satisfies Testcontainers here.

- [ ] **Step 12: Commit**

```bash
git add src tests migrations/0033_discovered_via.sql
git commit -m "Record which channel first brought an address here, write-once"
```

---

### Task 3: `MUI.Ares` — the client

**Files:**
- Create: `src/MUI.Ares/MUI.Ares.csproj`
- Create: `src/MUI.Ares/AresListedGame.cs`
- Create: `src/MUI.Ares/AresOptions.cs`
- Create: `src/MUI.Ares/AresGamesClient.cs`
- Modify: `MUIndex.slnx`
- Modify: `src/MUI.Crawler/MUI.Crawler.csproj` (project reference)
- Test: `tests/MUI.Crawler.Tests/Ares/AresGamesClientTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `sealed record AresListedGame(string? Name, string? Description, string? Hostname, int Port, string? Genre, string? Website, string? LastPing, string? Status)`
  - `interface IAresGames { Task<IReadOnlyList<AresListedGame>> ListAsync(CancellationToken ct = default); }`
  - `sealed record AresOptions { Uri BaseAddress; string ClientId; string ApiKey; TimeSpan Timeout; long MaxResponseBytes; void Validate(); }`
  - `sealed class AresGamesClient(HttpClient http, AresOptions options, ILogger<AresGamesClient>? log = null) : IAresGames`
  - `AresGamesClient.AuthorizationFor(AresOptions)` → `string`, `internal static`, exposed to tests
    via `InternalsVisibleTo`.

**Note on test placement:** these tests live in `MUI.Crawler.Tests` rather than a new
`MUI.Ares.Tests`. `MUI.I3.Tests` exists because `GatewayClient` is a 491-line framing protocol worth
its own suite; this is one GET and a deserialise, and a seventh CI leg for it is overhead. Do not add
a suite.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawler.Tests/Ares/AresGamesClientTests.cs`:

```csharp
using System.Net;
using System.Text;

using MUI.Ares;

namespace MUI.Crawler.Tests;

/// <summary>
/// What the client sends, and what it refuses to return.
/// </summary>
/// <remarks>
/// The handler is faked rather than a loopback server: what is under test is the header we present
/// and how a bad body is treated, neither of which involves a socket.
/// </remarks>
public class AresGamesClientTests
{
    private const string OneGame = """
        [
          {
            "name": "Battlestar Pacifica",
            "description": "A **Markdown** blurb.",
            "hostname": "bsgpacifica.org",
            "port": 4201,
            "genre": "Sci-Fi",
            "website": "https://bsgpacifica.org",
            "last_ping": "08/21/2026",
            "status": "Open"
          }
        ]
        """;

    private static AresOptions Options() => new()
    {
        BaseAddress = new Uri("https://arescentral.aresmush.com/"),
        ClientId = "muindex",
        ApiKey = "s3cret",
    };

    /// <summary>
    /// The documented header shape, exactly: one bearer credential whose value is the client id and
    /// the key joined by a colon. Getting this subtly wrong reads as a revoked key.
    /// </summary>
    [Test]
    public async Task TheCredentialIsTheClientIdAndKeyJoinedByAColon()
    {
        await Assert.That(AresGamesClient.AuthorizationFor(Options())).IsEqualTo("muindex:s3cret");
    }

    [Test]
    public async Task AListedGameComesBackWithEveryFieldTheHubHolds()
    {
        var client = Client(HttpStatusCode.OK, OneGame);

        var games = await client.ListAsync();

        var game = games.Single();
        await Assert.That(game.Name).IsEqualTo("Battlestar Pacifica");
        await Assert.That(game.Hostname).IsEqualTo("bsgpacifica.org");
        await Assert.That(game.Port).IsEqualTo(4201);
        await Assert.That(game.Genre).IsEqualTo("Sci-Fi");
        await Assert.That(game.Website).IsEqualTo("https://bsgpacifica.org");
        await Assert.That(game.Status).IsEqualTo("Open");
        await Assert.That(game.LastPing).IsEqualTo("08/21/2026");
    }

    /// <summary>
    /// A refusal is an exception and never an empty list. An empty list is a legitimate answer
    /// meaning "no games", and <c>AresCycle</c> would sweep every listing as delisted on the strength
    /// of it.
    /// </summary>
    [Test]
    public async Task ARefusedRequestThrowsRatherThanReturningNothing()
    {
        var client = Client(HttpStatusCode.Unauthorized, "nope");

        await Assert.That(async () => await client.ListAsync()).Throws<HttpRequestException>();
    }

    [Test]
    public async Task ABodyThatIsNotTheDocumentedShapeThrows()
    {
        var client = Client(HttpStatusCode.OK, "{\"error\":\"maintenance\"}");

        await Assert.That(async () => await client.ListAsync()).ThrowsException();
    }

    private static AresGamesClient Client(HttpStatusCode status, string body)
    {
        var http = new HttpClient(new StubHandler(status, body))
        {
            BaseAddress = new Uri("https://arescentral.aresmush.com/"),
        };

        return new AresGamesClient(http, Options());
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `The type or namespace name 'Ares' does not exist in the namespace 'MUI'`.

- [ ] **Step 3: Create the project**

`src/MUI.Ares/MUI.Ares.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <RootNamespace>MUI.Ares</RootNamespace>
    <AssemblyName>MUI.Ares</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="MUI.Crawler.Tests" />
  </ItemGroup>

  <!--
    No reference to MUI.Catalog, and none to MUI.Crawl, for the reason MUI.I3 states: what comes back
    off this connection is a third party's raw answer, and turning it into catalogue state belongs to
    whatever assembles a reading. The arrow stays one-way so AresCycle is testable against a captured
    body with no network in sight.
  -->

</Project>
```

Add it to `MUIndex.slnx` beside `MUI.I3` — copy the existing `<Project Path="src/MUI.I3/MUI.I3.csproj" />`
line's shape. Add a `<ProjectReference Include="..\MUI.Ares\MUI.Ares.csproj" />` to
`src/MUI.Crawler/MUI.Crawler.csproj`.

- [ ] **Step 4: Write the DTO and the options**

`src/MUI.Ares/AresListedGame.cs`:

```csharp
using System.Text.Json.Serialization;

namespace MUI.Ares;

/// <summary>
/// One game as AresCentral lists it.
/// </summary>
/// <remarks>
/// Every string is nullable because every one of them is a third party's field that may be absent or
/// blank, and a record that pretends otherwise turns a thin listing into a
/// <c>JsonException</c> for the whole pass. <see cref="Port"/> is not: the hub always sends a number,
/// and a 0 means "nothing to dial", which <c>AresCycle</c> handles rather than the deserialiser.
/// <para>
/// <see cref="LastPing"/> stays a string. It arrives as <c>MM/DD/YYYY</c> in an unstated timezone,
/// and parsing it to a <c>DateTimeOffset</c> would invent a precision the hub never sent. It is
/// stored as given and used for nothing — see <c>AresCycle</c>.
/// </para>
/// </remarks>
public sealed record AresListedGame(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("hostname")] string? Hostname,
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("genre")] string? Genre,
    [property: JsonPropertyName("website")] string? Website,
    [property: JsonPropertyName("last_ping")] string? LastPing,
    [property: JsonPropertyName("status")] string? Status);

/// <summary>The games AresCentral lists.</summary>
public interface IAresGames
{
    /// <summary>
    /// Every game the hub currently lists.
    /// </summary>
    /// <remarks>
    /// Throws rather than returning an empty list when the request fails. An empty list is a real
    /// answer meaning the hub lists nothing, and a caller that cannot tell the two apart will read a
    /// failed fetch as every game having been delisted at once.
    /// </remarks>
    Task<IReadOnlyList<AresListedGame>> ListAsync(CancellationToken ct = default);
}
```

`src/MUI.Ares/AresOptions.cs`:

```csharp
namespace MUI.Ares;

/// <summary>Where AresCentral is, and what it expects us to present.</summary>
public sealed record AresOptions
{
    public Uri BaseAddress { get; init; } = new("https://arescentral.aresmush.com/");

    /// <summary>The path the games list is at, relative to <see cref="BaseAddress"/>.</summary>
    public string GamesPath { get; init; } = "api/games";

    /// <summary>The client id AresCentral issued us.</summary>
    public string ClientId { get; init; } = "";

    /// <summary>The key that goes with it.</summary>
    public string ApiKey { get; init; } = "";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The most body we will read.
    /// </summary>
    /// <remarks>
    /// Read to a ceiling rather than trusting <c>Content-Length</c>, the same rule
    /// <c>IconFetcher</c> follows. A few hundred games with Markdown blurbs is well inside this.
    /// </remarks>
    public long MaxResponseBytes { get; init; } = 8 * 1024 * 1024;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "AresCentral needs both a client id and an API key; it issues them as a pair.");
        }

        if (Timeout <= TimeSpan.Zero || MaxResponseBytes <= 0)
        {
            throw new InvalidOperationException(
                "AresCentral needs a positive timeout and response ceiling.");
        }
    }
}
```

- [ ] **Step 5: Write the client**

`src/MUI.Ares/AresGamesClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MUI.Ares;

/// <summary>
/// Reads the AresCentral games list.
/// </summary>
/// <remarks>
/// The <see cref="HttpClient"/> arrives from <c>IHttpClientFactory</c> and is never constructed here
/// — the factory is what bounds the handler's lifetime. Redirects are off at the registration: a
/// redirect is a second address nobody ruled on.
/// </remarks>
public sealed class AresGamesClient(
    HttpClient http,
    AresOptions options,
    ILogger<AresGamesClient>? log = null) : IAresGames
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ILogger<AresGamesClient> _log = log ?? NullLogger<AresGamesClient>.Instance;

    /// <summary>
    /// The bearer credential AresCentral documents: the client id and the key, joined by a colon.
    /// </summary>
    internal static string AuthorizationFor(AresOptions options) =>
        $"{options.ClientId}:{options.ApiKey}";

    public async Task<IReadOnlyList<AresListedGame>> ListAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, options.GamesPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthorizationFor(options));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        // Throws on any non-success, which is the point: a caller must not be able to mistake a
        // refusal for a hub that lists no games.
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(ct);
        // The ceiling is ours and is enforced on the stream, not read off a header a server sets.
        await using var bounded = new BoundedStream(body, options.MaxResponseBytes);

        var games = await JsonSerializer.DeserializeAsync<List<AresListedGame>>(bounded, Json, ct)
            ?? throw new JsonException("AresCentral answered with a JSON null rather than a list of games.");

        _log.LogDebug("AresCentral listed {Count} games", games.Count);

        return games;
    }

    /// <summary>A read that stops at a ceiling rather than trusting the far end to be reasonable.</summary>
    private sealed class BoundedStream(Stream inner, long ceiling) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            _read += read;

            return _read > ceiling
                ? throw new InvalidOperationException(
                    $"AresCentral's answer passed {ceiling} bytes, which is more than a games list should be.")
                : read;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Crawler.Tests </dev/null`
Expected: PASS, all four `AresGamesClientTests`.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Ares MUIndex.slnx src/MUI.Crawler/MUI.Crawler.csproj tests/MUI.Crawler.Tests/Ares
git commit -m "Read the AresCentral games list, and refuse to call a failure an empty hub"
```

---

### Task 4: The `ares_listing` table

**Files:**
- Create: `migrations/0034_ares_listing.sql`
- Create: `src/MUI.Discovery/Ares/AresListing.cs`
- Create: `src/MUI.Crawler/Persistence/Ares/NpgsqlAresListingRepository.cs`
- Test: `tests/MUI.Crawler.Tests/Ares/AresListingPostgresTests.cs`

**Interfaces:**
- Consumes: `DiscoverySource` (Task 2).
- Produces:
  - `sealed record AresListing { string Hostname; int Port; string? Name; string? Description; string? Genre; string? Website; string? Status; string? LastPing; Guid? GameId; DateTimeOffset FirstSeenAt; DateTimeOffset LastListedAt; DateTimeOffset? DelistedAt; }`
  - `interface IAresListingRepository`:
    - `Task UpsertAsync(AresListing listing, CancellationToken ct)`
    - `Task BindAsync(string hostname, int port, Guid gameId, CancellationToken ct)`
    - `Task<int> DelistMissingAsync(DateTimeOffset asOf, CancellationToken ct)`
    - `Task<IReadOnlyList<AresListing>> AllAsync(CancellationToken ct)`
  - `sealed class NpgsqlAresListingRepository(NpgsqlDataSource source) : IAresListingRepository`

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawler.Tests/Ares/AresListingPostgresTests.cs`. Read the top of
`tests/MUI.Crawler.Tests/I3/I3BindingPostgresTests.cs` first and copy its fixture and skip
conventions exactly — this plan does not restate them because they are the file's own idiom.

```csharp
/// <summary>
/// A listing seen this pass is refreshed; one that has stopped appearing is dated, never removed.
/// </summary>
[Test]
public async Task AListingThatStopsAppearingIsDatedRatherThanDeleted()
{
    await using var source = await Fixture.DataSourceAsync();
    var listings = new NpgsqlAresListingRepository(source);
    var first = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
    var second = first.AddDays(1);

    await listings.UpsertAsync(Listing("gone.example.org", 4201, first), CancellationToken.None);
    await listings.UpsertAsync(Listing("stays.example.org", 4202, first), CancellationToken.None);

    // Second pass: only one of them comes back.
    await listings.UpsertAsync(Listing("stays.example.org", 4202, second), CancellationToken.None);
    var delisted = await listings.DelistMissingAsync(second, CancellationToken.None);

    await Assert.That(delisted).IsEqualTo(1);

    var rows = await listings.AllAsync(CancellationToken.None);
    await Assert.That(rows.Count).IsEqualTo(2);
    await Assert.That(rows.Single(r => r.Hostname == "gone.example.org").DelistedAt).IsEqualTo(second);
    await Assert.That(rows.Single(r => r.Hostname == "stays.example.org").DelistedAt).IsNull();
}

/// <summary>
/// A game that comes back after a delisting is listed again, not left dated — the column is the
/// hub's current opinion, not a tombstone.
/// </summary>
[Test]
public async Task ARelistedGameStopsBeingDelisted()
{
    await using var source = await Fixture.DataSourceAsync();
    var listings = new NpgsqlAresListingRepository(source);
    var first = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    await listings.UpsertAsync(Listing("back.example.org", 4201, first), CancellationToken.None);
    await listings.DelistMissingAsync(first.AddDays(1), CancellationToken.None);
    await listings.UpsertAsync(Listing("back.example.org", 4201, first.AddDays(2)), CancellationToken.None);

    var row = (await listings.AllAsync(CancellationToken.None))
        .Single(r => r.Hostname == "back.example.org");

    await Assert.That(row.DelistedAt).IsNull();
    // first_seen_at is when we first saw it listed and never moves; last_listed_at is the live one.
    await Assert.That(row.FirstSeenAt).IsEqualTo(first);
    await Assert.That(row.LastListedAt).IsEqualTo(first.AddDays(2));
}

private static AresListing Listing(string host, int port, DateTimeOffset at) => new()
{
    Hostname = host,
    Port = port,
    Name = "A Game",
    Description = "Blurb.",
    Genre = "Sci-Fi",
    Website = $"https://{host}",
    Status = "Open",
    LastPing = "08/21/2026",
    FirstSeenAt = at,
    LastListedAt = at,
};
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `NpgsqlAresListingRepository` not found.

- [ ] **Step 3: Write the migration**

Create `migrations/0034_ares_listing.sql`:

```sql
-- What AresCentral currently says, kept beside what we measured, so the two can be told apart.
--
-- Keyed on (hostname, port) rather than on the hub's name for a game: the address is the only thing
-- here that also means something to the crawler, and a game that renames itself on the hub is the
-- same listing, not a new one.
--
-- delisted_at rather than a delete. Nothing is ever deleted here (§7.5), and a game leaving the hub
-- is a fact worth the date — it does not end our listing, and the crawler keeps probing the address
-- forever either way. Cleared on relisting: this is the hub's current opinion, not a tombstone.
--
-- last_ping is the hub's own reachability check, stored as the string it arrives as. It reaches
-- nothing — not availability, not archive grace, not the probe schedule. §7.6 forbids importing
-- another prober's history, and a game we cannot reach must not look reachable because somebody else
-- could. It is here so an operator reading the table can see what the hub thought.
--
-- game_id is nullable and stays null until the ordinary crawl promotes the address to a game. This
-- table never mints one: a game exists only once a host answers for itself (§7.1).
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

CREATE TABLE ares_listing (
    hostname       text        NOT NULL,
    port           integer     NOT NULL,
    name           text,
    description    text,
    genre          text,
    website        text,
    status         text,
    last_ping      text,
    game_id        uuid        REFERENCES game (id) ON DELETE SET NULL,
    first_seen_at  timestamptz NOT NULL,
    last_listed_at timestamptz NOT NULL,
    delisted_at    timestamptz,
    PRIMARY KEY (hostname, port)
);

CREATE INDEX ares_listing_game_idx ON ares_listing (game_id) WHERE game_id IS NOT NULL;
CREATE INDEX ares_listing_live_idx ON ares_listing (last_listed_at) WHERE delisted_at IS NULL;
```

- [ ] **Step 4: Write the record and the repository**

`src/MUI.Discovery/Ares/AresListing.cs`:

```csharp
namespace MUI.Discovery;

/// <summary>
/// One row of what AresCentral says, as we last saw it.
/// </summary>
/// <remarks>
/// The hub's claims, kept apart from the catalogue's own measurements. Nothing on a public surface
/// reads this table — the values that reach a page go through <c>game_field</c> under
/// <c>FieldSource.AresCentral</c>, where they carry provenance. This is the pass's own memory: what
/// was listed last time, so a disappearance can be noticed.
/// </remarks>
public sealed record AresListing
{
    public required string Hostname { get; init; }

    public required int Port { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    public string? Genre { get; init; }

    public string? Website { get; init; }

    public string? Status { get; init; }

    /// <summary>
    /// The hub's own last reachability check, as the string it sent.
    /// </summary>
    /// <remarks>
    /// Never parsed and never used for anything. It arrives as <c>MM/DD/YYYY</c> in an unstated
    /// timezone, and it is somebody else's measurement — §7.6 forbids importing one.
    /// </remarks>
    public string? LastPing { get; init; }

    /// <summary>The game this address turned out to be, once the ordinary crawl promoted it.</summary>
    public Guid? GameId { get; init; }

    public required DateTimeOffset FirstSeenAt { get; init; }

    public required DateTimeOffset LastListedAt { get; init; }

    /// <summary>When the hub stopped listing it, or null while it is still listed.</summary>
    public DateTimeOffset? DelistedAt { get; init; }
}

/// <summary>Where the AresCentral pass remembers what it last saw.</summary>
public interface IAresListingRepository
{
    /// <summary>
    /// Records a listing as seen. Never moves <c>first_seen_at</c>, and clears any delisting.
    /// </summary>
    Task UpsertAsync(AresListing listing, CancellationToken ct);

    /// <summary>Attaches a listing to the game its address turned out to be.</summary>
    Task BindAsync(string hostname, int port, Guid gameId, CancellationToken ct);

    /// <summary>
    /// Dates every live listing the hub did not mention in the pass that ran at
    /// <paramref name="asOf"/>, and returns how many.
    /// </summary>
    /// <remarks>
    /// Only ever called after a wholly successful fetch. A truncated answer must not read as everyone
    /// having left at once.
    /// </remarks>
    Task<int> DelistMissingAsync(DateTimeOffset asOf, CancellationToken ct);

    Task<IReadOnlyList<AresListing>> AllAsync(CancellationToken ct);
}
```

`src/MUI.Crawler/Persistence/Ares/NpgsqlAresListingRepository.cs`: follow
`NpgsqlI3BindingRepository`'s idiom (Dapper `CommandDefinition`, `await using var connection = await
source.OpenConnectionAsync(ct)`, `ToUniversalTime()` on every timestamp in). The four statements:

```sql
-- UpsertAsync
INSERT INTO ares_listing (hostname, port, name, description, genre, website, status, last_ping,
                          first_seen_at, last_listed_at, delisted_at)
VALUES (@hostname, @port, @name, @description, @genre, @website, @status, @lastPing,
        @firstSeenAt, @lastListedAt, NULL)
ON CONFLICT (hostname, port) DO UPDATE
   SET name = EXCLUDED.name,
       description = EXCLUDED.description,
       genre = EXCLUDED.genre,
       website = EXCLUDED.website,
       status = EXCLUDED.status,
       last_ping = EXCLUDED.last_ping,
       last_listed_at = EXCLUDED.last_listed_at,
       -- first_seen_at is when the hub first listed it and never moves; delisted_at clears, because
       -- a game that came back is listed, not dated.
       delisted_at = NULL

-- BindAsync
UPDATE ares_listing SET game_id = @gameId WHERE hostname = @hostname AND port = @port

-- DelistMissingAsync
UPDATE ares_listing
   SET delisted_at = @asOf
 WHERE delisted_at IS NULL AND last_listed_at < @asOf

-- AllAsync
SELECT hostname AS Hostname, port AS Port, name AS Name, description AS Description,
       genre AS Genre, website AS Website, status AS Status, last_ping AS LastPing,
       game_id AS GameId, first_seen_at AS FirstSeenAt, last_listed_at AS LastListedAt,
       delisted_at AS DelistedAt
  FROM ares_listing
```

`DelistMissingAsync` returns the row count from `ExecuteAsync`.

Register `IAresListingRepository` nowhere yet — Task 6 does the DI.

- [ ] **Step 5: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Crawler.Tests </dev/null`
Expected: PASS. Needs Postgres.

- [ ] **Step 6: Commit**

```bash
git add migrations/0034_ares_listing.sql src/MUI.Discovery/Ares src/MUI.Crawler/Persistence/Ares tests/MUI.Crawler.Tests/Ares
git commit -m "Remember what AresCentral last listed, so a disappearance is a date"
```

---

### Task 5: `AresCycle` — one pass

**Files:**
- Create: `src/MUI.Crawler/Ares/AresCycle.cs`
- Test: `tests/MUI.Crawler.Tests/Ares/AresCycleTests.cs`

**Interfaces:**
- Consumes: `IAresGames`, `AresListedGame` (Task 3); `IAresListingRepository`, `AresListing` (Task 4);
  `DiscoverySource` (Task 2); `ICrawlTargetRepository`, `IGameFieldStore`, `FieldSource.AresCentral` (Task 1).
- Produces:
  - `sealed class AresCycle(IAresGames hub, ICrawlTargetRepository targets, IAresListingRepository listings, IGameFieldStore fields, TimeProvider time, ILogger<AresCycle>? log = null)`
  - `Task<AresCycleResult> RunAsync(CancellationToken ct = default)`
  - `sealed record AresCycleResult { int Listed; int Seeded; int Bound; int Described; int Unlistable; int Delisted; }`

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawler.Tests/Ares/AresCycleTests.cs`:

```csharp
using MUI.Ares;
using MUI.Catalog;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// What one AresCentral pass does, with the hub and every store replaced.
/// </summary>
public class AresCycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private static AresListedGame Game(
        string name, string host, int port, string? website = "https://example.org") =>
        new(name, "A **Markdown** blurb.", host, port, "Sci-Fi", website, "08/21/2026", "Open");

    /// <summary>
    /// An address nobody here has seen becomes a target and no more. No game exists yet, so there is
    /// nothing for the hub's values to attach to — §7.1's rule, which this pass does not get to bend.
    /// </summary>
    [Test]
    public async Task AnUnknownAddressBecomesATargetAndWritesNoFields()
    {
        var targets = new FakeTargets();
        var fields = new FakeFields();

        var result = await Cycle(new StubHub([Game("Pacifica", "bsgpacifica.org", 4201)]), targets, fields)
            .RunAsync();

        await Assert.That(result.Seeded).IsEqualTo(1);
        var planted = targets.Added.Single();
        await Assert.That(planted.Host).IsEqualTo("bsgpacifica.org");
        await Assert.That(planted.Port).IsEqualTo(4201);

        // Stranger-supplied, exactly like a REFERRAL: HostScopeGuard rules on it at dial time.
        await Assert.That(planted.IsOperatorSeed).IsFalse();
        await Assert.That(planted.DiscoveredVia).IsEqualTo(DiscoverySource.AresCentral);

        await Assert.That(fields.Written).IsEmpty();
    }

    /// <summary>
    /// Once the ordinary crawl has promoted the address, the hub's values land — every one of them
    /// under a rung that says a hub said so, never under one that claims we measured it.
    /// </summary>
    [Test]
    public async Task APromotedAddressTakesTheHubsValuesUnderItsOwnRung()
    {
        var game = Guid.CreateVersion7();
        var targets = new FakeTargets();
        targets.Existing[("bsgpacifica.org", 4201)] = game;
        var fields = new FakeFields();

        await Cycle(new StubHub([Game("Pacifica", "bsgpacifica.org", 4201)]), targets, fields).RunAsync();

        await Assert.That(fields.Written.All(f => f.Source == FieldSource.AresCentral)).IsTrue();
        await Assert.That(fields.Written.All(f => f.GameId == game)).IsTrue();
        await Assert.That(Value(fields, "NAME")).IsEqualTo("Pacifica");
        await Assert.That(Value(fields, "GENRE")).IsEqualTo("Sci-Fi");
        await Assert.That(Value(fields, "WEBSITE")).IsEqualTo("https://example.org");
        await Assert.That(Value(fields, "STATUS")).IsEqualTo("Open");
        await Assert.That(Value(fields, "DESCRIPTION")).IsEqualTo("A **Markdown** blurb.");
    }

    /// <summary>
    /// Everything on this list runs AresMUSH — that is what the list is. Inferred from the hub's own
    /// definition rather than read from a field, and recorded at the same weak rung as the rest.
    /// </summary>
    [Test]
    public async Task BeingOnTheListIsItselfTheCodebase()
    {
        var targets = new FakeTargets();
        targets.Existing[("bsgpacifica.org", 4201)] = Guid.CreateVersion7();
        var fields = new FakeFields();

        await Cycle(new StubHub([Game("Pacifica", "bsgpacifica.org", 4201)]), targets, fields).RunAsync();

        await Assert.That(Value(fields, "CODEBASE")).IsEqualTo("AresMUSH");
    }

    /// <summary>Parsers never fabricate: a field the hub left blank is a field we do not write.</summary>
    [Test]
    public async Task ABlankValueWritesNoFieldAtAll()
    {
        var targets = new FakeTargets();
        targets.Existing[("bsgpacifica.org", 4201)] = Guid.CreateVersion7();
        var fields = new FakeFields();

        await Cycle(
                new StubHub([Game("Pacifica", "bsgpacifica.org", 4201, website: "   ")]),
                targets, fields)
            .RunAsync();

        await Assert.That(fields.Written.Any(f => f.Field == "WEBSITE")).IsFalse();
        await Assert.That(fields.Written.Any(f => f.Field == "NAME")).IsTrue();
    }

    /// <summary>
    /// A listing with nothing to dial is recorded and not seeded. The hub carries entries whose port
    /// is 0 or whose hostname is missing, and planting one would be a target that can never answer.
    /// </summary>
    [Test]
    public async Task AListingWithNothingToDialIsRecordedAndNotSeeded()
    {
        var targets = new FakeTargets();
        var listings = new FakeListings();

        var result = await Cycle(
                new StubHub([Game("Unlaunched", "example.org", 0)]), targets, new FakeFields(), listings)
            .RunAsync();

        await Assert.That(result.Unlistable).IsEqualTo(1);
        await Assert.That(result.Seeded).IsEqualTo(0);
        await Assert.That(targets.Added).IsEmpty();
        await Assert.That(listings.Rows).HasCount(1);
    }

    /// <summary>
    /// The one that matters most. A fetch that fails writes nothing and, above all, does not sweep —
    /// otherwise one bad response dates every listing we hold as delisted at once.
    /// </summary>
    [Test]
    public async Task AFailedFetchWritesNothingAndDoesNotSweep()
    {
        var targets = new FakeTargets();
        var listings = new FakeListings();
        var fields = new FakeFields();

        await Assert.That(async () =>
                await Cycle(new ThrowingHub(), targets, fields, listings).RunAsync())
            .Throws<HttpRequestException>();

        await Assert.That(targets.Added).IsEmpty();
        await Assert.That(fields.Written).IsEmpty();
        await Assert.That(listings.Rows).IsEmpty();
        await Assert.That(listings.SweptAt).IsNull();
    }

    /// <summary>An empty list is a real answer and does sweep — the hub said it lists nothing.</summary>
    [Test]
    public async Task AnEmptyListIsAnAnswerAndSweeps()
    {
        var listings = new FakeListings();

        var result = await Cycle(new StubHub([]), new FakeTargets(), new FakeFields(), listings).RunAsync();

        await Assert.That(result.Listed).IsEqualTo(0);
        await Assert.That(listings.SweptAt).IsEqualTo(Now);
    }

    private static string? Value(FakeFields fields, string name) =>
        fields.Written.SingleOrDefault(f => f.Field == name)?.Value;

    private static AresCycle Cycle(
        IAresGames hub,
        FakeTargets targets,
        FakeFields fields,
        FakeListings? listings = null) =>
        new(hub, targets, listings ?? new FakeListings(), fields, new Support.SettableClock(Now));

    private sealed class StubHub(IReadOnlyList<AresListedGame> games) : IAresGames
    {
        public Task<IReadOnlyList<AresListedGame>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult(games);
    }

    private sealed class ThrowingHub : IAresGames
    {
        public Task<IReadOnlyList<AresListedGame>> ListAsync(CancellationToken ct = default) =>
            throw new HttpRequestException("401");
    }

    private sealed class FakeListings : IAresListingRepository
    {
        public Dictionary<(string, int), AresListing> Rows { get; } = [];

        public DateTimeOffset? SweptAt { get; private set; }

        public Task UpsertAsync(AresListing listing, CancellationToken ct)
        {
            Rows[(listing.Hostname, listing.Port)] = listing;
            return Task.CompletedTask;
        }

        public Task BindAsync(string hostname, int port, Guid gameId, CancellationToken ct)
        {
            if (Rows.TryGetValue((hostname, port), out var row))
            {
                Rows[(hostname, port)] = row with { GameId = gameId };
            }

            return Task.CompletedTask;
        }

        public Task<int> DelistMissingAsync(DateTimeOffset asOf, CancellationToken ct)
        {
            SweptAt = asOf;
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<AresListing>> AllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AresListing>>([.. Rows.Values]);
    }
}
```

`FakeTargets` and `FakeFields` are the ones in `tests/MUI.Crawler.Tests/I3/I3CycleTests.cs`, which are
`private sealed` nested classes. Lift both into `tests/MUI.Crawler.Tests/Support/` as `internal
sealed class FakeTargets` and `internal sealed class FakeFields`, delete them from
`I3CycleTests.cs`, and reference them from both files. Do this as the first edit of this step so both
suites keep compiling; run the Crawler suite once before writing the new file to prove the lift alone
broke nothing. Add `public DiscoverySource? DiscoveredVia` handling to `FakeTargets.ByAddressAsync`'s
constructed `CrawlTarget` only if a test needs it — none here does.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `AresCycle` not found.

- [ ] **Step 3: Write the cycle**

Create `src/MUI.Crawler/Ares/AresCycle.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using MUI.Ares;
using MUI.Catalog;
using MUI.Discovery;

namespace MUI.Crawler;

/// <summary>
/// One pass over AresCentral: take the games list, seed what is new, record what the hub says about
/// the ones we have already promoted, and date the ones that have stopped appearing.
/// </summary>
/// <remarks>
/// The first source this site reads by invitation. §7.6's etiquette clause says to ask for a
/// documented API in preference to scraping, and this is that having worked, which is why the pass
/// takes more than addresses: the values here are the game's own self-description, held by the
/// AresMUSH community's own hub, reached with credentials its maintainer issued. They are still
/// declared and are stored as such — <see cref="FieldSource.AresCentral"/>, below MSSP, never above
/// a human.
/// <para>
/// Nothing here matches addresses, resolves a name, or decides that two listings are one game. That
/// is <c>CatalogueBinder</c>'s and <c>IdentityMatcher</c>'s work, reached the ordinary way, through a
/// probe. This pass seeds and annotates; it never mints a game.
/// </para>
/// </remarks>
public sealed class AresCycle(
    IAresGames hub,
    ICrawlTargetRepository targets,
    IAresListingRepository listings,
    IGameFieldStore fields,
    TimeProvider time,
    ILogger<AresCycle>? log = null)
{
    private readonly ILogger<AresCycle> _log = log ?? NullLogger<AresCycle>.Instance;

    public async Task<AresCycleResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();

        // Deliberately not caught. A refusal must reach the caller as a failure: swallowing it here
        // and carrying on would run the sweep below against a list we never received.
        var games = await hub.ListAsync(cancellationToken);

        var result = new AresCycleResult { Listed = games.Count };

        foreach (var game in games)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var host = game.Hostname?.Trim();

            if (string.IsNullOrWhiteSpace(host) || game.Port <= 0)
            {
                // Listed but not dialable — in development, or a game that gave the hub no address.
                // Recording it is right; planting a target that can never answer is not.
                if (!string.IsNullOrWhiteSpace(host))
                {
                    await listings.UpsertAsync(Row(game, host, now), cancellationToken);
                }

                result.Unlistable++;
                continue;
            }

            await listings.UpsertAsync(Row(game, host, now), cancellationToken);

            var target = await targets.ByAddressAsync(host, game.Port, cancellationToken);

            if (target is null)
            {
                await targets.AddAsync(
                    new CrawlTarget
                    {
                        Id = Guid.CreateVersion7(),
                        Host = host,
                        Port = game.Port,
                        NextProbeAt = now,
                        FirstSeenAt = now,

                        // Stranger-supplied, exactly like a REFERRAL. HostScopeGuard rules on every
                        // one of these at dial time, and an operator seed it is not.
                        IsOperatorSeed = false,
                        DiscoveredVia = DiscoverySource.AresCentral,
                    },
                    cancellationToken);

                result.Seeded++;
                continue;
            }

            if (target.GameId is not { } gameId)
            {
                // Known address, not yet promoted: the ordinary crawl has not had it answer for
                // itself. There is nothing to hang a field on, and §7.1 says so.
                continue;
            }

            await listings.BindAsync(host, game.Port, gameId, cancellationToken);
            result.Bound++;

            var wrote = false;

            foreach (var (field, value) in Declared(game))
            {
                await fields.UpsertAsync(
                    new GameField(gameId, field, FieldSource.AresCentral, value, now, now),
                    cancellationToken);
                wrote = true;
            }

            if (wrote)
            {
                result.Described++;
            }
        }

        // Only after a fetch that wholly succeeded — see RunAsync's first statement. A truncated or
        // refused answer never reaches here, so it can never read as everyone having left at once.
        result.Delisted = await listings.DelistMissingAsync(now, cancellationToken);

        return result;
    }

    /// <summary>
    /// The fields the hub holds, skipping every one it left blank.
    /// </summary>
    /// <remarks>
    /// A blank is not a value. Writing one would put an empty string above whatever the crawler
    /// measured, on a rung the measurement cannot outrank in the other direction.
    /// </remarks>
    private static IEnumerable<(string Field, string Value)> Declared(AresListedGame game)
    {
        if (Meaningful(game.Name) is { } name)
        {
            yield return ("NAME", name);
        }

        if (Meaningful(game.Description) is { } description)
        {
            yield return ("DESCRIPTION", description);
        }

        if (Meaningful(game.Genre) is { } genre)
        {
            yield return ("GENRE", genre);
        }

        if (Meaningful(game.Website) is { } website)
        {
            yield return ("WEBSITE", website);
        }

        if (Meaningful(game.Status) is { } status)
        {
            yield return ("STATUS", status);
        }

        // Not a field the hub sends — a fact about what the list is. AresCentral lists AresMUSH
        // games, so appearing on it is the statement. Recorded at the same weak rung as the rest,
        // because it is still somebody else's say-so about somebody else's server.
        yield return (FieldObservations.CodebaseField, "AresMUSH");
    }

    private static string? Meaningful(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AresListing Row(AresListedGame game, string host, DateTimeOffset now) => new()
    {
        Hostname = host,
        Port = game.Port,
        Name = Meaningful(game.Name),
        Description = Meaningful(game.Description),
        Genre = Meaningful(game.Genre),
        Website = Meaningful(game.Website),
        Status = Meaningful(game.Status),
        LastPing = Meaningful(game.LastPing),
        FirstSeenAt = now,
        LastListedAt = now,
    };
}

/// <summary>What one pass did, for the log and for the operator.</summary>
public sealed record AresCycleResult
{
    /// <summary>Games the hub listed.</summary>
    public int Listed { get; set; }

    /// <summary>Addresses we did not have, now in the registry awaiting an ordinary probe.</summary>
    public int Seeded { get; set; }

    /// <summary>Listings attached to a game the crawl had already promoted.</summary>
    public int Bound { get; set; }

    /// <summary>Games whose fields the hub's values were written to this pass.</summary>
    public int Described { get; set; }

    /// <summary>Listings publishing no dialable address.</summary>
    public int Unlistable { get; set; }

    /// <summary>Listings the hub stopped mentioning, now dated. Never removed.</summary>
    public int Delisted { get; set; }
}
```

Check `FieldObservations.CodebaseField`'s exact name first with
`grep -n 'CodebaseField' src/MUI.Crawler/Crawl/FieldObservations.cs` — `I3Description` uses it, so it
exists, but confirm the spelling rather than trusting this plan.

- [ ] **Step 4: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Crawler.Tests </dev/null`
Expected: PASS, all seven `AresCycleTests`, and the existing `I3CycleTests` still green after the
fake lift.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Crawler/Ares tests/MUI.Crawler.Tests
git commit -m "Turn one AresCentral fetch into addresses, fields and a delisting date"
```

---

### Task 6: `AresService`, its options, and the wiring

**Files:**
- Create: `src/MUI.Crawler/Ares/AresService.cs`
- Modify: `src/MUI.Crawler/Advisory/AdvisoryLease.cs` (add `AresKey`)
- Modify: `src/MUI.Crawler/CrawlerServiceCollectionExtensions.cs:244-258` and `:302`, `:335`
- Modify: `src/MUI.Web/Data/CrawlerSettings.cs`
- Modify: `compose.yaml`
- Test: `tests/MUI.Crawler.Tests/Ares/AresServiceOptionsTests.cs`

**Interfaces:**
- Consumes: `AresCycle`, `AresCycleResult` (Task 5); `AresOptions`, `IAresGames` (Task 3);
  `IAresListingRepository` (Task 4); `LeasedBackgroundService`.
- Produces:
  - `sealed record AresServiceOptions { bool Enabled; long AdvisoryLockKey; TimeSpan Interval; TimeSpan LeaseRetryInterval; AresOptions Hub; void Validate(); }`
  - `sealed class AresService(...) : LeasedBackgroundService`
  - `AdvisoryLease.AresKey`
  - `CrawlerOptionsBuilder.Ares`, `CrawlerOptions.Ares`

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawler.Tests/Ares/AresServiceOptionsTests.cs`, mirroring
`tests/MUI.Crawler.Tests/I3/I3ServiceOptionsTests.cs`:

```csharp
using MUI.Ares;
using MUI.Crawler;

namespace MUI.Crawler.Tests;

public class AresServiceOptionsTests
{
    /// <summary>
    /// On by default, unlike I3. Joining I3 registers a name on somebody else's router permanently
    /// and must never be a side effect of `compose up`; a GET against a documented API with our own
    /// credentials registers nothing at all.
    /// </summary>
    [Test]
    public async Task ThePassIsOnByDefault()
    {
        await Assert.That(new AresServiceOptions().Enabled).IsTrue();
    }

    /// <summary>
    /// Refused at startup, beside the setting that caused it, rather than discovered as an
    /// authentication failure once an hour for ever.
    /// </summary>
    [Test]
    public async Task EnabledWithoutCredentialsIsRefusedAtStartup()
    {
        var options = new AresServiceOptions { Enabled = true };

        await Assert.That(() => options.Validate()).Throws<InvalidOperationException>();
    }

    /// <summary>A deployment that has turned the pass off is not asked for a key it will never use.</summary>
    [Test]
    public async Task DisabledWithoutCredentialsIsFine()
    {
        var options = new AresServiceOptions { Enabled = false };

        options.Validate();
    }

    [Test]
    public async Task ValidCredentialsAndPositiveIntervalsPass()
    {
        var options = new AresServiceOptions
        {
            Enabled = true,
            Hub = new AresOptions { ClientId = "muindex", ApiKey = "s3cret" },
        };

        options.Validate();
    }

    /// <summary>
    /// Its own lock, so a long crawl cycle cannot delay the hourly read and a deployment running
    /// with the crawler off still keeps its listings current.
    /// </summary>
    [Test]
    public async Task ThePassCompetesForItsOwnLock()
    {
        await Assert.That(new AresServiceOptions().AdvisoryLockKey).IsEqualTo(AdvisoryLease.AresKey);
        await Assert.That(AdvisoryLease.AresKey).IsNotEqualTo(AdvisoryLease.CrawlKey);
        await Assert.That(AdvisoryLease.AresKey).IsNotEqualTo(AdvisoryLease.I3Key);
        await Assert.That(AdvisoryLease.AresKey).IsNotEqualTo(AdvisoryLease.DnsClaimKey);
        await Assert.That(AdvisoryLease.AresKey).IsNotEqualTo(AdvisoryLease.PresenceMaintenanceKey);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `AresServiceOptions` and `AdvisoryLease.AresKey` not found.

- [ ] **Step 3: Add the lock key**

In `src/MUI.Crawler/Advisory/AdvisoryLease.cs`, after `DnsClaimKey`:

```csharp
    /// <summary>
    /// The AresCentral pass's key. <c>MUI_ARES</c>.
    /// </summary>
    /// <remarks>
    /// Its own key rather than the crawl lease's, for I3's reason: a long crawl cycle must not delay
    /// an hourly read of a hub, and a deployment running with the crawler off still has every reason
    /// to keep its listings current.
    /// </remarks>
    public const long AresKey = 0x4D55495F4152_4553L;
```

- [ ] **Step 4: Write the service and its options**

Create `src/MUI.Crawler/Ares/AresService.cs`:

```csharp
using Microsoft.Extensions.Logging;

using MUI.Ares;
using MUI.Catalog;
using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler;

/// <summary>What a deployment owns about the AresCentral pass.</summary>
public sealed record AresServiceOptions
{
    /// <summary>
    /// Whether this deployable runs the AresCentral pass. <b>On by default</b>, unlike I3's.
    /// </summary>
    /// <remarks>
    /// I3 is off by default because joining the network registers a name on somebody else's router
    /// permanently, and that must never happen as a side effect of <c>compose up</c>. This is a GET
    /// against a documented API with credentials a deployment either has or does not; it registers
    /// nothing, and a deployment with no key never runs it because <see cref="Validate"/> says so.
    /// </remarks>
    public bool Enabled { get; init; } = true;

    /// <summary>Which advisory lock the pass competes for (spec §12).</summary>
    public long AdvisoryLockKey { get; init; } = AdvisoryLease.AresKey;

    /// <summary>
    /// How often a pass runs.
    /// </summary>
    /// <remarks>
    /// Hourly. The list moves on the order of days — a game is added when somebody launches one — so
    /// this is already far more often than the data changes, and the cost is one request.
    /// </remarks>
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>How long a replica that could not take the lease waits before asking again.</summary>
    public TimeSpan LeaseRetryInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Where the hub is, and what it expects us to present.</summary>
    public AresOptions Hub { get; init; } = new();

    public void Validate()
    {
        if (Interval <= TimeSpan.Zero || LeaseRetryInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The AresCentral pass needs a positive interval and lease retry interval.");
        }

        if (Enabled)
        {
            // Refused at startup rather than discovered as a 401 once an hour for ever.
            Hub.Validate();
        }
    }
}

/// <summary>
/// The AresCentral pass, as an in-process <c>BackgroundService</c> gated on a Postgres advisory lock
/// (spec §12).
/// </summary>
/// <remarks>
/// The same shape as <see cref="I3Service"/> and for the same reason: N web replicas must run exactly
/// one of these, or the hub gets asked N times an hour by a site that promised to be polite.
/// </remarks>
public sealed class AresService(
    NpgsqlDataSource source,
    IAresGames hub,
    ICrawlTargetRepository targets,
    IAresListingRepository listings,
    IGameFieldStore fields,
    AresServiceOptions options,
    TimeProvider time,
    ILogger<AresService> logger,
    ILoggerFactory? loggers = null)
    : LeasedBackgroundService(source, options.AdvisoryLockKey, options.LeaseRetryInterval, time, logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("The AresCentral pass is disabled in configuration");
            return;
        }

        options.Validate();

        await RunLeaseLoopAsync(stoppingToken);
    }

    protected override async Task<TimeSpan> RunPassAsync(CancellationToken stoppingToken)
    {
        var result = await new AresCycle(
                hub, targets, listings, fields, Time, loggers?.CreateLogger<AresCycle>())
            .RunAsync(stoppingToken);

        logger.LogInformation("AresCentral pass complete: {Result}", result);

        return options.Interval;
    }

    protected override string LeaseLostMessage => "The AresCentral lease was lost; asking again";

    protected override string LeaseWaitingMessage =>
        "Another replica holds the AresCentral lease; this one will keep asking";

    /// <remarks>
    /// The commonest failures are a credential problem and the hub being down, and neither is a
    /// reason to take the web tier with it.
    /// </remarks>
    protected override string FailureMessage =>
        "The AresCentral pass failed; retrying after the lease interval";
}
```

- [ ] **Step 5: Wire it up**

In `src/MUI.Crawler/CrawlerServiceCollectionExtensions.cs`, after the `options.I3.Enabled` block:

```csharp
        // Gated on Enabled like the I3 pass, but on by default rather than off: this needs no
        // sidecar, only a credential pair. A deployment without one leaves Enabled false and the
        // pass never runs; a deployment with one gets it without opting in twice.
        if (options.Ares.Enabled)
        {
            services.AddSingleton(options.Ares);

            // A typed client through the factory, never a constructed HttpClient: the factory is what
            // bounds the handler's lifetime. Redirects off — a redirect is a second address nobody
            // ruled on.
            services.AddHttpClient<IAresGames, AresGamesClient>(client =>
                {
                    client.BaseAddress = options.Ares.Hub.BaseAddress;
                    client.Timeout = options.Ares.Hub.Timeout;
                })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                });

            services.TryAddSingleton<IAresListingRepository>(s => new NpgsqlAresListingRepository(
                s.GetRequiredService<NpgsqlDataSource>()));
            services.AddHostedService<AresService>();
        }
```

`AddHttpClient<IAresGames, AresGamesClient>` resolves `AresGamesClient`'s `AresOptions` parameter from
the container; the `services.AddSingleton(options.Ares)` above registers `AresServiceOptions`, not
`AresOptions`, so add `services.AddSingleton(options.Ares.Hub);` as well.

On `CrawlerOptionsBuilder`, beside `I3`:

```csharp
    /// <summary>
    /// The AresCentral pass. On unless a deployment turns it off, but it validates its credentials at
    /// startup, so a deployment with no key must turn it off explicitly.
    /// </summary>
    public AresServiceOptions Ares { get; set; } = new();
```

and `Ares = Ares,` in `Build()`, and `public AresServiceOptions Ares { get; init; } = new();` on
`CrawlerOptions`. Add `Ares.Validate();` wherever `CrawlerOptions` validates itself — find it with
`grep -n 'Validate' src/MUI.Crawler/CrawlerOptions.cs`.

- [ ] **Step 6: Read the environment**

In `src/MUI.Web/Data/CrawlerSettings.cs`, beside the I3 constants:

```csharp
    public const string AresEnabledEnvironmentVariable = "MUI_ARES_ENABLED";

    public const string AresEnabledConfigurationKey = "Crawler:Ares:Enabled";

    public const string AresClientIdEnvironmentVariable = "MUI_ARES_CLIENT_ID";

    public const string AresClientIdConfigurationKey = "Crawler:Ares:ClientId";

    public const string AresApiKeyEnvironmentVariable = "MUI_ARES_API_KEY";

    public const string AresApiKeyConfigurationKey = "Crawler:Ares:ApiKey";
```

and an `ApplyAres(builder, configuration)` beside `ApplyI3`, following its exact shape: read the three
values, parse `Enabled` with the same `bool.TryParse`-or-throw wording, set
`builder.Ares = builder.Ares with { Hub = builder.Ares.Hub with { ClientId = …, ApiKey = … } }`, and
finish with `builder.Ares.Validate();`.

**Default the pass off when no credentials are configured**, so a fresh clone and every existing
deployment start unchanged:

```csharp
        // On by default in the options record, but a deployment that never received credentials is
        // not opting in by omission. Turned off here rather than by defaulting Enabled to false, so
        // the record still says what the intended state is once a key exists.
        if (string.IsNullOrWhiteSpace(builder.Ares.Hub.ClientId)
            || string.IsNullOrWhiteSpace(builder.Ares.Hub.ApiKey))
        {
            builder.Ares = builder.Ares with { Enabled = false };
        }
```

Call `ApplyAres(builder, configuration);` beside the existing `ApplyI3(builder, configuration);`.

- [ ] **Step 7: Add the compose variables**

In `compose.yaml`, in the web service's `environment:` block beside `MUI_I3_API_KEY`:

```yaml
      MUI_ARES_CLIENT_ID: ${MUI_ARES_CLIENT_ID:-}
      MUI_ARES_API_KEY: ${MUI_ARES_API_KEY:-}
```

Do not add them to the I3 sidecar service.

- [ ] **Step 8: Run the tests**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawler.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Web.Tests </dev/null
```
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src compose.yaml tests
git commit -m "Run the AresCentral pass hourly under its own lease"
```

---

### Task 7: `mui-crawl --ares`

**Files:**
- Modify: `src/MUI.Crawler.Cli/Arguments.cs:34-38,118-122,191-201`
- Modify: `src/MUI.Crawler.Cli/Program.cs:107-146`
- Create: `src/MUI.Crawler.Cli/AresSummary.cs`
- Test: `tests/MUI.Crawler.Tests/CrawlSeedParsingTests.cs` (append)

**Interfaces:**
- Consumes: `AresCycle`, `AresServiceOptions`, `AresGamesClient`, `NpgsqlAresListingRepository`.
- Produces: `Arguments.Ares` (`bool`), `Arguments.AresClientId` / `Arguments.AresKey` (`string?`),
  `AresSummary.PrintAsync(NpgsqlDataSource)`.

- [ ] **Step 1: Write the failing test**

Append to `tests/MUI.Crawler.Tests/CrawlSeedParsingTests.cs` (match the file's existing idiom for
invoking the parser — read it first):

```csharp
[Test]
public async Task TheAresFlagAsksForOnePassOverTheHub()
{
    var parsed = Arguments.Parse(["--ares"]);

    await Assert.That(parsed.Ares).IsTrue();
}

/// <summary>
/// Credentials come from the environment by default, so an operator inside the deployment does not
/// paste a key into their shell history to run one pass.
/// </summary>
[Test]
public async Task AresCredentialsDefaultToTheEnvironment()
{
    var parsed = Arguments.Parse(["--ares", "--ares-client-id", "muindex", "--ares-key", "s3cret"]);

    await Assert.That(parsed.AresClientId).IsEqualTo("muindex");
    await Assert.That(parsed.AresKey).IsEqualTo("s3cret");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `Arguments` has no `Ares`.

- [ ] **Step 3: Add the flags**

In `src/MUI.Crawler.Cli/Arguments.cs`, in the help text beside the `--i3` lines:

```
          --ares                  Run one AresCentral pass instead of a crawl cycle: read the
                                  hub's games list, seed new addresses, record what it says.
          --ares-client-id <s>    The client id AresCentral issued. Defaults to $MUI_ARES_CLIENT_ID.
          --ares-key <string>     The key that goes with it. Defaults to $MUI_ARES_API_KEY.
```

the properties:

```csharp
    public bool Ares { get; init; }

    public string? AresClientId { get; init; } =
        Environment.GetEnvironmentVariable("MUI_ARES_CLIENT_ID");

    public string? AresKey { get; init; } = Environment.GetEnvironmentVariable("MUI_ARES_API_KEY");
```

and the switch arms beside `--i3`:

```csharp
                case "--ares":
                    parsed = parsed with { Ares = true };
                    continue;

                case "--ares-client-id":
                    parsed = parsed with { AresClientId = Next(args, ref i, "--ares-client-id") };
                    continue;

                case "--ares-key":
                    parsed = parsed with { AresKey = Next(args, ref i, "--ares-key") };
                    continue;
```

Match the surrounding arms' `continue`/`break` convention exactly rather than copying this block
blind.

- [ ] **Step 4: Run the pass from `Program.cs`**

After the `if (arguments.I3) { … }` block in `src/MUI.Crawler.Cli/Program.cs`:

```csharp
if (arguments.Ares)
{
    var aresOptions = new AresServiceOptions
    {
        Hub = new AresOptions
        {
            ClientId = arguments.AresClientId ?? "",
            ApiKey = arguments.AresKey ?? "",
        },
    };

    aresOptions.Validate();

    // The one place in this tree that builds an HttpClient by hand rather than through the factory,
    // and it is a process that exits after one request. The handler's lifetime is the program's.
    using var handler = new HttpClientHandler { AllowAutoRedirect = false };
    using var http = new HttpClient(handler)
    {
        BaseAddress = aresOptions.Hub.BaseAddress,
        Timeout = aresOptions.Hub.Timeout,
    };

    var aresResult = await new AresCycle(
            new AresGamesClient(http, aresOptions.Hub, loggerFactory.CreateLogger<AresGamesClient>()),
            new NpgsqlCrawlTargetRepository(source),
            new NpgsqlAresListingRepository(source),
            new NpgsqlGameFieldStore(source),
            TimeProvider.System,
            loggerFactory.CreateLogger<AresCycle>())
        .RunAsync();

    Console.WriteLine($"ares pass     {aresResult}");

    await AresSummary.PrintAsync(source);

    return 0;
}
```

Match the I3 block's exact conventions for obtaining `source`, `loggerFactory`, the field store's
type name and the exit path — read lines 100–150 of that file and copy them rather than trusting this
plan's guesses at the names.

- [ ] **Step 5: Write the summary**

Create `src/MUI.Crawler.Cli/AresSummary.cs`, modelled on `src/MUI.Crawler.Cli/I3Summary.cs`. Print
three lines: how many listings are held and how many are bound to a game; how many addresses in the
registry came from the hub (`SELECT count(*) FROM crawl_target WHERE discovered_via = 'ares_central'`);
and how many are currently delisted. Read `I3Summary.cs` for the column widths and phrasing.

- [ ] **Step 6: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Crawler.Tests </dev/null`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Crawler.Cli tests
git commit -m "Force one AresCentral pass from mui-crawl"
```

---

### Task 8: "First seen via" on the game page

**Files:**
- Modify: `src/MUI.Catalog/Views.cs` (`GamePage`, add `DiscoveredVia`)
- Modify: `src/MUI.Catalog/Persistence/Queries/NpgsqlGameQueries.*.cs` (the game-page read)
- Modify: `src/MUI.Web/Fixtures/FixtureGameQueries.cs`
- Create: `src/MUI.Web/Components/Text/Discovery.cs`
- Modify: `src/MUI.Web/Components/Pages/Game.razor` (after the endpoint loop, ~line 116)
- Modify: all five `src/MUI.Web/Resources/Messages*.resx`
- Test: `tests/MUI.Web.Tests/` — add `GameDiscoveryLineTests.cs`

**Interfaces:**
- Consumes: `DiscoverySource` (Task 2).
- Produces: `GamePage.DiscoveredVia` (`DiscoverySource?`, defaulted `null`, added last);
  `Discovery.FirstSeen(string tag, DiscoverySource source, string date)` → `string`.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Web.Tests/GameDiscoveryLineTests.cs`. Read an existing `MUI.Web.Tests` component
test first for the bUnit idiom, and note the two-harness rule: component tests live in **both**
`MUI.Web.Tests` and `MUI.Tests.BUnit` in some repos — check whether this repo has a second harness
with `ls tests` and mirror the test there too if it does.

```csharp
/// <summary>
/// A dated statement about our crawl, never a badge on the game.
/// </summary>
public class GameDiscoveryLineTests
{
    /// <summary>
    /// The sentence names the channel and the date together. "First seen via AresCentral" alone
    /// would read as an origin claim; with the date it is what it is — when this site found out.
    /// </summary>
    [Test]
    public async Task TheLineNamesTheChannelAndTheDate()
    {
        var line = Discovery.FirstSeen("en", DiscoverySource.AresCentral, "22 August 2026");

        await Assert.That(line).Contains("AresCentral");
        await Assert.That(line).Contains("22 August 2026");
    }

    /// <summary>
    /// §7.6: the backfill read several directories and recorded which one supplied a given address
    /// nowhere. Naming one here would be the accident-as-fact this whole design avoids.
    /// </summary>
    [Test]
    public async Task TheBackfillNamesNoDirectory()
    {
        var line = Discovery.FirstSeen("en", DiscoverySource.Backfill, "30 July 2026");

        await Assert.That(line).DoesNotContain("MudStats");
        await Assert.That(line).DoesNotContain("Mud Connector");
    }

    /// <summary>Every source has a sentence; a missing one must fail loudly rather than print a member name.</summary>
    [Test]
    [Arguments(DiscoverySource.OperatorSeed)]
    [Arguments(DiscoverySource.Submission)]
    [Arguments(DiscoverySource.Referral)]
    [Arguments(DiscoverySource.I3Mudlist)]
    [Arguments(DiscoverySource.AresCentral)]
    [Arguments(DiscoverySource.Backfill)]
    public async Task EverySourceHasItsOwnSentence(DiscoverySource source)
    {
        var line = Discovery.FirstSeen("en", source, "22 August 2026");

        await Assert.That(line).IsNotNullOrWhitespace();
        await Assert.That(line).DoesNotContain(source.ToString());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `Discovery` not found.

- [ ] **Step 3: Write the wording helper**

Create `src/MUI.Web/Components/Text/Discovery.cs`:

```csharp
using MUI.Discovery;

namespace MUI.Web.Components;

/// <summary>
/// How a game came to be here, as a reader should see it written.
/// </summary>
/// <remarks>
/// <b>Every sentence here is about this site, not about the game.</b> "First seen via AresCentral on
/// 22 August 2026" says when we found out and through which channel; it does not say the game
/// originated there, does not say it is listed only there, and must never be shortened to a badge
/// reading "AresCentral". §7.6 rejected an origin field precisely because any game worth listing
/// appears in several directories, and the date is what keeps this honest.
/// <para>
/// One message id per source rather than one template with a noun slotted in: a submission is
/// somebody handing us an address, a referral is another game's list naming it, and a backfill is a
/// pile of directories we cannot attribute individually. Those are three different sentences in
/// English and more in other languages.
/// </para>
/// </remarks>
public static class Discovery
{
    public static string FirstSeen(string tag, DiscoverySource source, string date)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(date);

        return Messages.For(
            tag,
            source switch
            {
                DiscoverySource.OperatorSeed => "game.firstSeen.operatorSeed",
                DiscoverySource.Submission => "game.firstSeen.submission",
                DiscoverySource.Referral => "game.firstSeen.referral",
                DiscoverySource.I3Mudlist => "game.firstSeen.i3Mudlist",
                DiscoverySource.AresCentral => "game.firstSeen.aresCentral",
                DiscoverySource.Backfill => "game.firstSeen.backfill",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(source),
                    source,
                    "No sentence for this discovery source. Add one rather than letting ToString answer."),
            },
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["date"] = date });
    }
}
```

Check `Messages.For`'s overload for argument dictionaries against
`src/MUI.Web/Components/Text/Provenance.cs` and `AboutPage.cs`'s `Point` helper, and match whichever
it actually is.

- [ ] **Step 4: Add the six message ids to all five bundles**

`src/MUI.Web/Resources/Messages.resx`:

```xml
  <data name="game.firstSeen.operatorSeed" xml:space="preserve">
    <value>First seen on {date}, from this site's own configured list.</value>
    <comment>how this site first learned of a game; a fact about our crawl, never about the game</comment>
  </data>
  <data name="game.firstSeen.submission" xml:space="preserve">
    <value>First seen on {date}, submitted through this site.</value>
    <comment>how this site first learned of a game; a fact about our crawl, never about the game</comment>
  </data>
  <data name="game.firstSeen.referral" xml:space="preserve">
    <value>First seen on {date}, named by another game's list.</value>
    <comment>how this site first learned of a game; a fact about our crawl, never about the game</comment>
  </data>
  <data name="game.firstSeen.i3Mudlist" xml:space="preserve">
    <value>First seen on {date}, listed on the I3 mudlist.</value>
    <comment>how this site first learned of a game; I3 is a protocol name and stays as written</comment>
  </data>
  <data name="game.firstSeen.aresCentral" xml:space="preserve">
    <value>First seen on {date}, listed on AresCentral.</value>
    <comment>how this site first learned of a game; AresCentral is a proper noun and stays as written</comment>
  </data>
  <data name="game.firstSeen.backfill" xml:space="preserve">
    <value>First seen on {date}, in this site's day-one list of addresses.</value>
    <comment>names no directory on purpose: which one supplied a given address was never recorded</comment>
  </data>
```

Translate all six into `Messages.de.resx`, `Messages.ja.resx`, `Messages.nl.resx` and
`Messages.zh-Hans.resx`, keeping `{date}`, `AresCentral` and `I3` verbatim.

- [ ] **Step 5: Carry the value to the page**

`GamePage` in `src/MUI.Catalog/Views.cs` gains, as the **last** parameter so existing positional
constructions still compile:

```csharp
    // How this site first learned of the game (migration 0033). Null for every game listed before
    // the column existed, which renders no line at all — a guess would be worse than silence.
    DiscoverySource? DiscoveredVia = null)
```

Add `discovered_via` to the game-page SELECT and map it through `DiscoverySources.From(...)`. Find the
query with `grep -rn 'GamePage(' src/MUI.Catalog/Persistence/Queries/`. Add a value to
`FixtureGameQueries` for one fixture game so the demo site exercises the line.

- [ ] **Step 6: Render the line**

In `src/MUI.Web/Components/Pages/Game.razor`, immediately after the `@foreach` over `Page.Endpoints`
closes (around line 116):

```razor
                @* How we came to know about this game, and when. A statement about this site's own
                   crawl, deliberately dated — a source name on its own would read as a claim about
                   where the game came from, which is not a thing we know. Absent for every game
                   listed before the column existed, and silence is correct there. *@
                @if (Page.DiscoveredVia is { } via)
                {
                    <p class="faint discovery">
                        @Discovery.FirstSeen(
                            Http.LocaleOf().Tag,
                            via,
                            Dates.Absolute(Http.LocaleOf().Tag, Page.Summary.FirstSeenAt))
                    </p>
                }
```

`GameSummary` may not carry `FirstSeenAt`; check with `grep -n 'FirstSeenAt' src/MUI.Catalog/Views.cs`
and, if it does not, add the date to `GamePage` alongside `DiscoveredVia` rather than reaching for
another record.

- [ ] **Step 7: Run the tests**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Web.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
```
Expected: PASS. There is an untranslated-string sweep in this repo
(`docs/2026-08-17-untranslated-sweep.md`); if a test enforces it, all five bundles must carry all six
ids or that test fails.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "Say when this site first saw a game, and through which channel"
```

---

### Task 9: Credit AresCentral on /about

**Files:**
- Modify: `src/MUI.Web/Components/Pages/AboutPage.cs`
- Modify: `src/MUI.Web/Components/Pages/About.razor`
- Modify: all five `src/MUI.Web/Resources/Messages*.resx`
- Test: `tests/MUI.Web.Tests/` — add to the existing about-page test file if there is one

**Context the implementer needs:** PR #134 removed the Sources and Licence sections from `/about`
and deleted `docs/import-sources.md`, on the grounds that the section duplicated that doc and
surfaced an unsettled licence question. The section is therefore **gone from `main`** and this task
rebuilds a smaller one. It credits **only sources this site reads on a standing basis** — AresCentral
and Intermud-3 — and not the historical backfill directories, whose crediting doc no longer exists
and whose licence question is what got the old section removed. Do not reintroduce `ImportSource`,
`ImportSourceState` or `AboutLicence`.

**Interfaces:**
- Consumes: nothing.
- Produces: `sealed record AboutFeed(string Name, string Url, string Note)`;
  `AboutSection.Feeds` (`IReadOnlyList<AboutFeed>`, defaulted `[]`).

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>
/// §7.6's etiquette clause: credit every source we read. AresCentral is read continuously with
/// credentials its maintainer issued, which is the clause working rather than an exception to it.
/// </summary>
[Test]
public async Task TheAboutPageCreditsEverySourceWeStandinglyRead()
{
    var page = AboutPage.For(new ProbeOptions());

    var credited = page.Sections.SelectMany(s => s.Feeds).Select(f => f.Name).ToList();

    await Assert.That(credited).Contains("AresCentral");
    await Assert.That(credited).Contains("Intermud-3");
}

/// <summary>
/// The dead backfill directories are not read and are not credited here. Their crediting doc was
/// deleted with the section that duplicated it (#134), and reviving a stale list of sites we no
/// longer fetch would state something untrue on a page whose whole point is not doing that.
/// </summary>
[Test]
public async Task TheBackfillDirectoriesAreNotListedAsSourcesWeRead()
{
    var page = AboutPage.For(new ProbeOptions());

    var credited = page.Sections.SelectMany(s => s.Feeds).Select(f => f.Name).ToList();

    await Assert.That(credited).DoesNotContain("MudStats");
    await Assert.That(credited).DoesNotContain("The Mud Connector");
}
```

Match `AboutPage.For`'s real signature — read the top of `AboutPage.cs` first; it took
`(ProbeOptions, DatasetLicenceOptions, string tag)` before #134 and will have changed.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `AboutSection` has no `Feeds`.

- [ ] **Step 3: Add the record and the section**

In `AboutPage.cs`:

```csharp
/// <summary>
/// One source this site reads on a standing basis, credited by name.
/// </summary>
/// <remarks>
/// Standing, not historical. This lists what we are reading now — a hub or a network that answers us
/// on a schedule — and not the directories the one-time day-one backfill took addresses from, which
/// are not fetched, cannot be re-fetched from this tree, and whose crediting doc was deleted with the
/// section that duplicated it. A credit for something we stopped doing is a claim about the present
/// that is not true.
/// </remarks>
public sealed record AboutFeed(string Name, string Url, string Note);
```

On `AboutSection`, beside the other structured blocks:

```csharp
    /// <summary>The standing sources credited under this section.</summary>
    public IReadOnlyList<AboutFeed> Feeds { get; init; } = [];
```

A new section, registered in `Sections` beside the crawler one:

```csharp
    private static AboutSection Reading(string tag) => new(
        "reading",
        Say(tag, "about.reading.heading"),
        [
            Point(tag, "about.reading.standing"),
            Point(tag, "about.reading.measured"),
        ])
    {
        // Names and addresses are these projects' own and are never translated; what each gives us
        // is our sentence about them, and so is a message.
        Feeds =
        [
            new("AresCentral", "https://arescentral.aresmush.com/",
                Say(tag, "about.feed.aresCentral.note")),
            new("Intermud-3", "https://www.intermud.org/",
                Say(tag, "about.feed.intermud3.note")),
        ],
    };
```

- [ ] **Step 4: Add the message ids to all five bundles**

`Messages.resx` (translate all four into the other four bundles):

```xml
  <data name="about.reading.heading" xml:space="preserve">
    <value>What we read</value>
    <comment>heading of the /about section crediting standing sources</comment>
  </data>
  <data name="about.reading.standing.lead" xml:space="preserve">
    <value>Two places tell us games exist.</value>
    <comment>lead half of a point; the .body half completes the same sentence</comment>
  </data>
  <data name="about.reading.standing.body" xml:space="preserve">
    <value>Both are read on a schedule, with permission, and both are credited below. Everything else here is an address this crawler found by probing.</value>
  </data>
  <data name="about.reading.measured.lead" xml:space="preserve">
    <value>What they tell us is labelled as theirs.</value>
  </data>
  <data name="about.reading.measured.body" xml:space="preserve">
    <value>A name or a genre one of them holds is shown as something that source says, never as something we measured. Where our own probe disagrees, the disagreement is what you see.</value>
  </data>
  <data name="about.feed.aresCentral.note" xml:space="preserve">
    <value>The AresMUSH community's own hub. Its maintainer issued us API credentials, and we read its games list hourly: the addresses, and what each game says about itself there.</value>
    <comment>what one standing source gives us; AresMUSH and AresCentral are proper nouns</comment>
  </data>
  <data name="about.feed.intermud3.note" xml:space="preserve">
    <value>A network many LP-family games are joined to. We take its mudlist of addresses, and ask games that opt in how many people are connected.</value>
    <comment>what one standing source gives us; I3 is a protocol name</comment>
  </data>
```

- [ ] **Step 5: Render the block**

In `About.razor`, inside the loop over `section.Points`, after the points, following the existing
pattern for how a section's structured extras render (read the `Identity` block for the shape):

```razor
                @if (section.Feeds.Count > 0)
                {
                    <ul class="feeds">
                        @foreach (var feed in section.Feeds)
                        {
                            <li>
                                <a href="@feed.Url" rel="noopener">@feed.Name</a>
                                — @feed.Note
                            </li>
                        }
                    </ul>
                }
```

- [ ] **Step 6: Run the tests**

Run: `dotnet build MUIndex.slnx -c Release && dotnet run -c Release --no-build --project tests/MUI.Web.Tests </dev/null`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "Credit the two sources this site reads on a schedule"
```

---

### Task 10: Amend the agent brief and the deploy notes

**Files:**
- Modify: `CLAUDE.md` (the two Never entries, and the HTTP client carve-out section)
- Modify: `docs/deploy.md`
- Modify: `README.md` if it enumerates the projects

**Interfaces:** none. Documentation only.

- [ ] **Step 1: Amend the "Import a value, or record where a game came from" entry**

Replace that bullet in `CLAUDE.md`'s **Never** list with:

```markdown
- **Import a value from a one-time scrape, or record a game's origin as a fact about the game.**
  Both halves are narrower than they were, and the narrowing is deliberate. The **backfill** still
  takes host and port and nothing else (spec §7.6): no `imported_measured`/`imported_asserted` field
  source, no imported presence or availability row, no half-weight archive grace. A **standing,
  authenticated source** may write values, under its own weak rung, because it is not the thing §7.6
  argued against — `i3_mudlist` (migration 0023) and `ares_central` (migration 0032) are the two, both
  below `mssp`, both far below `staff`. And `discovered_via` (migration 0033) records **which channel
  first told this site about an address, and when** — a dated statement about our own crawl. It may
  never be rendered as a badge, shortened to a source name, or read as exclusivity: any game worth
  listing appears in several places, so the column names whichever channel reached us first and
  nothing more. `IntervalOrigin` survives as a one-member enum on purpose — an undifferentiated total
  cannot be split back apart if another party's measurements are ever ingested.
```

- [ ] **Step 2: Extend the HTTP client carve-out**

Retitle the section `## The two HTTP clients, and why they are not the fetchers the rule above
forbids`, and add after the `IconFetcher` paragraphs:

```markdown
`AresGamesClient` reads the AresCentral games list — one authenticated GET, hourly, against a
documented API whose maintainer issued us credentials. **This is not the "no fetchers, no HTML parsers
for third-party sites" rule being bent either.** That rule is about one-time machinery pointed at
somebody's HTML, carried in CI for years after the fetch it existed for. This is a standing source
read for as long as the site runs, the same shape as the Intermud-3 gateway that is already here, and
§7.6's etiquette clause names asking for a documented API as the thing to do in preference to
scraping. There is no one-time import here and nothing belongs on `import/one-time`.

The same constraints apply and for the same reason — they are about how an `HttpClient` is held, not
about what is fetched: a typed client through `IHttpClientFactory` (**never** `new HttpClient()`),
`AllowAutoRedirect = false`, and the body read to a ceiling rather than trusted to `Content-Length`.
The URL here is ours and constant rather than owner-supplied, so the DNS-pinning argument is weaker
than `IconFetcher`'s, but the registration follows the same pattern so there is one way to do this.
```

- [ ] **Step 3: Document the environment variables**

In `docs/deploy.md`, wherever `MUI_I3_API_KEY` is documented, add `MUI_ARES_CLIENT_ID` and
`MUI_ARES_API_KEY`, stating that the pass stays off unless both are set, that credentials come from
AresCentral's maintainer, and that `docker compose run --entrypoint mui-crawl … --ares` forces one
pass.

- [ ] **Step 4: Verify the whole build and every suite**

Run:
```bash
dotnet build MUIndex.slnx -c Release
for s in Catalog Crawl Crawler Discovery Web; do
  dotnet run -c Release --no-build --project tests/MUI.$s.Tests </dev/null || echo "FAILED: $s"
done
dotnet run -c Release --no-build --project tests/MUI.I3.Tests </dev/null
```
Expected: every suite passes. Report the actual counts; do not claim green without the output.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md docs README.md
git commit -m "Say in the brief what the code now does"
```

---

## Self-Review

**Spec coverage.** §1 → Tasks 3–6. §2 field table → Task 5 Step 3 `Declared`. §2 precedence → Task 1.
§3 boundaries and the `IHttpClientFactory` rule → Task 3 Step 3, Task 6 Step 5. §4 one pass, all six
steps including the sweep guard → Task 5. §4 `last_ping` → Task 4 (stored) and Task 5 (`Row`, used
nowhere else). §5 three migrations → Tasks 1, 2, 4. §6 `DiscoverySource` and the five call sites →
Task 2; the page line → Task 8. §7 about page → Task 9. §8 credentials and CLI → Tasks 6 and 7. §9
errors → Task 6 (`FailureMessage`) and Task 5 (test `AFailedFetchWritesNothingAndDoesNotSweep`). §10
testing → the tests in Tasks 1–9. §11 brief amendments → Task 10. §12 out of scope: nothing reads
`status` as a lifecycle signal, and no task does.

**Known deviations from the spec, both deliberate.** The spec said the client's tests would live in
their own suite; Task 3 puts them in `MUI.Crawler.Tests` and states why. The spec said the about
credit joins an existing Sources section; that section was deleted in PR #134, so Task 9 rebuilds a
smaller one scoped to standing sources and says why.

**Placeholders.** None. Every code step carries the code. Three steps direct the implementer to read
a neighbouring file and match its idiom rather than reproducing it — `I3Summary.cs`'s column widths
(Task 7 Step 5), the Postgres fixture convention (Task 4 Step 1), and `About.razor`'s extras block
(Task 9 Step 5). Those are "copy the pattern next door", not "figure it out".

**Type consistency.** `DiscoverySource`/`DiscoverySources.ToDb`/`.From` are used with those names in
Tasks 2, 5, 7, 8. `IAresGames.ListAsync` is defined in Task 3 and consumed in Tasks 5 and 6.
`IAresListingRepository`'s four methods are defined in Task 4 and all four are used in Task 5's fake
and cycle. `AresOptions` vs `AresServiceOptions` stay distinct throughout, and Task 6 Step 5 flags
that both need registering. `FieldSource.AresCentral` (Task 1) is used in Task 5.

**Two risks the implementer must resolve rather than assume.** Task 2 Step 6 depends on whether
`MUI.Catalog` may reference `MUI.Discovery`; the step says to check and gives the alternative
placement. Task 8 Step 6 depends on whether `GameSummary` carries `FirstSeenAt`; the step says to
check and gives the alternative.
