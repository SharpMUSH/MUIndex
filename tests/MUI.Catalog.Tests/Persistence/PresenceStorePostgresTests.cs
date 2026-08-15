using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Spec §5.2, §5.4 against a real database. The three states must be distinguishable <b>by query</b>,
/// which is what these ask.
/// </summary>
public class PresenceStorePostgresTests
{
    private static readonly DateTimeOffset At = Seed.Now;

    [Test]
    public async Task TheThreeStatesAreDistinguishableByQuery()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        await writer.WriteAsync(game, PresenceReading.Counted(0, FieldSource.Who), At);
        // The middle hour is a failed probe: it writes no presence row at all.
        await writer.WriteAsync(
            game, PresenceReading.Unmeasurable(UnmeasurableReason.WhoUnparseable), At.AddHours(2));
        await writer.WriteAsync(game, PresenceReading.Counted(7, FieldSource.Who), At.AddHours(3));

        await using var connection = await db.DataSource.OpenConnectionAsync();

        var counted = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM presence_sample WHERE game_id = @game AND count IS NOT NULL",
            new { game });
        var uncountable = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM presence_sample WHERE game_id = @game AND count IS NULL",
            new { game });
        var atHourOne = await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM presence_sample WHERE game_id = @game AND at = @at",
            new { game, at = At.AddHours(1) });

        await Assert.That(counted).IsEqualTo(2);
        await Assert.That(uncountable).IsEqualTo(1);
        await Assert.That(atHourOne).IsEqualTo(0);
    }

    [Test]
    public async Task AMeasuredZeroIsAStoredCountAndNotANull()
    {
        // The distinction the heatmap draws between a filled cell and an empty one, at the level
        // where it is actually stored.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlPresenceStore(db.DataSource);

        await new PresenceWriter(store).WriteAsync(game, PresenceReading.Counted(0, FieldSource.Who), At);

        var sample = (await store.ForGameAsync(game, At.AddHours(-1), At.AddHours(1))).Single();

        await Assert.That(sample.Count).IsEqualTo(0);
        await Assert.That(sample.IsCounted).IsTrue();
        await Assert.That(sample.Reason).IsNull();
    }

    [Test]
    public async Task AnUncountableRowRoundTripsItsReason()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlPresenceStore(db.DataSource);

        await new PresenceWriter(store).WriteAsync(
            game, PresenceReading.Unmeasurable(UnmeasurableReason.WhoNotOffered), At);

        var sample = (await store.ForGameAsync(game, At.AddHours(-1), At.AddHours(1))).Single();

        await Assert.That(sample.Count).IsNull();
        await Assert.That(sample.Reason).IsEqualTo(UnmeasurableReason.WhoNotOffered);
    }

    [Test]
    public async Task AggregatesRoundTripAndCarryNoNames()
    {
        // §11: names are hashed in memory with a rotating salt and discarded. Only distributions
        // survive, and this is the shape of what survives.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlPresenceStore(db.DataSource);

        await store.AppendAsync(new PresenceSample
        {
            GameId = game,
            At = At,
            Count = 12,
            Source = FieldSource.Who,
            Aggregates = new PresenceAggregates([4, 3, 2, 1], distinctEstimate: 9, saltEpoch: "20260727T000000Z"),
        });

        var sample = (await store.ForGameAsync(game, At, At)).Single();

        await Assert.That(sample.Aggregates!.DistinctEstimate).IsEqualTo(9);
        await Assert.That(sample.Aggregates.IdleBuckets).IsEquivalentTo(new[] { 4, 3, 2, 1 });
        // The epoch travels with the estimate, or the estimate cannot be read later without guessing
        // which other estimates it may be compared with (§11).
        await Assert.That(sample.Aggregates.SaltEpoch).IsEqualTo("20260727T000000Z");
    }

    [Test]
    public async Task AnEstimateWithNoSaltEpochCannotBeConstructed()
    {
        // §11's promise is only keepable if every estimate says which salt it was taken under, so the
        // record refuses one that does not — at the point of construction, rather than in whichever
        // surface eventually tries to add two of them together.
        await Assert.That(() => new PresenceAggregates([4, 3], distinctEstimate: 9))
            .Throws<ArgumentException>();

        // Buckets on their own are not derived from names at all, so they need no epoch.
        await Assert.That(new PresenceAggregates([4, 3], distinctEstimate: null).SaltEpoch).IsNull();
    }

    [Test]
    public async Task ASeriesCrossingAMonthBoundaryLandsInTwoPartitions()
    {
        // The table is partitioned monthly and a missing partition is an insert error, so the store
        // makes the month before every append. A crawler is not entitled to lose a measurement to a
        // calendar rollover.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlPresenceStore(db.DataSource);
        var lastDay = new DateTimeOffset(2026, 7, 31, 23, 0, 0, TimeSpan.Zero);

        await store.AppendAsync(PresenceSample.Counted(game, lastDay, 3, FieldSource.Who));
        await store.AppendAsync(PresenceSample.Counted(game, lastDay.AddHours(2), 4, FieldSource.Who));

        await using var connection = await db.DataSource.OpenConnectionAsync();

        var partitions = await connection.QueryAsync<string>(
            """
            SELECT c.relname FROM pg_inherits
              JOIN pg_class c ON c.oid = pg_inherits.inhrelid
              JOIN pg_class p ON p.oid = pg_inherits.inhparent
             WHERE p.relname = 'presence_sample'
             ORDER BY c.relname
            """);

        await Assert.That(partitions.ToList())
            .IsEquivalentTo(new[] { "presence_sample_202607", "presence_sample_202608" });
        await Assert.That(await store.ForGameAsync(game, lastDay, lastDay.AddDays(1))).Count().IsEqualTo(2);
    }
}
