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
/// §5.3 writes a transition on a change of state or of cause; without this, an interval spanning a
/// crawl outage kept one row across the hole and <c>Reachability.FractionReachable</c> counted the
/// whole span as observed. Runs off <c>crawl_cycle.finished_at</c> rather than a per-target timeout,
/// since no fixed threshold works when §7.4 requires even a long-dark game to keep being probed and
/// <c>CRAWL DELAY</c> can stretch that further. Writes nothing when there's no cycle history to
/// compare against — not knowing when we stopped is a reason for silence, never a reason to guess.
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
