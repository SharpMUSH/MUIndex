using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The rankings page's "trending this week" board against a real database — always the week span,
/// independent of whichever window <c>/rankings</c> was asked to show (spec §9).
/// </summary>
public class TrendingThisWeekPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    private static NpgsqlGameQueries QueriesOn(TestDatabase db) =>
        new(db.DataSource) { Clock = () => Now };

    private static async Task WriteWeekAsync(
        TestDatabase db, Guid game, DateTimeOffset weekStart, int count, int samples = 24)
    {
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        for (var i = 0; i < samples; i++)
        {
            await writer.WriteAsync(
                game, PresenceReading.Counted(count, FieldSource.Who), weekStart.AddHours(i * 6.0 / samples));
        }
    }

    [Test]
    public async Task TheBiggestGainerRanksFirst()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var bigGain = await Seed.GameAsync(db, "big-gain", "Big Gain");
        var smallGain = await Seed.GameAsync(db, "small-gain", "Small Gain");

        // Big Gain: 10 -> 20 (+100%). Small Gain: 10 -> 12 (+20%). Both clear the 10% steady band,
        // so both appear, but Big Gain's larger change has to rank first.
        await WriteWeekAsync(db, bigGain, Now.AddDays(-13), count: 10);
        await WriteWeekAsync(db, bigGain, Now.AddDays(-6), count: 20);
        await WriteWeekAsync(db, smallGain, Now.AddDays(-13), count: 10);
        await WriteWeekAsync(db, smallGain, Now.AddDays(-6), count: 12);

        var rankings = await QueriesOn(db).RankingsAsync();
        var trending = rankings.TrendingThisWeek;

        await Assert.That(trending[0].Slug).IsEqualTo("big-gain").Because("the larger change ranks first");
        await Assert.That(trending[1].Slug).IsEqualTo("small-gain");
        await Assert.That(trending[0].Median).IsEqualTo(20);
        await Assert.That(trending[0].PriorMedian).IsEqualTo(10);
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

        await WriteWeekAsync(db, flat, Now.AddDays(-13), count: 20);
        await WriteWeekAsync(db, flat, Now.AddDays(-6), count: 20);
        await WriteWeekAsync(db, falling, Now.AddDays(-13), count: 20);
        await WriteWeekAsync(db, falling, Now.AddDays(-6), count: 10);

        var trending = (await QueriesOn(db).RankingsAsync()).TrendingThisWeek;

        await Assert.That(trending.Any(g => g.Slug == "flat")).IsFalse();
        await Assert.That(trending.Any(g => g.Slug == "falling")).IsFalse();
    }

    [Test]
    public async Task FewerThanTwentyFourSamplesEitherWeekIsExcludedFromTheBoard()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "thin", "Thin");

        await WriteWeekAsync(db, game, Now.AddDays(-13), count: 10, samples: 24);
        await WriteWeekAsync(db, game, Now.AddDays(-6), count: 20, samples: 5);

        var trending = (await QueriesOn(db).RankingsAsync()).TrendingThisWeek;

        await Assert.That(trending.Any(g => g.Slug == "thin")).IsFalse();
    }

    [Test]
    public async Task AGameYoungerThanTwoWeeksCanStillAppearOnTheBoardAtAScaledFloor()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "toddler", "Toddler", firstSeenAt: Now.AddDays(-10));

        await WriteWeekAsync(db, game, Now.AddDays(-9), count: 10, samples: 11);
        await WriteWeekAsync(db, game, Now.AddDays(-6), count: 20);

        var trending = (await QueriesOn(db).RankingsAsync()).TrendingThisWeek;

        await Assert.That(trending.Any(g => g.Slug == "toddler")).IsTrue();
    }
}
