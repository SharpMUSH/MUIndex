using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>
/// Which way an address hands over its MSSP report, as far as the crawl loop has learnt
/// (<c>crawl_target.mssp_route</c>, migration 0042). Null on a target means nothing is known yet.
/// </summary>
public enum MsspRoute
{
    /// <summary>A report has arrived over telnet option 70. The plaintext form is never sent.</summary>
    Telnet,

    /// <summary>Answered the plaintext <c>MSSP-REQUEST</c>, so it is asked every session.</summary>
    Plaintext,

    /// <summary>Sent <c>MSSP-REQUEST</c> and did not answer it. Never asked again.</summary>
    None,
}

/// <summary>
/// The two decisions <see cref="MsspRoute"/> exists for: what to ask this time, and what the answer
/// teaches.
/// </summary>
/// <remarks>
/// Here rather than in the crawl loop so both are a pure function of a target and a
/// <see cref="ProbeResult"/>, testable without a socket or a database.
/// </remarks>
public static class MsspRoutes
{
    /// <summary>What the probe is to do about <c>MSSP-REQUEST</c> at an address on this route.</summary>
    /// <remarks>
    /// An unknown address gets one trial, on a dial of its own, and only once the probe has seen the
    /// ordinary session come back without option 70 (<see cref="PlaintextMsspAsk.Trial"/>). The probe
    /// decides that last half, since only it has the session.
    /// </remarks>
    public static PlaintextMsspAsk Ask(MsspRoute? route) => route switch
    {
        null => PlaintextMsspAsk.Trial,
        MsspRoute.Plaintext => PlaintextMsspAsk.Ask,
        _ => PlaintextMsspAsk.Never,
    };

    /// <summary>
    /// The route this probe has taught, or <paramref name="current"/> when it taught nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Option 70 wins from any state, and once learnt is never unlearnt: the plaintext form is for
    /// games that have not given us MSSP the proper way, and a game that has once is not asked it
    /// again because one session's report went missing. Counted from the outcome rather than the
    /// report, so a report too large to keep still counts: the game offered it, we declined it.
    /// </para>
    /// <para>
    /// An unanswered request is final from the unknown state, which is the point. From
    /// <see cref="MsspRoute.Plaintext"/> it returns the address to unknown instead, so the next crawl
    /// gives it one more trial. A game that has answered before and misses once has more likely been
    /// slow than changed its code, and losing its only MSSP route for ever on one slow night would be
    /// our timing recorded as a fact about it (rule 5). A second miss, on the trial, settles it.
    /// </para>
    /// <para>
    /// A failed dial teaches nothing, and neither does a session that never sent the line.
    /// </para>
    /// </remarks>
    public static MsspRoute? Learn(MsspRoute? current, ProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is not ProbeOutcome.Answered)
        {
            return current;
        }

        if (result.MsspOutcome is not MsspOutcome.NotOffered
            && result.PlaintextMssp is not PlaintextMsspOutcome.Answered)
        {
            return MsspRoute.Telnet;
        }

        if (current is MsspRoute.Telnet)
        {
            return current;
        }

        return result.PlaintextMssp switch
        {
            PlaintextMsspOutcome.Answered => MsspRoute.Plaintext,
            PlaintextMsspOutcome.Unanswered when current is MsspRoute.Plaintext => null,
            PlaintextMsspOutcome.Unanswered => MsspRoute.None,
            _ => current,
        };
    }
}
