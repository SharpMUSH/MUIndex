using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The listing's growth direction against a real database (spec §9, the growth arrow and the
/// <c>trending</c> facet). Unlike <see cref="WindowSortPostgresTests"/>'s window, this is computed on
/// every listing rather than only when a window sort is active — see <c>GameSummary.Growth</c>.
/// </summary>
/// <remarks>
/// Not a two-bucket comparison (spec's earlier "this week against last week"): a fixed pair of weeks
/// has no answer for a game — or a whole deployment — younger than the buckets themselves, since the
/// older bucket is then simply empty no matter how its floor is scaled. A least-squares line through
/// each of a game's own daily medians works with as few as <see cref="GrowthTrend.MinimumDays"/>,
/// getting more confident as more days accumulate rather than being unusable until a fixed calendar
/// boundary passes.
/// </remarks>
public class DailyGrowthPostgresTests
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
    public async Task ARisingSeriesTrendsUp()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "rising", "Rising");

        await WriteDayAsync(db, game, Today(-13), count: 10);
        await WriteDayAsync(db, game, Today(-6), count: 15);
        await WriteDayAsync(db, game, Today(0), count: 20);

        await Assert.That(await GrowthOf(db, "rising")).IsEqualTo(GrowthDirection.Up);
    }

    [Test]
    public async Task TheGrowthChangeFractionIsPopulatedAlongsideTheDirection()
    {
        // GameSummary.GrowthChange is what a listing row's own percentage reads — a bare direction
        // says "up" without saying by how much.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "rising", "Rising");

        await WriteDayAsync(db, game, Today(-13), count: 10);
        await WriteDayAsync(db, game, Today(-6), count: 15);
        await WriteDayAsync(db, game, Today(0), count: 20);

        var listing = await QueriesOn(db).ListAsync(new GameFilter { IncludeArchived = true });
        var row = listing.Single(g => g.Slug == "rising");

        await Assert.That(row.Growth).IsEqualTo(GrowthDirection.Up);
        await Assert.That(row.GrowthChange).IsNotNull();
        await Assert.That(row.GrowthChange!.Value).IsGreaterThan(0.10);
    }

    [Test]
    public async Task AFallingSeriesTrendsDown()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "falling", "Falling");

        await WriteDayAsync(db, game, Today(-13), count: 20);
        await WriteDayAsync(db, game, Today(-6), count: 15);
        await WriteDayAsync(db, game, Today(0), count: 10);

        await Assert.That(await GrowthOf(db, "falling")).IsEqualTo(GrowthDirection.Down);
    }

    [Test]
    public async Task ASeriesWithinTenPercentEitherWayIsSteady()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "flat", "Flat");

        await WriteDayAsync(db, game, Today(-13), count: 20);
        await WriteDayAsync(db, game, Today(-6), count: 20);
        await WriteDayAsync(db, game, Today(0), count: 21);

        await Assert.That(await GrowthOf(db, "flat")).IsEqualTo(GrowthDirection.Steady);
    }

    [Test]
    public async Task ADayWithFewerThanSixSamplesDoesNotContributeItsOwnMedian()
    {
        // Only two days clear the per-day sample floor here (day -13 and day 0); the thin middle day
        // is dropped, not folded into a pooled figure — which leaves this game one day short of the
        // three a line needs, so it has no direction at all rather than a distorted one.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "thin", "Thin");

        await WriteDayAsync(db, game, Today(-13), count: 10);
        await WriteDayAsync(db, game, Today(-6), count: 999, samples: 5);
        await WriteDayAsync(db, game, Today(0), count: 20);

        await Assert.That(await GrowthOf(db, "thin")).IsNull();
    }

    [Test]
    public async Task FewerThanThreeDaysOfHistoryHasNoDirection()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "new", "New");

        await WriteDayAsync(db, game, Today(-1), count: 10);
        await WriteDayAsync(db, game, Today(0), count: 20);

        await Assert.That(await GrowthOf(db, "new")).IsNull();
    }

    [Test]
    public async Task ExactlyThreeDaysOfHistoryIsEnoughForADirection()
    {
        // The minimum a line can be fit through at all — proof against a real database that a young
        // game is not stuck waiting for a fixed calendar boundary it has not reached yet.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "toddler", "Toddler");

        await WriteDayAsync(db, game, Today(-2), count: 10);
        await WriteDayAsync(db, game, Today(-1), count: 15);
        await WriteDayAsync(db, game, Today(0), count: 20);

        await Assert.That(await GrowthOf(db, "toddler")).IsEqualTo(GrowthDirection.Up);
    }

    [Test]
    public async Task ACatalogueRowFarOlderThanItsMeasurementHistoryStillGetsADirection()
    {
        // The backfill-vs-crawl-catchup gap that broke the two-bucket design entirely: a game's row
        // can be a year old while real measurement only goes back a few days. The daily-medians query
        // never reads first_seen_at at all, so this is no longer a special case to get right.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "backfilled", "Backfilled", firstSeenAt: Now.AddYears(-1));

        await WriteDayAsync(db, game, Today(-4), count: 10);
        await WriteDayAsync(db, game, Today(-2), count: 15);
        await WriteDayAsync(db, game, Today(0), count: 20);

        await Assert.That(await GrowthOf(db, "backfilled")).IsEqualTo(GrowthDirection.Up);
    }

    [Test]
    public async Task ADayOutsideTheFourteenDaySpanDoesNotContribute()
    {
        // Fifteen days back is outside GrowthTrend.Span — only the three inside it may enter the
        // fit, so an outlier just beyond the edge cannot pull the line toward it.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "edge", "Edge");

        await WriteDayAsync(db, game, Today(-15), count: 999);
        await WriteDayAsync(db, game, Today(-13), count: 10);
        await WriteDayAsync(db, game, Today(-6), count: 15);
        await WriteDayAsync(db, game, Today(0), count: 20);

        await Assert.That(await GrowthOf(db, "edge")).IsEqualTo(GrowthDirection.Up);
    }

    private static async Task<GrowthDirection?> GrowthOf(TestDatabase db, string slug)
    {
        var listing = await QueriesOn(db).ListAsync(new GameFilter { IncludeArchived = true });

        return listing.Single(g => g.Slug == slug).Growth;
    }
}
