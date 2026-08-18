using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Tests;

/// <summary>
/// <see cref="ReviewMergeService"/> against a real PostgreSQL — the operator surface for spec §7.3's
/// <c>duplicate_review</c> queue, which accumulated with nothing to drain it until this shipped.
/// </summary>
/// <remarks>
/// The unit tests in <c>MUI.Discovery.Tests</c> prove the service's own logic against in-memory fakes;
/// this proves the same logic against the schema that actually enforces §7.3's guarantees —
/// <c>merge_log_absorbed_once_idx</c>, the no-chains trigger, and <c>duplicate_review</c>'s own
/// resolved-once behaviour.
/// </remarks>
public class ReviewMergePostgresTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly DateTimeOffset Then = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> GameAsync(NpgsqlDataSource source, string slug)
    {
        var id = Guid.CreateVersion7();

        await new NpgsqlGameStore(source).InsertAsync(new GameRecord(
            id,
            slug,
            slug,
            Tagline: null,
            LifecycleState.Active,
            IsClaimed: false,
            FirstSeenAt: Then.AddYears(-1),
            LastReachableAt: null,
            ArchivedAt: null));

        return id;
    }

    private static ReviewMergeService Service(NpgsqlDataSource source) => new(
        new CatalogueGameDirectory(new NpgsqlGameStore(source)),
        new NpgsqlDuplicateReviewRepository(source),
        new MergeApplier(
            new CatalogueEndpointDirectory(new NpgsqlEndpointStore(source)),
            new NpgsqlGameFieldStore(source),
            new NpgsqlMergeLog(source),
            TimeProvider.System),
        TimeProvider.System);

    [Test]
    public async Task AnOpenReviewIsResolvedAndTheMergeIsInForce()
    {
        await using var database = await PostgresFixture.MigratedAsync();

        var winner = await GameAsync(database.DataSource, "nightfall");
        var loser = await GameAsync(database.DataSource, "45-79-224-33");

        var reviews = new NpgsqlDuplicateReviewRepository(database.DataSource);
        var score = new IdentityScore(loser, 0.5, [new IdentitySignal("BannerHash", 0.5, true)]);
        var reviewId = await reviews.OpenAsync(winner, loser, score, Then, None);

        var result = await Service(database.DataSource).MergeAsync(
            winner, loser, "same connect screen, confirmed by hand", None);

        await Assert.That(result.ResolvedReviewId).IsEqualTo(reviewId);

        var merges = await new NpgsqlMergeLog(database.DataSource).ForGameAsync(loser, None);
        await Assert.That(merges).Count().IsEqualTo(1);
        await Assert.That(merges[0].IntoGameId).IsEqualTo(winner);
        await Assert.That(merges[0].IsInForce).IsTrue();
        await Assert.That(merges[0].SignalsJson).Contains("BannerHash");

        var stillOpen = await reviews.OpenPairsForAsync(winner, None);
        await Assert.That(stillOpen).IsEmpty();
    }

    [Test]
    public async Task APairWithNoOpenReviewIsStillMergedByHandWithNoSignals()
    {
        await using var database = await PostgresFixture.MigratedAsync();

        var winner = await GameAsync(database.DataSource, "corvid");
        var loser = await GameAsync(database.DataSource, "corvid-2");

        var result = await Service(database.DataSource).MergeAsync(
            winner, loser, "I run both, they are the same game", None);

        await Assert.That(result.ResolvedReviewId).IsNull();

        var merges = await new NpgsqlMergeLog(database.DataSource).ForGameAsync(loser, None);
        await Assert.That(merges[0].SignalsJson).IsEqualTo("[]");
    }

    [Test]
    public async Task AGameAlreadyAbsorbedElsewhereRefusesASecondMerge()
    {
        // merge_log_absorbed_once_idx, surfaced through the service rather than worked around by it —
        // an operator asking to merge an already-absorbed loser into a second winner is exactly the
        // "page with two answers" the constraint exists to refuse.
        await using var database = await PostgresFixture.MigratedAsync();

        var firstWinner = await GameAsync(database.DataSource, "aardwolf-mud");
        var secondWinner = await GameAsync(database.DataSource, "asgard-s-honor");
        var loser = await GameAsync(database.DataSource, "aardwolf-mud-2");

        var service = Service(database.DataSource);
        await service.MergeAsync(firstWinner, loser, "same game", None);

        await Assert.That(async () => await service.MergeAsync(secondWinner, loser, "also the same game", None))
            .Throws<PostgresException>();
    }

    [Test]
    public async Task MergingResolvesOnlyTheReviewForTheNamedPair()
    {
        await using var database = await PostgresFixture.MigratedAsync();

        var winner = await GameAsync(database.DataSource, "frontiers");
        var loser = await GameAsync(database.DataSource, "45-33-1-9");
        var bystander = await GameAsync(database.DataSource, "way-of-the-force");

        var reviews = new NpgsqlDuplicateReviewRepository(database.DataSource);
        var pairScore = new IdentityScore(loser, 0.5, []);
        var reviewId = await reviews.OpenAsync(winner, loser, pairScore, Then, None);

        var unrelatedScore = new IdentityScore(bystander, 0.45, []);
        var unrelatedReviewId = await reviews.OpenAsync(winner, bystander, unrelatedScore, Then, None);

        await Service(database.DataSource).MergeAsync(winner, loser, "confirmed", None);

        var stillOpenForWinner = await reviews.OpenPairsForAsync(winner, None);
        await Assert.That(stillOpenForWinner.Select(r => r.Id)).Contains(unrelatedReviewId);
        await Assert.That(stillOpenForWinner.Select(r => r.Id)).DoesNotContain(reviewId);
    }

    [Test]
    public async Task AGameIdThatDoesNotExistIsRefusedBeforeAnyWrite()
    {
        await using var database = await PostgresFixture.MigratedAsync();

        var winner = await GameAsync(database.DataSource, "corvid");
        var ghost = Guid.CreateVersion7();

        await Assert.That(async () => await Service(database.DataSource).MergeAsync(winner, ghost, "typo", None))
            .Throws<InvalidOperationException>();

        await Assert.That(await new NpgsqlMergeLog(database.DataSource).ForGameAsync(winner, None)).IsEmpty();
    }
}
