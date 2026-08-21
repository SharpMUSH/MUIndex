using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

using Npgsql;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Spec §5.2's rollups, against a real database.
/// </summary>
/// <remarks>
/// Does an hour still have three states after aggregation? <c>count(*)</c> and
/// <c>coalesce(sum(count), 0)</c> both turn "we got in and could not count" into a zero.
/// </remarks>
public class PresenceRollupPostgresTests
{
    private static readonly DateTimeOffset Hour = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The first instant of the hour after the last one a test wrote into.</summary>
    private static DateTimeOffset After(int hours) => Hour.AddHours(hours);

    [Test]
    public async Task TheThreeStatesSurviveTheRollup()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var samples = new NpgsqlPresenceStore(db.DataSource);
        var writer = new PresenceWriter(samples);

        // Hour 0: counted, including a measured zero.
        await writer.WriteAsync(game, PresenceReading.Counted(0, FieldSource.Who), Hour);
        await writer.WriteAsync(game, PresenceReading.Counted(4, FieldSource.Who), Hour.AddMinutes(30));

        // Hour 1: probed twice, uncountable both times. Hatched, never a zero.
        await writer.WriteAsync(
            game, PresenceReading.Unmeasurable(UnmeasurableReason.WhoUnparseable), After(1));
        await writer.WriteAsync(
            game, PresenceReading.Unmeasurable(UnmeasurableReason.WhoNotOffered), After(1).AddMinutes(20));

        // Hour 2: nothing at all — no row.

        // Hour 3: a single measured zero.
        await writer.WriteAsync(game, PresenceReading.Counted(0, FieldSource.Who), After(3));

        var maintenance = Maintenance(db);
        await maintenance.RunAsync(After(4));

        var rollups = await Rollups(db).ForGameAsync(game, PresenceGrain.Hour, Hour, After(3));

        await Assert.That(rollups).Count().IsEqualTo(3);

        var counted = rollups.Single(r => r.Bucket == Hour);
        await Assert.That(counted.CountedSamples).IsEqualTo(2);
        await Assert.That(counted.UnmeasurableSamples).IsEqualTo(0);
        await Assert.That(counted.MinCount).IsEqualTo(0);
        await Assert.That(counted.MaxCount).IsEqualTo(4);
        await Assert.That(counted.MeanCount).IsEqualTo(2m);
        await Assert.That(counted.IsCounted).IsTrue();

        var hatched = rollups.Single(r => r.Bucket == After(1));
        await Assert.That(hatched.CountedSamples).IsEqualTo(0);
        await Assert.That(hatched.UnmeasurableSamples).IsEqualTo(2);
        await Assert.That(hatched.MinCount).IsNull();
        await Assert.That(hatched.MaxCount).IsNull();
        await Assert.That(hatched.MeanCount).IsNull();
        await Assert.That(hatched.IsCounted).IsFalse();
        await Assert.That(hatched.IsUncountable).IsTrue();

        await Assert.That(rollups.Any(r => r.Bucket == After(2))).IsFalse();

        var measuredZero = rollups.Single(r => r.Bucket == After(3));
        await Assert.That(measuredZero.CountedSamples).IsEqualTo(1);
        await Assert.That(measuredZero.MinCount).IsEqualTo(0);
        await Assert.That(measuredZero.MaxCount).IsEqualTo(0);
        await Assert.That(measuredZero.MeanCount).IsEqualTo(0m);
        await Assert.That(measuredZero.IsCounted).IsTrue();
        await Assert.That(measuredZero.IsUncountable).IsFalse();
    }

    [Test]
    public async Task TheSchemaRefusesAnUncountableHourWithAZeroInIt()
    {
        // Enforced by a CHECK constraint, so no future writer can turn a hatched hour into an empty game.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);

        await using var connection = await db.DataSource.OpenConnectionAsync();

        // A hatched hour given a zero: three uncounted probes recorded as three probes that counted nobody.
        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO presence_rollup_hour
                    (game_id, hour, counted_samples, unmeasurable_samples, min_count, max_count, sum_count)
                VALUES (@game, @hour, 0, 3, 0, 0, 0)
                """,
                new { game, hour = Hour }))
            .Throws<PostgresException>();

        // An hour nobody measured, written as a row of zeroes rather than left absent.
        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO presence_rollup_hour (game_id, hour, counted_samples, unmeasurable_samples)
                VALUES (@game, @hour, 0, 0)
                """,
                new { game, hour = After(1) }))
            .Throws<PostgresException>();
    }

    [Test]
    public async Task ADayIsSummedFromItsHoursAndNotAveragedOverThem()
    {
        // Averaging the two hourly means would weight a lonely probe as heavily as three together.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        foreach (var (minute, count) in new[] { (0, 2), (20, 4), (40, 6) })
        {
            await writer.WriteAsync(game, PresenceReading.Counted(count, FieldSource.Who), Hour.AddMinutes(minute));
        }

        await writer.WriteAsync(game, PresenceReading.Counted(20, FieldSource.Who), After(1));
        await writer.WriteAsync(
            game, PresenceReading.Unmeasurable(UnmeasurableReason.WhoUnparseable), After(2));

        await Maintenance(db).RunAsync(After(3));

        var day = (await Rollups(db).ForGameAsync(
            game, PresenceGrain.Day, Midnight(Hour), After(3))).Single();

        await Assert.That(day.CountedSamples).IsEqualTo(4);
        await Assert.That(day.UnmeasurableSamples).IsEqualTo(1);
        await Assert.That(day.MinCount).IsEqualTo(2);
        await Assert.That(day.MaxCount).IsEqualTo(20);
        // (2 + 4 + 6 + 20) / 4, not (4 + 20) / 2.
        await Assert.That(day.MeanCount).IsEqualTo(8m);
    }

    [Test]
    public async Task RunningTwiceChangesNothingAndALateSampleIsStillPickedUp()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var samples = new NpgsqlPresenceStore(db.DataSource);
        var writer = new PresenceWriter(samples);

        await writer.WriteAsync(game, PresenceReading.Counted(3, FieldSource.Who), Hour);

        var maintenance = Maintenance(db);
        var first = await maintenance.RunAsync(After(1));
        var second = await maintenance.RunAsync(After(1));

        var afterTwoRuns = (await Rollups(db).ForGameAsync(game, PresenceGrain.Hour, Hour, Hour)).Single();

        // The rollup is a projection, not an accumulation: the second pass rewrites the same numbers.
        await Assert.That(afterTwoRuns.CountedSamples).IsEqualTo(1);
        await Assert.That(afterTwoRuns.MinCount).IsEqualTo(3);
        await Assert.That(afterTwoRuns.MaxCount).IsEqualTo(3);
        await Assert.That(second.HoursRolled).IsEqualTo(first.HoursRolled);
        await Assert.That(second.DaysRolled).IsEqualTo(first.DaysRolled);

        await Assert.That(await Rollups(db).ForGameAsync(game, PresenceGrain.Hour, Hour, After(1)))
            .Count().IsEqualTo(1);
        await Assert.That(await Rollups(db).ForGameAsync(game, PresenceGrain.Day, Midnight(Hour), After(1)))
            .Count().IsEqualTo(1);

        // A sample landing after its hour was rolled up is picked up by the overlap the next pass re-reads.
        await writer.WriteAsync(game, PresenceReading.Counted(9, FieldSource.Who), Hour.AddMinutes(50));
        await maintenance.RunAsync(After(1));

        var corrected = (await Rollups(db).ForGameAsync(game, PresenceGrain.Hour, Hour, Hour)).Single();
        await Assert.That(corrected.CountedSamples).IsEqualTo(2);
        await Assert.That(corrected.MaxCount).IsEqualTo(9);
    }

    [Test]
    public async Task EachGrainResumesFromItsOwnWatermarkAndNotFromTheHourly()
    {
        // The hourly watermark commits before daily aggregation runs, so an interruption between them
        // leaves hours rolled and days not. If the daily pass resumed from the hourly watermark it
        // would skip that gap, mark itself caught up, and let retention drop the only other copy.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));
        var start = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
        var now = start.AddDays(6);

        for (var day = 0; day < 6; day++)
        {
            await writer.WriteAsync(game, PresenceReading.Counted(day, FieldSource.Who), start.AddDays(day));
        }

        var rollups = Rollups(db);
        await rollups.RollUpAsync(PresenceGrain.Hour, start, now);
        await rollups.SetWatermarkAsync(PresenceGrain.Hour, now);

        await Maintenance(db).RunAsync(now);

        var days = await rollups.ForGameAsync(game, PresenceGrain.Day, Midnight(start), now);

        await Assert.That(days).Count().IsEqualTo(6);
        await Assert.That(days.Sum(d => d.CountedSamples)).IsEqualTo(6);
    }

    [Test]
    public async Task RetentionWaitsForTheGrainThatIsBehind()
    {
        // While the daily grain has not consumed a month, that month's raw rows are the only copy.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));
        var old = new DateTimeOffset(2026, 1, 15, 6, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

        await writer.WriteAsync(game, PresenceReading.Counted(11, FieldSource.Who), old);

        var rollups = Rollups(db);
        await rollups.RollUpAsync(PresenceGrain.Hour, old, now);
        await rollups.SetWatermarkAsync(PresenceGrain.Hour, now);

        var swept = await Maintenance(db, PresenceRetentionOptions.AsDesigned).SweepRetentionAsync(now);

        await Assert.That(swept.PartitionsDropped).IsEqualTo(0);
        await Assert.That(await SampleCount(db)).IsEqualTo(1);
    }

    [Test]
    public async Task TheEstimateColumnsAreGoneFromTheSchema()
    {
        // Nothing selects these columns any more, so the only proof the migration ran is asking the
        // catalogue what the table is made of.
        await using var db = await PostgresFixture.MigratedAsync();

        await using var connection = await db.DataSource.OpenConnectionAsync();
        var columns = (await connection.QueryAsync<string>(
            """
            SELECT column_name
              FROM information_schema.columns
             WHERE table_name IN ('presence_rollup_hour', 'presence_rollup_day')
            """)).ToList();

        await Assert.That(columns).DoesNotContain("peak_distinct_estimate");
        await Assert.That(columns).DoesNotContain("salt_epoch");

        await Assert.That(columns).Contains("counted_samples");
        await Assert.That(columns).Contains("unmeasurable_samples");
    }

    [Test]
    public async Task AnOldRowsEstimateKeysAreIgnoredRatherThanFatal()
    {
        // Rows written before the unique-player estimate was removed still carry distinctEstimate and
        // saltEpoch in their aggregates JSON. Deserializing must ignore unknown keys rather than throw
        // — a window that threw would take every other measurement in it down too.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlPresenceStore(db.DataSource);

        await store.AppendAsync(new PresenceSample
        {
            GameId = game,
            At = Hour,
            Count = 9,
            Source = FieldSource.Who,
            Aggregates = new PresenceAggregates([4, 3]),
        });

        await using (var connection = await db.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO presence_sample (game_id, at, count, source, aggregates)
                VALUES (@game, @at, 9, 'who', @aggregates::jsonb)
                """,
                new
                {
                    game,
                    at = Hour.AddMinutes(30),
                    aggregates =
                        """{"idleBuckets":[2,1],"distinctEstimate":9,"saltEpoch":"20260727T000000Z"}""",
                });
        }

        var samples = await store.ForGameAsync(game, Hour, Hour.AddHours(1));

        await Assert.That(samples).Count().IsEqualTo(2);

        var legacy = samples.Single(s => s.At == Hour.AddMinutes(30));
        await Assert.That(legacy.Aggregates!.IdleBuckets).IsEquivalentTo(new[] { 2, 1 });

        await Maintenance(db).RunAsync(After(2));

        var rolled = (await Rollups(db).ForGameAsync(game, PresenceGrain.Hour, Hour, Hour)).Single();

        await Assert.That(rolled.CountedSamples).IsEqualTo(2);
        await Assert.That(rolled.MaxCount).IsEqualTo(9);
    }

    [Test]
    public async Task TheSchemaHasToBeThereBeforeAPassMeansAnything()
    {
        // On a fresh database the migrations may not have run yet; asking is cheaper than throwing
        // 42P01 and standing down for a full retry interval.
        await using var fresh = await PostgresFixture.FreshDatabaseAsync();
        await Assert.That(await Maintenance(fresh).SchemaReadyAsync()).IsFalse();

        await using var migrated = await PostgresFixture.MigratedAsync();
        await Assert.That(await Maintenance(migrated).SchemaReadyAsync()).IsTrue();
    }

    [Test]
    public async Task TheHourStillRunningIsNotRolledUpUntilItIsOver()
    {
        // Rolling a half-finished hour would publish a min/max the rest of the hour is about to contradict.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        await writer.WriteAsync(game, PresenceReading.Counted(3, FieldSource.Who), Hour);

        await Maintenance(db).RunAsync(Hour.AddMinutes(30));

        await Assert.That(await Rollups(db).ForGameAsync(game, PresenceGrain.Hour, Hour, Hour)).IsEmpty();
    }

    [Test]
    public async Task PartitionsAreMadeAheadOfNeedSoAMonthEndingDoesNotBreakTheWriter()
    {
        // No DEFAULT partition on the raw table (a month with no partition is an insert error, not a
        // misfiled row), so partitions are made ahead of need rather than relying on per-append creation.
        await using var db = await PostgresFixture.MigratedAsync();

        await Maintenance(db).RunAsync(new DateTimeOffset(2026, 11, 20, 4, 0, 0, TimeSpan.Zero));

        var partitions = await PartitionNames(db);

        await Assert.That(partitions).Contains("presence_sample_202611");
        await Assert.That(partitions).Contains("presence_sample_202612");
        await Assert.That(partitions).Contains("presence_sample_202701");
    }

    [Test]
    public async Task NothingIsDroppedUntilADeploymentAnswersTheOpenQuestion()
    {
        // §15.4 is open, so the default is that a raw sample outlives everything. A deployment that
        // wants §5.2's ninety days says so; the code does not decide it on their behalf.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));
        var longAgo = new DateTimeOffset(2024, 1, 15, 6, 0, 0, TimeSpan.Zero);

        await writer.WriteAsync(game, PresenceReading.Counted(5, FieldSource.Who), longAgo);

        var report = await Maintenance(db).RunAsync(Hour);

        await Assert.That(report.PartitionsDropped).IsEqualTo(0);
        await Assert.That(await SampleCount(db)).IsEqualTo(1);
        await Assert.That(await PartitionNames(db)).Contains("presence_sample_202401");
    }

    [Test]
    public async Task RawPartitionsGoOnlyWhenTheyAreBothOldAndAlreadyRolledUp()
    {
        // The ordering that makes dropping raw samples survivable: a partition is dropped whole, and
        // only once the hourly and daily rollups have consumed every hour in it.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));
        var old = new DateTimeOffset(2026, 1, 15, 6, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

        await writer.WriteAsync(game, PresenceReading.Counted(11, FieldSource.Who), old);
        await writer.WriteAsync(game, PresenceReading.Unmeasurable(UnmeasurableReason.WhoNotOffered), old.AddHours(1));
        await writer.WriteAsync(game, PresenceReading.Counted(2, FieldSource.Who), now.AddDays(-1));

        var report = await Maintenance(db, PresenceRetentionOptions.AsDesigned).RunAsync(now);

        await Assert.That(report.PartitionsDropped).IsEqualTo(1);
        await Assert.That(await PartitionNames(db)).DoesNotContain("presence_sample_202601");
        await Assert.That(await PartitionNames(db)).Contains("presence_sample_202607");

        // What the dropped partition measured survives in the daily rollup, including that one of
        // those two hours was probed and uncountable rather than empty.
        var days = await Rollups(db).ForGameAsync(game, PresenceGrain.Day, Midnight(old), Midnight(old));
        var day = days.Single();

        await Assert.That(day.CountedSamples).IsEqualTo(1);
        await Assert.That(day.UnmeasurableSamples).IsEqualTo(1);
        await Assert.That(day.MaxCount).IsEqualTo(11);
    }

    [Test]
    public async Task AnUnrolledHourKeepsItsRawRowsHoweverOldTheyAre()
    {
        // Retention may never run ahead of the rollup: a failed rollup step must not let this
        // maintenance pass delete the raw rows it was supposed to read.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var old = new DateTimeOffset(2026, 1, 15, 6, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

        await new PresenceWriter(new NpgsqlPresenceStore(db.DataSource)).WriteAsync(
            game, PresenceReading.Counted(11, FieldSource.Who), old);

        var report = await Maintenance(db, PresenceRetentionOptions.AsDesigned)
            .SweepRetentionAsync(now);

        await Assert.That(report.PartitionsDropped).IsEqualTo(0);
        await Assert.That(await SampleCount(db)).IsEqualTo(1);
    }

    [Test]
    public async Task HourlyRollupsAgeOutAndDailyOnesDoNot()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));
        var old = new DateTimeOffset(2023, 1, 15, 6, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

        await writer.WriteAsync(game, PresenceReading.Counted(11, FieldSource.Who), old);

        await Maintenance(db, PresenceRetentionOptions.AsDesigned).RunAsync(now);

        var rollups = Rollups(db);

        await Assert.That(await rollups.ForGameAsync(game, PresenceGrain.Hour, old, old)).IsEmpty();
        await Assert.That(await rollups.ForGameAsync(game, PresenceGrain.Day, Midnight(old), Midnight(old)))
            .Count().IsEqualTo(1);
    }

    private static PresenceMaintenance Maintenance(TestDatabase db, PresenceRetentionOptions? retention = null) =>
        new(new NpgsqlPresenceStore(db.DataSource),
            new NpgsqlPresenceRollupStore(db.DataSource),
            retention ?? new PresenceRetentionOptions());

    private static NpgsqlPresenceRollupStore Rollups(TestDatabase db) => new(db.DataSource);

    /// <summary>Midnight UTC on the day <paramref name="at"/> falls in.</summary>
    private static DateTimeOffset Midnight(DateTimeOffset at) => new(at.UtcDateTime.Date, TimeSpan.Zero);

    private static async Task<IReadOnlyList<string>> PartitionNames(TestDatabase db)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();

        return (await connection.QueryAsync<string>(
            """
            SELECT c.relname FROM pg_inherits
              JOIN pg_class c ON c.oid = pg_inherits.inhrelid
              JOIN pg_class p ON p.oid = pg_inherits.inhparent
             WHERE p.relname = 'presence_sample'
             ORDER BY c.relname
            """)).ToList();
    }

    private static async Task<long> SampleCount(TestDatabase db)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();

        return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM presence_sample");
    }
}
