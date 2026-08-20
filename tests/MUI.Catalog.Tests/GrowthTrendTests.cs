namespace MUI.Catalog.Tests;

/// <summary>
/// The fold from a game's own daily medians into a direction — the whole of what the growth arrow
/// and the <c>trending</c> facet agree on, kept in one place so a page and a filter can never read
/// the same series as different directions.
/// </summary>
/// <remarks>
/// A least-squares line through whatever days a game has, not a two-bucket comparison — a game with
/// three days of measured history has three points to fit a line through and gets a (noisy) answer,
/// rather than waiting on a fixed calendar boundary that a young game or a young deployment cannot
/// yet have crossed.
/// </remarks>
public class GrowthTrendTests
{
    private static readonly DateOnly Day0 = new(2026, 7, 1);

    private static DailyMedian On(int offset, int median, int samples = 30) =>
        new(Day0.AddDays(offset), median, samples);

    [Test]
    public async Task AClearRiseAcrossThreeDaysIsUp() =>
        await Assert.That(GrowthTrend.Of([On(0, 10), On(1, 15), On(2, 20)]))
            .IsEqualTo(GrowthDirection.Up);

    [Test]
    public async Task AClearFallAcrossThreeDaysIsDown() =>
        await Assert.That(GrowthTrend.Of([On(0, 20), On(1, 15), On(2, 10)]))
            .IsEqualTo(GrowthDirection.Down);

    [Test]
    public async Task AFlatLineIsSteady() =>
        await Assert.That(GrowthTrend.Of([On(0, 10), On(1, 10), On(2, 10)]))
            .IsEqualTo(GrowthDirection.Steady);

    [Test]
    public async Task AllZeroMediansIsSteadyRatherThanADivideByZero() =>
        await Assert.That(GrowthTrend.Of([On(0, 0), On(1, 0), On(2, 0)]))
            .IsEqualTo(GrowthDirection.Steady);

    [Test]
    public async Task FewerThanThreeDaysHasNoDirection() =>
        await Assert.That(GrowthTrend.Of([On(0, 10), On(1, 20)])).IsNull();

    [Test]
    public async Task NoDaysAtAllHasNoDirection() =>
        await Assert.That(GrowthTrend.Of([])).IsNull();

    [Test]
    public async Task ExactlyThreeDaysIsEnoughToFitALine() =>
        // MinimumDays is 3, not 4 — a line through three points says something about the days
        // between its ends, even if the read gets more confident with more of them.
        await Assert.That(GrowthTrend.Of([On(0, 10), On(1, 10), On(2, 12)]))
            .IsNotEqualTo((GrowthDirection?)null);

    [Test]
    public async Task AGapBetweenMeasuredDaysUsesTheRealCalendarDistanceNotACompactedRank()
    {
        // Day 1 has no median at all (too few samples that day) — the line has to read the true
        // 10-day span between day 0 and day 10, not silently compact the gap into "three in a row".
        var days = new[] { On(0, 10), On(2, 14), On(10, 30) };
        var change = GrowthTrend.ChangeFraction(days);

        await Assert.That(change).IsNotNull();
        await Assert.That(Math.Abs(change!.Value - 20.0 / 18.0) < 0.0001).IsTrue();
        await Assert.That(GrowthTrend.Of(days)).IsEqualTo(GrowthDirection.Up);
    }

    [Test]
    public async Task TheOrderTheDaysArePassedInDoesNotMatter()
    {
        var sorted = new[] { On(0, 10), On(2, 14), On(10, 30) };
        var shuffled = new[] { On(10, 30), On(0, 10), On(2, 14) };

        await Assert.That(GrowthTrend.ChangeFraction(shuffled)).IsEqualTo(GrowthTrend.ChangeFraction(sorted));
    }

    [Test]
    public async Task ChangeFractionAgreesWithOfOnTheSameSeries()
    {
        var days = new[] { On(0, 10), On(1, 15), On(2, 20) };
        var change = GrowthTrend.ChangeFraction(days);

        await Assert.That(change).IsNotNull();
        await Assert.That(Math.Abs(change!.Value - 2.0 / 3.0) < 0.0001).IsTrue();
        await Assert.That(GrowthTrend.Of(days)).IsEqualTo(GrowthDirection.Up);
    }

    [Test]
    public async Task ADayBelowTheSampleFloorIsExcludedEvenWhenTheCallerDidNotFilterItOut()
    {
        // DailyMediansAsync is the only production caller today, and its own SQL already excludes a
        // thin day before this ever sees it — but the floor is part of this method's own contract
        // (rule 4: a day the crawler barely touched contributes no median), not something every future
        // caller must remember to re-implement. Three days, one of them a one-sample outlier: with the
        // floor enforced here, only two qualifying days remain — below MinimumDays — so this returns
        // null rather than a value skewed by the outlier.
        var days = new[] { On(0, 10), On(1, 999, samples: 1), On(2, 12) };

        await Assert.That(GrowthTrend.ChangeFraction(days)).IsNull();
        await Assert.That(GrowthTrend.Of(days)).IsNull();
    }

    [Test]
    public async Task ThreeRowsAcrossOnlyTwoDistinctDaysCountAsTwoDaysNotThree()
    {
        // A second row for a day already in the series (never produced by DailyMediansAsync's own
        // GROUP BY, but not this method's job to assume) must not let that day cast an extra vote in
        // the fit, or let three rows across two real days pass a floor stated in days.
        var days = new[] { On(0, 10), On(0, 14), On(1, 12) };

        await Assert.That(GrowthTrend.ChangeFraction(days)).IsNull();
        await Assert.That(GrowthTrend.Of(days)).IsNull();
    }

    [Test]
    public async Task ChangeFractionIsNullBelowTheMinimumJustLikeOf() =>
        await Assert.That(GrowthTrend.ChangeFraction([On(0, 10), On(1, 20)])).IsNull();
}
