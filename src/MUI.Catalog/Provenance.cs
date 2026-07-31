namespace MUI.Catalog;

/// <summary>
/// How a descriptive field's value was obtained. This is the spine of the whole product: nothing is
/// displayed without it, and the ordering below is the precedence when sources disagree (spec §5.1).
/// </summary>
/// <remarks>
/// Declared order is precedence order, highest first. <see cref="Staff"/> outranks everything and is
/// always logged; <see cref="Handshake"/> beats <see cref="Mssp"/> for capability fields because a
/// server offering an option is an observation and a game claiming it is an assertion.
/// </remarks>
public enum FieldSource
{
    Staff,
    Handshake,
    Owner,
    Who,
    Mssp,
    Banner,

    // There is deliberately no imported source here, and there was: ImportedMeasured for a directory
    // that ran its own probe, ImportedAsserted for a hand-maintained list. The backfill contributes
    // *addresses* and nothing else now (spec §7.6) — every value about a game is measured by this
    // crawler — so an imported field is a row that can no longer be written, and a source nothing can
    // produce is a ladder rung that only invites somebody to reach for it.
}

/// <summary>
/// A value together with where it came from and how old it is. There is no unlabelled data on this
/// site, so there is no way to carry a value without one of these.
/// </summary>
public sealed record Provenance(
    FieldSource Source,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastConfirmedAt)
{
    /// <summary>Whether this was observed by somebody rather than asserted by the game itself.</summary>
    public bool IsMeasured => Source is FieldSource.Handshake or FieldSource.Who;

    public TimeSpan AgeAt(DateTimeOffset now) => now - LastConfirmedAt;
}
