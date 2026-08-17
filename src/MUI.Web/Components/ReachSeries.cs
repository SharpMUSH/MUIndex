using MUI.Catalog;

namespace MUI.Web.Components;

/// <summary>
/// One day of the availability strip. <b>Reachable</b>, never <em>up</em>: we measured a socket from
/// one vantage point, and a game with a routing problem to our host is unreachable and perfectly
/// alive (spec §5.8).
/// </summary>
public sealed record ReachDay(DateOnly Date, ReachState State, FailureCause Cause)
{
    public string Label => State switch
    {
        ReachState.Reachable => $"{Date:d MMM} — reachable all day",
        ReachState.Degraded => $"{Date:d MMM} — degraded ({Wording.Cause(Cause)}): answered, could not finish",
        ReachState.Unreachable => $"{Date:d MMM} — unreachable ({Wording.Cause(Cause)})",
        _ => $"{Date:d MMM} — not measured; we were not watching this game yet",
    };

    public string Word => State switch
    {
        ReachState.Reachable => "reachable",
        ReachState.Degraded => "degraded",
        ReachState.Unreachable => "unreachable",
        _ => "not measured",
    };
}

/// <summary>
/// What the worst thing that happened in a day was.
/// </summary>
/// <remarks>
/// Four, not three. The design names three bar states, and a game we have watched for ninety days
/// only ever needs those — but the strip is ninety days wide for every game, including one found
/// last Tuesday. Painting the seventy-eight days before we knew it existed as "unreachable" would
/// record a decision of ours as a measurement of theirs, which is the one thing this site may never
/// do. So a day nobody watched is its own state and says so.
/// </remarks>
public enum ReachState
{
    /// <summary>Outside anything we observed. Not a fact about the game.</summary>
    Unmeasured,

    Reachable,

    /// <summary>We got in and could not finish (spec §5.3). A short bar, not just another colour.</summary>
    Degraded,

    Unreachable,
}

/// <summary>
/// The 90-day strip, and the three figures under it, derived from availability intervals.
/// </summary>
/// <remarks>
/// The strip follows status-page convention — ninety bars, oldest at the left — because readers
/// already know how to read one. It diverges in the two places the design names: degraded is a
/// <em>short</em> bar rather than only a different colour, and unreachable is hatched and outlined
/// so it reads as absent rather than as an alarm.
/// </remarks>
public sealed record ReachSummary(
    IReadOnlyList<ReachDay> Days,
    double? ReachableFraction,
    TimeSpan? LongestOutage,
    FailureCause LongestOutageCause,
    FailureCause LastCause)
{
    public int Window => Days.Count;

    public bool HasAnyMeasurement => Days.Any(d => d.State is not ReachState.Unmeasured);

    /// <summary>The sentence the graphic is an illustration of, never a substitute for.</summary>
    public string Sentence
    {
        get
        {
            if (!HasAnyMeasurement)
            {
                return $"Not yet measured over the last {Window} days.";
            }

            var parts = new List<string>(4);

            var bad = Days.Count(d => d.State is ReachState.Unreachable);
            var degraded = Days.Count(d => d.State is ReachState.Degraded);
            var unmeasured = Days.Count(d => d.State is ReachState.Unmeasured);
            var measured = Window - unmeasured;

            // The percentage's denominator is *observed* time, not the window (Reachability
            // .FractionReachable). The sentence has to say so, or a game found an hour ago reads
            // "Reachable 100.0% of the last 90 days" off a single successful probe — a true number
            // wearing a claim eighty-nine days wider than the evidence. Only when the window is
            // fully measured do the two denominators coincide and the shorter phrasing become true.
            parts.Add(ReachableFraction is { } f
                ? unmeasured == 0
                    ? $"Reachable {Wording.Percent(f)} of the last {Window} days."
                    : $"Reachable {Wording.Percent(f)} of the {DayCount(measured)} we have measured."
                : $"Reachability over the last {Window} days is not yet measured.");

            parts.Add(bad == 0
                ? unmeasured == 0
                    ? "No day in the window was unreachable."
                    : "No day we measured was unreachable."
                : $"{bad} {(bad == 1 ? "day" : "days")} unreachable.");

            if (degraded > 0)
            {
                parts.Add($"{degraded} {(degraded == 1 ? "day" : "days")} degraded — we got in and could not finish.");
            }

            if (LongestOutage is { } outage)
            {
                // The longest outage's own cause, not the most recent one. They are different
                // intervals and pairing a duration with somebody else's cause invents an event.
                parts.Add($"Longest outage {Wording.Duration(outage)}"
                    + (LongestOutageCause is FailureCause.None
                        ? "."
                        : $" ({Wording.Cause(LongestOutageCause)})."));
            }

            if (unmeasured > 0)
            {
                parts.Add($"{unmeasured} {(unmeasured == 1 ? "day" : "days")} predate anything we measured.");
            }

            return string.Join(' ', parts);
        }
    }

    private static string DayCount(int n) => n == 1 ? "1 day" : $"{n} days";

    /// <summary>Every spell that was not plain reachable, as words. The strip's text alternative.</summary>
    public IReadOnlyList<string> Spells
    {
        get
        {
            var spells = new List<string>();
            var i = 0;

            while (i < Days.Count)
            {
                var state = Days[i].State;
                if (state is ReachState.Reachable)
                {
                    i++;
                    continue;
                }

                var start = i;
                while (i < Days.Count && Days[i].State == state && Days[i].Cause == Days[start].Cause)
                {
                    i++;
                }

                var days = i - start;
                var range = days == 1
                    ? $"{Days[start].Date:d MMM}"
                    : $"{Days[start].Date:d MMM} – {Days[i - 1].Date:d MMM}";
                var cause = Days[start].Cause is FailureCause.None
                    ? string.Empty
                    : $" ({Wording.Cause(Days[start].Cause)})";

                spells.Add($"{range}: {days} {(days == 1 ? "day" : "days")} {Days[start].Word}{cause}");
            }

            return spells;
        }
    }
}

public static class ReachSeries
{
    /// <summary>How wide the strip is. Status-page convention, and readers know it.</summary>
    public const int WindowDays = 90;

    public static ReachSummary Build(
        IReadOnlyList<AvailabilityInterval> intervals,
        DateTimeOffset now,
        int windowDays = WindowDays)
    {
        var days = new List<ReachDay>(windowDays);

        for (var offset = windowDays - 1; offset >= 0; offset--)
        {
            var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(-offset);
            var dayEnd = dayStart.AddDays(1);
            if (dayEnd > now)
            {
                dayEnd = now;
            }

            var state = ReachState.Unmeasured;
            var cause = FailureCause.None;

            foreach (var interval in intervals)
            {
                var from = interval.FromAt > dayStart ? interval.FromAt : dayStart;
                var to = interval.ToAt ?? now;
                if (to > dayEnd)
                {
                    to = dayEnd;
                }

                if (to <= from)
                {
                    continue;
                }

                // The worst thing that happened in a day is what the day's bar says. A game that was
                // down for an hour was not "reachable that day"; a reader scanning ninety bars for
                // trouble has to be able to find it.
                var observed = interval.State switch
                {
                    AvailabilityState.Reachable => ReachState.Reachable,
                    AvailabilityState.Degraded => ReachState.Degraded,
                    _ => ReachState.Unreachable,
                };

                if (observed > state)
                {
                    state = observed;
                    cause = interval.Cause;
                }
            }

            days.Add(new ReachDay(DateOnly.FromDateTime(dayStart.UtcDateTime), state, cause));
        }

        var window = TimeSpan.FromDays(windowDays);
        var longest = Reachability.LongestOutage(intervals, window, now);

        return new ReachSummary(
            days,
            Reachability.FractionReachable(intervals, window, now),
            longest,
            CauseOfLongest(intervals, now, window, longest),
            LastCause(intervals, now, window));
    }

    /// <summary>
    /// The cause recorded against the interval that <em>is</em> the longest outage. Reading the
    /// most recent cause instead would pair a real duration with an unrelated event.
    /// </summary>
    private static FailureCause CauseOfLongest(
        IReadOnlyList<AvailabilityInterval> intervals,
        DateTimeOffset now,
        TimeSpan window,
        TimeSpan? longest)
    {
        if (longest is not { } target)
        {
            return FailureCause.None;
        }

        var from = now - window;

        foreach (var interval in intervals.Where(i => i.State is AvailabilityState.Unreachable))
        {
            var start = interval.FromAt > from ? interval.FromAt : from;
            var end = interval.ToAt ?? now;
            if (end > now)
            {
                end = now;
            }

            if (end > start && end - start == target)
            {
                return interval.Cause;
            }
        }

        return FailureCause.None;
    }

    private static FailureCause LastCause(
        IReadOnlyList<AvailabilityInterval> intervals,
        DateTimeOffset now,
        TimeSpan window) =>
        intervals
            .Where(i => i.State is not AvailabilityState.Reachable && i.Cause is not FailureCause.None)
            .Where(i => (i.ToAt ?? now) > now - window)
            .OrderByDescending(i => i.FromAt)
            .Select(i => i.Cause)
            .FirstOrDefault();

    /// <summary>
    /// When the game was last reachable, and for how long in total we have measured it reachable —
    /// the two facts the archive is built on.
    /// </summary>
    public static (DateTimeOffset? LastReachableAt, TimeSpan KnownLive, DateTimeOffset? FirstSeenAt) Known(
        IReadOnlyList<AvailabilityInterval> intervals,
        DateTimeOffset now)
    {
        var reachable = intervals.Where(i => i.State is AvailabilityState.Reachable).ToList();

        return (
            reachable.Count == 0 ? null : reachable.Max(i => i.ToAt ?? now),
            Reachability.CumulativeReachable(reachable, now),
            intervals.Count == 0 ? null : intervals.Min(i => i.FromAt));
    }
}

/// <summary>Machine vocabulary said the way a person would say it.</summary>
public static class Wording
{
    public static string Cause(FailureCause cause) => cause switch
    {
        FailureCause.Dns => "dns did not resolve",
        FailureCause.Refused => "connection refused",
        FailureCause.Tls => "tls failed",
        FailureCause.Timeout => "timed out",
        FailureCause.HandshakeStalled => "handshake stalled",
        _ => "no cause recorded",
    };

    /// <summary>
    /// A fraction as a percentage. Hand-formatted because <c>P1</c> under an invariant culture puts
    /// a space before the sign, and "96.7 %" reads as a typo in a sentence.
    /// </summary>
    public static string Percent(double fraction) => $"{fraction * 100:0.0}%";

    public static string Duration(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            var hours = span.Hours;
            return hours == 0 ? $"{(int)span.TotalDays}d" : $"{(int)span.TotalDays}d {hours}h";
        }

        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h" : $"{(int)span.TotalMinutes}m";
    }
}
