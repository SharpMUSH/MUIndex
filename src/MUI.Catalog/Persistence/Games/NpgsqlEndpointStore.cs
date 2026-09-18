using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>The <c>game_endpoint</c> table (spec §5.5, §7.3).</summary>
public sealed class NpgsqlEndpointStore(NpgsqlDataSource source) : IEndpointStore
{
    private const string Columns = """
        game_id AS GameId, host AS Host, port AS Port, kind AS Kind,
        first_seen_at AS FirstSeenAt, last_seen_at AS LastSeenAt, state AS State
        """;

    public async Task<IReadOnlyList<GameEndpoint>> ForGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM game_endpoint WHERE game_id = @gameId ORDER BY host, port",
            new { gameId },
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    /// <summary>
    /// Every address a reader should be shown for this listing: its own, and those of any game merged
    /// into it (issue #188).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A merge says two listings are one game, so every address either answered at is an address of
    /// that game. The absorbed game keeps its endpoint rows — nothing moves, and the merge stays a
    /// redirect — but the page its slug 301s <em>to</em> used to read only the winner's own, so those
    /// addresses went on being crawled and were shown nowhere a reader could reach. Found about to
    /// merge ChatMUD's two listings, where the loser's endpoint was the TLS door the crawl had just
    /// discovered.
    /// </para>
    /// <para>
    /// <b>Deliberately not <see cref="ForGameAsync"/>, and deliberately not on the interface.</b>
    /// That method also answers <c>IdentityMatcher</c> and <c>DnsClaim</c>, where "this game's
    /// addresses" means the ones it answered at itself. Widening it would let a DNS claim verified
    /// against the winner vouch for an absorbed game's hosts — a change to who can prove ownership of
    /// what, made as a side effect of a display fix. Living only on the concrete store keeps it out
    /// of reach of everything that consumes <see cref="IEndpointStore"/>.
    /// </para>
    /// <para>
    /// One hop is the whole walk: <c>merge_log_no_chains</c> refuses a game that is both absorbed and
    /// absorbing, and a reverted merge is two games again, so its addresses stay on their own page.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<GameEndpoint>> ForListingAsync(
        Guid gameId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"""
            SELECT {Columns}
              FROM game_endpoint
             WHERE game_id = @gameId
                OR game_id IN (SELECT from_game_id
                                 FROM merge_log
                                WHERE into_game_id = @gameId AND reverted_at IS NULL)
             ORDER BY host, port
            """,
            new { gameId },
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    public async Task<GameEndpoint?> ByAddressAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM game_endpoint WHERE host = @host AND port = @port",
            new { host = HostName.Normalize(host), port },
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public async Task UpsertAsync(GameEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // last_seen_at only ever moves forward, and first_seen_at never moves: an endpoint's age is
        // how long we have known the address, not how long the current answer has been true.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game_endpoint (game_id, host, port, kind, first_seen_at, last_seen_at, state)
            VALUES (@gameId, @host, @port, @kind, @firstSeenAt, @lastSeenAt, @state)
            ON CONFLICT (game_id, host, port) DO UPDATE
               SET kind = EXCLUDED.kind,
                   last_seen_at = GREATEST(game_endpoint.last_seen_at, EXCLUDED.last_seen_at),
                   state = EXCLUDED.state
            """,
            new
            {
                gameId = endpoint.GameId,
                host = HostName.Normalize(endpoint.Host),
                port = endpoint.Port,
                kind = SqlEnums.ToDb(endpoint.Kind),
                firstSeenAt = endpoint.FirstSeenAt.ToUniversalTime(),
                lastSeenAt = endpoint.LastSeenAt.ToUniversalTime(),
                state = SqlEnums.ToDb(endpoint.State),
            },
            cancellationToken: cancellationToken));
    }

    private sealed class Row
    {
        public Guid GameId { get; init; }

        public string Host { get; init; } = string.Empty;

        public int Port { get; init; }

        public string Kind { get; init; } = string.Empty;

        public DateTimeOffset FirstSeenAt { get; init; }

        public DateTimeOffset LastSeenAt { get; init; }

        public string State { get; init; } = string.Empty;

        public GameEndpoint ToRecord() => new(
            GameId, Host, Port, SqlEnums.ToEndpointKind(Kind), FirstSeenAt, LastSeenAt,
            SqlEnums.ToEndpointState(State));
    }
}
