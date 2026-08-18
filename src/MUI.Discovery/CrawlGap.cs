namespace MUI.Discovery;

/// <summary>
/// Whether a silence in the crawl means we stopped looking. Pure arithmetic: no clock, no storage.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap is a fact about the crawler and not about any game</b>, which is what makes it
/// expressible at all. <c>AvailabilityWriter</c> extends an interval whose state and cause have not
/// changed and has no notion of elapsed time, so it cannot tell "nothing changed" from "nobody
/// looked": a game reachable before a crawl outage and reachable after it presents identical input
/// either way, and keeps one interval across the hole. <c>Reachability.FractionReachable</c> then
/// counts that interval's whole span as observed, and the hole is published as measurement.
/// </para>
/// <para>
/// Measured, not hypothesised. On 2026-08-18 the catalogue held 173 intervals doing exactly this
/// across a fifteen-day outage, together claiming some 2,600 days of observation that never happened
/// — <c>abysmal-realms-mud</c> read <i>"Reachable 14.0% of the 19 days we have measured, 17 days
/// degraded"</i> about a game that answers fine. They were repaired by hand, twice, because the
/// boundary had to be reconstructed from the edges of gaps a previous repair left behind. Recording
/// the instant while it is still known is the whole of this type's purpose.
/// </para>
/// <para>
/// <b>Why a per-game threshold is not the answer.</b> The obvious guard — "this target is overdue,
/// so close its interval" — needs a per-target notion of overdue, and there is no constant that
/// works: §7.4 requires a game dark for years to still be probed weekly, and a polite
/// <c>CRAWL DELAY</c> can stretch that to a month. A threshold that fired at a day would shred the
/// history of exactly the population §7.4 exists to keep watching. The crawler, by contrast, knows
/// precisely when it last ran.
/// </para>
/// </remarks>
public static class CrawlGap
{
    /// <summary>
    /// How long the crawl may be silent before that silence is a hole rather than a pause.
    /// </summary>
    /// <remarks>
    /// <see cref="ProbeSchedule.BusyInterval"/>, and tied to it rather than chosen: nothing is probed
    /// more often than the busy cadence, so below this no game can have missed a scheduled probe and
    /// there is nothing to record. Above it, some game certainly did.
    /// <para>
    /// It also has to clear the ordinary interruptions comfortably, and it does. Measured on the
    /// production host on 2026-08-18: two container replacements produced crawl silences of about
    /// forty seconds, and a host reboot about two minutes.
    /// </para>
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
    /// <b>The answer is when we stopped looking, never when we noticed.</b> Closing an interval at
    /// the moment of discovery would credit the whole outage as observed time and merely stop it
    /// growing, which is the same false claim one cycle smaller.
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

        // A last cycle in the future is a clock that moved, not a silence. Reading it as one would
        // close every interval in the catalogue at an instant none of them had reached.
        var silence = now - finished;

        return silence > (threshold ?? Threshold) ? finished : null;
    }
}
