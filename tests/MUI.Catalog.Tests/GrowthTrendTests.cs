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

    [Test]
    public async Task ABigGameGainingManyPlayersOutranksATinyOneThatMerelyDoubled()
    {
        // The whole reason this figure is players rather than a percentage. As a fraction the small
        // game wins by a mile — 1 to 3 is +200%, 50 to 100 only +100% — which reads as though two
        // extra players were the larger event. Ranked on the players actually gained, they order the
        // way a reader means the question.
        var tiny = new[] { On(0, 1), On(1, 2), On(2, 3) };
        var big = new[] { On(0, 50), On(1, 75), On(2, 100) };

        await Assert.That(GrowthTrend.ChangePlayers(tiny)).IsEqualTo(2);
        await Assert.That(GrowthTrend.ChangePlayers(big)).IsEqualTo(50);
        await Assert.That(GrowthTrend.ChangeFraction(tiny)!.Value)
            .IsGreaterThan(GrowthTrend.ChangeFraction(big)!.Value);
    }

    [Test]
    public async Task APartialPlayerRoundsAwayFromZeroSoARealMoveNeverPrintsAsNone()
    {
        // Four flat-ish days ending one player higher fit a line rising 0.9 of a player across the
        // three days it spans — still a rise, and the arrow beside it says so, so it may not print
        // "0" and contradict its own glyph. Rounded away from zero in both directions, so the mirror
        // series falls to -1 rather than being rounded towards nothing.
        await Assert.That(GrowthTrend.ChangePlayers([On(0, 10), On(1, 10), On(2, 10), On(3, 11)])).IsEqualTo(1);
        await Assert.That(GrowthTrend.ChangePlayers([On(0, 11), On(1, 11), On(2, 11), On(3, 10)])).IsEqualTo(-1);
    }

    [Test]
    public async Task ChangePlayersReadsTheSameFitTheFractionDoes()
    {
        // Both numbers come off one fitted line: the fraction is that line's rise over the series'
        // own average level, so multiplying it back by that level has to land on the same rise.
        var days = new[] { On(0, 10), On(1, 15), On(2, 20) };

        // Mean 15, fraction 2/3 — a rise of ten players across the two days the line spans.
        await Assert.That(GrowthTrend.ChangeFraction(days)!.Value).IsEqualTo(2.0 / 3.0).Within(0.0001);
        await Assert.That(GrowthTrend.ChangePlayers(days)).IsEqualTo(10);
    }

    [Test]
    public async Task ChangePlayersIsNullBelowTheMinimumJustLikeTheFraction() =>
        await Assert.That(GrowthTrend.ChangePlayers([On(0, 10), On(1, 20)])).IsNull();

    [Test]
    public async Task AFlatSeriesGainsNoPlayersRatherThanRoundingUpToOne() =>
        // Away-from-zero rounding may not manufacture a player out of a line that did not move.
        await Assert.That(GrowthTrend.ChangePlayers([On(0, 10), On(1, 10), On(2, 10)])).IsEqualTo(0);
}
