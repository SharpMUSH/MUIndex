using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// The words the crawler strip is allowed to use, in one place so the page and the plain-text
/// rendering cannot drift apart.
/// </summary>
/// <remarks>
/// <b>Silence may not be given a cause.</b> A quiet pulse is consistent with a crashed host, a lease
/// held by a replica, slow servers, or nothing due — rule 5 forbids publishing a guess as a fact
/// about our own state just as it forbids publishing one about somebody else's game. So the copy
/// says what was observed (when the last probe landed) and stops, and it never says <em>uptime</em>
/// or <em>down</em> — same vocabulary discipline as the availability strip.
/// </remarks>
public static class CrawlerCopy
{
    /// <summary>The heartbeat, as a sentence.</summary>
    public static string State(string tag, CrawlerPulse pulse, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pulse);

        if (pulse.LastProbeAt is not { } last)
        {
            // Reached only by a caller that renders NotYet anyway, worded rather than empty so a
            // future caller that does render it says something true.
            return Messages.For(tag, "crawler.noProbe");
        }

        // The age is an argument, not concatenated on: a language that puts age before state
        // needs somewhere to say so. This sentence used to be English glued around a German
        // fragment.
        var age = Relative.Ago(tag, now - last);
        var args = new Dictionary<string, object?>(StringComparer.Ordinal) { ["age"] = age };

        return Messages.For(
            tag,
            // "Quiet", not "stopped": see the class remarks.
            pulse.State(now) is CrawlState.Working ? "crawler.live" : "crawler.quiet",
            args);
    }

    /// <summary>
    /// What the last completed cycle did, or null when there has not been one.
    /// </summary>
    /// <remarks>
    /// Three counters out of thirteen — the rest are in the API and the log, since a front-page
    /// strip that printed all of them would be a dashboard, not a provenance line under a search box.
    /// <c>Considered</c> is named "due" because that's what it counts and what the log calls it.
    /// </remarks>
    public static string? LastCycle(string tag, CrawlerPulse pulse)
    {
        ArgumentNullException.ThrowIfNull(pulse);

        if (pulse.LastCycle is not { } cycle)
        {
            return null;
        }

        // An empty cycle is a real answer and gets its own words: "0 due · 0 answered" reads like a
        // failure but means the registry is fully up to date.
        return cycle.Considered == 0
            ? Messages.For(tag, "crawler.cycle.nothingDue")
            : Messages.For(
                tag,
                "crawler.cycle",
                // Three plural arguments in one message rather than concatenated fragments: English
                // inflects none of them, but languages that do need the whole message to do it.
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["considered"] = cycle.Considered,
                    ["answered"] = cycle.Answered,
                    ["failed"] = cycle.Failed,
                });
    }

    /// <summary>The backlog, for the plain rendering, which has room for it.</summary>
    public static string Registry(string tag, CrawlerPulse pulse)
    {
        ArgumentNullException.ThrowIfNull(pulse);

        return Messages.For(
            tag,
            "crawler.registry",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["targets"] = pulse.TargetsKnown,
                ["due"] = pulse.DueNow,
            });
    }
}
