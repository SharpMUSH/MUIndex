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

    /// <summary>
    /// The full breakdown of the last cycle, for the status page rather than the front-page strip.
    /// </summary>
    /// <remarks>Six of the thirteen stored counters — the rest are in the API and the log, same
    /// restraint as <see cref="LastCycle"/> for the same reason, just a wider six.</remarks>
    public static string? FullCycle(string tag, CrawlerPulse pulse)
    {
        ArgumentNullException.ThrowIfNull(pulse);

        return pulse.LastCycle is { } cycle ? Cycle(tag, cycle) : null;
    }

    /// <summary>
    /// One cycle's six-counter breakdown — what <see cref="FullCycle"/> prints for the newest, and
    /// what the status page's history list prints per row so the two never state it two ways.
    /// </summary>
    public static string Cycle(string tag, CrawlCycleRecord cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        return cycle.Considered == 0
            ? Messages.For(tag, "crawler.cycle.nothingDue")
            : Messages.For(
                tag,
                "crawler.cycle.full",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["considered"] = cycle.Considered,
                    ["probed"] = cycle.Probed,
                    ["answered"] = cycle.Answered,
                    ["failed"] = cycle.Failed,
                    ["errored"] = cycle.Errored,
                    ["optedOut"] = cycle.OptedOut,
                });
    }

    /// <summary>When the last cycle finished and how long it took, both already formatted.</summary>
    public static string? CycleFinishedAt(string tag, CrawlerPulse pulse)
    {
        ArgumentNullException.ThrowIfNull(pulse);

        return pulse.LastCycle is not { } cycle
            ? null
            : Messages.Say(
                tag,
                "crawler.cycle.finishedAt",
                ("when", Dates.Stamp(tag, cycle.FinishedAt)),
                ("took", Wording.Duration(cycle.Took)));
    }

    /// <summary>
    /// What came of the window's probes — the note under the throughput figure.
    /// </summary>
    /// <remarks>
    /// <b>Every counter here is ours.</b> "Failed" counts dials of this crawler's that did not
    /// complete, which is a fact about our afternoon; it is not a count of games that were down, and
    /// the wording may not drift into saying so (rule 5, pointed inward — same discipline as
    /// <see cref="State"/>'s refusal to say <em>stopped</em>).
    /// </remarks>
    public static string WindowOutcome(string tag, CrawlWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return Messages.For(
            tag,
            "crawler.window.outcome",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["answered"] = window.Answered,
                ["failed"] = window.Failed,
            });
    }

    /// <summary>The window as one line, for the plain rendering, which has no tiles to split it over.</summary>
    public static string WindowLine(string tag, CrawlWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return Messages.For(
            tag,
            "crawler.window.plain",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["probed"] = window.Probed,
                ["answered"] = window.Answered,
                ["failed"] = window.Failed,
                ["span"] = Relative.Format(tag, window.Span),
            });
    }

    /// <summary>
    /// How many cycles the window holds, as the sentence above the ten newest of them.
    /// </summary>
    /// <remarks>
    /// The history list is ten rows whatever the loop did, so without this the page cannot tell a
    /// crawler that ran fourteen hundred cycles today from one that ran ten and stopped.
    /// </remarks>
    public static string WindowCycles(string tag, CrawlWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var args = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["cycles"] = window.Cycles,
            ["span"] = Relative.Format(tag, window.Span),
        };

        return Messages.For(tag, window.IsEmpty ? "crawler.history.ledeEmpty" : "crawler.history.lede", args);
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
