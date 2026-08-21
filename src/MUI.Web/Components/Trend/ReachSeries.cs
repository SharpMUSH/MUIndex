using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// One day of the availability strip. <b>Reachable</b>, never <em>up</em>: we measured a socket from
/// one vantage point, and a game with a routing problem to our host is unreachable and perfectly
/// alive (spec §5.8).
/// </summary>
public sealed record ReachDay(DateOnly Date, ReachState State, FailureCause Cause)
{
    /// <summary>
    /// One bar's title, in the reader's language. Ninety of these are drawn per game.
    /// </summary>
    /// <remarks>
    /// A day before we knew this game existed says so about us, never that the game was down — it
    /// gets its own id rather than a shade of "unreachable", so it never carries a cause.
    /// </remarks>
    public string Label(string tag) => State switch
    {
        ReachState.Reachable => Messages.Say(tag, "reach.day.reachable", ("d", Date)),
        ReachState.Degraded =>
            Messages.Say(tag, "reach.day.degraded", ("d", Date), ("cause", Wording.Cause(tag, Cause))),
        ReachState.Unreachable =>
            Messages.Say(tag, "reach.day.unreachable", ("d", Date), ("cause", Wording.Cause(tag, Cause))),
        _ => Messages.Say(tag, "reach.day.notMeasured", ("d", Date)),
    };

    /// <summary>
    /// The state as a noun, for the middle of a spell — "3 days unreachable".
    /// </summary>
    /// <remarks>
    /// Own ids, separate from the legend's, even though the English matches: the legend is a caption
    /// beside a swatch and this declines inside a sentence, which languages with grammatical case
    /// need in different forms.
    /// </remarks>
    public string Word(string tag) => Messages.For(tag, State switch
    {
        ReachState.Reachable => "reach.word.reachable",
        ReachState.Degraded => "reach.word.degraded",
        ReachState.Unreachable => "reach.word.unreachable",
        _ => "reach.word.notMeasured",
    });
}

/// <summary>
/// What the worst thing that happened in a day was.
/// </summary>
/// <remarks>
/// Four states, not three: the strip is always ninety days wide, even for a game found last week, so
/// the days before we knew it existed need their own state rather than being painted "unreachable" —
/// recording a decision of ours as a measurement of theirs is the one thing this site may never do.
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
/// Ninety bars, oldest at the left, following status-page convention. Degraded renders as a
/// <em>short</em> bar rather than only a different colour; unreachable is hatched and outlined so it
/// reads as absent rather than as an alarm.
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
    public string Sentence(string tag)
    {
        if (!HasAnyMeasurement)
        {
            return Messages.Say(tag, "reach.none", ("days", Window));
        }

        var parts = new List<string>(5);

        var bad = Days.Count(d => d.State is ReachState.Unreachable);
        var degraded = Days.Count(d => d.State is ReachState.Degraded);
        var unmeasured = Days.Count(d => d.State is ReachState.Unmeasured);
        var measured = Window - unmeasured;

        // Leading run of unmeasured days = before we knew the game existed (a fact about us).
        // Unmeasured days after that = a gap in our crawl (also a fact about us, different cause).
        var predating = Days.TakeWhile(d => d.State is ReachState.Unmeasured).Count();
        var unwatched = unmeasured - predating;

        // Denominator is *observed* time, not the window (Reachability.FractionReachable) — a game
        // found an hour ago must not read "Reachable 100.0% of the last 90 days" off one probe.
        // Separate ids rather than a substituted noun, so no locale has to make one sentence do both.
        parts.Add(ReachableFraction is { } f
            ? unmeasured == 0
                ? Messages.Say(tag, "reach.fraction.window", ("percent", Wording.Percent(f)), ("days", Window))
                : Messages.Say(tag, "reach.fraction.measured", ("percent", Wording.Percent(f)), ("days", measured))
            : Messages.Say(tag, "reach.fraction.unknown", ("days", Window)));

        parts.Add(bad == 0
            ? Messages.For(tag, unmeasured == 0
                ? "reach.unreachable.noneInWindow"
                : "reach.unreachable.noneMeasured")
            : Messages.Say(tag, "reach.unreachable.days", ("count", bad)));

        if (degraded > 0)
        {
            parts.Add(Messages.Say(tag, "reach.degraded.days", ("count", degraded)));
        }

        if (LongestOutage is { } outage)
        {
            // The longest outage's own cause, not the most recent one — pairing a duration with a
            // different interval's cause would invent an event.
            parts.Add(LongestOutageCause is FailureCause.None
                ? Messages.Say(tag, "reach.longestOutage", ("duration", Wording.Duration(outage)))
                : Messages.Say(
                    tag,
                    "reach.longestOutage.cause",
                    ("duration", Wording.Duration(outage)),
                    ("cause", Wording.Cause(tag, LongestOutageCause))));
        }

        // Never "unreachable for N days" and never a cause here — both are facts about our crawl,
        // and attaching a cause would turn one into a fact about the game.
        if (predating > 0)
        {
            parts.Add(Messages.Say(tag, "reach.predate", ("count", predating)));
        }

        if (unwatched > 0)
        {
            parts.Add(Messages.Say(tag, "reach.gap", ("count", unwatched)));
        }

        return string.Join(' ', parts);
    }

    /// <summary>Every spell that was not plain reachable, as words. The strip's text alternative.</summary>
    public IReadOnlyList<string> Spells(string tag)
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
                ? Messages.Say(tag, "reach.spell.oneDay", ("d", Days[start].Date))
                : Messages.Say(tag, "reach.spell.range", ("from", Days[start].Date), ("to", Days[i - 1].Date));

            spells.Add(Days[start].Cause is FailureCause.None
                ? Messages.Say(
                    tag,
                    "reach.spell",
                    ("range", range),
                    ("count", days),
                    ("word", Days[start].Word(tag)))
                : Messages.Say(
                    tag,
                    "reach.spell.cause",
                    ("range", range),
                    ("count", days),
                    ("word", Days[start].Word(tag)),
                    ("cause", Wording.Cause(tag, Days[start].Cause))));
        }

        return spells;
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

                // The worst thing that happened in a day is what the bar says — an hour of downtime
                // must not disappear into "reachable that day".
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
    /// The cause recorded against the interval that <em>is</em> the longest outage, rather than the
    /// most recent one — which would pair a real duration with an unrelated event.
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
