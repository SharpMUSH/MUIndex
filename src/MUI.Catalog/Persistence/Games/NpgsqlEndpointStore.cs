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
