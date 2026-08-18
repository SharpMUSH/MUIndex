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

    /// <summary>
    /// No path from here to there: the network or the host was unreachable, or our own interface
    /// was down.
    /// </summary>
    /// <remarks>
    /// <b>Legitimately a game's record, because of what the word already means here.</b> Reachable
    /// is measured from one vantage point at intervals — that is why nothing in this schema is
    /// called uptime, and why "a game with a routing problem to our host is unreachable and
    /// perfectly alive" is a sentence the vocabulary already had to be able to say. This is that
    /// sentence with a cause attached.
    /// <para>
    /// Before it existed, <c>ENETUNREACH</c> and <c>EHOSTUNREACH</c> fell through
    /// <c>DialFailure.Classify</c>'s catch-all to cause <c>error</c>, which
    /// <c>FailureReading.CauseOf</c> then mapped to <see cref="Timeout"/> — so a route that did not
    /// exist was published as a game that did not answer in time. Two different facts under one
    /// word, and the wrong one. <c>availability_interval.detail</c> keeps the errno that tells the
    /// three apart.
    /// </para>
    /// </remarks>
    NoRoute,
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

    /// <summary>
    /// A game whose owner asked to be left alone and then asked to come out of the listing as well
    /// (spec §11, migration 0025).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not <see cref="Excluded"/>, and the difference is whose statement it is.</b> An exclusion
    /// is our judgement that a thing is not a game for players, and it carries an argument a reader
    /// can disagree with. This is a game. What it is not is a game that wants to be in a directory,
    /// and the state has to be able to say that without also saying something false about what it is.
    /// </para>
    /// <para>
    /// <b>Reachable only under a standing opt-out.</b> Unlisting is offered to an owner who has
    /// already stopped the crawl on every address their game answers on — <c>OwnerOptOut</c>'s
    /// <c>AllStopped</c>. "They asked us to stop and they meant it" is one decision made twice, and
    /// the second half is not offered to somebody who has not made the first.
    /// </para>
    /// <para>
    /// <b>A probe undoes it, unlike an exclusion.</b> <c>ArchiveSweeper.RestoreAsync</c> relists it
    /// on the first answered probe — safe by construction, because an opted-out address is refused
    /// before the dial, so a probe that answers is proof that no opt-out stands. The exit an operator
    /// can work alone (delete the TXT record and wait out §7.4's floor) therefore brings the listing
    /// back with the crawl.
    /// </para>
    /// </remarks>
    Unlisted,
}
