using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// The trend chart, and the one rule it exists to keep.
/// </summary>
/// <remarks>
/// Unlike a typical polyline chart, which interpolates across unmeasured days (drawing our crawl
/// schedule as their quiet fortnight), §5.4's third state must be the absence of ink — a property of
/// the geometry, asserted as arithmetic below rather than through markup.
/// </remarks>
public class PresenceTrendTests
{
    private static readonly DateOnly Start = new(2026, 5, 1);

    /// <summary>The locale these assertions read in. The source bundle is what they are written against.</summary>
    private const string English = Locales.SourceTag;

    [Test]
    public async Task ADayNobodyMeasuredGetsNoInkAtAll()
    {
        // A line would draw a slope across the hole, saying a game emptied out over a week nobody
        // looked at; a bar says nothing about the day beside it.
        var series = Series(
            Counted(0, 10), Counted(1, 11), Counted(2, 12),
            Gap(3), Gap(4), Gap(5), Gap(6),
            Counted(7, 9), Counted(8, 8), Counted(9, 10));

        var bars = TrendGeometry.Bars(series);

        await Assert.That(bars.Select(b => b.Day.Date))
            .IsEquivalentTo(series.Days.Where(d => d.IsCounted).Select(d => d.Date))
            .Because("a bar exists for every counted day and for no other day");

        // The four gap columns own a stretch of canvas that no bar touches.
        var columns = TrendGeometry.Columns(series);
        var holeFrom = columns[3].X;
        var holeTo = columns[6].X + columns[6].Width;

        await Assert.That(bars.Any(b => b.X + b.Width > holeFrom && b.X < holeTo)).IsFalse();
    }

    [Test]
    public async Task ASingleMeasuredDayIsAWholeBarRatherThanASpeck()
    {
        // As a line, one measured day is one point with no path; a column doesn't need neighbours to exist.
        var series = Series(Gap(0), Counted(1, 14), Gap(2));

        var bars = TrendGeometry.Bars(series);

        await Assert.That(bars).Count().IsEqualTo(1);
        await Assert.That(bars[0].Day.Label(English)).Contains("14");
        await Assert.That(TrendGeometry.Zero - bars[0].MeanTop)
            .IsGreaterThan(100d)
            .Because("the only measurement in the range is also the ceiling, so its bar is the plot");
    }

    [Test]
    public async Task AnUncountableDayIsNotDrawnOnTheZeroLine()
    {
        // §5.4's middle state — a mark on the baseline would read as a measured empty game, so it sits in its own gutter and never gets a bar.
        var series = Series(
            Counted(0, 5), Counted(1, 6),
            Uncountable(2),
            Counted(3, 5), Counted(4, 4));

        var ticks = TrendGeometry.Ticks(English, series);

        await Assert.That(ticks).Count().IsEqualTo(1);
        var uncountableDay = Start.AddDays(2);

        await Assert.That(ticks[0].Label).IsEqualTo(Say("trend.day.notCounted", uncountableDay));
        await Assert.That(ticks[0].Label).IsNotEqualTo(Say("trend.day.notMeasured", uncountableDay));

        await Assert.That(TrendGeometry.Bars(series)).Count().IsEqualTo(4);
        await Assert.That(TrendGeometry.Bars(series).Any(b => b.Day.IsUncountable)).IsFalse();
    }

    [Test]
    public async Task AMeasuredZeroIsAMeasurementAndKeepsItsInk()
    {
        // A measured zero is a count and is drawn — a bar of no height would be indistinguishable from an unmeasured day.
        var series = Series(Counted(0, 4), Counted(1, 0), Counted(2, 3));

        var zero = TrendGeometry.Bars(series).Single(b => b.Day.Date == Start.AddDays(1));

        await Assert.That(TrendGeometry.Zero - zero.MeanTop)
            .IsGreaterThanOrEqualTo(TrendGeometry.MinimumInk);
        await Assert.That(TrendGeometry.Ticks(English, series)).IsEmpty();
        await Assert.That(series.Days[1].Label(English)).Contains("0 players");
    }

    [Test]
    public async Task TheSpreadOfADayRidesAboveItsMeanAndOnlyWhenThereIsOne()
    {
        // A game peaking at forty and idling at two has the same mean as one that sat at twenty-one all evening.
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
        // SVG path data is space separated; "0,5" would be read as two numbers — gibberish, and only
        // on somebody else's server. Culture built by hand since the test host runs in
        // globalization-invariant mode, where "de-DE" doesn't exist.
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

            // Same check for what the component writes into the markup.
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
    public async Task TheVerticalAxisIsLabelledWithTheValueEachGridlineStandsFor()
    {
        // The lines were already drawn (TrendGeometry.Gridlines); a reader still can't turn "a bar
        // this tall" into a number without a printed value beside each one.
        var series = Series(Counted(0, 4), Counted(1, 8), Counted(2, 20));

        var html = await Render.ComponentAsync<PresenceTrend>(
            new Dictionary<string, object?> { ["Series"] = series });

        await Assert.That(html).Contains("class=\"trend-yaxis")
            .Because("the value belongs beside the line it labels, not only in the caption below the chart");

        foreach (var (value, _) in TrendGeometry.Gridlines(series))
        {
            await Assert.That(html).Contains($">{value}<");
        }
    }

    [Test]
    public async Task TheCalendarIsLabelledWhereAMonthStartsAndIsNeverCrowded()
    {
        // A five-year range has sixty month starts, and sixty labels is a grey smear, so they thin instead of overprinting.
        var quarter = TrendSeries.Over(new DateOnly(2026, 5, 19), new DateOnly(2026, 8, 16), []);

        await Assert.That(TrendGeometry.Months(English, quarter).Select(m => m.Label))
            .IsEquivalentTo(new[] { "Jun", "Jul", "Aug" });

        var years = TrendSeries.Over(new DateOnly(2021, 1, 1), new DateOnly(2025, 12, 31), []);
        var months = TrendGeometry.Months(English, years);

        await Assert.That(months.Count).IsLessThanOrEqualTo(9);
        await Assert.That(months.Zip(months.Skip(1)).All(p => p.Second.X > p.First.X)).IsTrue();
        await Assert.That(months[0].Label).IsEqualTo("Jan 2021")
            .Because("a range that crosses a year has to say which January it is looking at");
        await Assert.That(months.All(m => m.Fraction is >= 0 and <= 1)).IsTrue();
    }

    [Test]
    public async Task TheRangeIsFilledSoAnAbsentBucketStaysAbsent()
    {
        // The rollup returns only the buckets it has; drawn straight off that list, this would space measured days evenly and draw a gap as a gentle slope.
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
        // Players are whole; a tenth of one is a sillier answer than 666, not a finer one. Floored, not rounded, so the figure is one we're sure of.
        var series = Series(Counted(0, 601, 731, 666.1m), Counted(1, 590, 640, 620.4m));

        await Assert.That(series.Sentence(English)).Contains("Typically 666 on");
        await Assert.That(series.Sentence(English)).DoesNotContain("666.1");
        await Assert.That(series.Days[0].Label(English)).Contains("666 on average");
        await Assert.That(series.Days[0].Label(English)).DoesNotContain("666.1");
        await Assert.That(series.PerWeek(English).Single()).Contains("typically 666");
        await Assert.That(series.PerWeek(English).Single()).DoesNotContain("666.1");

        // The wording is floored; the geometry isn't, or the bar would be shortened by a sentence.
        await Assert.That(series.Days[0].Average).IsEqualTo(666.1d);
    }

    [Test]
    public async Task TheSentenceNamesTheThreeStatesApart()
    {
        // A range probed but never countable is a different fact from one nobody looked at; the
        // summary is the accessible answer, so the distinction can't be carried by a cell shape here.
        var nothing = Series(Gap(0), Gap(1), Gap(2));
        var probed = Series(Uncountable(0), Uncountable(1));
        var measured = Series(Counted(0, 6), Counted(1, 6), Counted(2, 8));

        // Through the bundle so the assertion still holds after translation — the two must stay two in every locale.
        await Assert.That(nothing.Sentence(English))
            .IsEqualTo(Messages.For(English, "trend.none.notMeasured"));
        await Assert.That(nothing.Sentence(English))
            .IsNotEqualTo(Messages.For(English, "trend.none.probed"));
        await Assert.That(probed.Sentence(English)).Contains("no player count could be read");
        await Assert.That(measured.Sentence(English)).Contains("Typically 6");
        await Assert.That(measured.Sentence(English)).Contains("peaking at 8");

        // None names a cause: a failed probe writes no presence row.
        foreach (var sentence in new[] { nothing.Sentence(English), probed.Sentence(English) })
        {
            await Assert.That(sentence).DoesNotContain("unreachable");
            await Assert.That(sentence).DoesNotContain("down");
        }
    }

    [Test]
    public async Task ADirectionIsOnlyClaimedWhenBothEndsWereMeasured()
    {
        // A range measured at one end is not a trend — calling it one would publish our sampling as their decline.
        var lopsided = Series(
            Counted(0, 20), Counted(1, 20), Counted(2, 20),
            Gap(3), Gap(4), Gap(5), Gap(6), Gap(7));

        await Assert.That(lopsided.Sentence(English)).DoesNotContain("Down");
        await Assert.That(lopsided.Sentence(English)).DoesNotContain("Up about");

        var falling = Series(
            Counted(0, 30), Counted(1, 30), Counted(2, 28),
            Counted(3, 20), Counted(4, 12), Counted(5, 10));

        // Sign and spacing come off the locale's number format, not a literal in a template.
        await Assert.That(falling.Sentence(English)).Contains("Down about");
        await Assert.That(falling.Sentence(English)).Contains("63%");
        await Assert.That(falling.Sentence(English)).DoesNotContain("0.63");
    }

    [Test]
    public async Task PerWeekSaysWhichOfTheThreeStatesAWeekWas()
    {
        // A week with nothing in it has to say which kind of nothing it was.
        var series = Series(Enumerable.Range(0, 21)
            .Select(i => i switch
            {
                < 7 => Counted(i, 5),
                < 14 => Gap(i),
                _ => Uncountable(i),
            })
            .ToArray());

        var lines = series.PerWeek(English).ToList();

        await Assert.That(lines).Count().IsEqualTo(3);
        await Assert.That(lines[0]).Contains("typically 5");
        await Assert.That(lines[0]).Contains("7 days counted");
        await Assert.That(lines[1]).IsEqualTo(Messages.For(
            English,
            "trend.week.notMeasured",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["span"] = Messages.For(
                    English,
                    "trend.week.span",
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["from"] = Start.AddDays(7),
                        ["to"] = Start.AddDays(13),
                    }),
            }));
        await Assert.That(lines[2]).Contains("probed, no count could be read");
    }

    [Test]
    public async Task AWeekWithAllThreeStatesInItKeepsThemApart()
    {
        // "5 days of 7" would put uncountable days and unmeasured days into the same bucket — different facts about a game.
        var series = Series(
            Counted(0, 8), Counted(1, 9), Counted(2, 7), Counted(3, 8),
            Uncountable(4), Uncountable(5),
            Gap(6));

        var line = series.PerWeek(English).Single();

        await Assert.That(line).Contains("4 days counted");
        await Assert.That(line).Contains("2 days probed without a count");
        await Assert.That(line).Contains("1 day not measured");
    }

    /// <summary>
    /// A German request gets German month names, from CLDR rather than from an array in a component.
    /// </summary>
    /// <remarks>The date is a <c>{d, date}</c> argument rather than <c>ToString("d MMM yyyy")</c> at the call site — the old spelling read the month off the thread's culture, so no translator could fix a wrong month name.</remarks>
    [Test]
    public async Task ADateIsNamedInTheLanguageThePageWasAskedFor()
    {
        var day = Counted(20, 12);      // 21 May 2026
        var may = new System.Globalization.CultureInfo("de")
            .DateTimeFormat.AbbreviatedMonthNames[4];

        await Assert.That(day.Label("de")).Contains(may);
        await Assert.That(day.Label(English))
            .Contains(System.Globalization.CultureInfo
                .GetCultureInfo("en").DateTimeFormat.AbbreviatedMonthNames[4]);

        var year = TrendSeries.Over(new DateOnly(2026, 4, 20), new DateOnly(2026, 6, 10), []);

        await Assert.That(TrendGeometry.Months("de", year).Select(m => m.Label))
            .Contains(may);
    }

    /// <summary>
    /// The two absences stay two, in every locale this site offers.
    /// </summary>
    /// <remarks>An uncountable day is a measurement of an answering game; a gap day is a statement about our crawl. Collapsing them is §5.4's worst bug, and a translator could do it silently — checked per locale.</remarks>
    [Test]
    public async Task NoMeasurementAndNoReadableCountStayTwoDifferentStringsInEveryLocale()
    {
        var gap = Gap(0);
        var uncountable = Uncountable(1);

        foreach (var locale in Locales.All)
        {
            await Assert.That(gap.Label(locale.Tag))
                .IsNotEqualTo(uncountable.Label(locale.Tag))
                .Because($"{locale.Tag} renders both absences as one sentence");

            await Assert.That(Messages.Pattern(locale.Tag, "trend.day.notMeasured"))
                .IsNotEqualTo(Messages.Pattern(locale.Tag, "trend.day.notCounted"))
                .Because($"{locale.Tag} translated the two absences to one pattern");

            // Neither names a cause: a day with no presence row is not "unreachable".
            foreach (var absence in new[] { gap.Label(locale.Tag), uncountable.Label(locale.Tag) })
            {
                await Assert.That(absence)
                    .DoesNotContain(Messages.For(locale.Tag, "state.unreachable"));
            }
        }
    }

    private static string Say(string id, DateOnly date) =>
        Messages.For(
            English,
            id,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["d"] = date });

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
