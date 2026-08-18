namespace MUI.Discovery;

/// <summary>
/// Whether a silence in the crawl means we stopped looking. Pure arithmetic: no clock, no storage.
/// </summary>
/// <remarks>
/// The gap is a fact about the crawler, not about any game: <c>AvailabilityWriter</c> extends an
/// interval whose state hasn't changed and has no notion of elapsed time, so without this it can't
/// tell "nothing changed" from "nobody looked" — a crawl outage would otherwise be published as
/// observed reachability. A per-target overdue threshold isn't a substitute: §7.4 requires a game
/// dark for years to still be probed weekly, so no single constant works across the population; the
/// crawler knows precisely when it last ran instead.
/// </remarks>
public static class CrawlGap
{
    /// <summary>
    /// How long the crawl may be silent before that silence is a hole rather than a pause.
    /// </summary>
    /// <remarks>
    /// Tied to <see cref="ProbeSchedule.BusyInterval"/> rather than chosen independently: nothing is
    /// probed more often than the busy cadence, so below this threshold no game can have missed a
    /// scheduled probe, and above it comfortably clears ordinary interruptions like a container
    /// replacement or host reboot.
    /// </remarks>
    public static readonly TimeSpan Threshold = ProbeSchedule.BusyInterval;

    /// <summary>
    /// The instant the crawl demonstrably stopped, or null if it did not.
    /// </summary>
    /// <param name="lastCycleFinishedAt">
    /// When the last crawl cycle finished. Null on a deployment that has never crawled, which is not
    /// an outage and must not be read as one.
    /// </param>
    /// <remarks>
    /// Answers when the crawl stopped, never when we noticed — closing the interval at discovery time
    /// would still credit the whole outage as observed, just a cycle less of it.
    /// </remarks>
    public static DateTimeOffset? StoppedLookingAt(
        DateTimeOffset? lastCycleFinishedAt,
        DateTimeOffset now,
        TimeSpan? threshold = null)
    {
        if (lastCycleFinishedAt is not { } finished)
        {
            return null;
        }

        // A last cycle in the future is a clock that moved, not a silence; negative silence fails
        // the threshold check below on its own.
        var silence = now - finished;

        return silence > (threshold ?? Threshold) ? finished : null;
    }
}
