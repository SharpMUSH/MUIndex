using System.Globalization;

using MUI.Catalog;

namespace MUI.Web.Components;

/// <summary>
/// One day of the presence trend, in the three states of spec §5.4.
/// </summary>
/// <remarks>
/// The heatmap answers <em>when in a week is anyone on</em>, folding every week measured into one
/// grid. This answers the question that folding destroys: <em>is this game growing or dying</em>. A
/// game that doubled last month and one that halved produce the same heatmap.
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

    /// <summary>The mean to one decimal, or null where nothing was counted.</summary>
    public double? Average => Mean is { } m ? (double)Math.Round(m, 1) : null;

    public string Label => this switch
    {
        { IsGap: true } => $"{Date:d MMM yyyy} — no measurement",
        { IsUncountable: true } => $"{Date:d MMM yyyy} — probed, no count could be read",
        { Min: { } lo, Max: { } hi } when lo == hi =>
            $"{Date:d MMM yyyy} — {lo} players, every one of {Probes(CountedSamples)}",
        _ => $"{Date:d MMM yyyy} — {Average} on average, {Min}–{Max} across {Probes(CountedSamples)}",
    };

    private static string Probes(int n) => n == 1 ? "1 probe" : $"{n} probes";
}

/// <summary>
/// A game's measured presence over a calendar range, ready to draw and ready to say.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every day in the range is present, including the ones nobody measured.</b> The rollup returns
/// only the days it has, and a chart drawn straight off that list would space eleven measured days
/// evenly across a quarter and describe a gap as a gentle slope. So the range is filled to a day per
/// column here, where the missing ones become <see cref="TrendDay.IsGap"/> and stay visibly missing.
/// </para>
/// <para>
/// A gap is never a zero, and it is never given a cause: a failed probe writes no presence row at
/// all, so silence covers an hour we could not reach and an hour we never probed alike. Reachability
/// is the strip's question and it has the intervals that can tell the two apart.
/// </para>
/// </remarks>
public sealed record TrendSeries(DateOnly From, DateOnly To, IReadOnlyList<TrendDay> Days)
{
    /// <summary>
    /// The widest a range may be — the same bound the API answers at this grain.
    /// </summary>
    /// <remarks>
    /// Read off <see cref="Api.SeriesEndpoints.WidestWindow"/> rather than restated, so the page and
    /// the route a reader would script against cannot drift into disagreeing about how much history
    /// is available in one request.
    /// </remarks>
    public static readonly int MaximumDays =
        (int)Api.SeriesEndpoints.WidestWindow[PresenceGrain.Day].TotalDays;

    /// <summary>What a reader gets without asking for a range.</summary>
    /// <remarks>
    /// Ninety, the same window the reachability strip beside it covers, so the two graphics on the
    /// page are read against the same stretch of calendar rather than silently against two.
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
    /// The direction is the answer a reader came for, and it is taken as the difference between the
    /// mean of the first counted third of the range and the mean of the last — thirds rather than
    /// endpoints, because two single days can differ by a Saturday. It is stated only when both
    /// thirds have something in them: a range measured at one end is not a trend, and calling it one
    /// would be our sampling described as their decline.
    /// </remarks>
    public string Sentence
    {
        get
        {
            if (!HasAnyCount)
            {
                return HasAnyMeasurement
                    ? "Probed in this range, and no player count could be read from any of it."
                    : "No measurement in this range.";
            }

            var counted = Days.Where(d => d.IsCounted).ToList();
            var typical = Median(counted.Select(d => d.Average!.Value));
            var peak = Ceiling;

            var start = $"Typically {Number(typical)} on, peaking at {peak}, "
                + $"over {Measured(CountedDays)} of {Span(Days.Count)}.";

            return Direction(counted) is { } direction ? $"{start} {direction}" : start;
        }
    }

    /// <summary>One line per week, which is the "read as text" disclosure and the plain rendering.</summary>
    /// <remarks>
    /// <para>
    /// Weeks rather than days, because a quarter is ninety days and nobody reads ninety lines.
    /// </para>
    /// <para>
    /// <b>All three states survive the compression, including inside a week that has all of them.</b>
    /// The first draft said "5 days of 7", which is §5.4's collapse wearing a fraction: two days a
    /// probe got into and could not count and two days nobody looked are the same missing two there,
    /// and they are different facts about a game. So the remainder is broken out by name, and a week
    /// with nothing counted still says which kind of nothing it was.
    /// </para>
    /// </remarks>
    public IEnumerable<string> PerWeek()
    {
        foreach (var week in Days.Chunk(7))
        {
            var span = week.Length == 1
                ? $"{week[0].Date:d MMM}"
                : $"{week[0].Date:d MMM}–{week[^1].Date:d MMM}";

            var counted = week.Where(d => d.IsCounted).ToList();
            var uncountable = week.Count(d => d.IsUncountable);
            var unmeasured = week.Count(d => d.IsGap);

            if (counted.Count == 0)
            {
                yield return uncountable > 0
                    ? $"{span}: probed, no count could be read"
                    : $"{span}: not measured";

                continue;
            }

            var typical = Number(Median(counted.Select(d => d.Average!.Value)));
            var high = counted.Max(d => d.Max!.Value);
            var line = $"{span}: typically {typical}, peak {high}, {Measured(counted.Count)} counted";

            if (uncountable > 0)
            {
                line += $", {Measured(uncountable)} probed without a count";
            }

            if (unmeasured > 0)
            {
                line += $", {Measured(unmeasured)} not measured";
            }

            yield return line;
        }
    }

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

    private static string? Direction(IReadOnlyList<TrendDay> counted)
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

        // A tenth is the floor under "changed at all". Below it the two thirds are the same number
        // with noise on it, and reporting a direction would be reading our probe schedule.
        if (before <= 0 || Math.Abs(after - before) / before < 0.1)
        {
            return "Steady across the range.";
        }

        var change = (int)Math.Round(Math.Abs(after - before) / before * 100);

        return after > before
            ? $"Up about {change}% from the start of the range to the end."
            : $"Down about {change}% from the start of the range to the end.";
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();

        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    private static string Number(double value) =>
        value.ToString(value % 1 == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);

    private static string Measured(int n) => n == 1 ? "1 day" : $"{n} days";

    private static string Span(int n) => n == 1 ? "1 day" : $"{n} days";
}
