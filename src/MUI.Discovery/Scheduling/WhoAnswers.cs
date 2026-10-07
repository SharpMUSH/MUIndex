using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>
/// What a probe teaches about whether an address answers a pre-login <c>WHO</c>
/// (<c>crawl_target.who_answers_at</c>, migration 0043).
/// </summary>
/// <remarks>
/// A pure function of a target's memory and a <see cref="ProbeResult"/>, as <see cref="MsspRoutes"/>
/// is, so it is testable without a socket or a database.
/// </remarks>
public static class WhoAnswers
{
    /// <summary>
    /// The new value of <see cref="CrawlTarget.WhoAnswersAt"/>, or <paramref name="current"/> when
    /// this probe taught nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A counted <c>WHO</c> sets it, dated by the probe, and only from unknown: re-dating it on every
    /// crawl would be a write per probe to say what the row already says.
    /// </para>
    /// <para>
    /// The login prompt taking <c>WHO</c> for a character name clears it, because that is the game
    /// saying it has no pre-login <c>WHO</c> at this address any more, and typing it again every
    /// crawl is exactly what the restraint in <c>TelnetProbe.PublishedCountAsync</c> exists to stop.
    /// An unreadable answer does not: that is our parser meeting a dialect, not the game changing.
    /// </para>
    /// <para>
    /// A failed dial teaches nothing, and neither does a session that never typed <c>WHO</c>.
    /// </para>
    /// </remarks>
    public static DateTimeOffset? Learn(DateTimeOffset? current, ProbeResult result, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is not ProbeOutcome.Answered)
        {
            return current;
        }

        if (result.Who.HasCount)
        {
            return current ?? now;
        }

        return result.Who.Confidence is WhoConfidence.LoginPrompt ? null : current;
    }
}
