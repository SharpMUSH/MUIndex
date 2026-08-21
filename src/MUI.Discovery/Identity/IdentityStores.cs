namespace MUI.Discovery;

/// <summary>
/// The reverse field lookup identity needs: "which games carry this value for this field".
/// </summary>
/// <remarks>
/// A forward store reads by game id, which can't answer "who else calls themselves Corvid" without
/// scanning every game — this is the missing arrow. Implementations must compare case-insensitively
/// on field name and value, trimmed, distinct game ids, or the matcher passes tests and misses
/// candidates in production.
/// </remarks>
public interface IGameFieldIndex
{
    Task<IReadOnlyList<Guid>> GamesWithFieldAsync(string field, string value, CancellationToken ct);
}

/// <summary>An address a game is known to answer at.</summary>
/// <remarks>
/// The catalogue's own endpoint view carries no game id, because a page already knows whose page it is.
/// Identity works the other way round — from an address to a game — so it needs the arrow the view
/// omits.
/// </remarks>
public sealed record KnownEndpoint(
    Guid GameId,
    string Host,
    int Port,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

/// <summary>
/// Which game answers at an address. One of three narrow seams this project needs from catalogue
/// storage.
/// </summary>
/// <remarks>
/// Deliberately narrow: these interfaces state exactly what discovery reads and writes, so the storage
/// layer can implement them without discovery ever depending on a repository's full surface. An
/// in-memory implementation is what every test here runs against, with no network and no database.
/// </remarks>
public interface IEndpointDirectory
{
    Task<KnownEndpoint?> ByAddressAsync(string host, int port, CancellationToken ct);

    /// <summary>
    /// Every address on record for one game. <see cref="IdentityWeights.ResolvedEndpoint"/> is the
    /// reason this exists: asking "what does this candidate's own hostname resolve to" needs the
    /// hostname first, and <see cref="ByAddressAsync"/> only ever answers in the other direction.
    /// </summary>
    Task<IReadOnlyList<KnownEndpoint>> ForGameAsync(Guid gameId, CancellationToken ct);

    Task UpsertAsync(KnownEndpoint endpoint, CancellationToken ct);
}

/// <summary>Whether a game id still names a game.</summary>
/// <remarks>
/// Asked before a candidate is scored: an endpoint or field row outliving its game is a repair job, not
/// a match, and returning it would attach a probe to a game that is not there.
/// </remarks>
public interface IGameDirectory
{
    Task<bool> ExistsAsync(Guid gameId, CancellationToken ct);
}
