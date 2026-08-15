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

    public async Task<DateTimeOffset?> LastChangedAtAsync(
        Guid gameId, string field, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // lower(field), because MSSP spells NAME upper-case and IdentityFields spells it lower — the
        // same fold IGameFieldIndex's contract states, for the same reason: a comparison that missed
        // would report a name that has moved as one that never has, and re-mint a URL on the spot.
        //
        // Read as DateTime and not DateTimeOffset: Npgsql hands a bare timestamptz back as a UTC
        // DateTime, and Dapper's scalar path converts rather than mapping — asking for the offset
        // type here throws InvalidCastException at runtime, which the crawl loop would swallow as one
        // errored target. Measured, not assumed.
        var at = await connection.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
            """
            SELECT max(at) FROM field_change
             WHERE game_id = @gameId AND lower(field) = lower(@field)
            """,
            new { gameId, field },
            cancellationToken: cancellationToken));

        return at is { } moved
            ? new DateTimeOffset(DateTime.SpecifyKind(moved, DateTimeKind.Utc))
            : null;
    }

    /// <summary>
    /// The per-game change feed (spec §9), newest first. Not on <see cref="IGameFieldStore"/> because
    /// nothing on the write path reads it; it is the game page's, and the page reads it through
    /// <see cref="IGameQueries"/>.
    /// </summary>
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
             ORDER BY at DESC, id DESC
             LIMIT @limit
            """,
            new { gameId, limit },
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
