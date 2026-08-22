using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Tests;

/// <summary>
/// A pair somebody already merged is not a pair: <see cref="CatalogueBinder"/> opens no duplicate
/// review between two listings a merge in force has already made one (spec §7.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a production defect, measured.</b> Absorbing a game does not stop it being probed — a
/// merge is a redirect, so the loser keeps every endpoint and crawl target it had, for ever. Every
/// probe of the absorbed address therefore re-scored the winner as a rival and opened a review on a
/// question that had already been answered. On 2026-08-21 twenty-one of the sixty-one open rows were
/// exactly that, every one of them stamped <em>after</em> the merge that settled it, and the queue
/// refilled itself as fast as it could be drained.
/// </para>
/// <para>
/// Against a real database rather than a fake, because the claim is about the interaction between
/// <c>merge_log</c> and <c>duplicate_review</c> — two tables — and an in-memory pair of dictionaries
/// agreeing with each other proves nothing about the two that matter.
/// </para>
/// </remarks>
public class MergedPairReviewPostgresTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly DateTimeOffset Then = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Long enough to be a connect screen rather than a colour prompt (§7.3's length floor).</summary>
    private const string Banner = "Welcome to Corvid.\nA place for slow stories.\nType 'connect'.\n";

    [Test]
    public async Task ARivalThatIsAlreadyThisListingOpensNoReview()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var world = new World(database.DataSource);

        var winner = await world.GameAsync("corvid", "corvid.example.org", 4201);
        var loser = await world.GameAsync("corvid-2", "corvid.example.net", 4201);

        await world.MergeAsync(winner, loser);

        // The absorbed listing's own address, probed exactly as the crawler goes on probing it.
        var binding = await world.BindAsync(loser, "corvid.example.net", 4201);

        await Assert.That(binding!.ReviewedAgainst).IsNull();
        await Assert.That(await world.OpenReviewsAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task TheSameShapeWithNoMergeStillOpensOne()
    {
        // The control. Without this the test above passes for any reason at all, including the two
        // listings never having scored against each other in the first place.
        await using var database = await PostgresFixture.MigratedAsync();
        var world = new World(database.DataSource);

        var winner = await world.GameAsync("corvid", "corvid.example.org", 4201);
        var twin = await world.GameAsync("corvid-2", "corvid.example.net", 4201);
        _ = winner;

        var binding = await world.BindAsync(twin, "corvid.example.net", 4201);

        await Assert.That(binding!.ReviewedAgainst).IsNotNull();
        await Assert.That(await world.OpenReviewsAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task ProbingTheWinnerDoesNotOpenOneAgainstItsOwnLoser()
    {
        // The other direction: the winner keeps being probed too, and the absorbed twin is still a
        // candidate for it on the same evidence.
        await using var database = await PostgresFixture.MigratedAsync();
        var world = new World(database.DataSource);

        var winner = await world.GameAsync("corvid", "corvid.example.org", 4201);
        var loser = await world.GameAsync("corvid-2", "corvid.example.net", 4201);

        await world.MergeAsync(winner, loser);

        var binding = await world.BindAsync(winner, "corvid.example.org", 4201);

        await Assert.That(binding!.ReviewedAgainst).IsNull();
        await Assert.That(await world.OpenReviewsAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task ARevertedMergeMakesThePairReviewableAgain()
    {
        // Reverting a merge puts both listings back, and a question that is open again should be asked
        // again. Nothing here caches the answer.
        await using var database = await PostgresFixture.MigratedAsync();
        var world = new World(database.DataSource);

        var winner = await world.GameAsync("corvid", "corvid.example.org", 4201);
        var loser = await world.GameAsync("corvid-2", "corvid.example.net", 4201);

        var mergeId = await world.MergeAsync(winner, loser);
        await new NpgsqlMergeLog(database.DataSource).RevertAsync(mergeId, Then, None);

        var binding = await world.BindAsync(loser, "corvid.example.net", 4201);

        await Assert.That(binding!.ReviewedAgainst).IsNotNull();
        await Assert.That(await world.OpenReviewsAsync()).IsEqualTo(1);
    }

    /// <summary>The catalogue, the binder over it, and the two writes this test needs to make by hand.</summary>
    private sealed class World(NpgsqlDataSource source)
    {
        private readonly NpgsqlGameStore _games = new(source);
        private readonly NpgsqlEndpointStore _endpoints = new(source);
        private readonly NpgsqlGameFieldStore _fields = new(source);

        /// <summary>A listed game at one address, publishing the connect screen the pair matches on.</summary>
        public async Task<Guid> GameAsync(string slug, string host, int port)
        {
            var id = Guid.CreateVersion7();

            await _games.InsertAsync(new GameRecord(
                id,
                slug,
                slug,
                Tagline: null,
                LifecycleState.Active,
                IsClaimed: false,
                FirstSeenAt: Then.AddYears(-1),
                LastReachableAt: Then,
                ArchivedAt: null));

            await _endpoints.UpsertAsync(
                new GameEndpoint(id, host, port, EndpointKind.Telnet, Then, Then, EndpointState.Active), None);

            await _fields.UpsertAsync(
                new GameField(id, IdentityFields.BannerHash, FieldSource.Banner, BannerFingerprint.Of(Banner), Then, Then),
                None);

            return id;
        }

        public async Task<Guid> MergeAsync(Guid winner, Guid loser)
        {
            var id = Guid.CreateVersion7();

            await new NpgsqlMergeLog(source).RecordAsync(
                new MergeRecord(id, winner, loser, 0.5, "[]", Then, null, "same game"), None);

            return id;
        }

        /// <summary>One probe of an address already attributed to <paramref name="gameId"/>.</summary>
        public Task<Binding?> BindAsync(Guid gameId, string host, int port) =>
            Binder().BindAsync(
                new CrawlTarget
                {
                    Id = Guid.CreateVersion7(),
                    GameId = gameId,
                    Host = host,
                    Port = port,
                    NextProbeAt = Then,
                    FirstSeenAt = Then,
                },
                Probes.Answered(host: host, port: port, banner: Banner),
                None);

        public async Task<int> OpenReviewsAsync()
        {
            await using var connection = await source.OpenConnectionAsync(None);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM duplicate_review WHERE resolved_at IS NULL";
            return (int)(long)(await command.ExecuteScalarAsync(None))!;
        }

        private CatalogueBinder Binder() => new(
            _games,
            _endpoints,
            _fields,
            new NpgsqlSlugHistoryStore(source),
            new IdentityMatcher(
                new CatalogueGameDirectory(_games),
                new CatalogueEndpointDirectory(_endpoints),
                _fields,
                new NpgsqlGameFieldIndex(source),
                new DiscoveryOptions(),
                resolver: null,
                new NpgsqlMergeLog(source)),
            new NpgsqlDuplicateReviewRepository(source),
            new NpgsqlMergeLog(source),
            TimeProvider.System);
    }
}
