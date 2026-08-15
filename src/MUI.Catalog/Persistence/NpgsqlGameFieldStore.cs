using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// The <c>game_field</c> and <c>field_change</c> tables (spec §5.1).
/// </summary>
/// <remarks>
/// Keyed <c>(game_id, field, source)</c>, so measured and declared never contend for one row and both
/// survive with their own ages. Nothing here derives a winner: <see cref="FieldPrecedence"/> does
/// that on read, so a stored winner cannot go stale against the rows it summarises.
/// </remarks>
public sealed class NpgsqlGameFieldStore(NpgsqlDataSource source) : IGameFieldStore
{
    public async Task<IReadOnlyList<GameField>> ForGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT game_id AS GameId, field AS Field, source AS Source, value AS Value,
                   first_seen_at AS FirstSeenAt, last_confirmed_at AS LastConfirmedAt
              FROM game_field
             WHERE game_id = @gameId
             ORDER BY field, source
            """,
            new { gameId },
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    public async Task<IReadOnlyList<GameField>> ForGameAsync(
        Guid gameId,
        FieldSource only,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT game_id AS GameId, field AS Field, source AS Source, value AS Value,
                   first_seen_at AS FirstSeenAt, last_confirmed_at AS LastConfirmedAt
              FROM game_field
             WHERE game_id = @gameId AND source = @source
             ORDER BY field
            """,
            new { gameId, source = SqlEnums.ToDb(only) },
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    public async Task UpsertAsync(GameField field, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // first_seen_at is deliberately NOT overwritten on conflict: it means "when this
        // (game, field, source) was first seen", and a confirmation must not reset the age of the
        // row it confirms.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game_field (game_id, field, source, value, first_seen_at, last_confirmed_at)
            VALUES (@gameId, @field, @source, @value, @firstSeenAt, @lastConfirmedAt)
            ON CONFLICT (game_id, field, source) DO UPDATE
               SET value = EXCLUDED.value,
                   last_confirmed_at = GREATEST(game_field.last_confirmed_at, EXCLUDED.last_confirmed_at)
            """,
            new
            {
                gameId = field.GameId,
                field = field.Field,
                source = SqlEnums.ToDb(field.Source),
                value = field.Value,
                firstSeenAt = field.FirstSeenAt.ToUniversalTime(),
                lastConfirmedAt = field.LastConfirmedAt.ToUniversalTime(),
            },
            cancellationToken: cancellationToken));
    }

    public async Task RecordChangeAsync(FieldChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO field_change (game_id, field, source, old_value, new_value, at)
            VALUES (@gameId, @field, @source, @oldValue, @newValue, @at)
            """,
            new
            {
                gameId = change.GameId,
                field = change.Field,
                source = SqlEnums.ToDb(change.Source),
                oldValue = change.OldValue,
                newValue = change.NewValue,
                at = change.At.ToUniversalTime(),
            },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// The per-game change feed (spec §9), newest first — public events only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not on <see cref="IGameFieldStore"/> because nothing on the write path reads it; it is the
    /// game page's, and the page reads it through <see cref="IGameQueries"/>.
    /// </para>
    /// <para>
    /// <b><see cref="InternalFields"/> are excluded here, in the query.</b> They are machinery — a
    /// digest we computed, the screen we render in its own frame, the owner's decision about whether
    /// we render it at all — and the declared panel has always filtered them. The feed did not, so
    /// an owner reversing §11's suppression published "connect_screen_suppressed changed from true
    /// to false" on their game's public page, in plain text and in the API: the first internal field
    /// ever to reach those surfaces, announcing a choice §11 grants with no questions asked.
    /// </para>
    /// <para>
    /// Filtered in SQL rather than afterwards because the query is limited, and a filter applied
    /// after the limit would spend it on rows nobody may see. Nothing is deleted: the rows stay in
    /// <c>field_change</c> and are simply not events about the game.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<FieldChange>> ChangesAsync(
        Guid gameId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<ChangeRow>(new CommandDefinition(
            """
            SELECT game_id AS GameId, field AS Field, source AS Source,
                   old_value AS OldValue, new_value AS NewValue, at AS At
              FROM field_change
             WHERE game_id = @gameId
               AND NOT (field = ANY(@internal) OR field LIKE @internalPrefix)
             ORDER BY at DESC, id DESC
             LIMIT @limit
            """,
            new
            {
                gameId,
                limit,
                @internal = InternalFields.ExactNames.ToArray(),
                internalPrefix = InternalFields.ConnectScreen + "%",
            },
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    private sealed class Row
    {
        public Guid GameId { get; init; }

        public string Field { get; init; } = string.Empty;

        public string Source { get; init; } = string.Empty;

        public string Value { get; init; } = string.Empty;

        public DateTimeOffset FirstSeenAt { get; init; }

        public DateTimeOffset LastConfirmedAt { get; init; }

        public GameField ToRecord() => new(
            GameId, Field, SqlEnums.ToFieldSource(Source), Value, FirstSeenAt, LastConfirmedAt);
    }

    private sealed class ChangeRow
    {
        public Guid GameId { get; init; }

        public string Field { get; init; } = string.Empty;

        public string Source { get; init; } = string.Empty;

        public string? OldValue { get; init; }

        public string NewValue { get; init; } = string.Empty;

        public DateTimeOffset At { get; init; }

        public FieldChange ToRecord() => new(
            GameId, Field, SqlEnums.ToFieldSource(Source), OldValue, NewValue, At);
    }
}
