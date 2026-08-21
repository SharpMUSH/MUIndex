using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The listing's window sorts against a real database (spec §9).
/// </summary>
/// <remarks>
/// Four silent-failure modes: the typical count computed as a mean rather than a median (dragged by
/// one outlier night); the window read from raw samples alone, shortening once retention drops the
/// first partition; an uncountable probe folded in as a zero (rule 4); and an even sample count
/// landing one element high. The load-bearing case is a window spanning the rollup watermark reading
/// the same figures as the same samples read whole.
/// </remarks>
public class WindowSortPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    private static NpgsqlGameQueries QueriesOn(TestDatabase db) =>
        new(db.DataSource, time: new FixedClock(Now));

    [Test]
    public async Task TheTypicalCountIsAMedianAndOneBigEveningDoesNotMoveIt()
    {
        // Forty probes read ten and one read a hundred: the mean is 12.2, but only the median (10) is
        // a count somebody's server actually reported that often.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "uneven", "Uneven");
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        await writer.WriteAsync(game, PresenceReading.Counted(100, FieldSource.Who), Now.AddDays(-3));

        for (var i = 0; i < 40; i++)
        {
            await writer.WriteAsync(
                game, PresenceReading.Counted(10, FieldSource.Who), Now.AddDays(-2).AddMinutes(i * 20));
        }

        await Maintenance(db).RunAsync(Now);

        var window = await WindowOf(db, "uneven", GameSort.MedianWeek);

        await Assert.That(window).IsNotNull();
        await Assert.That(window!.Samples).IsEqualTo(41);
        await Assert.That(window.Median).IsEqualTo(10);

        await Assert.That(window.Peak).IsEqualTo(100);
    }

    [Test]
    public async Task AnEvenSampleCountTakesTheLowerMiddleValueAndNotTheOneAboveIt()
    {
        // `ceil(n / 2.0)`, not `(n + 1) / 2`: `sum()` over a bigint returns numeric, so the division
        // is exact and must not be written as if it rounded. Four readings of 1, 3, 9, 27 have a
        // discrete median of 3 — never 9, never 6.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "even", "Even");
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        foreach (var (hour, count) in new[] { (1, 1), (2, 3), (3, 9), (4, 27) })
        {
            await writer.WriteAsync(
                game, PresenceReading.Counted(count, FieldSource.Who), Now.AddDays(-2).AddHours(hour));
        }

        await Maintenance(db).RunAsync(Now);

        var window = await WindowOf(db, "even", GameSort.MedianWeek);

        await Assert.That(window!.Median).IsEqualTo(3);
        await Assert.That(window.Samples).IsEqualTo(4);
    }

    [Test]
    public async Task AWindowSpanningTheRollupWatermarkReadsTheSameAsOneThatDoesNot()
    {
        // Below the watermark only the rollup is guaranteed to survive (§5.2 authorises dropping
        // consumed raw partitions); above it only raw rows exist, since the rollup consumes whole
        // elapsed days and today is not one. Reading either half alone gives wrong figures silently.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "spanning", "Spanning");
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        await writer.WriteAsync(game, PresenceReading.Counted(4, FieldSource.Who), Now.AddDays(-3));
        await Maintenance(db).RunAsync(Now.AddDays(-1));
        await writer.WriteAsync(game, PresenceReading.Counted(8, FieldSource.Who), Now.AddMinutes(-30));

        var window = await WindowOf(db, "spanning", GameSort.MedianWeek);

        await Assert.That(window).IsNotNull();
        await Assert.That(window!.Samples).IsEqualTo(2).Because("one sample from each side of the watermark");

        await Assert.That(window.Median).IsEqualTo(4);
        await Assert.That(window.Peak).IsEqualTo(8);
    }

    [Test]
    public async Task AProbeThatCouldNotCountIsNotCountedAsAZero()
    {
        // Rule 4: an uncountable probe entering the distribution as a zero would drag the median of a
        // perfectly healthy game to the bottom of the listing.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "half-read", "Half Read");
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        await writer.WriteAsync(game, PresenceReading.Counted(20, FieldSource.Who), Now.AddDays(-2));

        for (var i = 1; i <= 5; i++)
        {
            await writer.WriteAsync(
                game,
                PresenceReading.Unmeasurable(UnmeasurableReason.WhoUnparseable),
                Now.AddDays(-2).AddHours(i));
        }

        await Maintenance(db).RunAsync(Now);

        var window = await WindowOf(db, "half-read", GameSort.MedianWeek);

        await Assert.That(window).IsNotNull();
        await Assert.That(window!.Samples).IsEqualTo(1).Because("five probes counted nothing, so they count for nothing");
        await Assert.That(window.Median).IsEqualTo(20);
    }

    [Test]
    public async Task AGameWithNothingCountableInTheWindowHasNoWindowAtAll()
    {
        // Absent, not present with zeroes in it.
        await using var db = await PostgresFixture.MigratedAsync();
        await Seed.GameAsync(db, "silent", "Silent");
        var uncounted = await Seed.GameAsync(db, "uncountable", "Uncountable");
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        await writer.WriteAsync(
            uncounted, PresenceReading.Unmeasurable(UnmeasurableReason.WhoNotOffered), Now.AddDays(-1));

        await Assert.That(await WindowOf(db, "silent", GameSort.MedianWeek)).IsNull();
        await Assert.That(await WindowOf(db, "uncountable", GameSort.MedianWeek)).IsNull();
    }

    [Test]
    public async Task TheWindowIsTheOneTheSortNamedAndNothingOlderReachesIt()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "long-record", "Long Record");
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        await writer.WriteAsync(game, PresenceReading.Counted(5, FieldSource.Who), Now.AddDays(-2));
        await writer.WriteAsync(game, PresenceReading.Counted(50, FieldSource.Who), Now.AddDays(-20));
        await writer.WriteAsync(game, PresenceReading.Counted(500, FieldSource.Who), Now.AddDays(-60));

        await Maintenance(db).RunAsync(Now);

        await Assert.That((await WindowOf(db, "long-record", GameSort.PeakWeek))!.Peak).IsEqualTo(5);
        await Assert.That((await WindowOf(db, "long-record", GameSort.PeakMonth))!.Peak).IsEqualTo(50);
        await Assert.That((await WindowOf(db, "long-record", GameSort.PeakQuarter))!.Peak).IsEqualTo(500);

        await Assert.That((await WindowOf(db, "long-record", GameSort.MedianWeek))!.Window)
            .IsEqualTo(SortWindows.Week);
        await Assert.That((await WindowOf(db, "long-record", GameSort.MedianQuarter))!.Window)
            .IsEqualTo(SortWindows.Quarter);
    }

    [Test]
    public async Task TheFarEndOfTheWindowKeepsTheWholeDayItFallsIn()
    {
        // The rollup buckets by whole day, so a midday cutoff is snapped back to cover the whole UTC
        // day it falls in rather than dropping part of it. `Seed.Now` is midday, which makes this
        // observable.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "edge", "Edge");
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        // Six hours older than the nominal cutoff, but in the same UTC day as it.
        await writer.WriteAsync(
            game, PresenceReading.Counted(7, FieldSource.Who), Now.AddDays(-7).AddHours(-6));

        // A day before that, which no snapping reaches.
        await writer.WriteAsync(
            game, PresenceReading.Counted(999, FieldSource.Who), Now.AddDays(-8).AddHours(-6));

        await Maintenance(db).RunAsync(Now);

        var window = await WindowOf(db, "edge", GameSort.PeakWeek);

        await Assert.That(window).IsNotNull();
        await Assert.That(window!.Samples).IsEqualTo(1);
        await Assert.That(window.Peak).IsEqualTo(7).Because("the day the cutoff fell in is kept whole, and the one before it is not");
    }

    [Test]
    public async Task AnOrderThatReadsNoWindowComputesNone()
    {
        // The aggregate scans the whole catalogue's presence series; a listing sorted by name should
        // not pay for a figure no surface reads.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "counted", "Counted");
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        await writer.WriteAsync(game, PresenceReading.Counted(3, FieldSource.Who), Now.AddMinutes(-10));

        var listed = await QueriesOn(db).ListAsync(new GameFilter { Sort = GameSort.Name });

        await Assert.That(listed.Single().PlayersOverWindow).IsNull();
    }

    /// <summary>
    /// The window one game came back with under one sort, read off the listing rather than off the
    /// query — so what is asserted is what a page would render.
    /// </summary>
    private static async Task<PresenceWindow?> WindowOf(TestDatabase db, string slug, GameSort sort) =>
        (await QueriesOn(db).ListAsync(new GameFilter { Sort = sort, IncludeArchived = true }))
            .Single(g => g.Slug == slug)
            .PlayersOverWindow;

    private static PresenceMaintenance Maintenance(TestDatabase db) =>
        new(new NpgsqlPresenceStore(db.DataSource),
            new NpgsqlPresenceRollupStore(db.DataSource),
            new PresenceRetentionOptions());

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}
