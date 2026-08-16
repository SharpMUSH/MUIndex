namespace MUI.Catalog;

/// <summary>
/// One completed crawl cycle, as stored.
/// </summary>
/// <remarks>
/// The counters are <c>MUI.Crawler</c>'s <c>CycleReport</c>, flattened. The type is declared here
/// rather than there because <c>MUI.Catalog</c> references nothing and everything references it —
/// the crawler maps its report onto this on the way to the store, and the site reads it back
/// without either end learning about the other.
/// </remarks>
/// <param name="StartedAt">When the cycle began claiming targets.</param>
/// <param name="FinishedAt">When it stopped, whether or not every target answered.</param>
public sealed record CrawlCycleRecord(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int Considered,
    int Probed,
    int Answered,
    int Failed,
    int Refused,
    int OptedOut,
    int Errored,
    int Listed,
    int Reviews,
    int Counted,
    int Unmeasurable,
    int Transitions,
    int Referrals)
{
    public TimeSpan Took => FinishedAt - StartedAt;
}

/// <summary>
/// What the crawler has been doing lately, read from what it wrote.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured rather than asserted, applied to our own instrument.</b> The web tier cannot see the
/// crawler and must not pretend to: they share a process only by accident of packaging, a replica
/// with <c>MUI_CRAWL_ENABLED=false</c> holds no lease and knows nothing, and an in-process
/// <c>bool</c> would make the front page's answer depend on which replica served the request. Every
/// field here comes out of the database the crawler writes to, with a time on it, exactly as a
/// game's facts do.
/// </para>
/// <para>
/// <b>It follows that this may not name a cause</b>, and <see cref="Quiet"/> is the whole of what
/// silence is allowed to mean. We cannot tell a crashed host from a lease held by a replica we
/// cannot see from a cycle that had nothing due, and the one time it mattered all three were live
/// hypotheses at once. Rule 5 is about not publishing our limits as somebody else's facts; the same
/// discipline pointed inward says a strip that cannot distinguish three causes may not print one.
/// </para>
/// </remarks>
/// <param name="LastProbeAt">
/// The newest <c>last_probed_at</c> in the registry — the crawler's pulse, and null before it has
/// ever completed a probe.
/// </param>
/// <param name="NextDueAt">
/// The soonest target due. Usually in the past on a healthy crawl, because a cycle claims a batch
/// and works through it rather than waiting for each to ripen.
/// </param>
/// <param name="DueNow">Targets whose next probe is already owed.</param>
/// <param name="TargetsKnown">Addresses in the registry, listed or not (§7.1: nothing retires).</param>
/// <param name="LastCycle">The newest completed cycle, or null before there has been one.</param>
public sealed record CrawlerPulse(
    DateTimeOffset? LastProbeAt,
    DateTimeOffset? NextDueAt,
    int DueNow,
    int TargetsKnown,
    CrawlCycleRecord? LastCycle)
{
    /// <summary>Nothing measured yet, which is what a deployment on its first run looks like.</summary>
    public static readonly CrawlerPulse Unknown = new(null, null, 0, 0, null);

    /// <summary>
    /// How long a pulse may be older than the poll interval before the strip stops calling it live.
    /// </summary>
    /// <remarks>
    /// Four times <c>DiscoveryOptions.PollInterval</c>'s default of thirty seconds. Generous on
    /// purpose: a cycle whose batch is all slow servers can legitimately go minutes without landing
    /// a probe, and a strip that cried wolf on that would be one nobody reads by the second week.
    /// </remarks>
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(2);

    /// <summary>Whether the last probe is recent enough to say so.</summary>
    public bool IsWorking(DateTimeOffset now) =>
        LastProbeAt is { } last && now - last <= Fresh;

    /// <summary>
    /// The reading, in the only three states the evidence supports.
    /// </summary>
    /// <remarks>
    /// <see cref="CrawlState.Quiet"/> is deliberately not "stopped", "stalled" or "down". It says
    /// what we know — no probe has landed lately — and leaves the cause to somebody who can see more
    /// than one row of a table.
    /// </remarks>
    public CrawlState State(DateTimeOffset now) => LastProbeAt is null
        ? CrawlState.NotYet
        : IsWorking(now) ? CrawlState.Working : CrawlState.Quiet;
}

/// <summary>What a reader may conclude about the crawler from what it wrote.</summary>
public enum CrawlState
{
    /// <summary>No probe has ever completed here. A fresh deployment, not a fault.</summary>
    NotYet,

    /// <summary>A probe landed within <see cref="CrawlerPulse.Fresh"/>.</summary>
    Working,

    /// <summary>None has. <b>This names no cause and must not be given one.</b></summary>
    Quiet,
}
