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
        // would meet it. NpgsqlMergeLog translates the raw constraint violation into the Npgsql-agnostic
        // MergeAlreadyAbsorbedException ReviewMergeService.MergeAsync knows how to catch (see
        // MergeLogConstraintViolations).
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlMergeLog(database.DataSource);

        var first = await GameAsync(database.DataSource, "aardwolf-mud");
        var second = await GameAsync(database.DataSource, "asgard-s-honor");
        var absorbed = await GameAsync(database.DataSource, "aardwolf-mud-2");

        await log.RecordAsync(Merge(first, absorbed), None);

        await Assert.That(async () => await log.RecordAsync(Merge(second, absorbed), None))
            .Throws<MergeAlreadyAbsorbedException>();
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
    public async Task AChainIsRefusedWhereItWouldBeCreated()
    {
        // A -> B then B -> C. Both rows have distinct from_game_id, so merge_log_absorbed_once_idx
        // accepts them — and A is then neither listed (the reads drop an absorbed game) nor redirected
        // (one hop or none), which is a 404 for a game whose rows are all still there. §7.5 broken by
        // two writes that were individually legal.
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlMergeLog(database.DataSource);

        var a = await GameAsync(database.DataSource, "a");
        var b = await GameAsync(database.DataSource, "b");
        var c = await GameAsync(database.DataSource, "c");

        await log.RecordAsync(Merge(into: b, from: a), None);

        await Assert.That(async () => await log.RecordAsync(Merge(into: c, from: b), None))
            .Throws<MergeWouldChainException>();
    }

    [Test]
    public async Task AnAbsorbedGameCannotBecomeSomebodyElsesSurvivor()
    {
        // The same hole approached from the other end. A is already absorbed by B; pointing C at A
        // would send C's readers to a page that redirects again. The row that forms the chain is the
        // one refused, whichever end it arrives from.
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlMergeLog(database.DataSource);

        var a = await GameAsync(database.DataSource, "a");
        var b = await GameAsync(database.DataSource, "b");
        var c = await GameAsync(database.DataSource, "c");

        await log.RecordAsync(Merge(into: b, from: a), None);

        await Assert.That(async () => await log.RecordAsync(Merge(into: a, from: c), None))
            .Throws<MergeWouldChainException>();
    }

    [Test]
    public async Task ACycleIsRefusedToo()
    {
        // A -> B, B -> A. Distinct from_game_id again, so nothing stopped it, and a read that followed
        // the chain instead of stopping at one hop would not terminate.
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlMergeLog(database.DataSource);

        var a = await GameAsync(database.DataSource, "a");
        var b = await GameAsync(database.DataSource, "b");

        await log.RecordAsync(Merge(into: b, from: a), None);

        await Assert.That(async () => await log.RecordAsync(Merge(into: a, from: b), None))
            .Throws<MergeWouldChainException>();
    }

    [Test]
    public async Task RevertingFreesThePairToBeMergedTheOtherWay()
    {
        // The guard is about merges in force, not about history. An operator who merged the wrong way
        // round reverts and merges again, and nothing in the record stops them.
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlMergeLog(database.DataSource);

        var a = await GameAsync(database.DataSource, "a");
        var b = await GameAsync(database.DataSource, "b");

        var id = await log.RecordAsync(Merge(into: b, from: a), None);
        await log.RevertAsync(id, Then.AddDays(1), None);

        await log.RecordAsync(Merge(into: a, from: b, Then.AddDays(2)), None);

        await Assert.That(await log.ForGameAsync(a, None)).Count().IsEqualTo(2);
    }

    [Test]
    public async Task AMergeCannotBeRevertedBeforeItWasMade()
    {
        // The third constraint in 0018, and the only one no test reached. RevertAsync writes whatever
        // instant the caller hands it — a clock skewed backwards, a replayed request, a fixture that
        // moved on — and the database is what refuses an undo dated before the act.
        await using var database = await PostgresFixture.MigratedAsync();
        var log = new NpgsqlMergeLog(database.DataSource);

        var survivor = await GameAsync(database.DataSource, "aardwolf-mud");
        var absorbed = await GameAsync(database.DataSource, "aardwolf-mud-2");

        var id = await log.RecordAsync(Merge(survivor, absorbed), None);

        // The constraint by name, not merely "something threw": the trigger and the partial index can
        // both raise from this table, and a test that accepted any of them would keep passing if this
        // check were dropped.
        var error = await Assert.That(async () => await log.RevertAsync(id, Then.AddDays(-1), None))
            .Throws<PostgresException>();

        await Assert.That(error!.ConstraintName).IsEqualTo("merge_log_reverted_after_merged");
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
