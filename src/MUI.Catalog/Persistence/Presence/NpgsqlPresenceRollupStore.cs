using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// The <c>presence_rollup_hour</c> and <c>presence_rollup_day</c> tables (spec §5.2).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not behind an interface: every method is an aggregation the database does in place,
/// and only a real PostgreSQL (what the tests use) proves it correct.
/// </para>
/// <para>
/// Both grains aggregate from the raw table; the day is never aggregated from the hour. The two
/// grains have different retentions (day forever, hour two years), so a day built from hours would
/// silently change answer once the hours it depended on were dropped.
/// </para>
/// </remarks>
public sealed class NpgsqlPresenceRollupStore(NpgsqlDataSource source) : IPresenceSeries
{
    /// <summary>
    /// Aggregates every raw sample in <c>[from, toExclusive)</c> into the given grain, and returns how
    /// many buckets it wrote.
    /// </summary>
    /// <remarks>
    /// An upsert: re-running it over an already-rolled window reproduces the same rows, which is what
    /// lets a late-arriving sample be folded in by simply reading its hour again.
    /// </remarks>
    public async Task<int> RollUpAsync(
        PresenceGrain grain,
        DateTimeOffset from,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken = default)
    {
        if (from >= toExclusive)
        {
            return 0;
        }

        // The hourly grain is partitioned (migration 0037) and has no DEFAULT partition, so a bucket
        // whose month has no partition is not a slow write but a failed one. Ensured here as well as
        // ahead of time in PresenceMaintenance, for the same reason NpgsqlPresenceStore.AppendAsync
        // does it per append rather than trusting the maintenance pass: a rollup of a span nobody
        // anticipated — a backfill, a test, a pass that has not run since before the month turned —
        // must write its months rather than fail on them.
        //
        // `toExclusive` is exclusive, so a span ending exactly at a month boundary must not create the
        // month it stops short of.
        if (grain is PresenceGrain.Hour)
        {
            await EnsurePartitionsThroughAsync(from, toExclusive.AddTicks(-1), cancellationToken);
        }

        var (table, bucket, unit) = Shape(grain);

        // Truncation is UTC on both sides so a session's TimeZone setting can never shift a
        // measurement into the neighbouring hour.
        //
        // The three-state rule lives in the FILTERs: min/max/sum run over counted samples only and
        // come back NULL when there were none, and a group only exists for a bucket some probe
        // actually wrote a row in — an unmeasured hour stays absent rather than becoming a zero.
        //
        // The distribution needs a second grouping level (one row per count, then per bucket), so
        // it's built in a CTE and joined back with a LEFT JOIN — a probed-but-uncountable bucket has
        // no distribution but must still produce its row (§5.4's hatched cell).
        var sql = $"""
            WITH tally AS (
                SELECT s.game_id,
                       date_trunc('{unit}', s.at AT TIME ZONE 'UTC') AT TIME ZONE 'UTC' AS bucket,
                       count(*) FILTER (WHERE s.count IS NOT NULL) AS counted,
                       count(*) FILTER (WHERE s.count IS NULL) AS unmeasurable,
                       min(s.count) AS lowest,
                       max(s.count) AS highest,
                       sum(s.count) AS total
                  FROM presence_sample s
                 WHERE s.at >= @from AND s.at < @to
                 GROUP BY 1, 2),
            frequency AS (
                SELECT s.game_id,
                       date_trunc('{unit}', s.at AT TIME ZONE 'UTC') AT TIME ZONE 'UTC' AS bucket,
                       s.count AS value,
                       count(*) AS times
                  FROM presence_sample s
                 WHERE s.at >= @from AND s.at < @to AND s.count IS NOT NULL
                 GROUP BY 1, 2, 3),
            histogram AS (
                SELECT game_id, bucket, jsonb_object_agg(value::text, times) AS counts
                  FROM frequency
                 GROUP BY game_id, bucket)
            INSERT INTO {table} (
                game_id, {bucket}, counted_samples, unmeasurable_samples,
                min_count, max_count, sum_count, count_histogram)
            SELECT t.game_id, t.bucket, t.counted, t.unmeasurable,
                   t.lowest, t.highest, t.total, h.counts
              FROM tally t
              LEFT JOIN histogram h ON h.game_id = t.game_id AND h.bucket = t.bucket
            ON CONFLICT (game_id, {bucket}) DO UPDATE SET
                counted_samples      = EXCLUDED.counted_samples,
                unmeasurable_samples = EXCLUDED.unmeasurable_samples,
                min_count            = EXCLUDED.min_count,
                max_count            = EXCLUDED.max_count,
                sum_count            = EXCLUDED.sum_count,
                count_histogram      = EXCLUDED.count_histogram
            """;

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { from = from.ToUniversalTime(), to = toExclusive.ToUniversalTime() },
            cancellationToken: cancellationToken));
    }

    /// <summary>One game's rolled-up buckets, inclusive of both ends, oldest first.</summary>
    public async Task<IReadOnlyList<PresenceRollup>> ForGameAsync(
        Guid gameId,
        PresenceGrain grain,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var (table, bucket, _) = Shape(grain);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"""
            SELECT game_id AS GameId, {bucket} AS Bucket, counted_samples AS CountedSamples,
                   unmeasurable_samples AS UnmeasurableSamples, min_count AS MinCount,
                   max_count AS MaxCount, sum_count AS SumCount, mean_count AS MeanCount
              FROM {table}
             WHERE game_id = @gameId AND {bucket} >= @from AND {bucket} <= @to
             ORDER BY {bucket}
            """,
            new { gameId, from = from.ToUniversalTime(), to = to.ToUniversalTime() },
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord(grain)).ToList();
    }

    /// <summary>How far this grain has consumed the raw table, or null if it never has.</summary>
    public async Task<DateTimeOffset?> WatermarkAsync(
        PresenceGrain grain,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var through = await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            "SELECT rolled_up_through FROM presence_rollup_state WHERE scope = @scope",
            new { scope = Scope(grain) },
            cancellationToken: cancellationToken));

        return Utc(through);
    }

    /// <summary>
    /// Records that everything before <paramref name="through"/> has been aggregated at this grain.
    /// </summary>
    /// <remarks>
    /// Only ever moves forward: a watermark that could regress would let retention's "already rolled
    /// up" check answer a stale yes, and that check guards a <c>DROP TABLE</c>.
    /// </remarks>
    public async Task SetWatermarkAsync(
        PresenceGrain grain,
        DateTimeOffset through,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO presence_rollup_state (scope, rolled_up_through)
            VALUES (@scope, @through)
            ON CONFLICT (scope) DO UPDATE
                SET rolled_up_through = GREATEST(presence_rollup_state.rolled_up_through, EXCLUDED.rolled_up_through),
                    updated_at = now()
            """,
            new { scope = Scope(grain), through = through.ToUniversalTime() },
            cancellationToken: cancellationToken));
    }

    /// <summary>Whether the tables a pass reads and writes exist yet.</summary>
    /// <remarks>
    /// On a fresh database, migrations may still be applying when the maintenance service starts. Not
    /// checking means every replica's first pass throws <c>42P01</c> and stands down for a full retry
    /// interval, turning a one-second race into minutes of no maintenance.
    /// </remarks>
    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT to_regclass('presence_sample') IS NOT NULL
               AND to_regclass('presence_rollup_hour') IS NOT NULL
               AND to_regclass('presence_rollup_day') IS NOT NULL
               AND to_regclass('presence_rollup_state') IS NOT NULL
            """,
            cancellationToken: cancellationToken));
    }

    /// <summary>The oldest raw sample there is, which is where a first rollup starts from.</summary>
    public async Task<DateTimeOffset?> EarliestSampleAtAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var earliest = await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            "SELECT min(at) FROM presence_sample", cancellationToken: cancellationToken));

        return Utc(earliest);
    }

    /// <summary>
    /// Deletes rolled-up buckets that start before <paramref name="cutoff"/>, and returns how many.
    /// </summary>
    /// <remarks>
    /// Row-by-row, not by partition: these tables aren't partitioned, since §5.2 keeps the daily grain
    /// forever, so the table that would most want partitioning is the one nothing ever deletes from.
    /// </remarks>
    private const string LockNotAvailable = "55P03";

    private const string DuplicateTable = "42P07";

    /// <summary>
    /// Makes every monthly partition of the hourly rollup up to and including
    /// <paramref name="through"/>, and returns the ones that did not exist yet.
    /// </summary>
    /// <remarks>
    /// The hourly rollup has been partitioned since migration 0037 and has no DEFAULT partition, so a
    /// month without one is not a slow write but a failed one. Created ahead of need for the same
    /// reason raw partitions are: the rollup pass writing on the first of the month must not be the
    /// thing that discovers a database it cannot issue DDL against.
    /// </remarks>
    public async Task<IReadOnlyList<string>> EnsurePartitionsThroughAsync(
        DateTimeOffset from,
        DateTimeOffset through,
        CancellationToken cancellationToken = default)
    {
        var scheme = PresencePartitions.HourlyRollups;
        var existing = (await PartitionsAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var created = new List<string>();

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        for (var month = PresencePartitions.MonthOf(from);
             month <= PresencePartitions.MonthOf(through);
             month = month.AddMonths(1))
        {
            if (existing.Contains(scheme.NameFor(month)))
            {
                continue;
            }

            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    scheme.CreateDdl(month), cancellationToken: cancellationToken));

                created.Add(scheme.NameFor(month));
            }
            catch (PostgresException error) when (error.SqlState == DuplicateTable)
            {
                // IF NOT EXISTS is checked, not locked, so two workers crossing a month boundary at
                // the same moment can both decide to create it. The loser's job is already done.
            }
        }

        return created;
    }

    /// <summary>Every partition currently attached to the hourly rollup, oldest first.</summary>
    public async Task<IReadOnlyList<string>> PartitionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var names = await connection.QueryAsync<string>(new CommandDefinition(
            PresencePartitions.PartitionsSql,
            new { table = PresencePartitions.HourlyRollups.Table },
            cancellationToken: cancellationToken));

        return [.. names];
    }

    /// <summary>
    /// Drops every whole monthly partition of the hourly rollup that ends at or before
    /// <paramref name="boundary"/>, and returns their names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What <see cref="DeleteBeforeAsync"/> would otherwise do one row at a time. At the measured rate
    /// (~6,300 rows a day) a year of hourly rollup is over two million rows, and deleting those leaves
    /// as many dead tuples for autovacuum to walk on a two-core box. Dropping the month is O(1) and
    /// returns the disk immediately.
    /// </para>
    /// <para>
    /// A partition is dropped only when its whole month is past the boundary — a month the boundary
    /// falls inside still holds hours that must be kept, and those are left to
    /// <see cref="DeleteBeforeAsync"/>. Same reading-the-name-not-the-bounds rule as raw presence: a
    /// partition an operator attached by hand is never ours to drop.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<string>> DropHourPartitionsEndingAtOrBeforeAsync(
        DateTimeOffset boundary,
        CancellationToken cancellationToken = default)
    {
        var scheme = PresencePartitions.HourlyRollups;
        var dropped = new List<string>();

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // Dropping a partition takes ACCESS EXCLUSIVE on the parent, which the rollup pass writing
        // into it would otherwise queue behind. Bounded, so maintenance can never be the reason a
        // rollup could not be written; whatever is skipped is dropped on the next pass.
        await connection.ExecuteAsync(new CommandDefinition(
            "SET lock_timeout = '5s'", cancellationToken: cancellationToken));

        foreach (var name in await PartitionsAsync(cancellationToken))
        {
            if (scheme.MonthFromName(name) is not { } month || month.AddMonths(1) > boundary)
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

    public async Task<int> DeleteBeforeAsync(
        PresenceGrain grain,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        var (table, bucket, _) = Shape(grain);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {table} WHERE {bucket} < @cutoff",
            new { cutoff = cutoff.ToUniversalTime() },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// A scalar <c>timestamptz</c>, as the offset it is.
    /// </summary>
    /// <remarks>
    /// Npgsql hands a bare <c>timestamptz</c> back as a UTC <see cref="DateTime"/>, and asking Dapper
    /// for a <see cref="DateTimeOffset"/> from a scalar throws rather than converting — the object
    /// mapper converts, the scalar path does not. Two of these guard a <c>DROP TABLE</c>, so they are
    /// converted deliberately here rather than left to whichever path a caller happens to take.
    /// </remarks>
    private static DateTimeOffset? Utc(DateTime? value) =>
        value is { } instant
            ? new DateTimeOffset(DateTime.SpecifyKind(instant, DateTimeKind.Utc))
            : null;

    /// <summary>
    /// The table, its bucket column and the truncation unit for a grain.
    /// </summary>
    /// <remarks>
    /// Interpolated into SQL, and safe to be: every string returned here is a literal in this method,
    /// chosen by an enum member, with no path by which a caller's text reaches a statement.
    /// </remarks>
    private static (string Table, string Bucket, string Unit) Shape(PresenceGrain grain) => grain switch
    {
        PresenceGrain.Hour => ("presence_rollup_hour", "hour", "hour"),
        PresenceGrain.Day => ("presence_rollup_day", "day", "day"),
        _ => throw new ArgumentOutOfRangeException(nameof(grain), grain, "No table holds that grain."),
    };

    private static string Scope(PresenceGrain grain) => grain switch
    {
        PresenceGrain.Hour => "hour",
        PresenceGrain.Day => "day",
        _ => throw new ArgumentOutOfRangeException(nameof(grain), grain, "No watermark holds that grain."),
    };

    private sealed class Row
    {
        public Guid GameId { get; init; }

        public DateTimeOffset Bucket { get; init; }

        public int CountedSamples { get; init; }

        public int UnmeasurableSamples { get; init; }

        public int? MinCount { get; init; }

        public int? MaxCount { get; init; }

        public long? SumCount { get; init; }

        public decimal? MeanCount { get; init; }

        public PresenceRollup ToRecord(PresenceGrain grain) => new()
        {
            GameId = GameId,
            Grain = grain,
            Bucket = Bucket,
            CountedSamples = CountedSamples,
            UnmeasurableSamples = UnmeasurableSamples,
            MinCount = MinCount,
            MaxCount = MaxCount,
            SumCount = SumCount,
            MeanCount = MeanCount,
        };
    }
}
