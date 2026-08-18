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
/// created before every append as well as ahead of need by the maintenance pass — the alternative to
/// the per-append check is losing a measurement to a calendar rollover. The whole partition lifecycle
/// (make ahead, list, drop once rolled up) lives here so two places can't name a month differently.
/// </remarks>
public sealed class NpgsqlPresenceStore(NpgsqlDataSource source) : IPresenceStore
{
    /// <summary>PostgreSQL's <c>duplicate_table</c>, which two workers racing a partition will see.</summary>
    private const string DuplicateTable = "42P07";

    /// <summary>PostgreSQL's <c>lock_not_available</c>, which a bounded wait for the parent gives.</summary>
    private const string LockNotAvailable = "55P03";

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
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await EnsurePartitionAsync(connection, month, cancellationToken);
    }

    /// <summary>
    /// Makes every monthly partition from <paramref name="from"/> up to and including the month
    /// <paramref name="through"/> falls in, and returns the ones that did not exist yet.
    /// </summary>
    /// <remarks>
    /// Called ahead of need so the per-append DDL check is never the first thing to discover a
    /// database it cannot write DDL to, at midnight on the 31st, with a measurement in hand.
    /// </remarks>
    public async Task<IReadOnlyList<string>> EnsurePartitionsThroughAsync(
        DateTimeOffset from,
        DateTimeOffset through,
        CancellationToken cancellationToken = default)
    {
        var existing = (await PartitionsAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var created = new List<string>();

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        for (var month = PresencePartitions.MonthOf(from);
             month <= PresencePartitions.MonthOf(through);
             month = month.AddMonths(1))
        {
            var name = PresencePartitions.NameFor(month);

            if (existing.Contains(name))
            {
                continue;
            }

            await EnsurePartitionAsync(connection, month, cancellationToken);
            created.Add(name);
        }

        return created;
    }

    /// <summary>Every partition currently attached to <c>presence_sample</c>, oldest first.</summary>
    public async Task<IReadOnlyList<string>> PartitionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var names = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT c.relname FROM pg_inherits
              JOIN pg_class c ON c.oid = pg_inherits.inhrelid
              JOIN pg_class p ON p.oid = pg_inherits.inhparent
             WHERE p.relname = 'presence_sample'
             ORDER BY c.relname
            """,
            cancellationToken: cancellationToken));

        return names.ToList();
    }

    /// <summary>
    /// Drops every partition whose whole range ends at or before <paramref name="boundary"/>, and
    /// returns what it dropped (spec §5.2, §15.4).
    /// </summary>
    /// <remarks>
    /// Whole partitions, never rows: a <c>DELETE</c> over hundreds of millions of rows is a day of
    /// vacuum, a <c>DROP TABLE</c> of a month is a catalogue update. A month is kept until every hour
    /// in it has aged out — up to a month longer than asked for, the safe direction to be wrong in.
    /// <see cref="PresenceMaintenance"/> never sets the boundary later than what rollups have
    /// consumed; an unaggregated partition is the only copy there is.
    /// </remarks>
    public async Task<IReadOnlyList<string>> DropPartitionsEndingAtOrBeforeAsync(
        DateTimeOffset boundary,
        CancellationToken cancellationToken = default)
    {
        var dropped = new List<string>();

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // Detaching a partition takes ACCESS EXCLUSIVE on the parent, which a crawl inserting into it
        // would otherwise queue behind. Bounded, so a maintenance pass can never be the reason a probe
        // could not write down what it measured; whatever is skipped is dropped on the next pass.
        await connection.ExecuteAsync(new CommandDefinition(
            "SET lock_timeout = '5s'", cancellationToken: cancellationToken));

        foreach (var name in await PartitionsAsync(cancellationToken))
        {
            // Not ours, so not ours to drop.
            if (PresencePartitions.MonthFromName(name) is not { } month
                || month.AddMonths(1) > boundary)
            {
                continue;
            }

            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    $"DROP TABLE IF EXISTS {name}", cancellationToken: cancellationToken));

                dropped.Add(name);
            }
            catch (PostgresException error) when (error.SqlState == LockNotAvailable)
            {
                // Something is writing to the month. It will still be droppable next pass.
            }
        }

        return dropped;
    }

    private static async Task EnsurePartitionAsync(
        NpgsqlConnection connection,
        DateTimeOffset month,
        CancellationToken cancellationToken)
    {
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                PresencePartitions.CreateDdl(month), cancellationToken: cancellationToken));
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
            Aggregates = ReadAggregates(Aggregates),
        };

        /// <summary>
        /// Reads the <c>aggregates</c> column, dropping an estimate that does not name its epoch.
        /// </summary>
        /// <remarks>
        /// One unreadable row may not cost the window it is in. <see cref="PresenceAggregates"/>
        /// refuses construction with an estimate and no salt epoch (§11); deserialising straight into
        /// it made that refusal throw mid-read, so one bad row took the whole window with it. Reading
        /// through this invariant-free shape first confines the refusal to the row it's about. Only
        /// the estimate is dropped, not the row — <see cref="NpgsqlPresenceRollupStore"/> filters the
        /// same case in SQL, so both paths agree on what's readable.
        /// </remarks>
        private static PresenceAggregates? ReadAggregates(string? json)
        {
            if (json is null)
            {
                return null;
            }

            var stored = JsonSerializer.Deserialize<StoredAggregates>(json, Json);

            if (stored is null)
            {
                return null;
            }

            return new PresenceAggregates(stored.IdleBuckets ?? []);
        }
    }

    /// <summary>
    /// The <c>aggregates</c> column as it is on disk, with no invariant of its own.
    /// </summary>
    /// <remarks>
    /// A row written before the unique-player estimate was removed may still carry
    /// <c>distinctEstimate</c>/<c>saltEpoch</c> keys; they aren't members here, so an old row can't
    /// bring that estimate back to life.
    /// </remarks>
    private sealed record StoredAggregates(IReadOnlyList<int>? IdleBuckets);
}
