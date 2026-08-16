namespace MUI.Catalog;

/// <summary>
/// What a probe found, as stored in an availability interval (spec §5.3). Intervals rather than
/// samples: a game up for three years is one row, and only a change of cause opens a new one.
/// </summary>
public enum AvailabilityState
{
    /// <summary>
    /// Deliberately <em>reachable</em> rather than <c>Up</c> (spec §5.8). We measured a socket from
    /// one vantage point; we did not measure whether the game was up, and "up" claims we did. A game
    /// with a routing problem to our host is unreachable and perfectly alive.
    /// </summary>
    Reachable,
    Degraded,
    Unreachable,
}

/// <summary>Why a probe failed. Only a change of cause writes a new interval.</summary>
public enum FailureCause
{
    None,
    Dns,
    Refused,
    Tls,
    Timeout,
    HandshakeStalled,
}

/// <summary>
/// A game's presentational state, derived from availability history — never stored as a fact and
/// never set by hand (spec §7.4).
/// </summary>
public enum LifecycleState
{
    Active,
    Quiet,
    Dark,

    /// <summary>
    /// Out of the default listing, out of the rankings, out of the "active today" figure — and still
    /// probed weekly, still permanently addressable, still present in the historical series for the
    /// periods it was actually up. One successful probe reverses it (spec §7.5).
    /// </summary>
    Archived,

    /// <summary>
    /// An address that answers like a game and is not one: a dev instance, a stock mudlib demo, a
    /// tool on the network, a server whose name is still <c>Your MUD Name</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not archived, and the difference is what the state means rather than what it hides.</b>
    /// Archiving says "this stopped answering", and a probe that gets an answer reverses it
    /// immediately and without a human (§7.5) — so archiving something that answers perfectly well
    /// flips it back every cycle and records a change each time. These answer fine. What they are
    /// not is games somebody could go and play.
    /// </para>
    /// <para>
    /// <b>It is our judgement, so it is stored as ours</b> and carries a reason the page shows. Rule
    /// 5 forbids recording a decision of ours as a measurement of theirs, and nothing about a socket
    /// tells us that a mud called <c>test</c> is not for players — an editor decided that.
    /// </para>
    /// <para>
    /// Everything §7.5 promises still holds: the page, the URL, the history and the change feed all
    /// survive, and it is probed for ever. Only the default listing, the rankings and the "active
    /// today" figure drop it.
    /// </para>
    /// </remarks>
    Excluded,
}
