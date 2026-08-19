using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The listing's growth direction against a real database (spec §9, the growth arrow and the
/// <c>trending</c> facet). Unlike <see cref="WindowSortPostgresTests"/>'s window, this is computed on
/// every listing rather than only when a window sort is active — see <c>GameSummary.Growth</c>.
/// </summary>
/// <remarks>
/// The load-bearing case is the boundary between the two weeks: a sample written the instant the
/// current week starts must count once, for the current week, and not bleed into the prior one — a
/// query that double-counted or dropped that instant would silently understate or overstate both
/// medians without ever failing on sample count alone.
/// </remarks>
public class WeeklyGrowthPostgresTests
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
    public async Task AGameCountingMoreThanTenPercentHigherThisWeekTrendsUp()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "rising", "Rising");

        await WriteWeekAsync(db, game, Now.AddDays(-13), count: 10);
        await WriteWeekAsync(db, game, Now.AddDays(-6), count: 20);

        var growth = await GrowthOf(db, "rising");

        await Assert.That(growth).IsEqualTo(GrowthDirection.Up);
    }

    [Test]
    public async Task AGameCountingMoreThanTenPercentLowerThisWeekTrendsDown()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "falling", "Falling");

        await WriteWeekAsync(db, game, Now.AddDays(-13), count: 20);
        await WriteWeekAsync(db, game, Now.AddDays(-6), count: 10);

        var growth = await GrowthOf(db, "falling");

        await Assert.That(growth).IsEqualTo(GrowthDirection.Down);
    }

    [Test]
    public async Task AGameWithinTenPercentEitherWayIsSteady()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "flat", "Flat");

        await WriteWeekAsync(db, game, Now.AddDays(-13), count: 20);
        await WriteWeekAsync(db, game, Now.AddDays(-6), count: 21);

        var growth = await GrowthOf(db, "flat");

        await Assert.That(growth).IsEqualTo(GrowthDirection.Steady);
    }

    [Test]
    public async Task FewerThanTwentyFourSamplesEitherWeekHasNoDirection()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "thin", "Thin");

        await WriteWeekAsync(db, game, Now.AddDays(-13), count: 10, samples: 24);
        await WriteWeekAsync(db, game, Now.AddDays(-6), count: 20, samples: 5);

        await Assert.That(await GrowthOf(db, "thin")).IsNull();
    }

    [Test]
    public async Task AGameYoungerThanTwoWeeksHasNoPriorWeekAndNoDirection()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "new", "New");

        await WriteWeekAsync(db, game, Now.AddDays(-6), count: 10);

        await Assert.That(await GrowthOf(db, "new")).IsNull();
    }

    [Test]
    public async Task ASampleAtTheWeekBoundaryCountsOnceAndOnTheCurrentSide()
    {
        // The instant the current window's lower bound floors to — the same UTC-midnight snap every
        // other window boundary in this codebase uses (WindowSortPostgresTests's "keeps the whole
        // day" case). A sample there has to land on exactly one side of the split.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "edge", "Edge");
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        var boundary = new DateTimeOffset(
            DateOnly.FromDateTime((Now - SortWindows.Week).UtcDateTime).ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero);

        // 24 samples strictly before the boundary (prior week, count 10).
        for (var i = 0; i < 24; i++)
        {
            await writer.WriteAsync(
                game, PresenceReading.Counted(10, FieldSource.Who), boundary.AddDays(-6).AddHours(i));
        }

        // The boundary instant itself, plus 23 more strictly after it — 24 samples only if the
        // boundary one is attributed here (current week, count 20).
        await writer.WriteAsync(game, PresenceReading.Counted(20, FieldSource.Who), boundary);

        for (var i = 0; i < 23; i++)
        {
            await writer.WriteAsync(
                game, PresenceReading.Counted(20, FieldSource.Who), boundary.AddHours(i + 1));
        }

        // Off by even one sample on either side of the split drops the current week's tally below
        // the 24-sample floor and the direction to null — the discriminating outcome is Up, not
        // Steady or Down, since a misattributed boundary sample can't produce a wrong direction, only
        // a missing one.
        await Assert.That(await GrowthOf(db, "edge")).IsEqualTo(GrowthDirection.Up);
    }

    [Test]
    public async Task AGameMeasuredForLessThanTheWindowGetsAProportionallyScaledFloorInsteadOfNull()
    {
        // The catalogue row can be much older than the game's real measurement history — a backfilled
        // import predates the crawler catching up to it by weeks. What has to drive the floor is when
        // we actually started measuring (the earliest presence sample), not game.first_seen_at: only
        // the prior week's last 3.5 days have any presence data here, so 15 samples clears the scaled
        // floor (12) though it is far short of the full week's 24 — the "best effort" this game gets
        // rather than a permanent null.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "toddler", "Toddler", firstSeenAt: Now.AddYears(-1));

        await WriteWeekAsync(db, game, Now.AddDays(-11), count: 10, samples: 15);
        await WriteWeekAsync(db, game, Now.AddDays(-6), count: 20);

        await Assert.That(await GrowthOf(db, "toddler")).IsEqualTo(GrowthDirection.Up);
    }

    [Test]
    public async Task AWholeBatchYoungerThanTwoWeeksSplitsWhateverHistoryExistsInsteadOfLeavingThePriorWeekEmpty()
    {
        // The load-bearing case a floor-only fix cannot reach: every game's real measurement history
        // starts inside the fixed split's *current* week (day 6 back, not day 14), so the fixed prior
        // window — day 14 to day 7 — has exactly zero real samples no matter how far its floor
        // scales. The window itself has to move: split the 6 days of actual history into two
        // comparable halves instead, each with its own floor scaled off its own span (spec's "best
        // effort" ask, taken to its actual endpoint — a brand-new deployment's first fortnight).
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "sapling", "Sapling", firstSeenAt: Now.AddYears(-1));

        await WriteWeekAsync(db, game, Now.AddDays(-6), count: 10, samples: 15);
        await WriteWeekAsync(db, game, Now.AddDays(-2), count: 20);

        await Assert.That(await GrowthOf(db, "sapling")).IsEqualTo(GrowthDirection.Up);
    }

    [Test]
    public async Task AGameMeasuredForLessThanTwoDaysStillHasNoDirectionEvenAtTheScaledFloor()
    {
        // An old catalogue row (a year-old backfill) must not rescue this either — the floor scales
        // off measurement history, not the row's age.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "newborn", "Newborn", firstSeenAt: Now.AddYears(-1));

        await WriteWeekAsync(db, game, Now.AddDays(-1), count: 10, samples: 24);

        await Assert.That(await GrowthOf(db, "newborn")).IsNull();
    }

    private static async Task<GrowthDirection?> GrowthOf(TestDatabase db, string slug)
    {
        var listing = await QueriesOn(db).ListAsync(new GameFilter { IncludeArchived = true });

        return listing.Single(g => g.Slug == slug).Growth;
    }
}
