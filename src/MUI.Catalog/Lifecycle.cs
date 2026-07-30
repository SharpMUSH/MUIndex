namespace MUI.Catalog;

/// <summary>
/// What a probe found, as stored in an availability interval (spec §5.3). Intervals rather than
/// samples: a game up for three years is one row, and only a change of cause opens a new one.
/// </summary>
public enum AvailabilityState
{
    Up,
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
}
