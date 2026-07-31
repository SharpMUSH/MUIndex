using MUI.Catalog;
using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// The availability strip. Ninety bars, the worst state of each day, and the word is
/// <em>reachable</em> — never "uptime", because we measured a socket from one vantage point.
/// </summary>
public class ReachSeriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 20, 0, 0, TimeSpan.Zero);

    private static AvailabilityInterval Span(double from, double? to, AvailabilityState state, FailureCause cause) =>
        new()
        {
            GameId = Guid.Empty,
            State = state,
            FromAt = Now.AddDays(-from),
            ToAt = to is { } t ? Now.AddDays(-t) : null,
            Cause = cause,
        };

    [Test]
    public async Task TheStripIsNinetyDaysWideWithTheOldestFirst()
    {
        var summary = ReachSeries.Build([Span(400, null, AvailabilityState.Reachable, FailureCause.None)], Now);

        await Assert.That(summary.Days.Count()).IsEqualTo(90);
        await Assert.That(summary.Days[0].Date).IsLessThan(summary.Days[^1].Date);
    }

    [Test]
    public async Task ADayWeWereNotWatchingIsItsOwnStateAndNotAnOutage()
    {
        // Painting the days before we found a game as unreachable would record our own ignorance as
        // a measurement of theirs, which is the one thing this site may never do.
        var summary = ReachSeries.Build([Span(10, null, AvailabilityState.Reachable, FailureCause.None)], Now);

        await Assert.That(summary.Days.Count(d => d.State is ReachState.Unmeasured)).IsGreaterThan(70);
        await Assert.That(summary.Days.Any(d => d.State is ReachState.Unreachable)).IsFalse();
        await Assert.That(summary.Sentence).Contains("predate anything we measured");
    }

    [Test]
    public async Task DegradedIsItsOwnStateBetweenReachableAndUnreachable()
    {
        // We got in and could not finish (spec §5.3). The strip draws it as a short bar rather than
        // as another colour, and the words keep it distinct too.
        var summary = ReachSeries.Build(
        [
            Span(90, 20, AvailabilityState.Reachable, FailureCause.None),
            Span(20, 18, AvailabilityState.Degraded, FailureCause.HandshakeStalled),
            Span(18, null, AvailabilityState.Reachable, FailureCause.None),
        ], Now);

        await Assert.That(summary.Days.Any(d => d.State is ReachState.Degraded)).IsTrue();
        await Assert.That(summary.Sentence).Contains("degraded");
        await Assert.That(summary.Sentence).Contains("could not finish");
    }

    [Test]
    public async Task TheWorstThingInADayIsWhatTheDaySays()
    {
        // A game down for an hour was not "reachable that day"; a reader scanning ninety bars for
        // trouble has to be able to find it.
        var summary = ReachSeries.Build(
        [
            Span(90, 5.5, AvailabilityState.Reachable, FailureCause.None),
            Span(5.5, 5.4, AvailabilityState.Unreachable, FailureCause.Refused),
            Span(5.4, null, AvailabilityState.Reachable, FailureCause.None),
        ], Now);

        await Assert.That(summary.Days.Count(d => d.State is ReachState.Unreachable)).IsEqualTo(1);
    }

    [Test]
    public async Task TheLongestOutageCarriesItsOwnCauseAndNotTheMostRecentOne()
    {
        // Pairing a real duration with an unrelated event invents an incident that never happened.
        var summary = ReachSeries.Build(
        [
            Span(90, 60, AvailabilityState.Reachable, FailureCause.None),
            Span(60, 50, AvailabilityState.Unreachable, FailureCause.Refused),
            Span(50, 10, AvailabilityState.Reachable, FailureCause.None),
            Span(10, 9, AvailabilityState.Unreachable, FailureCause.Timeout),
            Span(9, null, AvailabilityState.Reachable, FailureCause.None),
        ], Now);

        await Assert.That(summary.LongestOutageCause).IsEqualTo(FailureCause.Refused);
        await Assert.That(summary.LastCause).IsEqualTo(FailureCause.Timeout);
        await Assert.That(summary.Sentence).Contains("connection refused");
    }

    [Test]
    public async Task EverySpellThatWasNotReachableIsAvailableAsWords()
    {
        // The strip is one image with one label; the detail lives in text so nothing is available
        // only to a reader who can see it.
        var summary = ReachSeries.Build(
        [
            Span(90, 40, AvailabilityState.Reachable, FailureCause.None),
            Span(40, 37, AvailabilityState.Unreachable, FailureCause.Dns),
            Span(37, null, AvailabilityState.Reachable, FailureCause.None),
        ], Now);

        await Assert.That(summary.Spells.Count()).IsEqualTo(1);
        await Assert.That(summary.Spells[0]).Contains("unreachable");
        await Assert.That(summary.Spells[0]).Contains("dns did not resolve");
    }

    [Test]
    public async Task TheWordIsReachableAndNeverUptime()
    {
        // A naming rule with teeth: a game with a routing problem to our host is unreachable and
        // perfectly alive, and "uptime" claims we measured something we did not.
        var summary = ReachSeries.Build([Span(90, null, AvailabilityState.Reachable, FailureCause.None)], Now);
        var html = await Render.ComponentAsync<AvailabilityStrip>(new() { ["Summary"] = summary });

        await Assert.That(html.ToLowerInvariant()).DoesNotContain("uptime");
        await Assert.That(html).Contains("reachable");
    }

    [Test]
    public async Task TheStripIsOneLabelledImageRatherThanNinetyAnnouncedBars()
    {
        var summary = ReachSeries.Build([Span(90, null, AvailabilityState.Reachable, FailureCause.None)], Now);
        var html = await Render.ComponentAsync<AvailabilityStrip>(new() { ["Summary"] = summary });

        await Assert.That(html).Contains("role=\"img\"");
        await Assert.That(html).Contains("aria-label=\"Reachable");
    }
}
