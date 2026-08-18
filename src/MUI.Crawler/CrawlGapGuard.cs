using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Discovery;

using Microsoft.Extensions.Logging;

namespace MUI.Crawler;

/// <summary>
/// Ends every open availability interval when the crawl has been silent long enough to have been
/// stopped rather than merely quiet (spec §5.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>The third reason a transition is written.</b> §5.3 says a change of state or of cause writes
/// one, and until this existed there were only those two — so an interval could not notice that
/// nobody had looked at it, and a game reachable either side of an outage kept one row across the
/// hole while <c>Reachability.FractionReachable</c> counted the row's whole span as observed.
/// </para>
/// <para>
/// <b>It runs where the knowledge is.</b> Whether we were watching is a fact about the crawler, and
/// the crawler is the only thing that knows it — <c>crawl_cycle.finished_at</c>, written every poll.
/// Asking each target instead needs a per-target notion of overdue, and no constant works: §7.4
/// requires a game dark for years to still be probed weekly and <c>CRAWL DELAY</c> can stretch that
/// to a month, so a threshold tight enough to catch an outage would shred the history of the
/// population §7.4 exists to keep watching.
/// </para>
/// <para>
/// <b>Nothing is guessed.</b> A deployment with no cycles behind it, or one predating migration 0017
/// and running without the table at all, writes nothing: not knowing when we stopped is a reason for
/// silence and never a reason to pick an instant.
/// </para>
/// </remarks>
public sealed class CrawlGapGuard(
    IAvailabilityStore availability,
    ICrawlCycles? cycles,
    TimeProvider time,
    TimeSpan? threshold = null,
    ILogger<CrawlGapGuard>? logger = null)
{
    /// <summary>
    /// Ends the open intervals if the crawl was interrupted, and answers when it stopped.
    /// </summary>
    public async Task<DateTimeOffset?> CloseAnyGapAsync(CancellationToken cancellationToken = default)
    {
        if (cycles is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var pulse = await cycles.PulseAsync(now, cancellationToken);

        if (CrawlGap.StoppedLookingAt(pulse.LastCycle?.FinishedAt, now, threshold) is not { } stopped)
        {
            return null;
        }

        var closed = await availability.CloseOpenIntervalsAsync(stopped, cancellationToken);

        // Loudly, because this is the one startup path that edits history rather than adding to it,
        // and because the silence it is reporting is ours.
        logger?.LogWarning(
            "The crawl was silent for {Silence} — ending {Closed} open intervals at {Stopped}, "
            + "so the gap reads as unmeasured rather than as time we watched",
            now - stopped,
            closed,
            stopped);

        return stopped;
    }
}
