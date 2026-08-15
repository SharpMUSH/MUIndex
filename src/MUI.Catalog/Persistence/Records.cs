namespace MUI.Catalog.Persistence;

/// <summary>
/// A game as the <c>game</c> table holds it (spec §5, §5.7).
/// </summary>
/// <remarks>
/// Named <c>GameRecord</c> rather than <c>Game</c> because <see cref="GameSummary"/> and
/// <see cref="GamePage"/> are what a page consumes; this is the stored row and it is nobody's view
/// model. <see cref="LastReachableAt"/> is null when we have never once reached the game, which is a
/// different fact from "reachable a long time ago" and is what §7.5's grace is measured from.
/// </remarks>
/// <param name="SubmittedAt">
/// When somebody handed us this game's address through the public form, or null when the crawler
/// found it for itself (migration 0010).
/// </param>
public sealed record GameRecord(
    Guid Id,
    string Slug,
    string Name,
    string? Tagline,
    LifecycleState State,
    bool IsClaimed,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastReachableAt = null,
    DateTimeOffset? ArchivedAt = null,
    DateTimeOffset? SubmittedAt = null);

/// <summary>What kind of socket an endpoint is (spec §5.5).</summary>
public enum EndpointKind
{
    Telnet,
    Tls,
    WebSocket,
    Http,
}

/// <summary>
/// Whether an address still answers. Never a deletion: an endpoint a game has moved off is
/// <see cref="Gone"/> and still probed at the §7.4 floor, so a game that moves back is found again.
/// </summary>
public enum EndpointState
{
    Active,
    Stale,
    Gone,
}

/// <summary>An address a game answers on (spec §5.5).</summary>
public sealed record GameEndpoint(
    Guid GameId,
    string Host,
    int Port,
    EndpointKind Kind,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    EndpointState State);

/// <summary>
/// Who measured an availability interval.
/// </summary>
/// <remarks>
/// One member, and it is kept as an enum on purpose. It had a second — <c>ImportedMeasured</c>, for a
/// span a third-party directory had probed, credited toward archive grace at half weight because we
/// could not audit their prober. The backfill no longer imports history (spec §7.6), so every
/// interval in the table is ours and the weighting has nothing to weigh.
///
/// The column stays because the day some other party's measurements are ingested, an undifferentiated
/// total would already be in the table and unsplittable. A one-member enum costs a column; losing the
/// distinction costs the history.
/// </remarks>
public enum IntervalOrigin
{
    FirstParty,
}

/// <summary>
/// One canonical spelling of a host, so an address is one row rather than several (spec §7.3).
/// </summary>
/// <remarks>
/// The <c>game_endpoint_host_is_canonical</c> CHECK is the teeth; this is the tool that satisfies it.
/// Both exist because the unique index on <c>(host, port)</c> is only an identity guarantee if one
/// machine has one spelling — <c>MUD.Example.ORG.</c> and <c>mud.example.org</c> are different
/// strings and would otherwise be two rows, which is exactly the duplicate listing §7.3 exists to
/// prevent.
/// </remarks>
public static class HostName
{
    public static string Normalize(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var trimmed = host.Trim().TrimEnd('.');

        return trimmed.ToLowerInvariant();
    }
}
