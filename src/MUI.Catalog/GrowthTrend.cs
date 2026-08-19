namespace MUI.Catalog;

/// <summary>
/// A week's median against the week before it, in three words. Never a member for "not enough data"
/// — that is <c>null</c>, the same absence every other derived facet uses, so a reader who filters on
/// it can ask for it the same way they ask for an unidentified codebase.
/// </summary>
public enum GrowthDirection
{
    Down,
    Steady,
    Up,
}

/// <summary>
/// Turns two windows' medians into the one direction the growth arrow and the <c>trending</c> facet
/// both read — computed once so the two can never disagree about the same pair of numbers.
/// </summary>
public static class GrowthTrend
{
    /// <summary>
    /// How far apart two medians must be, as a fraction of the larger, before the panel calls it a
    /// change rather than noise. Below this a difference of one or two players on a small game reads
    /// as "up" for no reason a probe interval could support.
    /// </summary>
    public const double SteadyBand = 0.10;

    /// <summary>
    /// How little of a window a game must have existed in before there is no such thing as a trend to
    /// read off it — no floor, however scaled, rescues a game seen for a few hours.
    /// </summary>
    public static readonly TimeSpan MinimumOverlap = TimeSpan.FromDays(2);

    /// <summary>The floor <see cref="RequiredSamples"/> never scales below, even for a sliver of a window.</summary>
    private const int MinimumFloor = 6;

    /// <summary>
    /// The direction, or <c>null</c> where either window falls short of the sample floor every
    /// median needs before it ranks (<see cref="SortWindows.MinimumSamples"/> by default —
    /// <see cref="RequiredSamples"/> scales it down for a game younger than the window). A thin
    /// current week, a thin prior week, or no prior week at all leaves nothing to compare.
    /// </summary>
    public static GrowthDirection? Of(
        int? median, int? samples, int? priorMedian, int? priorSamples,
        int requiredSamples = SortWindows.MinimumSamples, int requiredPriorSamples = SortWindows.MinimumSamples)
    {
        if (median is not { } current || samples is not { } n || n < requiredSamples
            || priorMedian is not { } prior || priorSamples is not { } priorN
            || priorN < requiredPriorSamples)
        {
            return null;
        }

        var basis = Math.Max(current, prior);

        if (basis == 0)
        {
            return GrowthDirection.Steady;
        }

        var delta = (current - prior) / (double)basis;

        return delta switch
        {
            > SteadyBand => GrowthDirection.Up,
            < -SteadyBand => GrowthDirection.Down,
            _ => GrowthDirection.Steady,
        };
    }

    /// <summary>
    /// How many counted samples <paramref name="windowFrom"/>–<paramref name="windowTo"/> needs
    /// before its median may enter a comparison — the full <see cref="SortWindows.MinimumSamples"/>
    /// for a game that existed for the whole window, scaled down for one that only overlapped part of
    /// it, so a game short of two weeks old gets a best-effort read rather than a permanent
    /// <c>null</c> for its whole first fortnight.
    /// </summary>
    /// <remarks>
    /// A null <paramref name="firstSeenAt"/> is treated as "existed for the whole window" — every real
    /// row has the column set, and a caller with no better data must not be handed a floor lower than
    /// it has actually earned.
    /// </remarks>
    public static int RequiredSamples(DateTimeOffset windowFrom, DateTimeOffset windowTo, DateTimeOffset? firstSeenAt)
    {
        var overlapFrom = firstSeenAt is { } seen && seen > windowFrom ? seen : windowFrom;
        var overlap = windowTo - overlapFrom;

        if (overlap < MinimumOverlap)
        {
            return int.MaxValue;
        }

        var span = windowTo - windowFrom;

        return Math.Max(MinimumFloor, (int)Math.Ceiling(SortWindows.MinimumSamples * (overlap / span)));
    }
}
