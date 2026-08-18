namespace MUI.Catalog;

/// <summary>
/// A closed or open span during which a game was in one state for one reason (spec §5.3).
/// </summary>
/// <remarks>
/// Intervals rather than samples: a game reachable for three years is one row, and "reachable over
/// 90 days" and "longest outage" become arithmetic over a handful of them.
/// </remarks>
public sealed record AvailabilityInterval
{
    public required Guid GameId { get; init; }

    public required AvailabilityState State { get; init; }

    public required DateTimeOffset FromAt { get; init; }

    /// <summary>Null while the interval is still open.</summary>
    public DateTimeOffset? ToAt { get; init; }

    public FailureCause Cause { get; init; } = FailureCause.None;

    /// <summary>
    /// What the dial actually said, when it said anything. Null on a reachable interval.
    /// </summary>
    /// <remarks>
    /// Evidence, never a key: <see cref="Cause"/> is what the site reasons about, and several of its
    /// values are wastebaskets ("timeout" covers a socket timeout, an exhausted probe budget, and any
    /// error with no word of its own). This is the sentence underneath it, kept because container
    /// logs last half an hour and a dark game outlives them. It plays no part in deciding a
    /// transition (§5.3).
    /// </remarks>
    public string? Detail { get; init; }

    public bool IsOpen => ToAt is null;

    public TimeSpan DurationAt(DateTimeOffset now) => (ToAt ?? now) - FromAt;
}

public interface IAvailabilityStore
{
    /// <summary>The currently open interval for a game, or null if it has never been probed.</summary>
    Task<AvailabilityInterval?> OpenIntervalAsync(Guid gameId, CancellationToken cancellationToken = default);

    Task OpenAsync(AvailabilityInterval interval, CancellationToken cancellationToken = default);

    Task CloseAsync(Guid gameId, DateTimeOffset at, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvailabilityInterval>> ForGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends every open interval at <paramref name="at"/>, and returns how many were ended.
    /// </summary>
    /// <remarks>
    /// The one write that is about the crawler rather than a game — the only way the catalogue can
    /// say "we stopped watching". State and cause are kept as they were: a game dark when we stopped
    /// looking was dark, and rewriting that swaps one false claim for another. Each game opens a
    /// fresh interval on its next probe.
    /// <para>
    /// Intervals that began after <paramref name="at"/> are left alone — one can start after the last
    /// recorded cycle, and ending it earlier than it began is a row the table refuses.
    /// </para>
    /// </remarks>
    Task<int> CloseOpenIntervalsAsync(DateTimeOffset at, CancellationToken cancellationToken = default);
}

/// <summary>
/// Extends the open interval or closes it and opens a new one. <b>Only a change of state or cause
/// writes a transition</b> — a hundred consecutive timeouts are one interval, not a hundred.
/// </summary>
public interface IAvailabilityWriter
{
    Task<AvailabilityOutcome> ObserveAsync(
        Guid gameId,
        AvailabilityState state,
        FailureCause cause,
        DateTimeOffset at,
        string? detail = null,
        CancellationToken cancellationToken = default);
}

public enum AvailabilityOutcome
{
    /// <summary>Same state and cause as the open interval; nothing was written.</summary>
    Extended,

    /// <summary>State or cause changed; the open interval was closed and a new one opened.</summary>
    Transitioned,

    /// <summary>There was no open interval; the first one was opened.</summary>
    Opened,
}

/// <summary>Reachability arithmetic over intervals. Never called "uptime" (spec §5.8).</summary>
public static class Reachability
{
    /// <summary>
    /// The fraction of <paramref name="window"/> ending at <paramref name="now"/> during which the
    /// game was <see cref="AvailabilityState.Reachable"/>, or null when nothing overlaps the window
    /// at all — a game we have never probed is not "0% reachable", it is unmeasured.
    /// </summary>
    public static double? FractionReachable(
        IReadOnlyList<AvailabilityInterval> intervals,
        TimeSpan window,
        DateTimeOffset now)
    {
        var from = now - window;
        var observed = TimeSpan.Zero;
        var reachable = TimeSpan.Zero;

        foreach (var interval in intervals)
        {
            var start = interval.FromAt > from ? interval.FromAt : from;
            var end = interval.ToAt ?? now;
            if (end > now)
            {
                end = now;
            }

            if (end <= start)
            {
                continue;
            }

            var span = end - start;
            observed += span;
            if (interval.State is AvailabilityState.Reachable)
            {
                reachable += span;
            }
        }

        return observed == TimeSpan.Zero ? null : reachable / observed;
    }

    /// <summary>The longest unreachable span overlapping the window, or null if there was none.</summary>
    public static TimeSpan? LongestOutage(
        IReadOnlyList<AvailabilityInterval> intervals,
        TimeSpan window,
        DateTimeOffset now)
    {
        var from = now - window;
        TimeSpan? longest = null;

        foreach (var interval in intervals.Where(i => i.State is AvailabilityState.Unreachable))
        {
            var start = interval.FromAt > from ? interval.FromAt : from;
            var end = interval.ToAt ?? now;
            if (end > now)
            {
                end = now;
            }

            if (end <= start)
            {
                continue;
            }

            var span = end - start;
            if (longest is null || span > longest)
            {
                longest = span;
            }
        }

        return longest;
    }

    /// <summary>
    /// Cumulative reachable time, which is what <see cref="ArchivePolicy"/> credits. Cumulative and
    /// not span: a game reachable for two years out of five is credited with two.
    /// </summary>
    public static TimeSpan CumulativeReachable(
        IReadOnlyList<AvailabilityInterval> intervals,
        DateTimeOffset now) =>
        intervals
            .Where(i => i.State is AvailabilityState.Reachable)
            .Aggregate(TimeSpan.Zero, (total, i) => total + i.DurationAt(now));
}
