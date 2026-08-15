using Dapper;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Tests;

/// <summary>
/// A submitted address, from the form to a hidden listing, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// The three halves of this feature meet here and nowhere else: the submission writes a crawl target
/// with a marker on it, the crawl cycle dials it and mints a game, and the game carries the marker
/// that keeps it off the listing until somebody claims it. Each half has its own tests upstream, and
/// none of them would have caught the marker being dropped in between.
/// </remarks>
public class SubmissionPostgresTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static SubmissionService Submissions(
        NpgsqlDataSource source,
        IHostResolver resolver,
        SubmissionOptions? options = null) =>
        new(
            new NpgsqlCrawlTargetRepository(source),
            new CatalogueEndpointDirectory(new NpgsqlEndpointStore(source)),
            new HostScopeGuard(resolver),
            new NpgsqlSubmissionLog(source),
            options ?? new SubmissionOptions(),
            TimeProvider.System);

    private static CrawlCycle Cycle(NpgsqlDataSource source, IProbe probe, IHostResolver resolver)
    {
        var discovery = new DiscoveryOptions
        {
            GlobalInterval = TimeSpan.Zero,
            PerHostInterval = TimeSpan.Zero,
        };

        var games = new NpgsqlGameStore(source);
        var endpoints = new NpgsqlEndpointStore(source);
        var fields = new NpgsqlGameFieldStore(source);
        var availability = new NpgsqlAvailabilityStore(source);
        var targets = new NpgsqlCrawlTargetRepository(source);
        var time = TimeProvider.System;

        return new CrawlCycle(
            targets,
            probe,
            new HostScopeGuard(resolver),
            new ProbeIngestor(
                new PresenceWriter(new NpgsqlPresenceStore(source)),
                new AvailabilityWriter(availability),
                new FieldReconciler(fields),
                games,
                new ArchiveSweeper(games, availability, availability)),
            new CatalogueBinder(
                games,
                endpoints,
                fields,
                new IdentityMatcher(
                    new CatalogueGameDirectory(games),
                    new CatalogueEndpointDirectory(endpoints),
                    fields,
                    new NpgsqlGameFieldIndex(source),
                    discovery),
                new NpgsqlDuplicateReviewRepository(source),
                time),
            new ReferralGraphWriter(new NpgsqlReferralRepository(source), targets, discovery, time),
            new CrawlRateLimiter(discovery, time),
            new HostGate(),
            discovery,
            time);
    }

    /// <summary>
    /// The whole path: submit, crawl, and the game is real and hidden.
    /// </summary>
    [Test]
    public async Task ASubmittedAddressIsCrawledAndTheGameItBecomesIsHiddenUntilClaimed()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("mud.example.org", "203.0.113.10");

        var receipt = await Submissions(source, resolver)
            .SubmitAsync("mud.example.org", "4201", Source(1), None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.Accepted);

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "Tidewater Nights"), ("CODEBASE", "PennMUSH 1.8.8p0")),
            banner: "Welcome to Tidewater Nights",
            who: new WhoReading(WhoConfidence.Count, 4)));

        var report = await Cycle(source, probe, resolver).RunAsync();

        await Assert.That(report.Answered).IsEqualTo(1);
        await Assert.That(report.Listed).IsEqualTo(1);

        await using var connection = await source.OpenConnectionAsync();

        // The game exists, is measured, and carries the marker the target was holding for it.
        var game = await connection.QuerySingleAsync<(Guid Id, string Slug, DateTimeOffset? SubmittedAt)>(
            "SELECT id, slug, submitted_at FROM game");

        await Assert.That(game.Slug).IsEqualTo("tidewater-nights");
        await Assert.That(game.SubmittedAt).IsNotNull();

        // And is on none of the site's reads, which is the thing the marker is for.
        var queries = new NpgsqlGameQueries(source);

        await Assert.That(await queries.FindAsync("tidewater-nights")).IsNull();
        await Assert.That((await queries.ListAsync(new GameFilter { IncludeArchived = true })).Count)
            .IsEqualTo(0);

        // Until somebody proves they run it.
        await new NpgsqlGameStore(source).SetClaimedAsync(game.Id, true);

        await Assert.That(await queries.FindAsync("tidewater-nights")).IsNotNull();
    }

    /// <summary>
    /// A game the crawler found for itself is listed on sight, exactly as §7.1 says.
    /// </summary>
    /// <remarks>
    /// The control for the test above, through the same pipeline. If the marker were being written
    /// for every game rather than only for submitted ones, the whole catalogue would vanish and the
    /// test above would still be green.
    /// </remarks>
    [Test]
    public async Task AGameTheCrawlerFoundForItselfIsListedOnSight()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver();

        await CrawlSeeds.PlantAsync(
            new NpgsqlCrawlTargetRepository(source),
            [new CrawlSeed("mush.example.org", 4201)],
            TimeProvider.System);

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host, port: target.Port, mssp: Probes.Mssp(("NAME", "Gaslight Row"))));

        await Cycle(source, probe, resolver).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<DateTimeOffset?>(
            "SELECT submitted_at FROM game")).IsNull();

        await Assert.That(await new NpgsqlGameQueries(source).FindAsync("gaslight-row")).IsNotNull();
    }

    /// <summary>
    /// Submitting a second port of a game we already list attaches to it and leaves it public.
    /// </summary>
    /// <remarks>
    /// The takeover this feature could otherwise have shipped. The address is not one we hold an
    /// endpoint for, so the submission is accepted and a target is written with the marker on it —
    /// and then §7.3's matcher says the probe <em>is</em> that game, the binder attaches rather than
    /// creating, and no marker is ever written to a game row. A version of this that copied the
    /// marker after binding would let anybody hide any listed game by naming one of its ports.
    /// </remarks>
    [Test]
    public async Task SubmittingASecondPortOfAListedGameDoesNotHideIt()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("mush.example.org", "203.0.113.10");

        await CrawlSeeds.PlantAsync(
            new NpgsqlCrawlTargetRepository(source),
            [new CrawlSeed("mush.example.org", 4201)],
            TimeProvider.System);

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "Tidewater Nights"), ("CREATED", "2004")),
            banner: "Welcome to Tidewater Nights"));

        await Cycle(source, probe, resolver).RunAsync();

        var queries = new NpgsqlGameQueries(source);

        await Assert.That(await queries.FindAsync("tidewater-nights")).IsNotNull();

        // Somebody submits the same game's other port.
        var receipt = await Submissions(source, resolver)
            .SubmitAsync("mush.example.org", "4202", Source(2), None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.Accepted);

        await Cycle(source, probe, resolver).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        // One game, still public, and no marker on it.
        await Assert.That(await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM game"))
            .IsEqualTo(1);
        await Assert.That(await connection.ExecuteScalarAsync<DateTimeOffset?>(
            "SELECT submitted_at FROM game")).IsNull();
        await Assert.That(await queries.FindAsync("tidewater-nights")).IsNotNull();
    }

    /// <summary>
    /// A refusal writes a submission row and touches nothing else in the schema.
    /// </summary>
    [Test]
    public async Task ARefusalWritesNoTargetNoGameAndNoAvailability()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver().Resolving("internal.example.org", "169.254.169.254");

        var receipt = await Submissions(source, resolver)
            .SubmitAsync("internal.example.org", "4201", Source(3), None);

        await Assert.That(receipt.Outcome).IsEqualTo(SubmissionOutcome.RefusedNotRoutable);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM crawl_target")).IsEqualTo(0);
        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM game")).IsEqualTo(0);
        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM availability_interval")).IsEqualTo(0);
        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM presence_sample")).IsEqualTo(0);

        // The one row it did write says what we decided, and has nowhere to name a game.
        await Assert.That(await connection.ExecuteScalarAsync<string>(
            "SELECT outcome FROM game_submission")).IsEqualTo("refused_not_routable");
    }

    /// <summary>The bound is read out of the table, so several web replicas share one count.</summary>
    [Test]
    public async Task TheRateLimitCountsRowsInTheTable()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;
        var resolver = new FakeHostResolver();
        var options = new SubmissionOptions { PerSource = 2, Window = TimeSpan.FromHours(1) };
        var mine = Source(4);

        // Two different services against one database, which is the deployment this is written for.
        await Submissions(source, resolver, options).SubmitAsync("a.example.org", "4201", mine, None);
        await Submissions(source, resolver, options).SubmitAsync("b.example.org", "4201", mine, None);

        var third = await Submissions(source, resolver, options)
            .SubmitAsync("c.example.org", "4201", mine, None);

        await Assert.That(third.Outcome).IsEqualTo(SubmissionOutcome.TooMany);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM game_submission")).IsEqualTo(2);
    }

    /// <summary>
    /// Every outcome the service records is one the table's vocabulary accepts.
    /// </summary>
    /// <remarks>
    /// A CHECK constraint and a C# enum are two spellings of one vocabulary, and the day they
    /// disagree is the day a submission throws inside a request handler. Asserted by writing one row
    /// of each rather than by reading the constraint, because reading it would test the reader.
    /// </remarks>
    [Test]
    public async Task EveryRecordedOutcomeIsAValueTheTableAccepts()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlSubmissionLog(database.DataSource);

        foreach (var outcome in Enum.GetValues<SubmissionOutcome>()
            .Where(o => o is not SubmissionOutcome.TooMany))
        {
            await log.RecordAsync(
                new SubmissionRecord(
                    Guid.CreateVersion7(),
                    outcome is SubmissionOutcome.Malformed ? null : "mud.example.org",
                    outcome is SubmissionOutcome.Malformed ? null : 4201,
                    DateTimeOffset.UtcNow,
                    outcome,
                    // The table refuses an accepted row that names no target, and refuses a target on
                    // any other outcome — so this is not decoration, it is the constraint.
                    outcome is SubmissionOutcome.Accepted ? await TargetAsync(database.DataSource) : null,
                    Source(5)),
                None);
        }

        await using var connection = await database.DataSource.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM game_submission")).IsEqualTo(6);
    }

    private static async Task<Guid> TargetAsync(NpgsqlDataSource source) =>
        await new NpgsqlCrawlTargetRepository(source).AddAsync(
            new CrawlTarget
            {
                Id = Guid.CreateVersion7(),
                Host = "mud.example.org",
                Port = 4201,
                NextProbeAt = DateTimeOffset.UtcNow,
                FirstSeenAt = DateTimeOffset.UtcNow,
                SubmittedAt = DateTimeOffset.UtcNow,
            },
            None);

    /// <summary>A digest-shaped source, which the table's own CHECK requires.</summary>
    private static string Source(int seed) => new string((char)('a' + seed % 6), 64);
}
