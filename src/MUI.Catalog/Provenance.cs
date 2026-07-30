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

    /// <summary>
    /// Imported from a third party that ran its own probe — MudStats, MudVerse, Grapevine. Worth more
    /// than a self-report, because it is still a measurement; worth less than ours, because we cannot
    /// audit their probe, parser or failure handling (spec §7.6).
    /// </summary>
    ImportedMeasured,

    /// <summary>
    /// Imported from a hand-maintained list. Seeds discovery and endpoints and nothing else — no
    /// history, no presence, no archive grace.
    /// </summary>
    ImportedAsserted,
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
    public bool IsMeasured => Source is FieldSource.Handshake or FieldSource.Who or FieldSource.ImportedMeasured;

    public TimeSpan AgeAt(DateTimeOffset now) => now - LastConfirmedAt;
}
