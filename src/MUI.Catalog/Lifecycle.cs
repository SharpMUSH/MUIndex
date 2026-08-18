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
    /// Previously <c>ENETUNREACH</c>/<c>EHOSTUNREACH</c> fell through <c>DialFailure.Classify</c>'s
    /// catch-all to <see cref="Timeout"/> — a route that did not exist was published as a game that
    /// did not answer in time. <c>availability_interval.detail</c> keeps the errno that tells the
    /// causes apart.
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
    /// Not archived: archiving reverses on any answered probe, which would flip an excluded game
    /// back every cycle since these answer perfectly well. What they are not is games somebody
    /// could go and play.
    /// </para>
    /// <para>
    /// It is our judgement, so it is stored as ours with a reason the page shows — rule 5 forbids
    /// recording a decision of ours as a measurement of theirs.
    /// </para>
    /// </remarks>
    Excluded,

    /// <summary>
    /// A game whose owner asked to be left alone and then asked to come out of the listing as well
    /// (spec §11, migration 0025).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="Excluded"/>: exclusion is our judgement that a thing isn't a game for players.
    /// This is a game — what it is not is a game that wants to be in a directory.
    /// </para>
    /// <para>
    /// Offered only to an owner who has already stopped the crawl on every address their game
    /// answers on (<c>OwnerOptOut</c>'s <c>AllStopped</c>) — unlisting is that same "stop" decision
    /// made twice.
    /// </para>
    /// <para>
    /// A probe undoes it, unlike an exclusion: <c>ArchiveSweeper.RestoreAsync</c> relists on the
    /// first answered probe, safe because an opted-out address is refused before the dial, so an
    /// answered probe proves no opt-out stands.
    /// </para>
    /// </remarks>
    Unlisted,
}
