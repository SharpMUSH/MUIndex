using System.Net;

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
/// Each of the nine <see cref="MuiMcpTools"/> tools, called over the real MCP transport against a
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

    // ── game_rename ─────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task GameRenameWritesNameAsStaffAndMintsANewSlug()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        var gameId = await SeedGameAsync(source, "old-slug-game", now);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString, clock: new FixedClock(now));
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.CallAsync<GameRenameResult>("game_rename", new Dictionary<string, object?>
        {
            ["gameSlug"] = "old-slug-game",
            ["newName"] = "Brand New Name",
            ["because"] = "The operator asked us to fix the listed name, 2026-08-18.",
        });

        await Assert.That(result.OldSlug).IsEqualTo("old-slug-game");
        await Assert.That(result.NewSlug).IsNotEqualTo("old-slug-game");
        await Assert.That(result.Name).IsEqualTo("Brand New Name");

        // The game table itself moved, and the row is reachable under its new slug.
        var games = new NpgsqlGameStore(source);
        var renamed = await games.BySlugAsync(result.NewSlug, CancellationToken.None);
        await Assert.That(renamed).IsNotNull();
        await Assert.That(renamed!.Id).IsEqualTo(gameId);
        await Assert.That(renamed.Name).IsEqualTo("Brand New Name");

        // NAME landed as a staff row with correct precedence (spec §5.1), same check as
        // GameFieldSetWritesAsStaffAndOutranksASubsequentMeasuredRow.
        var fields = new NpgsqlGameFieldStore(source);
        var nameRows = (await fields.ForGameAsync(gameId, CancellationToken.None))
            .Where(f => f.Field == "NAME")
            .ToList();

        var staffNameRow = nameRows.Single(f => f.Source == FieldSource.Staff);
        await Assert.That(staffNameRow.Value).IsEqualTo("Brand New Name");
        await Assert.That(FieldPrecedence.Winner(nameRows)!.Source).IsEqualTo(FieldSource.Staff);

        // The old slug is retired, not deleted, and points at the new one.
        var slugs = new NpgsqlSlugHistoryStore(source);
        await Assert.That(await slugs.CurrentSlugAsync("old-slug-game", CancellationToken.None))
            .IsEqualTo(result.NewSlug);
    }

    /// <summary>
    /// The whole point of a rename is that a stranger's bookmark keeps working — exercised over real
    /// HTTP against the actual <c>FormerSlugRedirects</c> middleware, not just the database row it
    /// writes.
    /// </summary>
    [Test]
    public async Task GameRenameOldSlugRedirectsForReal()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        await SeedGameAsync(source, "redirect-me", now);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString, clock: new FixedClock(now));
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.CallAsync<GameRenameResult>("game_rename", new Dictionary<string, object?>
        {
            ["gameSlug"] = "redirect-me",
            ["newName"] = "Redirected Elsewhere",
            ["because"] = "Testing the redirect for real.",
        });

        var response = await site.Client.GetAsync($"/g/redirect-me");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo($"/g/{result.NewSlug}");
    }

    [Test]
    public async Task GameRenameRequiresBecause()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        var gameId = await SeedGameAsync(source, "needs-because", now);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.TryCallAsync("game_rename", new Dictionary<string, object?>
        {
            ["gameSlug"] = "needs-because",
            ["newName"] = "Should Not Land",
            ["because"] = "   ",
        });

        await Assert.That(result.IsError).IsTrue();

        // Nothing was written: the refusal happens before either write.
        var games = new NpgsqlGameStore(source);
        await Assert.That((await games.BySlugAsync("needs-because", CancellationToken.None))!.Name)
            .IsEqualTo("needs-because");

        var fields = new NpgsqlGameFieldStore(source);
        await Assert.That((await fields.ForGameAsync(gameId, CancellationToken.None)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task GameRenameRefusesAnUnknownGame()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.TryCallAsync("game_rename", new Dictionary<string, object?>
        {
            ["gameSlug"] = "does-not-exist-anywhere",
            ["newName"] = "Anything",
            ["because"] = "Checking the refusal.",
        });

        await Assert.That(result.IsError).IsTrue();
    }

    [Test]
    public async Task GameRenameRefusesWhenAlreadyNamedThat()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        await SeedGameAsync(source, "same-name-game", now);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        // SeedGameAsync names the game the same as its slug.
        var result = await client.TryCallAsync("game_rename", new Dictionary<string, object?>
        {
            ["gameSlug"] = "same-name-game",
            ["newName"] = "same-name-game",
            ["because"] = "Checking the refusal.",
        });

        await Assert.That(result.IsError).IsTrue();
    }

    /// <summary>
    /// A rename that folds to another game's slug is not an error — <c>GameSlug.UniqueAsync</c>
    /// appends a numeric suffix the same way it does for any other mint — so this is "handled
    /// cleanly" in the sense the brief asks for: no raw exception, no refusal, just a different URL.
    /// </summary>
    [Test]
    public async Task GameRenameHandlesANameThatCollidesWithAnotherGamesSlug()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        await SeedGameAsync(source, "pennmush", now);
        await SeedGameAsync(source, "some-other-game", now);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString, clock: new FixedClock(now));
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.CallAsync<GameRenameResult>("game_rename", new Dictionary<string, object?>
        {
            ["gameSlug"] = "some-other-game",
            ["newName"] = "pennmush",
            ["because"] = "Deliberately colliding with an existing slug.",
        });

        await Assert.That(result.NewSlug).IsNotEqualTo("pennmush");
        await Assert.That(result.NewSlug).StartsWith("pennmush-");

        // The original holder of "pennmush" is untouched.
        var games = new NpgsqlGameStore(source);
        var original = await games.BySlugAsync("pennmush", CancellationToken.None);
        await Assert.That(original).IsNotNull();
        await Assert.That(original!.Name).IsEqualTo("pennmush");
    }

    // ── game_merge ──────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task GameMergeResolvesAnOpenReviewAndCarriesItsEvidenceOntoTheMergeLog()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        var winnerId = await SeedGameAsync(source, "furrymuck", now);
        var loserId = await SeedGameAsync(source, "furrymuck-com-8888", now);

        var reviews = new NpgsqlDuplicateReviewRepository(source);
        var score = new IdentityScore(
            loserId, 0.5, [new IdentitySignal("BannerHash", 0.5, Matched: true)]);
        await reviews.OpenAsync(winnerId, loserId, score, now, CancellationToken.None);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString, clock: new FixedClock(now));
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.CallAsync<GameMergeResult>("game_merge", new Dictionary<string, object?>
        {
            ["winnerSlug"] = "furrymuck",
            ["loserSlug"] = "furrymuck-com-8888",
            ["because"] = "Same connect screen, same banner hash, reached by hostname vs raw IP.",
        });

        await Assert.That(result.WinnerSlug).IsEqualTo("furrymuck");
        await Assert.That(result.LoserSlug).IsEqualTo("furrymuck-com-8888");
        await Assert.That(result.ResolvedReviewId).IsNotNull();
        await Assert.That(result.Score).IsEqualTo(0.5);

        // The review is closed, not deleted.
        var openPairs = await reviews.OpenPairsForAsync(winnerId, CancellationToken.None);
        await Assert.That(openPairs.Any(r => r.Id == result.ResolvedReviewId)).IsFalse();

        // merge_log carries the review's own score and signals.
        var merges = new NpgsqlMergeLog(source);
        var record = (await merges.ForGameAsync(loserId, CancellationToken.None)).Single();
        await Assert.That(record.IntoGameId).IsEqualTo(winnerId);
        await Assert.That(record.FromGameId).IsEqualTo(loserId);
        await Assert.That(record.Score).IsEqualTo(0.5);
        await Assert.That(record.IsInForce).IsTrue();
        await Assert.That(record.SignalsJson).Contains("BannerHash");
    }

    [Test]
    public async Task GameMergeIsStillUsableWithNoOpenReview()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        var winnerId = await SeedGameAsync(source, "operator-spotted-winner", now);
        var loserId = await SeedGameAsync(source, "operator-spotted-loser", now);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString, clock: new FixedClock(now));
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.CallAsync<GameMergeResult>("game_merge", new Dictionary<string, object?>
        {
            ["winnerSlug"] = "operator-spotted-winner",
            ["loserSlug"] = "operator-spotted-loser",
            ["because"] = "Spotted by hand; the matcher never flagged this pair.",
        });

        await Assert.That(result.ResolvedReviewId).IsNull();
        await Assert.That(result.Score).IsEqualTo(0);

        var merges = new NpgsqlMergeLog(source);
        var record = (await merges.ForGameAsync(loserId, CancellationToken.None)).Single();
        await Assert.That(record.IntoGameId).IsEqualTo(winnerId);
        await Assert.That(record.SignalsJson).IsEqualTo("[]");
    }

    /// <summary>
    /// The loser's page redirects for real over HTTP once merged — the same property
    /// <see cref="GameRenameOldSlugRedirectsForReal"/> checks for a rename, exercised here for a merge.
    /// </summary>
    [Test]
    public async Task GameMergeLoserPageRedirectsForReal()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        await SeedGameAsync(source, "merge-winner", now);
        await SeedGameAsync(source, "merge-loser", now);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString, clock: new FixedClock(now));
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        await client.CallAsync<GameMergeResult>("game_merge", new Dictionary<string, object?>
        {
            ["winnerSlug"] = "merge-winner",
            ["loserSlug"] = "merge-loser",
            ["because"] = "Testing the merge redirect for real.",
        });

        var response = await site.Client.GetAsync("/g/merge-loser");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/g/merge-winner");
    }

    [Test]
    public async Task GameMergeRefusesMergingAGameIntoItself()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        await SeedGameAsync(source, "self-merge", DateTimeOffset.UtcNow);

        var result = await client.TryCallAsync("game_merge", new Dictionary<string, object?>
        {
            ["winnerSlug"] = "self-merge",
            ["loserSlug"] = "self-merge",
            ["because"] = "Checking the refusal.",
        });

        await Assert.That(result.IsError).IsTrue();
    }

    [Test]
    public async Task GameMergeRefusesAnUnknownSlug()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        await SeedGameAsync(source, "known-game", DateTimeOffset.UtcNow);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.TryCallAsync("game_merge", new Dictionary<string, object?>
        {
            ["winnerSlug"] = "known-game",
            ["loserSlug"] = "does-not-exist-anywhere",
            ["because"] = "Checking the refusal.",
        });

        await Assert.That(result.IsError).IsTrue();
    }

    [Test]
    public async Task GameMergeRequiresBecause()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        var loserId = await SeedGameAsync(source, "because-winner", now);
        await SeedGameAsync(source, "because-loser", now);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString);
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var result = await client.TryCallAsync("game_merge", new Dictionary<string, object?>
        {
            ["winnerSlug"] = "because-winner",
            ["loserSlug"] = "because-loser",
            ["because"] = "   ",
        });

        await Assert.That(result.IsError).IsTrue();

        var merges = new NpgsqlMergeLog(source);
        await Assert.That((await merges.ForGameAsync(loserId, CancellationToken.None)).Count).IsEqualTo(0);
    }

    /// <summary>
    /// A loser already absorbed elsewhere is <c>merge_log_absorbed_once_idx</c> refusing, surfaced as
    /// the schema's own message rather than an unhandled exception.
    /// </summary>
    [Test]
    public async Task GameMergeRefusesALoserAlreadyAbsorbedElsewhere()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        await SeedGameAsync(source, "first-winner", now);
        await SeedGameAsync(source, "twice-absorbed-loser", now);
        await SeedGameAsync(source, "second-winner", now);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString, clock: new FixedClock(now));
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        await client.CallAsync<GameMergeResult>("game_merge", new Dictionary<string, object?>
        {
            ["winnerSlug"] = "first-winner",
            ["loserSlug"] = "twice-absorbed-loser",
            ["because"] = "First merge.",
        });

        var result = await client.TryCallAsync("game_merge", new Dictionary<string, object?>
        {
            ["winnerSlug"] = "second-winner",
            ["loserSlug"] = "twice-absorbed-loser",
            ["because"] = "Second merge should be refused.",
        });

        await Assert.That(result.IsError).IsTrue();
    }

    /// <summary>
    /// A survivor of one merge asked to be absorbed by a third game is <c>merge_log_no_chains</c>
    /// refusing -- a distinct guard from <see cref="GameMergeRefusesALoserAlreadyAbsorbedElsewhere"/>'s
    /// <c>merge_log_absorbed_once_idx</c>: that one stops the same loser being folded in twice, this
    /// one stops a survivor from also being somebody else's loser, which would leave the middle game's
    /// URL redirecting nowhere reachable (spec §7.5).
    /// </summary>
    [Test]
    public async Task GameMergeRefusesARedirectChain()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var source = NpgsqlDataSource.Create(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        await SeedGameAsync(source, "chain-end", now);
        var middleId = await SeedGameAsync(source, "chain-middle", now);
        await SeedGameAsync(source, "chain-start", now);

        await using var site = await SiteHost.StartAsync(
            settings: Settings(), connectionString: database.ConnectionString, clock: new FixedClock(now));
        await using var client = await McpTestClient.ConnectAsync(site, Token);

        // chain-middle absorbs chain-start, making chain-middle a survivor.
        await client.CallAsync<GameMergeResult>("game_merge", new Dictionary<string, object?>
        {
            ["winnerSlug"] = "chain-middle",
            ["loserSlug"] = "chain-start",
            ["because"] = "First merge.",
        });

        // Now asking chain-middle -- already a survivor -- to be absorbed by chain-end would leave
        // chain-start's redirect resolving to a game with no page of its own.
        var result = await client.TryCallAsync("game_merge", new Dictionary<string, object?>
        {
            ["winnerSlug"] = "chain-end",
            ["loserSlug"] = "chain-middle",
            ["because"] = "Would form a chain and should be refused.",
        });

        await Assert.That(result.IsError).IsTrue();

        var merges = new NpgsqlMergeLog(source);
        var middlesMerges = await merges.ForGameAsync(middleId, CancellationToken.None);
        await Assert.That(middlesMerges.Count).IsEqualTo(1);
        await Assert.That(middlesMerges.Single().IsInForce).IsTrue();
    }
}
