using System.Text.Json;

using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// The <c>presence_sample</c> table (spec §5.2, §5.4). Append-only; nothing here ever updates a
/// sample.
/// </summary>
/// <remarks>
/// The table is RANGE-partitioned monthly, and this is the only class that knows it. A partition is
/// created before every append rather than by a nightly job, because the alternative is losing a
/// measurement to a calendar rollover, and the check costs one <c>CREATE TABLE IF NOT EXISTS</c>
/// against a catalogue lookup.
/// </remarks>
public sealed class NpgsqlPresenceStore(NpgsqlDataSource source) : IPresenceStore
{
    /// <summary>PostgreSQL's <c>duplicate_table</c>, which two workers racing a partition will see.</summary>
    private const string DuplicateTable = "42P07";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task AppendAsync(PresenceSample sample, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sample);

        await EnsurePartitionAsync(sample.At, cancellationToken);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // The nullable parameters carry explicit casts because Npgsql cannot infer a type from a
        // null, and `aggregates` needs one regardless: Dapper sends a string as text, and text is
        // not jsonb.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO presence_sample (game_id, at, count, source, unmeasurable_reason, aggregates)
            VALUES (@gameId, @at, @count::integer, @source, @reason::text, @aggregates::jsonb)
            ON CONFLICT (game_id, at) DO NOTHING
            """,
            new
            {
                gameId = sample.GameId,
                at = sample.At.ToUniversalTime(),
                count = sample.Count,
                source = SqlEnums.ToDb(sample.Source),
                reason = sample.Reason is { } reason ? SqlEnums.ToDb(reason) : null,
                aggregates = sample.Aggregates is null ? null : JsonSerializer.Serialize(sample.Aggregates, Json),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<PresenceSample>> ForGameAsync(
        Guid gameId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT game_id AS GameId, at AS At, count AS Count, source AS Source,
                   unmeasurable_reason AS Reason, aggregates::text AS Aggregates
              FROM presence_sample
             WHERE game_id = @gameId AND at >= @from AND at <= @to
             ORDER BY at
            """,
            new { gameId, from = from.ToUniversalTime(), to = to.ToUniversalTime() },
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    /// <summary>
    /// Makes sure the monthly partition covering <paramref name="month"/> exists.
    /// </summary>
    public async Task EnsurePartitionAsync(DateTimeOffset month, CancellationToken cancellationToken = default)
    {
        var utc = month.UtcDateTime;
        var start = new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddMonths(1);
        var name = $"presence_sample_{start:yyyyMM}";

        // Interpolated rather than parameterised because a table name and a partition bound cannot be
        // parameters in PostgreSQL. Both values are derived from a DateTimeOffset, so there is no
        // caller-controlled text anywhere in this statement.
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {name}
            PARTITION OF presence_sample
            FOR VALUES FROM ('{start:yyyy-MM-dd HH:mm:sszzz}') TO ('{end:yyyy-MM-dd HH:mm:sszzz}')
            """;

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        }
        catch (PostgresException error) when (error.SqlState == DuplicateTable)
        {
            // IF NOT EXISTS is checked, not locked, so two workers crossing a month boundary at the
            // same moment can both decide to create it. The loser's job is already done.
        }
    }

    private sealed class Row
    {
        public Guid GameId { get; init; }

        public DateTimeOffset At { get; init; }

        public int? Count { get; init; }

        public string Source { get; init; } = string.Empty;

        public string? Reason { get; init; }

        public string? Aggregates { get; init; }

        public PresenceSample ToRecord() => new()
        {
            GameId = GameId,
            At = At,
            Count = Count,
            Source = SqlEnums.ToFieldSource(Source),
            Reason = Reason is null ? null : SqlEnums.ToUnmeasurableReason(Reason),
            Aggregates = Aggregates is null
                ? null
                : JsonSerializer.Deserialize<PresenceAggregates>(Aggregates, Json),
        };
    }
}
