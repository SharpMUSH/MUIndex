using MUI.Catalog;
using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// The trend chart, and the one rule it exists to keep.
/// </summary>
/// <remarks>
/// Every chart of this kind on every other site draws one continuous polyline, which interpolates
/// across the days nobody measured — and an interpolated gap is our crawl schedule drawn as their
/// quiet fortnight. §5.4's third state is the absence of a measurement, so here it must be the
/// absence of ink, and that is a property of the path data rather than of any style. It is asserted
/// as arithmetic below, which is why the geometry is a plain type and not markup.
/// </remarks>
public class PresenceTrendTests
{
    private static readonly DateOnly Start = new(2026, 5, 1);

    [Test]
    public async Task TheLineBreaksWhereNothingWasMeasured()
    {
        // Three measured days, a four-day hole, three more. One path would draw a slope across the
        // hole and say a game emptied out over a week nobody looked at it.
        var series = Series(
            Counted(0, 10), Counted(1, 11), Counted(2, 12),
            Gap(3), Gap(4), Gap(5), Gap(6),
            Counted(7, 9), Counted(8, 8), Counted(9, 10));

        var paths = TrendGeometry.MeanPaths(series);

        await Assert.That(paths).Count().IsEqualTo(2)
            .Because("a run of measured days on each side of a gap is two lines, never one");

        // And neither path spans the hole: each has exactly its own three points.
        foreach (var path in paths)
        {
            await Assert.That(path.Count(c => c is 'M' or 'L')).IsEqualTo(3);
        }

        await Assert.That(TrendGeometry.BandPaths(series)).Count().IsEqualTo(2);
    }

    [Test]
    public async Task ASingleMeasuredDayIsDrawnAsAPointRatherThanDropped()
    {
        // A one-point path renders as nothing in most engines, which would be a measurement silently
        // lost — the failure mode of "just break the polyline" done carelessly.
        var series = Series(Gap(0), Counted(1, 14), Gap(2));

        await Assert.That(TrendGeometry.MeanPaths(series)).IsEmpty();

        var dots = TrendGeometry.Dots(series);

        await Assert.That(dots).Count().IsEqualTo(1);
        await Assert.That(dots[0].Label).Contains("14");
    }

    [Test]
    public async Task AnUncountableDayIsNotDrawnOnTheZeroLine()
    {
        // §5.4's middle state. A mark on the baseline reads as a measured empty game, which is the
        // collapse this codebase may never ship — so it sits in its own gutter beneath the plot and
        // never contributes a point to the line.
        var series = Series(
            Counted(0, 5), Counted(1, 6),
            Uncountable(2),
            Counted(3, 5), Counted(4, 4));

        var ticks = TrendGeometry.Ticks(series);

        await Assert.That(ticks).Count().IsEqualTo(1);
        await Assert.That(ticks[0].Label).Contains("no count could be read");

        // And it breaks the line exactly as a gap does: it is not a day we counted, so the run on
        // each side of it is its own path and nothing is drawn across the middle.
        await Assert.That(TrendGeometry.MeanPaths(series)).Count().IsEqualTo(2);
        await Assert.That(TrendGeometry.Dots(series)).IsEmpty();
    }

    [Test]
    public async Task AMeasuredZeroIsAMeasurementAndStaysOnTheLine()
    {
        // The other half of the same rule, and the easy one to get wrong in the opposite direction:
        // we got in and nobody was there is a count, so it is drawn.
        var series = Series(Counted(0, 4), Counted(1, 0), Counted(2, 3));

        await Assert.That(TrendGeometry.MeanPaths(series)).Count().IsEqualTo(1);
        await Assert.That(TrendGeometry.Ticks(series)).IsEmpty();
        await Assert.That(series.Days[1].Label).Contains("0 players");
    }

    [Test]
    public async Task CoordinatesAreInvariantWhateverTheCulture()
    {
        // SVG path data is space separated and a machine writing "0,5" emits coordinates every
        // renderer reads as two numbers. The chart would not be subtly wrong; it would be gibberish,
        // and only on somebody else's server.
        // A culture built by hand rather than looked up by name: the test host runs in
        // globalization-invariant mode, where "de-DE" does not exist — and a test that silently did
        // not run would be worse than none, since this failure only ever appears on somebody else's
        // server.
        var original = System.Globalization.CultureInfo.CurrentCulture;
        var comma = (System.Globalization.CultureInfo)
            System.Globalization.CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = comma;

            var path = TrendGeometry.MeanPaths(Series(Counted(0, 3), Counted(1, 7)))[0];

            await Assert.That(path).DoesNotContain(",");

            // And the same for what the component writes into the markup.
            await Assert.That(0.5.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture))
                .IsEqualTo("0,5")
                .Because("the culture has to actually be in force, or this test proves nothing");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Test]
    public async Task TheRangeIsFilledSoAnAbsentBucketStaysAbsent()
    {
        // The rollup returns only the buckets it has. A chart drawn straight off that list would
        // space eleven measured days evenly across a quarter and draw a gap as a gentle slope.
        var buckets = new List<PresenceRollup>
        {
            Bucket(Start, 4, 10),
            Bucket(Start.AddDays(9), 4, 12),
        };

        var series = TrendSeries.Over(Start, Start.AddDays(9), buckets);

        await Assert.That(series.Days).Count().IsEqualTo(10);
        await Assert.That(series.Days.Count(d => d.IsGap)).IsEqualTo(8);
        await Assert.That(series.CountedDays).IsEqualTo(2);
    }

    [Test]
    public async Task TheSentenceNamesTheThreeStatesApart()
    {
        // A range probed all through and never countable is a different fact from a range nobody
        // looked at, and neither is an empty game. The summary is the accessible answer, so it is
        // the one place the distinction cannot be carried by a cell shape.
        var nothing = Series(Gap(0), Gap(1), Gap(2));
        var probed = Series(Uncountable(0), Uncountable(1));
        var measured = Series(Counted(0, 6), Counted(1, 6), Counted(2, 8));

        await Assert.That(nothing.Sentence).IsEqualTo("No measurement in this range.");
        await Assert.That(probed.Sentence).Contains("no player count could be read");
        await Assert.That(measured.Sentence).Contains("Typically 6");
        await Assert.That(measured.Sentence).Contains("peaking at 8");

        // And none of them names a cause: a failed probe writes no presence row, so silence here
        // cannot tell an outage of theirs from a gap of ours.
        foreach (var sentence in new[] { nothing.Sentence, probed.Sentence })
        {
            await Assert.That(sentence).DoesNotContain("unreachable");
            await Assert.That(sentence).DoesNotContain("down");
        }
    }

    [Test]
    public async Task ADirectionIsOnlyClaimedWhenBothEndsWereMeasured()
    {
        // A range measured at one end is not a trend, and calling it one would publish our sampling
        // as their decline.
        var lopsided = Series(
            Counted(0, 20), Counted(1, 20), Counted(2, 20),
            Gap(3), Gap(4), Gap(5), Gap(6), Gap(7));

        await Assert.That(lopsided.Sentence).DoesNotContain("Down");
        await Assert.That(lopsided.Sentence).DoesNotContain("Up about");

        var falling = Series(
            Counted(0, 30), Counted(1, 30), Counted(2, 28),
            Counted(3, 20), Counted(4, 12), Counted(5, 10));

        await Assert.That(falling.Sentence).Contains("Down about");
    }

    [Test]
    public async Task PerWeekSaysWhichOfTheThreeStatesAWeekWas()
    {
        // The "read as text" disclosure and the plain rendering are the same lines, so a week with
        // nothing in it has to say which kind of nothing it was.
        var series = Series(Enumerable.Range(0, 21)
            .Select(i => i switch
            {
                < 7 => Counted(i, 5),
                < 14 => Gap(i),
                _ => Uncountable(i),
            })
            .ToArray());

        var lines = series.PerWeek().ToList();

        await Assert.That(lines).Count().IsEqualTo(3);
        await Assert.That(lines[0]).Contains("typically 5");
        await Assert.That(lines[0]).Contains("7 days counted");
        await Assert.That(lines[1]).IsEqualTo($"{Start.AddDays(7):d MMM}–{Start.AddDays(13):d MMM}: not measured");
        await Assert.That(lines[2]).Contains("probed, no count could be read");
    }

    [Test]
    public async Task AWeekWithAllThreeStatesInItKeepsThemApart()
    {
        // The collapse this line is most likely to make, because it is the one that reads as tidy:
        // "5 days of 7" puts two days a probe could not count and two days nobody looked into the
        // same missing bucket, and they are different facts about a game.
        var series = Series(
            Counted(0, 8), Counted(1, 9), Counted(2, 7), Counted(3, 8),
            Uncountable(4), Uncountable(5),
            Gap(6));

        var line = series.PerWeek().Single();

        await Assert.That(line).Contains("4 days counted");
        await Assert.That(line).Contains("2 days probed without a count");
        await Assert.That(line).Contains("1 day not measured");
    }

    private static TrendSeries Series(params TrendDay[] days) =>
        new(days[0].Date, days[^1].Date, days);

    private static TrendDay Counted(int offset, int players) =>
        new(Start.AddDays(offset), 4, 0, players, players, players);

    private static TrendDay Uncountable(int offset) => new(Start.AddDays(offset), 0, 2, null, null, null);

    private static TrendDay Gap(int offset) => new(Start.AddDays(offset), 0, 0, null, null, null);

    private static PresenceRollup Bucket(DateOnly day, int samples, int count) => new()
    {
        GameId = Guid.Empty,
        Grain = PresenceGrain.Day,
        Bucket = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        CountedSamples = samples,
        UnmeasurableSamples = 0,
        MinCount = count,
        MaxCount = count,
        SumCount = (long)count * samples,
    };
}
