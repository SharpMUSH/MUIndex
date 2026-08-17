using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The listing's window sorts against a real database (spec §9).
/// </summary>
/// <remarks>
/// <para>
/// Four things can go wrong here and each of them fails silently on a page that still renders. The
/// typical count can be computed as a mean, which one linked evening drags upward — the reason
/// migration 0019 put a distribution in the rollup at all. The window can be read from raw samples
/// alone, which shortens without saying so the day retention drops its first partition. An
/// uncountable probe can be folded in as a zero, which is rule 4 broken where the arithmetic makes it
/// easy. And the walk can land one element high on an even sample count, which shipped once on
/// <c>/rankings</c> already.
/// </para>
/// <para>
/// So every test here is about the arithmetic rather than the plumbing, and the load-bearing one is
/// that a window spanning the rollup watermark returns the same figures as the same samples read
/// whole.
/// </para>
/// </remarks>
public class WindowSortPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    private static NpgsqlGameQueries QueriesOn(TestDatabase db) =>
        new(db.DataSource) { Clock = () => Now };

    [Test]
    public async Task TheTypicalCountIsAMedianAndOneBigEveningDoesNotMoveIt()
    {
        // The reason 0019 put a distribution in the rollup, applied to the listing. Forty probes read
        // ten and one read a hundred: the mean is 12.2, the mean of the two daily means is 55, and
        // the number a reader asking "how busy is this normally" wants is 10. Only the last of those
        // is a count somebody's server actually reported.
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

        // The evening is not thrown away, it is reported as what it was: the largest single reading.
        await Assert.That(window.Peak).IsEqualTo(100);
    }

    [Test]
    public async Task AnEvenSampleCountTakesTheLowerMiddleValueAndNotTheOneAboveIt()
    {
        // `ceil(n / 2.0)` and not `(n + 1) / 2`. `sum()` over a bigint returns numeric, so the
        // division is exact and four samples ask for element 2 — which the second value satisfies.
        // Integer division would ask for 2 as well; the failure is subtler and shipped on /rankings
        // once: it is the numeric division being written as if it rounded. Four readings of 1, 3, 9
        // and 27 have a discrete median of 3, and never 9 and never 6.
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
        // The whole reason the query is a union. Below the watermark only the rollup is guaranteed to
        // survive (§5.2 authorises dropping the raw partitions it has consumed); above it only the
        // raw rows exist, because the rollup consumes whole elapsed days and today is not one. Read
        // either half alone and the figures are wrong in a direction nothing on the page reveals.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "spanning", "Spanning");
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        // Three days ago, and this morning. The maintenance pass rolls the first and cannot roll the
        // second, so one sample lands on each side of the boundary.
        await writer.WriteAsync(game, PresenceReading.Counted(4, FieldSource.Who), Now.AddDays(-3));
        await Maintenance(db).RunAsync(Now.AddDays(-1));
        await writer.WriteAsync(game, PresenceReading.Counted(8, FieldSource.Who), Now.AddMinutes(-30));

        var window = await WindowOf(db, "spanning", GameSort.MedianWeek);

        await Assert.That(window).IsNotNull();
        await Assert.That(window!.Samples).IsEqualTo(2).Because("one sample from each side of the watermark");

        // Two readings, so the walk takes the lower — and a raw sample and a rolled-up one have to
        // enter that walk as the same kind of thing, which is what the shared frequency table is for.
        await Assert.That(window.Median).IsEqualTo(4);
        await Assert.That(window.Peak).IsEqualTo(8);
    }

    [Test]
    public async Task AProbeThatCouldNotCountIsNotCountedAsAZero()
    {
        // Rule 4 where the arithmetic makes breaking it easy: an uncountable probe entering the
        // distribution as a zero would drag the median of a game whose DOING header is past our
        // parser straight to the bottom of the listing while it runs perfectly well.
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
        // Absent, and emphatically not present with zeroes in it. The listing puts such a game below
        // the break with a sentence saying which of the two reasons it was, and a row of noughts
        // would say the third thing that is not true.
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
        // Three sorts, three spans, and a sample outside a span may not be in it. Without this the
        // "7 days" on the control is decoration and every window is the same window.
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
        // The rollup is bucketed by day and a bucket is all or nothing, so a cutoff at midday drops
        // the whole day it lands in — a seventh of a seven-day window, thrown away to make the label
        // exact. The window is snapped back to a whole UTC day instead, so it covers at least what it
        // says and never less. `Seed.Now` is midday, which is what makes this observable at all.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "edge", "Edge");
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        // Six hours older than the nominal seven-day cutoff, and in the same UTC day as it.
        await writer.WriteAsync(
            game, PresenceReading.Counted(7, FieldSource.Who), Now.AddDays(-7).AddHours(-6));

        // And one the day before that, which no snapping reaches.
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
        // The aggregate is a scan over the presence series of the whole catalogue. A listing sorted
        // by name that computed it anyway would pay for a figure no surface reads.
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
}
