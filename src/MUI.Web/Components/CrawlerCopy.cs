using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// The words the crawler strip is allowed to use, in one place so the page and the plain-text
/// rendering cannot drift apart.
/// </summary>
/// <remarks>
/// <para>
/// <b>The hard rule here is that silence may not be given a cause.</b> A quiet pulse is consistent
/// with a crashed host, a lease held by a replica this process cannot see, a batch of slow servers,
/// and a registry with nothing due — and on the day this was written three of those four were live
/// hypotheses at once. Rule 5 forbids publishing our limits as facts about somebody else's game;
/// pointed at ourselves it forbids publishing a guess as a fact about our own. So the copy says what
/// was observed — when the last probe landed — and stops.
/// </para>
/// <para>
/// It also never says <em>uptime</em> and never says <em>down</em>. Same vocabulary discipline as
/// the availability strip, for the same reason.
/// </para>
/// </remarks>
public static class CrawlerCopy
{
    /// <summary>The heartbeat, as a sentence.</summary>
    public static string State(string tag, CrawlerPulse pulse, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pulse);

        if (pulse.LastProbeAt is not { } last)
        {
            // Reached only by a caller that renders NotYet anyway, and worded rather than empty so
            // that a future caller which does render it says something true.
            return Messages.For(tag, "crawler.noProbe");
        }

        // The age arrives as an argument rather than being concatenated on: the ladder that builds
        // it already localizes, and a language that puts the age before the state has nowhere to
        // say so if the two are glued together. This whole sentence used to be English around a
        // German fragment, on the one strip whose job is to let a reader discount the counts above
        // it — the provenance line for four figures, in a language its reader did not choose.
        var age = Relative.Ago(tag, now - last);
        var args = new Dictionary<string, object?>(StringComparer.Ordinal) { ["age"] = age };

        return Messages.For(
            tag,
            // "Quiet", not "stopped": see the class remarks. The age is the measurement; the reader
            // draws their own conclusion, which is more than we can honestly draw for them.
            pulse.State(now) is CrawlState.Working ? "crawler.live" : "crawler.quiet",
            args);
    }

    /// <summary>
    /// What the last completed cycle did, or null when there has not been one.
    /// </summary>
    /// <remarks>
    /// Three counters out of thirteen. The rest are in the API and the log; a front-page strip that
    /// printed all of them would be a dashboard, and this is a line of provenance under a search box.
    /// <c>Considered</c> is named "due" because that is what it counts and what the log calls it.
    /// </remarks>
    public static string? LastCycle(string tag, CrawlerPulse pulse)
    {
        ArgumentNullException.ThrowIfNull(pulse);

        if (pulse.LastCycle is not { } cycle)
        {
            return null;
        }

        // An empty cycle is a real answer and gets its own words, because "0 due · 0 answered" reads
        // like a failure and means the opposite: everything in the registry is up to date.
        // The strip already reads "crawler live · last probe 4m ago", so "last cycle:" was a label
        // for a clause that follows one anyway. What the cycle did, in the strip's own voice.
        return cycle.Considered == 0
            ? Messages.For(tag, "crawler.cycle.nothingDue")
            : Messages.For(
                tag,
                "crawler.cycle",
                // Three counters, three plural arguments in one message. English inflects none of
                // them, which is exactly why they may not be three concatenated fragments: the
                // languages that do inflect them would have no way to reach the words.
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
