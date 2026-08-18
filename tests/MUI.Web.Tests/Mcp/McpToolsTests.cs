using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler;
using MUI.Crawler.Persistence;
using MUI.Discovery;
using MUI.Web.Data;
using MUI.Web.Mcp;
using MUI.Web.Tests.Support;

using Npgsql;

namespace MUI.Web.Tests.Mcp;

/// <summary>
/// Each of the seven <see cref="MuiMcpTools"/> tools, called over the real MCP transport against a
/// real Postgres — the properties under test are the ones a caller cannot see from the tool's C#
/// alone: what actually landed in the database, and what the tool refuses.
/// </summary>
public class McpToolsTests
{
    private const string Token = "test-mcp-token-0123456789abcdef";

    private static Dictionary<string, string?> Settings() => new()
    {
        [CrawlerSettings.EnabledConfigurationKey] = "false",
        [MuiMcp.TokenConfigurationKey] = Token,
    };

    private static async Task<Guid> SeedGameAsync(NpgsqlDataSource source, string slug, DateTimeOffset now)
    {
        var id = Guid.CreateVersion7();

        await new NpgsqlGameStore(source).InsertAsync(new GameRecord(
            id, slug, slug, Tagline: null, LifecycleState.Active, IsClaimed: false, FirstSeenAt: now));

        return id;
    }

    // ── crawl_seed_add ──────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task CrawlSeedAddPlantsANewAddressAndIsIdempotent()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var first = await client.CallAsync<CrawlSeedAddResult>("crawl_seed_add", new Dictionary<string, object?>
        {
            ["host"] = "mush.example.org",
            ["port"] = 4201,
        });

        await Assert.That(first.WasNewlyPlanted).IsTrue();

        var targets = new NpgsqlCrawlTargetRepository(source);
        var planted = await targets.ByAddressAsync("mush.example.org", 4201, CancellationToken.None);

        await Assert.That(planted).IsNotNull();
        await Assert.That(planted!.IsOperatorSeed).IsFalse();

        // Idempotent: a second call about an address already known keeps its own schedule rather
        // than dragging it forward (CrawlSeeds.PlantAsync's own contract).
        var second = await client.CallAsync<CrawlSeedAddResult>("crawl_seed_add", new Dictionary<string, object?>
        {
            ["host"] = "mush.example.org",
            ["port"] = 4201,
        });

        await Assert.That(second.WasNewlyPlanted).IsFalse();
    }

    [Test]
    public async Task CrawlSeedAddHonoursTheExemptFlag()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        await client.CallAsync<CrawlSeedAddResult>("crawl_seed_add", new Dictionary<string, object?>
        {
            ["host"] = "127.0.0.1",
            ["port"] = 4201,
            ["exempt"] = true,
        });

        var targets = new NpgsqlCrawlTargetRepository(source);
        var planted = await targets.ByAddressAsync("127.0.0.1", 4201, CancellationToken.None);

        await Assert.That(planted!.IsOperatorSeed).IsTrue();
    }

    // ── crawl_opt_out_record ────────────────────────────────────────────────────────────────────

    [Test]
    public async Task CrawlOptOutRecordRefusesAnEmptyBecause()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.TryCallAsync("crawl_opt_out_record", new Dictionary<string, object?>
        {
            ["host"] = "example.org",
            ["because"] = "   ",
        });

        await Assert.That(result.IsError).IsTrue();

        var optOuts = new NpgsqlCrawlOptOutRepository(source);
        await Assert.That(await optOuts.StandingAsync("example.org", 4201, CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task CrawlOptOutRecordWritesAStandingRequest()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var recorded = await client.CallAsync<CrawlOptOut>("crawl_opt_out_record", new Dictionary<string, object?>
        {
            ["host"] = "example.org",
            ["because"] = "Operator emailed asking to stop, 2026-08-18.",
        });

        await Assert.That(recorded.Host).IsEqualTo("example.org");
        await Assert.That(recorded.Source).IsEqualTo(OptOutSource.Request);

        var optOuts = new NpgsqlCrawlOptOutRepository(source);
        var standing = await optOuts.StandingAsync("example.org", 4201, CancellationToken.None);

        await Assert.That(standing).IsNotNull();
        await Assert.That(standing!.Detail).IsEqualTo("Operator emailed asking to stop, 2026-08-18.");
    }

    // ── crawl_due_targets ───────────────────────────────────────────────────────────────────────

    [Test]
    public async Task CrawlDueTargetsListsWhatIsDue()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var time = TimeProvider.System;

        await new NpgsqlCrawlTargetRepository(source).AddAsync(
            new CrawlTarget
            {
                Id = Guid.CreateVersion7(),
                Host = "due.example.org",
                Port = 4201,
                NextProbeAt = time.GetUtcNow(),
                FirstSeenAt = time.GetUtcNow(),
            },
            CancellationToken.None);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var due = await client.CallAsync<List<CrawlDueTarget>>(
            "crawl_due_targets", new Dictionary<string, object?> { ["batch"] = 10 });

        await Assert.That(due.Any(t => t.Host == "due.example.org" && t.Port == 4201)).IsTrue();
    }

    // ── crawl_run_cycle ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task CrawlRunCycleDryRunPlantsSeedsAndListsDueButRunsNothing()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.CallAsync<CrawlRunCycleResult>(
            "crawl_run_cycle",
            new Dictionary<string, object?>
            {
                ["seeds"] = new[] { "dryrun.example.org:4201" },
                ["dryRun"] = true,
            });

        await Assert.That(result.DryRun).IsTrue();
        await Assert.That(result.SeedsNewlyPlanted).IsEqualTo(1);
        await Assert.That(result.Cycles).IsNull();
        await Assert.That(result.Due).IsNotNull();
        await Assert.That(result.Due!.Any(t => t.Host == "dryrun.example.org")).IsTrue();
    }

    /// <summary>
    /// A real run with nothing due dials nobody — <c>CrawlCycle.RunAsync</c> returns
    /// <c>CycleReport.Empty</c> immediately when <c>DueAsync</c> finds nothing — so this exercises the
    /// real (non-dry) path, including taking the advisory lease, without a network dial anywhere.
    /// </summary>
    [Test]
    public async Task CrawlRunCycleARealRunTakesTheLeaseAndRuns()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.CallAsync<CrawlRunCycleResult>(
            "crawl_run_cycle", new Dictionary<string, object?> { ["cycles"] = 1 });

        await Assert.That(result.DryRun).IsFalse();
        await Assert.That(result.LeaseHeld).IsTrue();
        await Assert.That(result.Cycles).IsNotNull();
        await Assert.That(result.Cycles!.Count).IsEqualTo(1);
        await Assert.That(result.Cycles![0].Considered).IsEqualTo(0);
    }

    /// <summary>
    /// The lock-contention behaviour the tool's own description promises: with another session
    /// already holding the crawl lease — standing in for the hosted crawler — a real run acquires
    /// nothing and reports zero cycles rather than double-crawling.
    /// </summary>
    [Test]
    public async Task CrawlRunCycleNoOpsWhenTheLeaseIsHeldElsewhere()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        await using var heldElsewhere = await AdvisoryLease.TryAcquireAsync(
            source, AdvisoryLease.CrawlKey, CancellationToken.None);

        await Assert.That(heldElsewhere).IsNotNull();

        var result = await client.CallAsync<CrawlRunCycleResult>(
            "crawl_run_cycle", new Dictionary<string, object?> { ["cycles"] = 1 });

        await Assert.That(result.LeaseHeld).IsFalse();
        await Assert.That(result.Cycles).IsNotNull();
        await Assert.That(result.Cycles!.Count).IsEqualTo(0);
    }

    // ── crawl_summary ───────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task CrawlSummaryReadsBackWhatLanded()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        await SeedGameAsync(source, "summarised-game", now);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var summary = await client.CallAsync<CrawlSummaryData>("crawl_summary");

        var games = summary.Totals.Single(t => t.Label == "games");
        await Assert.That(games.Count).IsEqualTo(1);
        await Assert.That(summary.Games.Any(g => g.Slug == "summarised-game")).IsTrue();
    }

    // ── game_field_set ──────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task GameFieldSetWritesAsStaffAndOutranksASubsequentMeasuredRow()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        var gameId = await SeedGameAsync(source, "pennmush-hand-fixed", now);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString, clock: new FixedClock(now));
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.CallAsync<GameFieldSetResult>("game_field_set", new Dictionary<string, object?>
        {
            ["gameSlug"] = "pennmush-hand-fixed",
            ["field"] = "DESCRIPTION",
            ["value"] = "Hand-corrected by staff.",
        });

        await Assert.That(result.GameSlug).IsEqualTo("pennmush-hand-fixed");
        await Assert.That(result.Field).IsEqualTo("DESCRIPTION");
        await Assert.That(result.PreviousValue).IsNull();
        await Assert.That(result.NewValue).IsEqualTo("Hand-corrected by staff.");

        var fields = new NpgsqlGameFieldStore(source);

        var staffRows = await fields.ForGameAsync(gameId, FieldSource.Staff, CancellationToken.None);
        var staffRow = staffRows.Single(f => f.Field == "DESCRIPTION");
        await Assert.That(staffRow.Value).IsEqualTo("Hand-corrected by staff.");

        // Simulate a probe writing a MEASURED/asserted row for the same field afterwards.
        await fields.UpsertAsync(
            new GameField(
                gameId, "DESCRIPTION", FieldSource.Mssp, "The game's own MSSP description.", now, now),
            CancellationToken.None);

        var everyDescriptionRow = (await fields.ForGameAsync(gameId, CancellationToken.None))
            .Where(f => f.Field == "DESCRIPTION")
            .ToList();

        await Assert.That(everyDescriptionRow.Count).IsEqualTo(2);

        var winner = FieldPrecedence.Winner(everyDescriptionRow);

        await Assert.That(winner).IsNotNull();
        await Assert.That(winner!.Source).IsEqualTo(FieldSource.Staff);
        await Assert.That(winner.Value).IsEqualTo("Hand-corrected by staff.");
    }

    [Test]
    public async Task GameFieldSetRefusesAnUnknownField()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        var gameId = await SeedGameAsync(source, "unknown-field-game", now);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.TryCallAsync("game_field_set", new Dictionary<string, object?>
        {
            ["gameSlug"] = "unknown-field-game",
            ["field"] = "NOT-A-REAL-FIELD",
            ["value"] = "x",
        });

        await Assert.That(result.IsError).IsTrue();

        var fields = new NpgsqlGameFieldStore(source);
        await Assert.That((await fields.ForGameAsync(gameId, CancellationToken.None)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task GameFieldSetRefusesAnUnknownGame()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.TryCallAsync("game_field_set", new Dictionary<string, object?>
        {
            ["gameSlug"] = "does-not-exist-anywhere",
            ["field"] = "DESCRIPTION",
            ["value"] = "x",
        });

        await Assert.That(result.IsError).IsTrue();
    }

    /// <summary>PLAYERS/UPTIME live only in the presence series (spec §5.2) and must never become a
    /// GameField row, even a staff-written one.</summary>
    [Test]
    public async Task GameFieldSetRefusesTheVolatileFields()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        var gameId = await SeedGameAsync(source, "volatile-field-game", now);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.TryCallAsync("game_field_set", new Dictionary<string, object?>
        {
            ["gameSlug"] = "volatile-field-game",
            ["field"] = "PLAYERS",
            ["value"] = "5",
        });

        await Assert.That(result.IsError).IsTrue();

        var fields = new NpgsqlGameFieldStore(source);
        await Assert.That((await fields.ForGameAsync(gameId, CancellationToken.None)).Count).IsEqualTo(0);
    }
}
