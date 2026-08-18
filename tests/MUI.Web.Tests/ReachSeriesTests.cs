using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// The availability strip. Ninety bars, the worst state of each day, and the word is
/// <em>reachable</em> — never "uptime", because we measured a socket from one vantage point.
/// </summary>
public class ReachSeriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 20, 0, 0, TimeSpan.Zero);

    /// <summary>The locale these assertions read in — the source bundle they are written against.</summary>
    private const string English = Locales.SourceTag;

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
    public async Task EveryCauseTheCatalogueCanStoreHasWordsOfItsOwn()
    {
        // A cause added to the enum and forgotten here silently renders as "no cause recorded" rather
        // than failing — the one place that silence is correct is FailureCause.None.
        foreach (var cause in Enum.GetValues<FailureCause>().Where(c => c is not FailureCause.None))
        {
            await Assert.That(Wording.Cause(English, cause))
                .IsNotEqualTo(Wording.Cause(English, FailureCause.None));
        }
    }

    [Test]
    public async Task AHoleInTheMiddleIsNotDaysThatPredateUs()
    {
        // Two different admissions: days before we found a game predate our measurement; a hole
        // inside its history means we stopped measuring. Once conflated, a repaired interval left 409
        // games' mid-history gaps reading as if they predated our discovery of the game.
        var summary = ReachSeries.Build(
        [
            Span(80, 70, AvailabilityState.Reachable, FailureCause.None),
            Span(20, null, AvailabilityState.Reachable, FailureCause.None),
        ], Now);

        var unmeasured = summary.Days.Count(d => d.State is ReachState.Unmeasured);
        var predating = summary.Days.TakeWhile(d => d.State is ReachState.Unmeasured).Count();
        var sentence = summary.Sentence(English);

        // Both present and differing, so neither number can stand in for the other.
        await Assert.That(predating).IsGreaterThan(0);
        await Assert.That(unmeasured - predating).IsGreaterThan(predating);

        await Assert.That(sentence).Contains($"{predating} days predate anything we measured");
        await Assert.That(sentence).Contains($"{unmeasured - predating} days went unmeasured");
    }

    [Test]
    public async Task AGameWithNothingButAHeadStartSaysNothingAboutAHole()
    {
        // A game found last week has unmeasured days and no gap, and must not be told it has one.
        var summary = ReachSeries.Build([Span(7, null, AvailabilityState.Reachable, FailureCause.None)], Now);

        var sentence = summary.Sentence(English);

        await Assert.That(sentence).Contains("predate anything we measured");
        await Assert.That(sentence).DoesNotContain("went unmeasured");
    }

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
        // Painting pre-discovery days as unreachable would record our own ignorance as a measurement of theirs (rule 5).
        var summary = ReachSeries.Build([Span(10, null, AvailabilityState.Reachable, FailureCause.None)], Now);

        await Assert.That(summary.Days.Count(d => d.State is ReachState.Unmeasured)).IsGreaterThan(70);
        await Assert.That(summary.Days.Any(d => d.State is ReachState.Unreachable)).IsFalse();
        await Assert.That(summary.Sentence(English)).Contains("predate anything we measured");
    }

    [Test]
    public async Task DegradedIsItsOwnStateBetweenReachableAndUnreachable()
    {
        // We got in and could not finish (spec §5.3), drawn as a short bar rather than another colour.
        var summary = ReachSeries.Build(
        [
            Span(90, 20, AvailabilityState.Reachable, FailureCause.None),
            Span(20, 18, AvailabilityState.Degraded, FailureCause.HandshakeStalled),
            Span(18, null, AvailabilityState.Reachable, FailureCause.None),
        ], Now);

        await Assert.That(summary.Days.Any(d => d.State is ReachState.Degraded)).IsTrue();
        await Assert.That(summary.Sentence(English)).Contains("degraded");
        await Assert.That(summary.Sentence(English)).Contains("could not finish");
    }

    [Test]
    public async Task TheWorstThingInADayIsWhatTheDaySays()
    {
        // A game down for an hour was not "reachable that day".
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
        // Pairing a real duration with an unrelated cause invents an incident that never happened.
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
        // Cause named through the bundle, not spelled here, so this survives translation.
        await Assert.That(summary.Sentence(English))
            .Contains(Wording.Cause(English, FailureCause.Refused));
        await Assert.That(summary.Sentence(English))
            .DoesNotContain(Wording.Cause(English, FailureCause.Timeout));
    }

    [Test]
    public async Task EverySpellThatWasNotReachableIsAvailableAsWords()
    {
        // The strip is one image with one label; detail lives in text so nothing is sight-only.
        var summary = ReachSeries.Build(
        [
            Span(90, 40, AvailabilityState.Reachable, FailureCause.None),
            Span(40, 37, AvailabilityState.Unreachable, FailureCause.Dns),
            Span(37, null, AvailabilityState.Reachable, FailureCause.None),
        ], Now);

        await Assert.That(summary.Spells(English).Count).IsEqualTo(1);
        await Assert.That(summary.Spells(English)[0])
            .Contains(Messages.For(English, "reach.word.unreachable"));
        await Assert.That(summary.Spells(English)[0])
            .Contains(Wording.Cause(English, FailureCause.Dns));
    }

    [Test]
    public async Task TheWordIsReachableAndNeverUptime()
    {
        // A game with a routing problem to our host is unreachable and perfectly alive; "uptime" claims we measured something we didn't.
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
