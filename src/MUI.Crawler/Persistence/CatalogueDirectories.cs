using Dapper;

using MUI.Catalog.Persistence;
using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// Whether a game id still names a game (spec §7.3).
/// </summary>
/// <remarks>
/// Asked before a candidate is scored: an endpoint or field row outliving its game is a repair job,
/// not a match, and returning it would attach a probe to a game that is not there.
/// </remarks>
public sealed class CatalogueGameDirectory(IGameStore games) : IGameDirectory
{
    public async Task<bool> ExistsAsync(Guid gameId, CancellationToken ct) =>
        await games.ByIdAsync(gameId, ct) is not null;
}

/// <summary>
/// Which game answers at an address (spec §7.3's strongest signal).
/// </summary>
/// <remarks>
/// <see cref="IEndpointStore"/> already answers this; discovery states its own narrow interface so
/// nothing in <c>MUI.Discovery</c> depends on a repository's full surface. This adapter is
/// deliberately thin — no reinterpretation, no second idea of what an endpoint is.
/// </remarks>
public sealed class CatalogueEndpointDirectory(IEndpointStore endpoints) : IEndpointDirectory
{
    public async Task<KnownEndpoint?> ByAddressAsync(string host, int port, CancellationToken ct)
    {
        var endpoint = await endpoints.ByAddressAsync(host, port, ct);

        return endpoint is null
            ? null
            : new KnownEndpoint(
                endpoint.GameId, endpoint.Host, endpoint.Port, endpoint.FirstSeenAt, endpoint.LastSeenAt);
    }

    public async Task<IReadOnlyList<KnownEndpoint>> ForGameAsync(Guid gameId, CancellationToken ct)
    {
        var rows = await endpoints.ForGameAsync(gameId, ct);

        return rows
            .Select(row => new KnownEndpoint(row.GameId, row.Host, row.Port, row.FirstSeenAt, row.LastSeenAt))
            .ToList();
    }

    public Task UpsertAsync(KnownEndpoint endpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return endpoints.UpsertAsync(
            new GameEndpoint(
                endpoint.GameId,
                endpoint.Host,
                endpoint.Port,
                EndpointKind.Telnet,
                endpoint.FirstSeenAt,
                endpoint.LastSeenAt,
                EndpointState.Active),
            ct);
    }
}

/// <summary>
/// The reverse field lookup identity needs: "which games carry this value for this field".
/// </summary>
/// <remarks>
/// A forward store reads by game id and cannot answer "who else calls themselves Corvid" without
/// scanning every game — this is the missing arrow. Contract: case-insensitive on both field name and
/// value, trimmed on both sides. Load-bearing, not cosmetic: field names are stored lower-case
/// (<c>name</c>, <c>created</c>, ...) but MSSP writes them upper-case, so folding only the value would
/// pass every fixture test and find nothing in production, scoring every probe as fresh. The predicate
/// mirrors the functional index <c>game_field_field_value_idx</c> (<c>0006_crawl_registry.sql</c>)
/// exactly, so the planner can use it instead of a sequential scan.
/// </remarks>
public sealed class NpgsqlGameFieldIndex(NpgsqlDataSource source) : IGameFieldIndex
{
    public async Task<IReadOnlyList<Guid>> GamesWithFieldAsync(string field, string value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        if (value.Trim().Length == 0)
        {
            // A blank is an absence, and two absences must never score as an agreement. Refused here
            // too, not just in the matcher, since this is a public interface.
            return [];
        }

        await using var connection = await source.OpenConnectionAsync(ct);

        var ids = await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT DISTINCT game_id
              FROM game_field
             WHERE lower(btrim(field)) = lower(btrim(@field))
               AND lower(btrim(value)) = lower(btrim(@value))
               -- Repeats the partial index's predicate so the planner can use it. Must stay a bound,
               -- not just an optimization: PostgreSQL's btree can't hold a row past ~2704 bytes, and
               -- an unbounded index on a connect screen refuses the INSERT outright. Every real §7.3
               -- signal is short, so this changes no answer.
               AND length(value) <= 256
            """,
            new { field, value },
            cancellationToken: ct));

        return ids.ToList();
    }
}
