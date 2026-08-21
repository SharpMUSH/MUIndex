using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The rankings page's trending board against a real database — a line fitted through each game's
/// own daily medians over its last <see cref="GrowthTrend.Span"/>, independent of whichever window
/// <c>/rankings</c> was asked to show (spec §9).
/// </summary>
public class TrendingThisWeekPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    private static NpgsqlGameQueries QueriesOn(TestDatabase db) =>
        new(db.DataSource) { Clock = () => Now };

    private static DateOnly Today(int offset) => DateOnly.FromDateTime(Now.AddDays(offset).UtcDateTime);

    private static async Task WriteDayAsync(TestDatabase db, Guid game, DateOnly day, int count, int samples = 24)
    {
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        for (var i = 0; i < samples; i++)
        {
            await writer.WriteAsync(game, PresenceReading.Counted(count, FieldSource.Who), start.AddHours(i * 6.0 / samples));
        }
    }

    [Test]
    public async Task ARisingSeriesAppearsWithTheRealMeasuredEndpoints()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "rising", "Rising");

        await WriteDayAsync(db, game, Today(-6), count: 10);
        await WriteDayAsync(db, game, Today(-3), count: 15);
        await WriteDayAsync(db, game, Today(0), count: 20);

        var trending = (await QueriesOn(db).RankingsAsync()).TrendingThisWeek;

        await Assert.That(trending.Any(g => g.Slug == "rising")).IsTrue();

        var row = trending.Single(g => g.Slug == "rising");

        await Assert.That(row.EarliestMedian).IsEqualTo(10);
        await Assert.That(row.LatestMedian).IsEqualTo(20);
    }

    [Test]
    public async Task TheBiggestGainerRanksFirst()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var bigGain = await Seed.GameAsync(db, "big-gain", "Big Gain");
        var smallGain = await Seed.GameAsync(db, "small-gain", "Small Gain");

        // Big Gain: 10 -> 20 (+100%). Small Gain: 10 -> 12 (+20%). Both clear the 10% steady band,
        // so both appear, but Big Gain's larger change has to rank first.
        await WriteDayAsync(db, bigGain, Today(-6), count: 10);
        await WriteDayAsync(db, bigGain, Today(-3), count: 15);
        await WriteDayAsync(db, bigGain, Today(0), count: 20);
        await WriteDayAsync(db, smallGain, Today(-6), count: 10);
        await WriteDayAsync(db, smallGain, Today(-3), count: 11);
        await WriteDayAsync(db, smallGain, Today(0), count: 12);

        var trending = (await QueriesOn(db).RankingsAsync()).TrendingThisWeek;

        await Assert.That(trending[0].Slug).IsEqualTo("big-gain").Because("the larger change ranks first");
        await Assert.That(trending[1].Slug).IsEqualTo("small-gain");
    }

    [Test]
    public async Task ASteadyOrDecliningGameDoesNotAppearOnTheBoard()
    {
        // The board reads the same classification the listing row's arrow does
        // (GrowthTrend.Of == Up), not a bare sort on the numbers — a steady or falling game must
        // never appear under a heading that says "trending".
        await using var db = await PostgresFixture.MigratedAsync();
        var flat = await Seed.GameAsync(db, "flat", "Flat");
        var falling = await Seed.GameAsync(db, "falling", "Falling");

        await WriteDayAsync(db, flat, Today(-6), count: 20);
        await WriteDayAsync(db, flat, Today(-3), count: 20);
        await WriteDayAsync(db, flat, Today(0), count: 20);
        await WriteDayAsync(db, falling, Today(-6), count: 20);
        await WriteDayAsync(db, falling, Today(-3), count: 15);
        await WriteDayAsync(db, falling, Today(0), count: 10);

        var trending = (await QueriesOn(db).RankingsAsync()).TrendingThisWeek;

        await Assert.That(trending.Any(g => g.Slug == "flat")).IsFalse();
        await Assert.That(trending.Any(g => g.Slug == "falling")).IsFalse();
    }

    [Test]
    public async Task ADayWithTooFewSamplesIsExcludedFromTheSeriesRatherThanSkewingIt()
    {
        // A middle day with only 3 samples (below the 6-sample floor) is dropped entirely, not folded
        // in with an outlier count — which also drops this game below the 3-day minimum, since only
        // two days are left once it goes.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "thin", "Thin");

        await WriteDayAsync(db, game, Today(-13), count: 10);
        await WriteDayAsync(db, game, Today(-7), count: 999, samples: 3);
        await WriteDayAsync(db, game, Today(0), count: 20);

        var trending = (await QueriesOn(db).RankingsAsync()).TrendingThisWeek;

        await Assert.That(trending.Any(g => g.Slug == "thin")).IsFalse();
    }

    [Test]
    public async Task ExactlyThreeDaysOfHistoryIsEnoughToAppear()
    {
        // The minimum a line can be fit through at all — proof against a real database that a young
        // game is not stuck waiting for a fixed calendar boundary it has not reached yet.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "toddler", "Toddler", firstSeenAt: Now.AddYears(-1));

        await WriteDayAsync(db, game, Today(-2), count: 10);
        await WriteDayAsync(db, game, Today(-1), count: 15);
        await WriteDayAsync(db, game, Today(0), count: 20);

        var trending = (await QueriesOn(db).RankingsAsync()).TrendingThisWeek;

        await Assert.That(trending.Any(g => g.Slug == "toddler")).IsTrue();
    }

    [Test]
    public async Task ACatalogueRowFarOlderThanItsMeasurementHistoryStillAppears()
    {
        // The backfill-vs-crawl-catchup gap that broke the two-bucket design entirely: a game's row
        // can be a year old while real measurement only goes back a few days. The daily-medians query
        // never reads first_seen_at at all, so this is no longer a special case to get right.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "backfilled", "Backfilled", firstSeenAt: Now.AddYears(-1));

        await WriteDayAsync(db, game, Today(-4), count: 10);
        await WriteDayAsync(db, game, Today(-2), count: 15);
        await WriteDayAsync(db, game, Today(0), count: 20);

        var trending = (await QueriesOn(db).RankingsAsync()).TrendingThisWeek;

        await Assert.That(trending.Any(g => g.Slug == "backfilled")).IsTrue();
    }
}
