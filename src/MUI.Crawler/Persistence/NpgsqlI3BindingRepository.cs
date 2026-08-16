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
        answers_who AS AnswersWho, first_seen_at AS FirstSeenAt, last_seen_at AS LastSeenAt,
        last_asked_at AS LastAskedAt
        """;

    /// <remarks>
    /// <b>The upsert never touches <c>game_id</c>.</b> A binding is established once, by
    /// <see cref="BindAsync"/>, after an address has answered for itself; a routine mudlist refresh
    /// has learned nothing about identity and must not be able to unset one. The router relisting a
    /// mud at a new address is a fact about the address, not a reason to forget which game it is.
    /// </remarks>
    public async Task UpsertAsync(
        string mudName,
        string host,
        int port,
        bool answersWho,
        DateTimeOffset seenAt,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mudName);

        await using var connection = await source.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO i3_mud (mud_name, host, port, answers_who, first_seen_at, last_seen_at)
            VALUES (@mudName, @host, @port, @answersWho, @seenAt, @seenAt)
            ON CONFLICT (mud_name) DO UPDATE SET
                host = EXCLUDED.host,
                port = EXCLUDED.port,
                answers_who = EXCLUDED.answers_who,
                last_seen_at = EXCLUDED.last_seen_at
            """,
            new { mudName, host, port, answersWho, seenAt },
            cancellationToken: ct));
    }

    /// <remarks>
    /// Idempotent, and it will not move a binding that already points somewhere. Two muds resolving
    /// onto one game is refused by <c>i3_mud_one_per_game_idx</c> rather than by a check here: a
    /// read-then-write would lose the race, and what it would produce is one game counted twice.
    /// </remarks>
    public async Task BindAsync(string mudName, Guid gameId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mudName);

        await using var connection = await source.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE i3_mud SET game_id = @gameId
            WHERE mud_name = @mudName AND game_id IS NULL
            """,
            new { mudName, gameId },
            cancellationToken: ct));
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
