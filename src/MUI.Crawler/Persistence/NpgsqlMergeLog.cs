using Dapper;

using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// <see cref="IMergeLog"/> over <c>merge_log</c> (spec §7.3, migration 0018).
/// </summary>
/// <remarks>
/// <para>
/// <b>Recording the row <em>is</em> performing the merge</b>, which is why there is no second write
/// here and no transaction wrapping one. A merge is a redirect: the absorbed game keeps every row it
/// had, the public reads skip it, and its page answers with the survivor's. All of that is derived
/// from this table on read, so a merge cannot half-happen and cannot exist unlogged.
/// </para>
/// <para>
/// <b>The uniqueness that matters is enforced by the schema, not here.</b>
/// <c>merge_log_absorbed_once_idx</c> refuses a second merge in force for one absorbed game, and this
/// lets it raise rather than checking first: a read-then-write would lose the race between two
/// operators, and the failure it would produce is a page with two answers to "where does this go".
/// </para>
/// </remarks>
public sealed class NpgsqlMergeLog(NpgsqlDataSource source) : IMergeLog
{
    private const string Columns = """
        id AS Id, into_game_id AS IntoGameId, from_game_id AS FromGameId,
        score AS Score, signals::text AS SignalsJson, at AS At, reverted_at AS RevertedAt
        """;

    public async Task<Guid> RecordAsync(MergeRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await source.OpenConnectionAsync(ct);

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO merge_log (id, into_game_id, from_game_id, score, signals, at, reverted_at)
            VALUES (@id, @into, @from, @score, @signals::jsonb, @at, @revertedAt)
            RETURNING id
            """,
            new
            {
                id = record.Id,
                into = record.IntoGameId,
                from = record.FromGameId,
                score = record.Score,
                signals = record.SignalsJson,
                at = record.At.ToUniversalTime(),
                revertedAt = record.RevertedAt?.ToUniversalTime(),
            },
            cancellationToken: ct));
    }

    /// <remarks>
    /// <c>WHERE reverted_at IS NULL</c> is what makes reverting twice keep the first answer. When the
    /// merge stopped being in force is a fact about the moment somebody undid it, and a second call —
    /// a retry, two operators, a replayed request — must not move it to whenever the second call
    /// arrived.
    /// </remarks>
    public async Task RevertAsync(Guid mergeId, DateTimeOffset at, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE merge_log SET reverted_at = @at WHERE id = @id AND reverted_at IS NULL",
            new { id = mergeId, at = at.ToUniversalTime() },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<MergeRecord>> ForGameAsync(Guid gameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // Either side, because "what absorbed me" and "what did I absorb" are the same history read
        // from the two ends, and a survivor that could not ask the second question would have to know
        // the absorbed game's id already to find out it had absorbed anything.
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"""
            SELECT {Columns} FROM merge_log
             WHERE into_game_id = @gameId OR from_game_id = @gameId
             ORDER BY at
            """,
            new { gameId },
            cancellationToken: ct));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    /// <summary>
    /// The shape Dapper can materialise. <see cref="MergeRecord"/> is positional and carries a derived
    /// member, and Npgsql hands back <c>DateTime</c> for <c>timestamptz</c> — so a direct map looks for
    /// a constructor that does not exist. The same pattern as every other repository here.
    /// </summary>
    private sealed class Row
    {
        public Guid Id { get; init; }

        public Guid IntoGameId { get; init; }

        public Guid FromGameId { get; init; }

        public double Score { get; init; }

        public string SignalsJson { get; init; } = string.Empty;

        public DateTimeOffset At { get; init; }

        public DateTimeOffset? RevertedAt { get; init; }

        public MergeRecord ToRecord() => new(Id, IntoGameId, FromGameId, Score, SignalsJson, At, RevertedAt);
    }
}
