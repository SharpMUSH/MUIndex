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
    /// The direction, or <c>null</c> where either window falls short of the sample floor every
    /// median needs before it ranks (<see cref="SortWindows.MinimumSamples"/>) — a game with a
    /// thin current week, a thin prior week, or no prior week at all has nothing to compare.
    /// </summary>
    public static GrowthDirection? Of(int? median, int? samples, int? priorMedian, int? priorSamples)
    {
        if (median is not { } current || samples is not { } n || n < SortWindows.MinimumSamples
            || priorMedian is not { } prior || priorSamples is not { } priorN
            || priorN < SortWindows.MinimumSamples)
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
}
