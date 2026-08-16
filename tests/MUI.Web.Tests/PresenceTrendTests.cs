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
/// absence of ink, and that is a property of the geometry rather than of any style. It is asserted
/// as arithmetic below, which is why the geometry is a plain type and not markup.
/// </remarks>
public class PresenceTrendTests
{
    private static readonly DateOnly Start = new(2026, 5, 1);

    [Test]
    public async Task ADayNobodyMeasuredGetsNoInkAtAll()
    {
        // Three measured days, a four-day hole, three more. A line would draw a slope across the
        // hole and say a game emptied out over a week nobody looked at it; a bar cannot, because a
        // bar is a statement about its own day and says nothing about the day beside it.
        var series = Series(
            Counted(0, 10), Counted(1, 11), Counted(2, 12),
            Gap(3), Gap(4), Gap(5), Gap(6),
            Counted(7, 9), Counted(8, 8), Counted(9, 10));

        var bars = TrendGeometry.Bars(series);

        await Assert.That(bars.Select(b => b.Day.Date))
            .IsEquivalentTo(series.Days.Where(d => d.IsCounted).Select(d => d.Date))
            .Because("a bar exists for every counted day and for no other day");

        // And nothing is drawn across the hole: the four gap columns own a stretch of canvas that
        // no bar touches.
        var columns = TrendGeometry.Columns(series);
        var holeFrom = columns[3].X;
        var holeTo = columns[6].X + columns[6].Width;

        await Assert.That(bars.Any(b => b.X + b.Width > holeFrom && b.X < holeTo)).IsFalse();
    }

    [Test]
    public async Task ASingleMeasuredDayIsAWholeBarRatherThanASpeck()
    {
        // This is what the form is for. As a line, a run of one day is one point — no path at all —
        // and the fallback was a two-pixel dot, which on a ninety-day range measured three times is
        // a chart of nothing. A column does not need its neighbours to exist.
        var series = Series(Gap(0), Counted(1, 14), Gap(2));

        var bars = TrendGeometry.Bars(series);

        await Assert.That(bars).Count().IsEqualTo(1);
        await Assert.That(bars[0].Day.Label).Contains("14");
        await Assert.That(TrendGeometry.Zero - bars[0].MeanTop)
            .IsGreaterThan(100d)
            .Because("the only measurement in the range is also the ceiling, so its bar is the plot");
    }

    [Test]
    public async Task AnUncountableDayIsNotDrawnOnTheZeroLine()
    {
        // §5.4's middle state. A mark on the baseline reads as a measured empty game, which is the
        // collapse this codebase may never ship — so it sits in its own gutter beneath the plot and
        // never gets a bar.
        var series = Series(
            Counted(0, 5), Counted(1, 6),
            Uncountable(2),
            Counted(3, 5), Counted(4, 4));

        var ticks = TrendGeometry.Ticks(series);

        await Assert.That(ticks).Count().IsEqualTo(1);
        await Assert.That(ticks[0].Label).Contains("no count could be read");

        await Assert.That(TrendGeometry.Bars(series)).Count().IsEqualTo(4);
        await Assert.That(TrendGeometry.Bars(series).Any(b => b.Day.IsUncountable)).IsFalse();
    }

    [Test]
    public async Task AMeasuredZeroIsAMeasurementAndKeepsItsInk()
    {
        // The other half of the same rule, and the easy one to get wrong in the opposite direction:
        // we got in and nobody was there is a count, so it is drawn — and a bar of no height is
        // indistinguishable from the day beside it that nobody looked at.
        var series = Series(Counted(0, 4), Counted(1, 0), Counted(2, 3));

        var zero = TrendGeometry.Bars(series).Single(b => b.Day.Date == Start.AddDays(1));

        await Assert.That(TrendGeometry.Zero - zero.MeanTop)
            .IsGreaterThanOrEqualTo(TrendGeometry.MinimumInk);
        await Assert.That(TrendGeometry.Ticks(series)).IsEmpty();
        await Assert.That(series.Days[1].Label).Contains("0 players");
    }

    [Test]
    public async Task TheSpreadOfADayRidesAboveItsMeanAndOnlyWhenThereIsOne()
    {
        // The shape a mean alone hides: a game that peaks at forty and idles at two has the same
        // mean as one that sat at twenty-one all evening, and they are not the same game.
        var series = Series(Counted(0, 2, 40, 21), Counted(1, 21, 21, 21));

        var bars = TrendGeometry.Bars(series);

        await Assert.That(bars[0].PeakTop).IsLessThan(bars[0].MeanTop)
            .Because("the busiest probe of the day is above the mean of them, and up is a smaller y");
        await Assert.That(bars[0].HasCap).IsTrue();

        await Assert.That(bars[1].HasCap).IsFalse()
            .Because("every probe read the same number, so there is no spread to draw");
        await Assert.That(bars[1].MeanTop).IsEqualTo(bars[0].MeanTop)
            .Because("both days mean twenty-one and the bar is drawn to the mean");
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

            var bar = TrendGeometry.Bars(Series(Counted(0, 3), Counted(1, 7)))[0];
            var path = TrendGeometry.Column(bar.X, bar.Width, bar.MeanTop, TrendGeometry.Zero);

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
    public async Task TheCalendarIsLabelledWhereAMonthStartsAndIsNeverCrowded()
    {
        // Ninety columns with nothing under them are ninety columns of nothing in particular. But a
        // five-year range has sixty month starts in it, and sixty labels is a grey smear rather than
        // an axis, so they thin instead of overprinting.
        var quarter = TrendSeries.Over(new DateOnly(2026, 5, 19), new DateOnly(2026, 8, 16), []);

        await Assert.That(TrendGeometry.Months(quarter).Select(m => m.Label))
            .IsEquivalentTo(new[] { "Jun", "Jul", "Aug" });

        var years = TrendSeries.Over(new DateOnly(2021, 1, 1), new DateOnly(2025, 12, 31), []);
        var months = TrendGeometry.Months(years);

        await Assert.That(months.Count).IsLessThanOrEqualTo(9);
        await Assert.That(months.Zip(months.Skip(1)).All(p => p.Second.X > p.First.X)).IsTrue();
        await Assert.That(months[0].Label).IsEqualTo("Jan 2021")
            .Because("a range that crosses a year has to say which January it is looking at");
        await Assert.That(months.All(m => m.Fraction is >= 0 and <= 1)).IsTrue();
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
    public async Task APlayerCountIsNeverReportedWithADecimal()
    {
        // "Typically 666.1 on" is arithmetic printed where a measurement was asked for. Players are
        // whole, and a tenth of one is not a finer answer than 666 — it is a sillier one. Down
        // rather than nearest, so the figure is one we are sure of.
        var series = Series(Counted(0, 601, 731, 666.1m), Counted(1, 590, 640, 620.4m));

        await Assert.That(series.Sentence).Contains("Typically 666 on");
        await Assert.That(series.Sentence).DoesNotContain("666.1");
        await Assert.That(series.Days[0].Label).Contains("666 on average");
        await Assert.That(series.Days[0].Label).DoesNotContain("666.1");
        await Assert.That(series.PerWeek().Single()).Contains("typically 666");
        await Assert.That(series.PerWeek().Single()).DoesNotContain("666.1");

        // The wording is floored; the geometry is not, or the bar would be shortened by a sentence.
        await Assert.That(series.Days[0].Average).IsEqualTo(666.1d);
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

    private static TrendDay Counted(int offset, int low, int high, decimal mean) =>
        new(Start.AddDays(offset), 4, 0, low, high, mean);

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
