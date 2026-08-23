namespace MUI.Discovery;

/// <summary>
/// One row of what AresCentral says, as we last saw it.
/// </summary>
/// <remarks>
/// The hub's claims, kept apart from the catalogue's own measurements. <b>Nothing on a public surface
/// reads this table</b> — the values that reach a page go through <c>game_field</c> under
/// <c>FieldSource.AresCentral</c>, where they carry provenance and can be outranked. This is the
/// pass's own memory: what was listed last time, so a disappearance can be noticed.
/// </remarks>
public sealed record AresListing
{
    public required string Hostname { get; init; }

    public required int Port { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    public string? Genre { get; init; }

    public string? Website { get; init; }

    public string? Status { get; init; }

    /// <summary>
    /// The hub's own last reachability check, as the string it sent.
    /// </summary>
    /// <remarks>
    /// Never parsed and never used for anything. It arrives as <c>MM/DD/YYYY</c> in an unstated
    /// timezone, and it is somebody else's measurement — §7.6 forbids importing one.
    /// </remarks>
    public string? LastPing { get; init; }

    /// <summary>The game this address turned out to be, once the ordinary crawl promoted it.</summary>
    public Guid? GameId { get; init; }

    public required DateTimeOffset FirstSeenAt { get; init; }

    public required DateTimeOffset LastListedAt { get; init; }

    /// <summary>When the hub stopped listing it, or null while it is still listed.</summary>
    public DateTimeOffset? DelistedAt { get; init; }
}

/// <summary>Where the AresCentral pass remembers what it last saw.</summary>
public interface IAresListingRepository
{
    /// <summary>
    /// Records a listing as seen. Never moves <c>first_seen_at</c>, and clears any delisting.
    /// </summary>
    Task UpsertAsync(AresListing listing, CancellationToken ct);

    /// <summary>Attaches a listing to the game its address turned out to be.</summary>
    Task BindAsync(string hostname, int port, Guid gameId, CancellationToken ct);

    /// <summary>
    /// Dates every still-listed row the hub did not mention in the pass that ran at
    /// <paramref name="asOf"/>, and returns how many.
    /// </summary>
    /// <remarks>
    /// <b>Only ever called after a wholly successful fetch.</b> A refused or truncated answer must not
    /// read as everyone having left at once. An already-dated row is left alone: the first date is
    /// when the hub stopped mentioning it, and moving it forward would erase that.
    /// </remarks>
    Task<int> DelistMissingAsync(DateTimeOffset asOf, CancellationToken ct);

    Task<IReadOnlyList<AresListing>> AllAsync(CancellationToken ct);
}
