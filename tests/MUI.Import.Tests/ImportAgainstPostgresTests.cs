using Dapper;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Import.Sources;
using MUI.Import.Tests.Support;

using Npgsql;

namespace MUI.Import.Tests;

/// <summary>
/// One whole import into a real PostgreSQL 17, from the recorded TinTin fixture to the rows it lands.
/// </summary>
/// <remarks>
/// The in-memory tests prove the pipeline's arithmetic; this proves the two things only a database
/// can answer. <c>availability_interval.origin</c> is a column with a <c>'first_party'</c> default —
/// so an import that reached for the wrong write path would look correct in every fake and credit a
/// third party's history at full weight in production — and the provenance sidecar's uniqueness is an
/// index, so "re-running the backfill changes nothing" is only true if the index says so.
/// </remarks>
public class ImportAgainstPostgresTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> SeedGameAsync(NpgsqlDataSource source, string host, int port)
    {
        var games = new NpgsqlGameStore(source);
        var endpoints = new NpgsqlEndpointStore(source);
        var gameId = Guid.NewGuid();

        await games.InsertAsync(new GameRecord(
            gameId, $"seed-{gameId:N}"[..20], "Alter Aeon", null, LifecycleState.Active, false, Now.AddYears(-1)));

        await endpoints.UpsertAsync(new GameEndpoint(
            gameId, host, port, EndpointKind.Telnet, Now.AddYears(-1), Now.AddDays(-1), EndpointState.Active));

        return gameId;
    }

    private static ImportPipeline PipelineFor(
        NpgsqlDataSource source,
        InMemoryCrawlTargetRepository targets,
        IImportProvenanceStore provenance) =>
        new(targets,
            new NpgsqlGameStore(source),
            new NpgsqlEndpointStore(source),
            new NpgsqlGameFieldStore(source),
            new NpgsqlPresenceStore(source),
            new ImportedAvailabilityWriter(new NpgsqlAvailabilityStore(source)),
            provenance,
            new ManualTimeProvider(Now));

    [Test]
    public async Task TheSidecarTableIsPartOfTheSchemaTheRunnerApplies()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var connection = await database.DataSource.OpenConnectionAsync();

        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'import_provenance')");

        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task TheWholeTinTinFixtureImportsIntoARealDatabase()
    {
        await using var database = await PostgresFixture.MigratedAsync();

        // One of the fixture's five games is already in the catalogue, reached by our own probe.
        var gameId = await SeedGameAsync(database.DataSource, "alteraeon.com", 3000);

        var targets = new InMemoryCrawlTargetRepository();
        var provenance = new NpgsqlImportProvenanceStore(database.DataSource);
        var pipeline = PipelineFor(database.DataSource, targets, provenance);

        var etiquette = TinTinMsspCrawlerSource.DefaultEtiquette();
        var (_, client) = FakeHttp.Serving(
            (etiquette.RobotsUri.AbsoluteUri, "User-agent: *\nCrawl-delay: 0\n"),
            (etiquette.BulkExportUri!.AbsoluteUri, Fixture.Read("tintin-mssp-mudlist.html")));

        var fetcher = new DirectoryFetcher(client, etiquette, new ManualTimeProvider(Now));
        await fetcher.PrimeRobotsAsync(CancellationToken.None);

        var report = await pipeline.RunAsync(new TinTinMsspCrawlerSource(fetcher), CancellationToken.None);

        await Assert.That(report.GamesSeen).IsEqualTo(5);
        await Assert.That(report.Matched).IsEqualTo(1);
        await Assert.That(report.TargetsAdded).IsGreaterThan(5);
        await Assert.That(report.FieldsWritten).IsGreaterThan(0);
        await Assert.That(report.PresenceRows).IsEqualTo(1);

        // Everything landed under the imported source, and nothing pretended to be ours.
        await using var connection = await database.DataSource.OpenConnectionAsync();

        var sources = (await connection.QueryAsync<string>(
            "SELECT DISTINCT source FROM game_field WHERE game_id = @gameId", new { gameId })).ToList();

        await Assert.That(sources).IsEquivalentTo(new[] { "imported_measured" });

        var presenceSource = await connection.ExecuteScalarAsync<string>(
            "SELECT source FROM presence_sample WHERE game_id = @gameId", new { gameId });

        await Assert.That(presenceSource).IsEqualTo("imported_measured");
    }

    [Test]
    public async Task AnImportedReachableSpanIsStoredWithTheOriginThatEarnsItHalfWeight()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var gameId = await SeedGameAsync(database.DataSource, "anachronism.example", 4000);

        var targets = new InMemoryCrawlTargetRepository();
        var provenance = new NpgsqlImportProvenanceStore(database.DataSource);
        var pipeline = PipelineFor(database.DataSource, targets, provenance);

        var record = new ImportedGame
        {
            SourceName = "MudStats",
            SourceKey = "anachronism",
            Name = "Anachronism",
            Endpoints = [new ImportedEndpoint("anachronism.example", 4000, EndpointKind.Telnet)],
            Availability = [new ImportedAvailability(Now.AddYears(-4), Now, true)],
        };

        await pipeline.RunAsync(
            new FakeSource("MudStats", ImportTier.Measured, FakeSource.ExportEtiquette("MudStats"), [record]),
            CancellationToken.None);

        await using var connection = await database.DataSource.OpenConnectionAsync();

        var origin = await connection.ExecuteScalarAsync<string>(
            "SELECT origin FROM availability_interval WHERE game_id = @gameId", new { gameId });

        // 'first_party' is this column's DEFAULT, so getting it wrong is silent everywhere except
        // here — and it is exactly the half of §7.5 that decides whether somebody else's decade
        // counts as ours.
        await Assert.That(origin).IsEqualTo("imported_measured");

        // Read back the way ArchiveSweeper reads it, and weighted the way ArchivePolicy weights it.
        var history = new NpgsqlAvailabilityStore(database.DataSource);
        var ours = await history.CumulativeReachableAsync(gameId, Now);
        var imported = await history.CumulativeImportedMeasuredReachableAsync(gameId, Now);

        await Assert.That(ours).IsEqualTo(TimeSpan.Zero);
        await Assert.That(imported).IsGreaterThan(TimeSpan.FromDays(1400));
        await Assert.That(ArchivePolicy.GraceFor(TimeSpan.Zero, imported))
            .IsEqualTo(ArchivePolicy.GraceFor(imported / 2));
    }

    [Test]
    public async Task RunningTheSameImportTwiceChangesNothingTheSecondTime()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var gameId = await SeedGameAsync(database.DataSource, "anachronism.example", 4000);

        var targets = new InMemoryCrawlTargetRepository();
        var provenance = new NpgsqlImportProvenanceStore(database.DataSource);
        var pipeline = PipelineFor(database.DataSource, targets, provenance);

        var record = new ImportedGame
        {
            SourceName = "MudStats",
            SourceKey = "anachronism",
            Name = "Anachronism",
            SourceUri = new Uri("https://mudstats.com/World/Anachronism"),
            Endpoints = [new ImportedEndpoint("anachronism.example", 4000, EndpointKind.Telnet)],
            Fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["GENRE"] = "Fantasy" },
            Presence = [new ImportedPresence(Now.AddHours(-2), 17)],
            Availability = [new ImportedAvailability(Now.AddYears(-2), Now.AddDays(-1), true)],
        };

        var source = new FakeSource(
            "MudStats", ImportTier.Measured, FakeSource.ExportEtiquette("MudStats"), [record]);

        await pipeline.RunAsync(source, CancellationToken.None);
        var second = await pipeline.RunAsync(source, CancellationToken.None);

        await Assert.That(second.PresenceRows).IsEqualTo(0);
        await Assert.That(second.AvailabilityRows).IsEqualTo(0);
        await Assert.That(second.FieldsWritten).IsEqualTo(0);

        await using var connection = await database.DataSource.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM presence_sample WHERE game_id = @gameId", new { gameId }))
            .IsEqualTo(1);
        await Assert.That(await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM availability_interval WHERE game_id = @gameId", new { gameId }))
            .IsEqualTo(1);
        await Assert.That(await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM field_change WHERE game_id = @gameId", new { gameId }))
            .IsEqualTo(0);
    }

    [Test]
    public async Task TheSchemaItselfRefusesHistoryFromAnAssertedSource()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var gameId = await SeedGameAsync(database.DataSource, "anachronism.example", 4000);

        await using var connection = await database.DataSource.OpenConnectionAsync();

        // The code cannot produce this row — AssertedHistorySink holds nothing it could write with —
        // so the CHECK is the second lock, on the path a hand-written INSERT would take.
        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO import_provenance
                    (game_id, subject_kind, subject_at, source_name, source_key, tier, imported_at)
                VALUES (@gameId, 'presence', @at, 'The MUD Connector', 'x', 'imported_asserted', @at)
                """,
                new { gameId, at = Now }))
            .Throws<PostgresException>();
    }

    [Test]
    public async Task TheAboutPagesAttributionCountsWhatActuallyLanded()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await SeedGameAsync(database.DataSource, "anachronism.example", 4000);

        var targets = new InMemoryCrawlTargetRepository();
        var provenance = new NpgsqlImportProvenanceStore(database.DataSource);
        var pipeline = PipelineFor(database.DataSource, targets, provenance);

        var record = new ImportedGame
        {
            SourceName = "MudStats",
            SourceKey = "anachronism",
            Name = "Anachronism",
            Endpoints = [new ImportedEndpoint("anachronism.example", 4000, EndpointKind.Telnet)],
            Fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["GENRE"] = "Fantasy" },
        };

        await pipeline.RunAsync(
            new FakeSource("MudStats", ImportTier.Measured, FakeSource.ExportEtiquette("MudStats"), [record]),
            CancellationToken.None);

        var contribution = (await provenance.ContributionsAsync()).Single();

        await Assert.That(contribution.SourceName).IsEqualTo("MudStats");
        await Assert.That(contribution.Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(contribution.Values).IsEqualTo(2);
        await Assert.That(contribution.LastImportedAt).IsEqualTo(Now);
    }
}
