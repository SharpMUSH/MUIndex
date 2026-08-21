using Dapper;

using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// <see cref="II3BindingRepository"/> over <c>i3_mud</c> (migration 0021).
/// </summary>
public sealed class NpgsqlI3BindingRepository(NpgsqlDataSource source) : II3BindingRepository
{
    private const string Columns = """
        mud_name AS MudName, game_id AS GameId, host AS Host, port AS Port,
        answers_who AS AnswersWho, is_up AS IsUp, first_seen_at AS FirstSeenAt,
        last_seen_at AS LastSeenAt, last_asked_at AS LastAskedAt
        """;

    /// <remarks>
    /// Never touches <c>game_id</c>: a binding is established once, by <see cref="BindAsync"/>, and a
    /// routine mudlist refresh has learned nothing about identity and must not be able to unset one.
    /// </remarks>
    public async Task UpsertAsync(
        string mudName,
        string host,
        int port,
        bool answersWho,
        bool isUp,
        DateTimeOffset seenAt,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mudName);

        await using var connection = await source.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO i3_mud (mud_name, host, port, answers_who, is_up, first_seen_at, last_seen_at)
            VALUES (@mudName, @host, @port, @answersWho, @isUp, @seenAt, @seenAt)
            ON CONFLICT (mud_name) DO UPDATE SET
                host = EXCLUDED.host,
                port = EXCLUDED.port,
                answers_who = EXCLUDED.answers_who,
                is_up = EXCLUDED.is_up,
                last_seen_at = EXCLUDED.last_seen_at
            """,
            new { mudName, host, port, answersWho, isUp, seenAt },
            cancellationToken: ct));
    }

    /// <remarks>
    /// Idempotent; won't move a binding that already points somewhere. Two muds resolving onto one
    /// game is refused by <c>i3_mud_one_per_game_idx</c> rather than a check here, since a
    /// read-then-write would lose the race. That refusal is caught and turned into a false return,
    /// because one game holding several I3 names is ordinary on this network, not exceptional — only
    /// this specific constraint is caught; any other integrity failure stays a surprise.
    /// </remarks>
    public async Task<bool> BindAsync(string mudName, Guid gameId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mudName);

        await using var connection = await source.OpenConnectionAsync(ct);

        try
        {
            return await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE i3_mud SET game_id = @gameId
                WHERE mud_name = @mudName AND game_id IS NULL
                """,
                new { mudName, gameId },
                cancellationToken: ct)) == 1;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation
            && e.ConstraintName == "i3_mud_one_per_game_idx")
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<I3Binding>> AllAsync(CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        return (await connection.QueryAsync<I3Binding>(new CommandDefinition(
            $"SELECT {Columns} FROM i3_mud ORDER BY mud_name",
            cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<I3Binding>> AskableAsync(
        DateTimeOffset notSince,
        int limit,
        CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        return (await connection.QueryAsync<I3Binding>(new CommandDefinition(
            $"""
            SELECT {Columns} FROM i3_mud
            WHERE game_id IS NOT NULL
              AND answers_who
              AND is_up
              AND (last_asked_at IS NULL OR last_asked_at < @notSince)
            ORDER BY last_asked_at NULLS FIRST
            LIMIT @limit
            """,
            new { notSince, limit },
            cancellationToken: ct))).ToList();
    }

    public async Task MarkAskedAsync(string mudName, DateTimeOffset at, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE i3_mud SET last_asked_at = @at WHERE mud_name = @mudName",
            new { mudName, at },
            cancellationToken: ct));
    }
}
