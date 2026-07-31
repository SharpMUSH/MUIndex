using MUI.Catalog;
using MUI.Catalog.Persistence;

namespace MUI.Import;

/// <summary>One address a directory lists for a game.</summary>
public sealed record ImportedEndpoint(string Host, int Port, EndpointKind Kind);

/// <summary>
/// One span a third-party prober recorded.
/// </summary>
/// <remarks>
/// <see cref="To"/> is null when the source's export ends with the span still running. The import
/// closes it at the import instant rather than leaving it open: an open interval means "and it is
/// still like this, because we are watching", and we are not — we read a file. The partial unique
/// index on <c>availability_interval</c> would also refuse a second open row for a game we probe
/// ourselves, which is the same rule enforced by the schema.
/// </remarks>
public sealed record ImportedAvailability(DateTimeOffset From, DateTimeOffset? To, bool Reachable);

/// <summary>
/// One player count a third-party prober recorded, with the moment it recorded it.
/// </summary>
/// <remarks>
/// <see cref="At"/> is required and is never the import instant standing in for an unknown one. A
/// directory that publishes a count without saying when it was taken yields no presence at all —
/// dating somebody else's measurement to the moment we happened to read the page is fabrication, and
/// it lands in the day-of-week × hour heatmap as a fact about the game.
/// </remarks>
public sealed record ImportedPresence(DateTimeOffset At, int Count);

/// <summary>
/// What one importer yields for one game.
/// </summary>
/// <remarks>
/// Deliberately not a <c>GameRecord</c>. An import may not mint a listing: a host becomes a listed
/// game by answering for itself (spec §7.2), so the most this record can do for an address we do not
/// already know is put it in the crawl registry and let the crawler find out.
/// </remarks>
public sealed record ImportedGame
{
    /// <summary>The directory this came from, as it will appear on the about page.</summary>
    public required string SourceName { get; init; }

    /// <summary>That directory's own identifier for the game, so a re-import is recognisable.</summary>
    public required string SourceKey { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<ImportedEndpoint> Endpoints { get; init; } = [];

    /// <summary>
    /// Descriptive values, keyed by <see cref="FieldRegistry"/> names. A capability relayed out of
    /// somebody else's MSSP read belongs under <c>capability.x.declared</c> and never
    /// <c>.measured</c>: the third party measured that the server <em>answered</em>, and the game
    /// declared what was in the answer.
    /// </summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Empty for every asserted source, and refused by the pipeline if it is not.</summary>
    public IReadOnlyList<ImportedAvailability> Availability { get; init; } = [];

    /// <summary>Empty for every asserted source, and refused by the pipeline if it is not.</summary>
    public IReadOnlyList<ImportedPresence> Presence { get; init; } = [];

    /// <summary>The page this record came from, recorded in provenance so a value can be traced back.</summary>
    public Uri? SourceUri { get; init; }
}
