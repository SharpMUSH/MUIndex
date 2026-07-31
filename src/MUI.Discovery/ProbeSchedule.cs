namespace MUI.Discovery;

/// <summary>
/// When a target is next due. Pure arithmetic: no clock, no storage, no I/O.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no retirement here, and there must never be.</b> This schedule serves a permanent
/// registry that grows monotonically and outlives every process that touches it, so failure has only
/// one effect: the interval lengthens, and it is clamped. Spec §7.4 is explicit that a game dark for
/// two years is still probed weekly, forever, including after it has been archived — which is exactly
/// what no incumbent managed (spec §3). A bounded single-run frontier can afford to give a host up
/// because its run ends; this one has no such moment, so it has no such verdict. There is no
/// <c>Never</c>, no <c>TimeSpan.MaxValue</c> and no sentinel meaning "stop" — that absence is asserted
/// by <c>ProbeScheduleTests.ThereIsNoNeverInThisSchedule</c>.
/// </para>
/// <para>
/// <b>The wording trap.</b> §7.4 calls the clamp a "floor". It means a floor under <em>frequency</em>
/// — "still probed weekly" — which is a <em>ceiling</em> on the interval. Read the other way round it
/// would mean "never probe more often than weekly", which is the opposite behaviour. The constant is
/// therefore called <see cref="LongestInterval"/> and every comparison against it takes the smaller
/// value.
/// </para>
/// <para>
/// <b>Where §7.4 and §7.7/§11 disagree.</b> A server may ask, through <c>CRAWL DELAY</c>, for a gap
/// longer than a week. §11 says that request is honoured as a floor; §7.4 wants a weekly ceiling.
/// §7.7 resolves it in favour of politeness and this implements that resolution: the backoff is
/// clamped to <see cref="LongestInterval"/> <em>first</em> and the server's request applied
/// <em>afterwards</em>, composing as <c>max(CRAWL DELAY, backoff)</c>, so a server asking for thirty
/// days gets thirty days. §7.4's point is that we never give up, not that we override the operator
/// whose machine we are dialling.
/// </para>
/// </remarks>
public static class ProbeSchedule
{
    /// <summary>The gap after a successful probe of a game with nobody on it.</summary>
    public static readonly TimeSpan BaseInterval = TimeSpan.FromHours(6);

    /// <summary>The gap after a successful probe that found players — §7.7's "tightened for recent activity".</summary>
    public static readonly TimeSpan BusyInterval = TimeSpan.FromHours(2);

    /// <summary>
    /// The longest gap the backoff may produce: §7.4's permanent weekly probe, expressed as a ceiling
    /// on the interval rather than a floor under it. See the type's remarks.
    /// </summary>
    public static readonly TimeSpan LongestInterval = TimeSpan.FromDays(7);

    /// <summary>Caps the exponent so a long-dead target cannot overflow the multiplication.</summary>
    public const int MaxDoublings = 20;

    /// <summary>How long from a probe until that target is due again.</summary>
    public static TimeSpan Next(
        int consecutiveFailures,
        TimeSpan? crawlDelay,
        ActivityBand activity = ActivityBand.Unknown)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailures);

        var interval = consecutiveFailures == 0
            ? activity is ActivityBand.Busy ? BusyInterval : BaseInterval
            : BaseInterval * Math.Pow(2, Math.Min(consecutiveFailures - 1, MaxDoublings));

        if (interval > LongestInterval)
        {
            interval = LongestInterval;
        }

        // max(CRAWL DELAY, backoff), and in this order: the clamp bounds how far *our* backoff may
        // lengthen, and is not licence to knock weekly at a server that asked for a month.
        return crawlDelay is { } requested && requested > interval ? requested : interval;
    }

    /// <summary>The instant that gap lands on.</summary>
    public static DateTimeOffset NextProbeAt(
        DateTimeOffset now,
        int consecutiveFailures,
        TimeSpan? crawlDelay,
        ActivityBand activity = ActivityBand.Unknown) =>
        now + Next(consecutiveFailures, crawlDelay, activity);
}
