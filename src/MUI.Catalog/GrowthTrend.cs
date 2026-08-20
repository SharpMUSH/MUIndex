namespace MUI.Catalog;

/// <summary>
/// A trend fitted across a game's own measured history, in three words. Never a member for "not
/// enough data" — that is <c>null</c>, the same absence every other derived facet uses, so a reader
/// who filters on it can ask for it the same way they ask for an unidentified codebase.
/// </summary>
public enum GrowthDirection
{
    Down,
    Steady,
    Up,
}

/// <summary>One day's measured median, for whichever day the crawler had enough samples to take one.</summary>
/// <remarks>
/// A day with too few counted samples for its own median has no row at all (rule 4) — it is absent
/// from the series a game's trend is fitted over, never a zero or a repeat of its neighbour.
/// </remarks>
public sealed record DailyMedian(DateOnly Day, int Median, int Samples);

/// <summary>
/// Turns a game's own daily medians into the one direction the growth arrow and the <c>trending</c>
/// facet both read — computed once so the two can never disagree about the same series.
/// </summary>
/// <remarks>
/// Not a two-bucket comparison (this week against last week): a fixed pair of buckets has no answer
/// for a game whose measured history is younger than the buckets themselves — the older bucket is
/// simply empty, no matter how its floor is scaled, for as long as the game (or the whole
/// deployment) is under two weeks old. A least-squares line through however many days of median the
/// game actually has works with as few as <see cref="MinimumDays"/> — noisily at first, more surely
/// as more days accumulate, rather than being unusable until a fixed calendar boundary passes.
/// </remarks>
public static class GrowthTrend
{
    /// <summary>
    /// How far apart the fitted line's endpoints must be, as a fraction of the series' average level,
    /// before the panel calls it a change rather than noise. Below this a difference of one or two
    /// players on a small game reads as "up" for no reason a probe interval could support.
    /// </summary>
    public const double SteadyBand = 0.10;

    /// <summary>
    /// The fewest distinct days with a median a line may be fit through. Two points always fit a line
    /// exactly, which is indistinguishable from noise; three is the first count where the line says
    /// anything about the days between its ends.
    /// </summary>
    public const int MinimumDays = 3;

    /// <summary>How many counted samples one day needs before that day contributes its own median.</summary>
    public const int MinimumSamplesPerDay = 6;

    /// <summary>How far back a trend looks, at most — <c>NpgsqlGameQueries</c>'s query window.</summary>
    public static readonly TimeSpan Span = TimeSpan.FromDays(14);

    /// <summary>
    /// The direction, or <c>null</c> below <see cref="MinimumDays"/> distinct days of median.
    /// </summary>
    public static GrowthDirection? Of(IReadOnlyList<DailyMedian> days)
    {
        var change = ChangeFraction(days);

        if (change is not { } delta)
        {
            return null;
        }

        return delta switch
        {
            > SteadyBand => GrowthDirection.Up,
            < -SteadyBand => GrowthDirection.Down,
            _ => GrowthDirection.Steady,
        };
    }

    /// <summary>
    /// How far the fitted line rises (or falls) across the days it was fit over, as a fraction of
    /// their average level — the one number <see cref="Of"/>'s direction and the trending board's
    /// ranked "by how much" both read, so the two can never disagree about the same series.
    /// </summary>
    /// <remarks>
    /// An ordinary least-squares slope against the day a sample fell on, not a bare first-day-versus-
    /// last-day difference — two noisy endpoints would make exactly the two-bucket mistake this
    /// replaced. The slope is read over the full span between the earliest and latest day actually
    /// present, so a gap where the crawler missed a day (too few samples for that day's own median)
    /// still contributes its true calendar distance rather than being silently compressed out.
    /// </remarks>
    public static double? ChangeFraction(IReadOnlyList<DailyMedian> days)
    {
        ArgumentNullException.ThrowIfNull(days);

        // Both the floor and the one-row-per-day rule are this method's own contract, not something
        // every caller must remember to re-apply — DailyMediansAsync's SQL already excludes a thin day
        // and never emits two rows for one (its GROUP BY sees to that), but a day another caller (a
        // fixture, a future query) hands in unfiltered, or hands in twice, must not fit a line through
        // it either, or cast that day an extra vote in the regression by counting it as two points.
        var counted = days
            .Where(d => d.Samples >= MinimumSamplesPerDay)
            .GroupBy(d => d.Day)
            .Select(g => g.First())
            .ToList();

        if (counted.Count < MinimumDays)
        {
            return null;
        }

        var earliest = counted.Min(d => d.Day);
        var points = counted.Select(d => (X: (double)(d.Day.DayNumber - earliest.DayNumber), Y: (double)d.Median)).ToList();

        var meanX = points.Average(p => p.X);
        var meanY = points.Average(p => p.Y);
        var covariance = points.Sum(p => (p.X - meanX) * (p.Y - meanY));
        var variance = points.Sum(p => (p.X - meanX) * (p.X - meanX));

        // Every day in the series distinct by construction, so this is unreachable in practice — a
        // defensive read rather than a crash if a caller ever hands in duplicate days.
        if (variance == 0)
        {
            return 0;
        }

        var slope = covariance / variance;
        var span = points.Max(p => p.X) - points.Min(p => p.X);
        var predictedChange = slope * span;

        return meanY == 0 ? 0 : predictedChange / meanY;
    }
}
