namespace MUI.Catalog.Tests;

/// <summary>
/// The fold from two medians into a direction — the whole of what the growth arrow and the
/// <c>trending</c> facet agree on, kept in one place so a page and a filter can never read the same
/// two numbers as different directions.
/// </summary>
/// <remarks>
/// Below the sample floor a window is not a measurement of anything (spec's median sort rule) — a
/// game probed four times has no median worth ranking, and no direction worth drawing either. The
/// floor applies to both windows independently: a game with plenty of history this week and almost
/// none last week has no *prior* to compare against, so it has no direction, not an assumed "up".
/// </remarks>
public class GrowthTrendTests
{
    [Test]
    public async Task MoreThanTenPercentHigherIsUp() =>
        await Assert.That(GrowthTrend.Of(median: 12, samples: 30, priorMedian: 10, priorSamples: 30))
            .IsEqualTo(GrowthDirection.Up);

    [Test]
    public async Task MoreThanTenPercentLowerIsDown() =>
        await Assert.That(GrowthTrend.Of(median: 8, samples: 30, priorMedian: 10, priorSamples: 30))
            .IsEqualTo(GrowthDirection.Down);

    [Test]
    public async Task WithinTenPercentEitherWayIsSteady() =>
        await Assert.That(GrowthTrend.Of(median: 21, samples: 30, priorMedian: 20, priorSamples: 30))
            .IsEqualTo(GrowthDirection.Steady);

    [Test]
    public async Task IdenticalMediansAreSteady() =>
        await Assert.That(GrowthTrend.Of(median: 5, samples: 30, priorMedian: 5, priorSamples: 30))
            .IsEqualTo(GrowthDirection.Steady);

    [Test]
    public async Task BothMediansAtZeroIsSteadyRatherThanADivideByZero() =>
        await Assert.That(GrowthTrend.Of(median: 0, samples: 30, priorMedian: 0, priorSamples: 30))
            .IsEqualTo(GrowthDirection.Steady);

    [Test]
    public async Task TooFewCurrentSamplesHasNoDirection() =>
        await Assert.That(GrowthTrend.Of(median: 12, samples: 5, priorMedian: 6, priorSamples: 30))
            .IsNull();

    [Test]
    public async Task TooFewPriorSamplesHasNoDirection() =>
        // A game with a full week of fresh history and almost none the week before has nothing to
        // compare against — not an assumed rise from "as good as zero".
        await Assert.That(GrowthTrend.Of(median: 12, samples: 30, priorMedian: 6, priorSamples: 5))
            .IsNull();

    [Test]
    public async Task NoPriorWindowAtAllHasNoDirection() =>
        // A game newer than one window-length has no "last week" to have been trending from.
        await Assert.That(GrowthTrend.Of(median: 12, samples: 30, priorMedian: null, priorSamples: null))
            .IsNull();

    [Test]
    public async Task NoCurrentWindowAtAllHasNoDirection() =>
        await Assert.That(GrowthTrend.Of(median: null, samples: null, priorMedian: 12, priorSamples: 30))
            .IsNull();

    [Test]
    public async Task ARequiredFloorBelowTheDefaultLetsAThinnerWindowRank() =>
        await Assert.That(GrowthTrend.Of(
                median: 12, samples: 10, priorMedian: 10, priorSamples: 10,
                requiredSamples: 8, requiredPriorSamples: 8))
            .IsEqualTo(GrowthDirection.Up);

    [Test]
    public async Task DefaultFloorsStillApplyWhenNotOverridden() =>
        await Assert.That(GrowthTrend.Of(median: 12, samples: 10, priorMedian: 10, priorSamples: 10))
            .IsNull();
}

/// <summary>
/// The sample floor a window needs before its median may enter a comparison at all — scaled down
/// from the full-week floor by how much of the window a young game could actually have been probed
/// in, so a game short of two weeks old is not permanently <c>null</c> (spec's "best effort" ask).
/// </summary>
public class GrowthTrendRequiredSamplesTests
{
    private static readonly DateTimeOffset WindowFrom = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowTo = WindowFrom + SortWindows.Week;

    [Test]
    public async Task AGameSeenWellBeforeTheWindowNeedsTheFullFloor() =>
        await Assert.That(GrowthTrend.RequiredSamples(WindowFrom, WindowTo, WindowFrom.AddYears(-1)))
            .IsEqualTo(SortWindows.MinimumSamples);

    [Test]
    public async Task NoFirstSeenDateStillNeedsTheFullFloor() =>
        // Every real row has the column set (not nullable); a caller with no better data must not be
        // handed a lowered floor it never earned.
        await Assert.That(GrowthTrend.RequiredSamples(WindowFrom, WindowTo, null))
            .IsEqualTo(SortWindows.MinimumSamples);

    [Test]
    public async Task AGameSeenPartwayThroughTheWindowNeedsAProportionallyScaledFloor() =>
        // Only the window's last 3 of 7 days were even possible to probe in.
        await Assert.That(GrowthTrend.RequiredSamples(WindowFrom, WindowTo, WindowTo.AddDays(-3)))
            .IsEqualTo((int)Math.Ceiling(SortWindows.MinimumSamples * 3.0 / 7));

    [Test]
    public async Task AGameSeenLessThanTwoDaysBeforeTheWindowEndsIsUnreachable() =>
        // There is no such thing as a trend read off a few hours of data — no floor is low enough.
        await Assert.That(GrowthTrend.RequiredSamples(WindowFrom, WindowTo, WindowTo.AddHours(-6)))
            .IsEqualTo(int.MaxValue);

    [Test]
    public async Task AGameNotYetSeenWhenTheWindowEndsIsUnreachable() =>
        await Assert.That(GrowthTrend.RequiredSamples(WindowFrom, WindowTo, WindowTo.AddDays(1)))
            .IsEqualTo(int.MaxValue);
}
