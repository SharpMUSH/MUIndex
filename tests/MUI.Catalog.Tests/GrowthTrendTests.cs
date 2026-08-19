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
}
