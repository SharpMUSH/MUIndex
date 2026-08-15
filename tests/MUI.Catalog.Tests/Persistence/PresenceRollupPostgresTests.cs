using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

using Npgsql;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Spec §5.2's rollups, against a real database.
/// </summary>
/// <remarks>
/// The question every one of these asks is §5.4's: <b>does an hour still have three states after it
/// has been aggregated?</b> A rollup is the easiest place in this codebase to lose that distinction —
/// <c>count(*)</c> and <c>coalesce(sum(count), 0)</c> both turn "we got in and could not count" into
/// a zero, and the graph that reads it then says a healthy game was empty.
/// </remarks>
public class PresenceRollupPostgresTests
{
    private static readonly DateTimeOffset Hour = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The first instant of the hour after the last one a test wrote into.</summary>
    private static DateTimeOffset After(int hours) => Hour.AddHours(hours);

    [Test]
    public async Task TheThreeStatesSurviveTheRollup()
    {
        // The worst bug this codebase could ship, asserted at the seam where it would happen.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var samples = new NpgsqlPresenceStore(db.DataSource);
        var writer = new PresenceWriter(samples);

        // Hour 0: counted, and one of the counts is a measured zero — we got in and nobody was there.
        await writer.WriteAsync(game, PresenceReading.Counted(0, FieldSource.Who), Hour);
        await writer.WriteAsync(game, PresenceReading.Counted(4, FieldSource.Who), Hour.AddMinutes(30));

        // Hour 1: probed twice, uncountable both times. Hatched, and never a zero.
        await writer.WriteAsync(
            game, PresenceReading.Unmeasurable(UnmeasurableReason.WhoUnparseable), After(1));
        await writer.WriteAsync(
            game, PresenceReading.Unmeasurable(UnmeasurableReason.WhoNotOffered), After(1).AddMinutes(20));

        // Hour 2: nothing at all. A failed probe writes no presence row, and neither does an hour we
        // never reached — the empty cell is the absence of a row here too.

        // Hour 3: a single measured zero, which is a filled cell on its own.
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

        // The middle state. Two probes reached the game and neither could count, so there is a row —
        // and it carries no number at all, rather than the zero that would render it as empty.
        var hatched = rollups.Single(r => r.Bucket == After(1));
        await Assert.That(hatched.CountedSamples).IsEqualTo(0);
        await Assert.That(hatched.UnmeasurableSamples).IsEqualTo(2);
        await Assert.That(hatched.MinCount).IsNull();
        await Assert.That(hatched.MaxCount).IsNull();
        await Assert.That(hatched.MeanCount).IsNull();
        await Assert.That(hatched.IsCounted).IsFalse();
        await Assert.That(hatched.IsUncountable).IsTrue();

        // The third state is the absence of a row, exactly as it is in presence_sample. A rollup that
        // wrote a row of zeroes here would have invented a measurement.
        await Assert.That(rollups.Any(r => r.Bucket == After(2))).IsFalse();

        // And a measured zero is not any of the other two: it is a filled cell with a number in it.
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
        // Half the design is in CHECK constraints, and this is the one that means no future writer —
        // ours or a hand-typed UPDATE at four in the morning — can turn a hatched hour into an empty
        // game.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);

        await using var connection = await db.DataSource.OpenConnectionAsync();

        // A hatched hour given a zero: three probes that could not count, recorded as three probes
        // that counted nobody.
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
        // Three probes in one hour and one in the next is the normal shape of a crawl, and averaging
        // the two hourly means would weight the lonely probe three times too heavily.
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
    public async Task AUniquePlayerEstimateIsNeverCombinedAcrossSaltEpochs()
    {
        // §11. Within one epoch the estimate means something; across a rotation the hashes it was
        // derived from cannot be compared, and they are not kept anyway. The honest answer is no
        // number rather than a bigger one.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var samples = new NpgsqlPresenceStore(db.DataSource);

        await samples.AppendAsync(new PresenceSample
        {
            GameId = game,
            At = Hour,
            Count = 9,
            Source = FieldSource.Who,
            Aggregates = new PresenceAggregates([4, 3, 2], distinctEstimate: 7, saltEpoch: "20260727T000000Z"),
        });
        await samples.AppendAsync(new PresenceSample
        {
            GameId = game,
            At = Hour.AddMinutes(30),
            Count = 9,
            Source = FieldSource.Who,
            Aggregates = new PresenceAggregates([4, 3, 2], distinctEstimate: 5, saltEpoch: "20260727T000000Z"),
        });

        // The next hour straddles a rotation.
        await samples.AppendAsync(new PresenceSample
        {
            GameId = game,
            At = After(1),
            Count = 9,
            Source = FieldSource.Who,
            Aggregates = new PresenceAggregates([4, 3, 2], distinctEstimate: 7, saltEpoch: "20260727T000000Z"),
        });
        await samples.AppendAsync(new PresenceSample
        {
            GameId = game,
            At = After(1).AddMinutes(30),
            Count = 9,
            Source = FieldSource.Who,
            Aggregates = new PresenceAggregates([4, 3, 2], distinctEstimate: 6, saltEpoch: "20260803T000000Z"),
        });

        await Maintenance(db).RunAsync(After(2));

        var rollups = await Rollups(db).ForGameAsync(game, PresenceGrain.Hour, Hour, After(1));

        var oneEpoch = rollups.Single(r => r.Bucket == Hour);
        await Assert.That(oneEpoch.SaltEpoch).IsEqualTo("20260727T000000Z");
        await Assert.That(oneEpoch.PeakDistinctEstimate).IsEqualTo(7);

        var straddling = rollups.Single(r => r.Bucket == After(1));
        await Assert.That(straddling.SaltEpoch).IsNull();
        await Assert.That(straddling.PeakDistinctEstimate).IsNull();

        // And the day, which contains both, inherits the refusal rather than the larger number.
        var day = (await Rollups(db).ForGameAsync(game, PresenceGrain.Day, Midnight(Hour), After(2))).Single();
        await Assert.That(day.SaltEpoch).IsNull();
        await Assert.That(day.PeakDistinctEstimate).IsNull();
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
        await maintenance.RunAsync(After(1));
        var second = await maintenance.RunAsync(After(1));

        var afterTwoRuns = (await Rollups(db).ForGameAsync(game, PresenceGrain.Hour, Hour, Hour)).Single();
        await Assert.That(afterTwoRuns.CountedSamples).IsEqualTo(1);
        await Assert.That(second.HoursRolled).IsGreaterThanOrEqualTo(0);

        // A sample that lands after its own hour was rolled up — a probe that finished slowly, or a
        // replica whose clock was behind — is picked up by the overlap the next pass re-reads.
        await writer.WriteAsync(game, PresenceReading.Counted(9, FieldSource.Who), Hour.AddMinutes(50));
        await maintenance.RunAsync(After(1));

        var corrected = (await Rollups(db).ForGameAsync(game, PresenceGrain.Hour, Hour, Hour)).Single();
        await Assert.That(corrected.CountedSamples).IsEqualTo(2);
        await Assert.That(corrected.MaxCount).IsEqualTo(9);
    }

    [Test]
    public async Task TheHourStillRunningIsNotRolledUpUntilItIsOver()
    {
        // Rolling a half-finished hour would publish a min and a max that the rest of the hour is
        // about to contradict.
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
        // The raw table has no DEFAULT partition on purpose (migration 0003), so a month with no
        // partition is an insert error rather than a misfiled row. The store makes one per append;
        // this makes them before anybody needs them, which is what keeps a midnight-on-the-31st crawl
        // from depending on that.
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
        // Inside the retention window, so it stays whole.
        await Assert.That(await PartitionNames(db)).Contains("presence_sample_202607");

        // And what the dropped partition measured is still there in the shape that outlives it —
        // including that one of those two hours was probed and uncountable rather than empty.
        var days = await Rollups(db).ForGameAsync(game, PresenceGrain.Day, Midnight(old), Midnight(old));
        var day = days.Single();

        await Assert.That(day.CountedSamples).IsEqualTo(1);
        await Assert.That(day.UnmeasurableSamples).IsEqualTo(1);
        await Assert.That(day.MaxCount).IsEqualTo(11);
    }

    [Test]
    public async Task AnUnrolledHourKeepsItsRawRowsHoweverOldTheyAre()
    {
        // Retention may never run ahead of the rollup. If the rollup has not consumed an hour, the
        // raw rows for it are the only copy there is — so a maintenance pass whose rollup step failed
        // must not then delete the thing the rollup was supposed to read.
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
