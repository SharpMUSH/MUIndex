using System.Globalization;

using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// One day of the presence trend, in the three states of spec §5.4.
/// </summary>
/// <remarks>
/// The heatmap answers <em>when in a week is anyone on</em>, folding every week into one grid. This
/// answers the question folding destroys: <em>is this game growing or dying</em>.
/// </remarks>
public sealed record TrendDay(
    DateOnly Date,
    int CountedSamples,
    int UnmeasurableSamples,
    int? Min,
    int? Max,
    decimal? Mean)
{
    /// <summary>Something in this day was counted — a measured zero included.</summary>
    public bool IsCounted => CountedSamples > 0;

    /// <summary>Probed all day and never countable. Hatched, not zero.</summary>
    public bool IsUncountable => CountedSamples == 0 && UnmeasurableSamples > 0;

    /// <summary>Not measured. Emphatically not an empty game.</summary>
    public bool IsGap => CountedSamples == 0 && UnmeasurableSamples == 0;

    /// <summary>The mean to one decimal, or null where nothing was counted. What the bar is drawn to.</summary>
    public double? Average => Mean is { } m ? (double)Math.Round(m, 1) : null;

    /// <summary>
    /// The mean as a number of players — floored, never rounded up.
    /// </summary>
    /// <remarks>
    /// Players are whole; "666.1 players were on" is arithmetic printed where a measurement was
    /// asked for. Floored rather than rounded, so the site only states the integer it is sure of.
    /// The exact mean survives in <see cref="Average"/>, which the chart is drawn from, so the bar
    /// is never shortened by the wording.
    /// </remarks>
    public int? Typical => Mean is { } m ? (int)Math.Floor(m) : null;

    /// <summary>
    /// What one column says, as a sentence in the reader's language.
    /// </summary>
    /// <remarks>
    /// Four shapes and the reader gets exactly one id, none assembled from fragments.
    /// <b>The gap and the uncountable day are two different ids and must stay two.</b> A day nobody
    /// measured names no cause — a failed probe writes no presence row, so it covers a dial we could
    /// not complete and one we never attempted alike — while a day probed all through without a
    /// readable count is a measurement of a game that was answering. Collapsing them is §5.4's worst
    /// bug, so the two ids read nothing like each other and a test holds them apart in every locale.
    /// </remarks>
    public string Label(string tag) => this switch
    {
        { IsGap: true } => Messages.Say(tag, "trend.day.notMeasured", ("d", Date)),
        { IsUncountable: true } => Messages.Say(tag, "trend.day.notCounted", ("d", Date)),

        // Every probe returning the same number is its own sentence rather than a range with that
        // number written twice: "24–24" is arithmetic printed where a measurement was asked for.
        { Min: { } lo, Max: { } hi } when lo == hi => Messages.Say(
            tag,
            "trend.day.flat",
            ("d", Date),
            ("count", lo),
            ("probes", CountedSamples)),

        _ => Messages.Say(
            tag,
            "trend.day.counted",
            ("d", Date),
            ("typical", Typical),
            ("low", Min),
            ("high", Max),
            ("probes", CountedSamples)),
    };
}

/// <summary>
/// A game's measured presence over a calendar range, ready to draw and ready to say.
/// </summary>
/// <remarks>
/// <b>Every day in the range is present, including the ones nobody measured.</b> The rollup returns
/// only the days it has, and a chart drawn straight off that list would space eleven measured days
/// evenly across a quarter and describe a gap as a gentle slope. So the range is filled to a day per
/// column here, and the missing ones become <see cref="TrendDay.IsGap"/> and stay visibly missing —
/// never a zero, never given a cause. Reachability is the strip's question, from intervals that can
/// tell an outage of theirs from a gap of ours.
/// </remarks>
public sealed record TrendSeries(DateOnly From, DateOnly To, IReadOnlyList<TrendDay> Days)
{
    /// <summary>
    /// The widest a range may be — the same bound the API answers at this grain.
    /// </summary>
    /// <remarks>
    /// Read off <see cref="Api.SeriesEndpoints.WidestWindow"/> rather than restated, so the page and
    /// the scriptable route cannot drift into disagreeing.
    /// </remarks>
    public static readonly int MaximumDays =
        (int)Api.SeriesEndpoints.WidestWindow[PresenceGrain.Day].TotalDays;

    /// <summary>What a reader gets without asking for a range.</summary>
    /// <remarks>
    /// Ninety, the same window the reachability strip covers, so both graphics read against the
    /// same stretch of calendar.
    /// </remarks>
    public const int DefaultDays = 90;

    public bool HasAnyMeasurement => Days.Any(d => !d.IsGap);

    public bool HasAnyCount => Days.Any(d => d.IsCounted);

    /// <summary>Highest count measured in the range, which is what the axis is scaled to.</summary>
    public int Ceiling => Days.Max(d => d.Max ?? 0);

    /// <summary>Days we counted something on, out of days in the range.</summary>
    public int CountedDays => Days.Count(d => d.IsCounted);

    /// <summary>
    /// The chart said in words, and said first.
    /// </summary>
    /// <remarks>
    /// Taken as the difference between the mean of the first counted third and the last — thirds
    /// rather than endpoints, since two single days can differ by a Saturday. Stated only when both
    /// thirds have something in them: a range measured at one end is not a trend, and calling it one
    /// would be our sampling described as their decline.
    /// </remarks>
    public string Sentence(string tag)
    {
        if (!HasAnyCount)
        {
            // Neither names a cause: probed-but-uncountable is a measurement of a game that
            // answered; no measurement is a statement about our crawl, not their game.
            return Messages.For(tag, HasAnyMeasurement
                ? "trend.none.probed"
                : "trend.none.notMeasured");
        }

        var counted = Days.Where(d => d.IsCounted).ToList();
        var typical = Median(counted.Select(d => d.Average!.Value));

        var start = Messages.Say(
            tag,
            "trend.summary",
            ("typical", Number(typical)),
            ("peak", Ceiling),
            ("counted", CountedDays),
            ("days", Days.Count));

        // Two sentences, not one with a word substituted in — "Steady across the range" is a whole
        // clause a translator can reorder.
        return Direction(tag, counted) is { } direction ? $"{start} {direction}" : start;
    }

    /// <summary>One line per week, which is the "read as text" disclosure and the plain rendering.</summary>
    /// <remarks>
    /// Weeks rather than days, since a quarter is ninety days and nobody reads ninety lines.
    /// <b>All three states survive the compression, including inside a week that has all of them.</b>
    /// "5 days of 7" would be §5.4's collapse wearing a fraction — uncountable and unmeasured days
    /// are different facts about a game — so the remainder is broken out by name, and a week with
    /// nothing counted still says which kind of nothing it was.
    /// </remarks>
    public IEnumerable<string> PerWeek(string tag)
    {
        foreach (var week in Days.Chunk(7))
        {
            var span = week.Length == 1
                ? Messages.Say(tag, "trend.week.oneDay", ("d", week[0].Date))
                : Messages.Say(tag, "trend.week.span", ("from", week[0].Date), ("to", week[^1].Date));

            var counted = week.Where(d => d.IsCounted).ToList();
            var uncountable = week.Count(d => d.IsUncountable);
            var unmeasured = week.Count(d => d.IsGap);

            if (counted.Count == 0)
            {
                yield return Messages.Say(
                    tag,
                    uncountable > 0 ? "trend.week.notCounted" : "trend.week.notMeasured",
                    ("span", span));

                continue;
            }

            var line = Messages.Say(
                tag,
                "trend.week.counted",
                ("span", span),
                ("typical", Number(Median(counted.Select(d => d.Average!.Value)))),
                ("peak", counted.Max(d => d.Max!.Value)),
                ("days", counted.Count));

            // Folded through a two-argument message, not a comma written here — the separator
            // belongs to a language.
            if (uncountable > 0)
            {
                line = Join(tag, line, Messages.Say(tag, "trend.week.uncounted", ("count", uncountable)));
            }

            if (unmeasured > 0)
            {
                line = Join(tag, line, Messages.Say(tag, "trend.week.unmeasured", ("count", unmeasured)));
            }

            yield return line;
        }
    }

    private static string Join(string tag, string line, string clause) =>
        Messages.Say(tag, "trend.week.and", ("line", line), ("clause", clause));

    /// <summary>
    /// The rollup's buckets as a day per column, with the days it does not hold left as gaps.
    /// </summary>
    public static TrendSeries Over(DateOnly from, DateOnly to, IReadOnlyList<PresenceRollup> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        var byDay = buckets.ToDictionary(b => DateOnly.FromDateTime(b.Bucket.UtcDateTime));
        var days = new List<TrendDay>();

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            days.Add(byDay.TryGetValue(date, out var bucket)
                ? new TrendDay(
                    date,
                    bucket.CountedSamples,
                    bucket.UnmeasurableSamples,
                    bucket.MinCount,
                    bucket.MaxCount,
                    bucket.MeanCount)
                : new TrendDay(date, 0, 0, null, null, null));
        }

        return new TrendSeries(from, to, days);
    }

    private static string? Direction(string tag, IReadOnlyList<TrendDay> counted)
    {
        if (counted.Count < 6)
        {
            return null;
        }

        var third = counted.Count / 3;
        var first = counted.Take(third).Select(d => d.Average!.Value).ToList();
        var last = counted.TakeLast(third).Select(d => d.Average!.Value).ToList();

        if (first.Count == 0 || last.Count == 0)
        {
            return null;
        }

        var before = first.Average();
        var after = last.Average();

        // A tenth is the floor under "changed at all" — below it, reporting a direction would be
        // reading noise as our probe schedule.
        if (before <= 0 || Math.Abs(after - before) / before < 0.1)
        {
            return Messages.For(tag, "trend.direction.steady");
        }

        // Handed to the formatter as a fraction, so the sign and its spacing are the locale's.
        var change = Math.Round(Math.Abs(after - before) / before, 2);

        return Messages.Say(
            tag,
            after > before ? "trend.direction.up" : "trend.direction.down",
            ("change", change));
    }


    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();

        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    /// <summary>A count of players, floored — see <see cref="TrendDay.Typical"/> for why down.</summary>
    private static string Number(double value) =>
        Math.Floor(value).ToString("0", CultureInfo.InvariantCulture);
}
