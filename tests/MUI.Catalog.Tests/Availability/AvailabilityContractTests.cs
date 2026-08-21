using MUI.Catalog;
using MUI.Catalog.Tests.Support;

namespace MUI.Catalog.Tests;

/// <summary>
/// Intervals rather than samples (spec §5.3), and the arithmetic that reads them.
/// </summary>
public class AvailabilityContractTests
{
    private static readonly Guid Game = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    private static AvailabilityInterval Span(
        AvailabilityState state,
        double fromDaysAgo,
        double? toDaysAgo,
        FailureCause cause = FailureCause.None) =>
        new()
        {
            GameId = Game,
            State = state,
            FromAt = Now - TimeSpan.FromDays(fromDaysAgo),
            ToAt = toDaysAgo is null ? null : Now - TimeSpan.FromDays(toDaysAgo.Value),
            Cause = cause,
        };

    [Test]
    public async Task AGameReachableThroughoutIsOneOpenRow()
    {
        var store = new InMemoryAvailabilityStore();
        await store.OpenAsync(Span(AvailabilityState.Reachable, 1095, null));

        var open = await store.OpenIntervalAsync(Game);

        await Assert.That(store.Intervals).Count().IsEqualTo(1);
        await Assert.That(open!.IsOpen).IsTrue();
        await Assert.That(open.DurationAt(Now).TotalDays).IsEqualTo(1095).Within(0.001);
    }

    [Test]
    public async Task ReachableFractionIsNullWhenNothingOverlapsTheWindow()
    {
        // A game we have never probed is not "0% reachable" — it is unmeasured. Returning zero here
        // would put a fact we never established onto a public page.
        var fraction = Reachability.FractionReachable([], TimeSpan.FromDays(90), Now);

        await Assert.That(fraction).IsNull();
    }

    [Test]
    public async Task ReachableFractionCountsOnlyTheObservedPartOfTheWindow()
    {
        // Reachable for the last 30 days, unreachable for the 10 before that, nothing known before.
        // 30 of 40 observed days, not 30 of 90.
        List<AvailabilityInterval> intervals =
        [
            Span(AvailabilityState.Unreachable, 40, 30, FailureCause.Refused),
            Span(AvailabilityState.Reachable, 30, null),
        ];

        var fraction = Reachability.FractionReachable(intervals, TimeSpan.FromDays(90), Now);

        await Assert.That(fraction!.Value).IsEqualTo(0.75).Within(0.001);
    }

    [Test]
    public async Task LongestOutageIsTheWorstUnreachableSpanNotTheirSum()
    {
        List<AvailabilityInterval> intervals =
        [
            Span(AvailabilityState.Unreachable, 60, 58, FailureCause.Timeout),
            Span(AvailabilityState.Reachable, 58, 20),
            Span(AvailabilityState.Unreachable, 20, 15, FailureCause.Dns),
            Span(AvailabilityState.Reachable, 15, null),
        ];

        var longest = Reachability.LongestOutage(intervals, TimeSpan.FromDays(90), Now);

        await Assert.That(longest!.Value.TotalDays).IsEqualTo(5).Within(0.001);
    }

    [Test]
    public async Task LongestOutageIsNullWhenTheGameWasNeverUnreachable()
    {
        var longest = Reachability.LongestOutage(
            [Span(AvailabilityState.Reachable, 90, null)],
            TimeSpan.FromDays(90),
            Now);

        await Assert.That(longest).IsNull();
    }

    [Test]
    public async Task CumulativeReachableIsSummedNotSpanned()
    {
        // Reachable for two years out of five: credited with two, which is what ArchivePolicy reads.
        List<AvailabilityInterval> intervals =
        [
            Span(AvailabilityState.Reachable, 1825, 1460),
            Span(AvailabilityState.Unreachable, 1460, 365, FailureCause.Dns),
            Span(AvailabilityState.Reachable, 365, 0),
        ];

        var cumulative = Reachability.CumulativeReachable(intervals, Now);

        await Assert.That(cumulative.TotalDays).IsEqualTo(730).Within(0.001);
    }

    [Test]
    public async Task DegradedIsNeitherReachableNorUnreachable()
    {
        // It exists because a connection that opens and never finishes negotiating is a real,
        // distinct observation — and the design draws it as its own bar.
        List<AvailabilityInterval> intervals = [Span(AvailabilityState.Degraded, 10, null)];

        var fraction = Reachability.FractionReachable(intervals, TimeSpan.FromDays(90), Now);
        var longest = Reachability.LongestOutage(intervals, TimeSpan.FromDays(90), Now);

        await Assert.That(fraction!.Value).IsEqualTo(0);
        await Assert.That(longest).IsNull();
    }

    [Test]
    public async Task ClosingAnIntervalLeavesItInTheHistoryRatherThanRemovingIt()
    {
        var store = new InMemoryAvailabilityStore();
        await store.OpenAsync(Span(AvailabilityState.Reachable, 10, null));

        await store.CloseAsync(Game, Now);

        var all = await store.ForGameAsync(Game);
        await Assert.That(all).Count().IsEqualTo(1);
        await Assert.That(all[0].IsOpen).IsFalse();
        await Assert.That(await store.OpenIntervalAsync(Game)).IsNull();
    }
}
