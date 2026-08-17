using MUI.Catalog;

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
            return "no probe has finished here yet";
        }

        // The age localizes; the sentence around it has not been through the bundle yet, so a German
        // reader meets one German fragment in an English line. That is the fallback working — it
        // tells them truthfully which part has been translated — and not a reason to leave the age
        // English too.
        var age = Relative.Ago(tag, now - last);

        return pulse.State(now) is CrawlState.Working
            ? $"crawler live · last probe {age}"
            // "Quiet", not "stopped": see the class remarks. The age is the measurement; the reader
            // draws their own conclusion, which is more than we can honestly draw for them.
            : $"crawler quiet · last probe {age}";
    }

    /// <summary>
    /// What the last completed cycle did, or null when there has not been one.
    /// </summary>
    /// <remarks>
    /// Three counters out of thirteen. The rest are in the API and the log; a front-page strip that
    /// printed all of them would be a dashboard, and this is a line of provenance under a search box.
    /// <c>Considered</c> is named "due" because that is what it counts and what the log calls it.
    /// </remarks>
    public static string? LastCycle(CrawlerPulse pulse)
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
            ? "nothing due this cycle"
            : $"{cycle.Considered} due · {cycle.Answered} answered · {cycle.Failed} failed";
    }

    /// <summary>The backlog, for the plain rendering, which has room for it.</summary>
    public static string Registry(CrawlerPulse pulse)
    {
        ArgumentNullException.ThrowIfNull(pulse);

        return $"{pulse.TargetsKnown} addresses in the registry, {pulse.DueNow} due now";
    }
}
