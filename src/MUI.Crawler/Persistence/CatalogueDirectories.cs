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
/// The catalogue's own <see cref="IEndpointStore"/> already answers this; discovery states it as its
/// own narrow interface so that nothing in <c>MUI.Discovery</c> depends on a repository's full
/// surface, and an in-memory implementation is what every test there runs against. This adapter is
/// the join, and it is deliberately thin: no reinterpretation, no second idea of what an endpoint is.
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
/// <para>
/// A forward store reads by game id, which cannot answer "who else calls themselves Corvid" without
/// scanning every game. This is the missing arrow, and its contract is specific:
/// <b>case-insensitive on both field name and value, trimmed on both sides, distinct game ids</b>. An
/// implementation that folded only the value would pass every test written against a fixture using
/// its own spelling and find nothing in production — because the identity signals are spelled
/// <c>name</c>, <c>created</c>, <c>website</c>, <c>contact</c>, <c>codebase</c> and MSSP writes
/// <c>NAME</c>, <c>CREATED</c>, <c>WEBSITE</c>, <c>CONTACT</c>, <c>CODEBASE</c>. The matcher would
/// then score every probe as fresh and mint a duplicate listing per crawl.
/// </para>
/// <para>
/// <c>0006_crawl_registry.sql</c> adds the functional index this predicate can actually use.
/// <c>game_field_field_value_idx</c> indexes the raw columns and a folded comparison cannot use it,
/// so without it this is a sequential scan of <c>game_field</c> five times per probe.
/// </para>
/// </remarks>
public sealed class NpgsqlGameFieldIndex(NpgsqlDataSource source) : IGameFieldIndex
{
    public async Task<IReadOnlyList<Guid>> GamesWithFieldAsync(string field, string value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        if (value.Trim().Length == 0)
        {
            // A blank is an absence, and two absences must never score as an agreement. Refusing here
            // as well as in the matcher, because this is a public interface and the next caller may
            // not know that.
            return [];
        }

        await using var connection = await source.OpenConnectionAsync(ct);

        var ids = await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT DISTINCT game_id
              FROM game_field
             WHERE lower(btrim(field)) = lower(btrim(@field))
               AND lower(btrim(value)) = lower(btrim(@value))
               -- The bound is here as well as on the index, and both are deliberate. PostgreSQL's
               -- btree cannot hold a row past ~2704 bytes, and a connect screen is thousands of
               -- characters, so an unbounded index refused the INSERT and cost the game its whole
               -- ingestion. The index is now partial; repeating its predicate here is what lets the
               -- planner use it rather than sequentially scanning game_field once per identity
               -- signal per probe. It changes no answer: every §7.3 signal — a name, a year, a
               -- hostname, a hash, a token — is short, and a value longer than this is not one.
               AND length(value) <= 256
            """,
            new { field, value },
            cancellationToken: ct));

        return ids.ToList();
    }
}
