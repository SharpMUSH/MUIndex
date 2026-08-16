using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// The <c>presence_rollup_hour</c> and <c>presence_rollup_day</c> tables (spec §5.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not behind an interface.</b> Every method here is an aggregation the database does
/// in place — the rows never travel to this process to be summed — and an in-memory stand-in for it
/// would be a second implementation of §5.4's three states written by the same person as the first,
/// agreeing with it for the same reasons. What proves this correct is a real PostgreSQL, which is
/// what the tests use.
/// </para>
/// <para>
/// <b>Both grains are aggregated from the raw table, and the day is not aggregated from the hour.</b>
/// It would be cheaper, and it would make the day depend on the hour surviving: the two grains have
/// different retentions by design (§5.2 keeps the day for ever and the hour for two years), so a day
/// built from hours would be a permanent copy derived from an impermanent one, and re-running it
/// after the hours had been dropped would silently produce a different answer from the first run.
/// Reading raw for both means each grain is a projection of the same source, and retention never runs
/// ahead of the rollup, so that source is there when it matters.
/// </para>
/// </remarks>
public sealed class NpgsqlPresenceRollupStore(NpgsqlDataSource source) : IPresenceSeries
{
    /// <summary>
    /// Aggregates every raw sample in <c>[from, toExclusive)</c> into the given grain, and returns how
    /// many buckets it wrote.
    /// </summary>
    /// <remarks>
    /// An upsert, so re-running it over a window that has already been rolled produces the same rows:
    /// the rollup is a projection of the raw table rather than an accumulation, which is what lets a
    /// late-arriving sample be folded in by simply reading its hour again.
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

        var (table, bucket, unit) = Shape(grain);

        // Truncation happens in UTC on both sides so that a session's TimeZone setting can never move
        // a measurement into the neighbouring hour.
        //
        // The three-state rule is in the two FILTERs and in what is missing: min/max/sum are over
        // counted samples alone and come back NULL when there were none, and a group is only produced
        // for a bucket some probe actually wrote a row in — so an hour nobody measured stays absent
        // rather than arriving as a zero.
        // The distribution (migration 0019) needs a second level of grouping — one row per distinct
        // count before one row per bucket — so the aggregate is built in a CTE and joined back. It
        // is exact rather than bucketed: an entry per count some probe actually read, which is
        // bounded by the number of counted samples in the bucket and is a handful.
        //
        // A LEFT JOIN, because a bucket that was probed and never counted has no distribution at all
        // and must still produce its row — that is §5.4's hatched cell, and dropping it here would
        // turn "probed, could not count" into "not measured".
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
    /// Only ever moves forward. Two replicas racing a maintenance pass are already prevented by the
    /// advisory lock the hosted service takes, but a watermark that could go backwards would make
    /// retention's "already rolled up" question answerable with a stale yes, and that question guards
    /// a <c>DROP TABLE</c>.
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

    /// <summary>
    /// Whether the tables a pass reads and writes exist yet.
    /// </summary>
    /// <remarks>
    /// The maintenance service starts with the web tier, and on a fresh database the migrations are
    /// being applied by whichever replica holds the crawl lease at the same moment. Asking is one
    /// catalogue lookup; not asking means every replica's first pass throws <c>42P01</c> and stands
    /// down for the whole retry interval, which turns "the schema arrived a second later" into five
    /// minutes of no maintenance and an error in the log that looks like a real fault.
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
    /// Row-by-row rather than by partition, because these two tables are not partitioned: they are
    /// smaller than the raw one by the number of probes an hour holds, and §5.2 keeps the daily grain
    /// for ever, so the table that would most want partitioning is the one nothing ever deletes from.
    /// </remarks>
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
