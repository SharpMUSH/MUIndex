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
/// <param name="CorroboratedAt">
/// When a probe first showed this submitted address to be a game, which is what publishes it without
/// a claim (spec §7.8, migration 0022). Null on every game the crawler found for itself, because
/// there was never anything to corroborate.
/// </param>
/// <param name="CorroboratedBy">
/// The signals that were true at that moment, and not a moment since. Written once with
/// <see cref="CorroboratedAt"/> and never revised: it answers "why was this published", which a
/// column kept current could not.
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
    DateTimeOffset? SubmittedAt = null,
    DateTimeOffset? CorroboratedAt = null,
    IReadOnlyList<string>? CorroboratedBy = null);

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
/// One member, kept as an enum on purpose: if another party's measurements are ever ingested, an
/// undifferentiated total already in the table could not be split back apart. A one-member enum
/// costs a column now to preserve that split later.
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
