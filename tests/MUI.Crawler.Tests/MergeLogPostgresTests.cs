using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Tests;

/// <summary>
/// §7.3's "merges are reversible and logged", against the database that has to enforce it.
/// </summary>
/// <remarks>
/// <see cref="IMergeLog"/> shipped with no implementation and no table under it, so the catalogue
/// could reach the judgement "these two listings are one game" and had nowhere to put it. The
/// assertions that matter here are the ones only the real schema can make: that a game cannot be
/// absorbed twice at once, and that a reverted merge leaves the history behind rather than the row.
/// </remarks>
public class MergeLogPostgresTests
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

    private static MergeRecord Merge(Guid into, Guid from, DateTimeOffset? at = null) => new(
        Guid.CreateVersion7(),
        into,
        from,
        Score: 0.5,
        SignalsJson: """[{"Name":"BannerHash","Weight":0.5,"Matched":true}]""",
        At: at ?? Then,
        RevertedAt: null);

    [Test]
    public async Task AMergeIsRecordedAndIsInForce()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlMergeLog(database.DataSource);

        var survivor = await GameAsync(database.DataSource, "aardwolf-mud");
        var absorbed = await GameAsync(database.DataSource, "aardwolf-mud-2");

        var id = await log.RecordAsync(Merge(survivor, absorbed), None);

        var records = await log.ForGameAsync(absorbed, None);

        await Assert.That(records).Count().IsEqualTo(1);
        await Assert.That(records[0].Id).IsEqualTo(id);
        await Assert.That(records[0].IntoGameId).IsEqualTo(survivor);
        await Assert.That(records[0].IsInForce).IsTrue();
    }

    [Test]
    public async Task AGameIsOnBothSidesOfItsOwnHistory()
    {
        // "Every merge this game was on either side of." A survivor has to be able to answer "what did
        // I absorb", or the only way to find out is to know the absorbed game's id already.
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlMergeLog(database.DataSource);

        var survivor = await GameAsync(database.DataSource, "aardwolf-mud");
        var absorbed = await GameAsync(database.DataSource, "aardwolf-mud-2");

        await log.RecordAsync(Merge(survivor, absorbed), None);

        await Assert.That(await log.ForGameAsync(survivor, None)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task AGameCannotBeAbsorbedByTwoGamesAtOnce()
    {
        // Not a merge that needs resolving later — a page with two answers to "where does this
        // redirect". The index refuses it where somebody would create it, rather than where a reader
        // would meet it.
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlMergeLog(database.DataSource);

        var first = await GameAsync(database.DataSource, "aardwolf-mud");
        var second = await GameAsync(database.DataSource, "asgard-s-honor");
        var absorbed = await GameAsync(database.DataSource, "aardwolf-mud-2");

        await log.RecordAsync(Merge(first, absorbed), None);

        await Assert.That(async () => await log.RecordAsync(Merge(second, absorbed), None))
            .Throws<PostgresException>();
    }

    [Test]
    public async Task RevertingLeavesTheRowAndTheJudgementBehindIt()
    {
        // §7.5 reaches here too. A merge somebody undid is a thing that happened, and the next person
        // to look at the pair needs to know it was already tried.
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlMergeLog(database.DataSource);

        var survivor = await GameAsync(database.DataSource, "aardwolf-mud");
        var absorbed = await GameAsync(database.DataSource, "aardwolf-mud-2");

        var id = await log.RecordAsync(Merge(survivor, absorbed), None);
        await log.RevertAsync(id, Then.AddDays(1), None);

        var records = await log.ForGameAsync(absorbed, None);

        await Assert.That(records).Count().IsEqualTo(1);
        await Assert.That(records[0].IsInForce).IsFalse();
        await Assert.That(records[0].RevertedAt).IsEqualTo(Then.AddDays(1));

        // And the pair can be merged again afterwards, which the partial index is what allows.
        await log.RecordAsync(Merge(survivor, absorbed, Then.AddDays(2)), None);

        await Assert.That(await log.ForGameAsync(absorbed, None)).Count().IsEqualTo(2);
    }

    [Test]
    public async Task RevertingTwiceDoesNotRewriteWhenTheFirstRevertHappened()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlMergeLog(database.DataSource);

        var survivor = await GameAsync(database.DataSource, "aardwolf-mud");
        var absorbed = await GameAsync(database.DataSource, "aardwolf-mud-2");

        var id = await log.RecordAsync(Merge(survivor, absorbed), None);
        await log.RevertAsync(id, Then.AddDays(1), None);
        await log.RevertAsync(id, Then.AddDays(9), None);

        var records = await log.ForGameAsync(absorbed, None);

        await Assert.That(records[0].RevertedAt).IsEqualTo(Then.AddDays(1));
    }

    [Test]
    public async Task AGameCannotAbsorbItself()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlMergeLog(database.DataSource);

        var game = await GameAsync(database.DataSource, "aardwolf-mud");

        await Assert.That(async () => await log.RecordAsync(Merge(game, game), None))
            .Throws<PostgresException>();
    }
}
