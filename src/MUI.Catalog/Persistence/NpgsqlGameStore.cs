using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>The <c>game</c> table (spec §5, §5.7, §7.5).</summary>
public sealed class NpgsqlGameStore(NpgsqlDataSource source) : IGameStore
{
    // Aliased rather than relying on Dapper's underscore matching, which is a process-wide static
    // this library has no business setting on its host's behalf.
    private const string Columns = """
        id AS Id, slug AS Slug, name AS Name, tagline AS Tagline, state AS State,
        is_claimed AS IsClaimed, first_seen_at AS FirstSeenAt,
        last_reachable_at AS LastReachableAt, archived_at AS ArchivedAt
        """;

    public async Task<GameRecord?> ByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM game WHERE id = @id",
            new { id },
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public async Task<GameRecord?> BySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM game WHERE slug = @slug",
            new { slug },
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public async Task InsertAsync(GameRecord game, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game (id, slug, name, tagline, state, is_claimed, first_seen_at,
                              last_reachable_at, archived_at)
            VALUES (@id, @slug, @name, @tagline, @state, @isClaimed, @firstSeenAt,
                    @lastReachableAt, @archivedAt)
            """,
            new
            {
                id = game.Id,
                slug = game.Slug,
                name = game.Name,
                tagline = game.Tagline,
                state = SqlEnums.ToDb(game.State),
                isClaimed = game.IsClaimed,
                firstSeenAt = game.FirstSeenAt.ToUniversalTime(),
                lastReachableAt = game.LastReachableAt?.ToUniversalTime(),
                archivedAt = game.ArchivedAt?.ToUniversalTime(),
            },
            cancellationToken: cancellationToken));
    }

    public async Task SetStateAsync(
        Guid id,
        LifecycleState state,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // archived_at is set on the way in and cleared on the way out, because the schema's
        // game_archived_games_have_a_date constraint holds the two in step. Nothing else about the
        // row is touched: archiving changes presentation and never the record (§7.5).
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE game
               SET state = @state,
                   archived_at = CASE WHEN @state = 'archived' THEN @at ELSE NULL END
             WHERE id = @id
            """,
            new { id, state = SqlEnums.ToDb(state), at = at.ToUniversalTime() },
            cancellationToken: cancellationToken));
    }

    public async Task SetClaimedAsync(
        Guid id,
        bool isClaimed,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE game SET is_claimed = @isClaimed WHERE id = @id",
            new { id, isClaimed },
            cancellationToken: cancellationToken));
    }

    public async Task MarkReachableAsync(
        Guid id,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // GREATEST, because probes for a multi-port game are not serialised against each other and an
        // older answer arriving late must not walk this backwards.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE game SET last_reachable_at = GREATEST(last_reachable_at, @at) WHERE id = @id",
            new { id, at = at.ToUniversalTime() },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<GameRecord>> UnarchivedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM game WHERE state <> 'archived' ORDER BY first_seen_at",
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    private sealed class Row
    {
        public Guid Id { get; init; }

        public string Slug { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string? Tagline { get; init; }

        public string State { get; init; } = string.Empty;

        public bool IsClaimed { get; init; }

        public DateTimeOffset FirstSeenAt { get; init; }

        public DateTimeOffset? LastReachableAt { get; init; }

        public DateTimeOffset? ArchivedAt { get; init; }

        public GameRecord ToRecord() => new(
            Id, Slug, Name, Tagline, SqlEnums.ToLifecycleState(State), IsClaimed,
            FirstSeenAt, LastReachableAt, ArchivedAt);
    }
}
