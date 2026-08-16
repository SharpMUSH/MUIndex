using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// The crawl loop's own record of itself (migration 0017).
/// </summary>
/// <remarks>
/// <b>Nothing here is a measurement of a game</b>, which is why a TTL is allowed to reach it at all
/// — see the migration. The writer is <c>CrawlerService</c>; the readers are the front page's strip
/// and the maintenance sweep.
/// </remarks>
public interface ICrawlCycles
{
    /// <summary>Records a completed cycle.</summary>
    Task RecordAsync(CrawlCycleRecord cycle, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the crawler has been doing, in one round trip.
    /// </summary>
    /// <remarks>
    /// One query rather than four, because this is on the front page's path and the front page is
    /// the most-served document here. Every figure comes from a table the crawler writes, so the
    /// answer is the same whichever replica is asked — including one that does no crawling.
    /// </remarks>
    Task<CrawlerPulse> PulseAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Drops cycles older than the cutoff, and returns how many rows went.</summary>
    Task<int> SweepAsync(DateTimeOffset before, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether migration 0017 has been applied.
    /// </summary>
    /// <remarks>
    /// Asked rather than discovered as an exception, the same way the presence pass asks. A
    /// deployment mid-upgrade should render a front page without a strip, not a 500.
    /// </remarks>
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ICrawlCycles"/>
public sealed class NpgsqlCrawlCycles(NpgsqlDataSource source) : ICrawlCycles
{
    public async Task RecordAsync(CrawlCycleRecord cycle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO crawl_cycle (
                started_at, finished_at, considered, probed, answered, failed, refused, opted_out,
                errored, listed, reviews, counted, unmeasurable, transitions, referrals)
            VALUES (
                @startedAt, @finishedAt, @considered, @probed, @answered, @failed, @refused,
                @optedOut, @errored, @listed, @reviews, @counted, @unmeasurable, @transitions,
                @referrals)
            """,
            new
            {
                startedAt = cycle.StartedAt.ToUniversalTime(),
                finishedAt = cycle.FinishedAt.ToUniversalTime(),
                considered = cycle.Considered,
                probed = cycle.Probed,
                answered = cycle.Answered,
                failed = cycle.Failed,
                refused = cycle.Refused,
                optedOut = cycle.OptedOut,
                errored = cycle.Errored,
                listed = cycle.Listed,
                reviews = cycle.Reviews,
                counted = cycle.Counted,
                unmeasurable = cycle.Unmeasurable,
                transitions = cycle.Transitions,
                referrals = cycle.Referrals,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<CrawlerPulse> PulseAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // The registry half and the cycle half are unrelated aggregates over different tables, so
        // they are cross-joined rather than joined: one row out, and no cycle yet still yields the
        // registry figures instead of nothing.
        var row = await connection.QuerySingleAsync<PulseRow>(new CommandDefinition(
            """
            SELECT r.last_probe_at   AS LastProbeAt,
                   r.next_due_at     AS NextDueAt,
                   r.due_now         AS DueNow,
                   r.targets_known   AS TargetsKnown,
                   c.started_at      AS StartedAt,
                   c.finished_at     AS FinishedAt,
                   c.considered      AS Considered,
                   c.probed          AS Probed,
                   c.answered        AS Answered,
                   c.failed          AS Failed,
                   c.refused         AS Refused,
                   c.opted_out       AS OptedOut,
                   c.errored         AS Errored,
                   c.listed          AS Listed,
                   c.reviews         AS Reviews,
                   c.counted         AS Counted,
                   c.unmeasurable    AS Unmeasurable,
                   c.transitions     AS Transitions,
                   c.referrals       AS Referrals
              FROM (SELECT max(last_probed_at)                                    AS last_probe_at,
                           min(next_probe_at)                                     AS next_due_at,
                           count(*) FILTER (WHERE next_probe_at <= @now)::integer AS due_now,
                           count(*)::integer                                      AS targets_known
                      FROM crawl_target) AS r
              LEFT JOIN LATERAL (
                    SELECT *
                      FROM crawl_cycle
                     ORDER BY finished_at DESC
                     LIMIT 1) AS c ON true
            """,
            new { now = now.ToUniversalTime() },
            cancellationToken: cancellationToken));

        return new CrawlerPulse(
            row.LastProbeAt,
            row.NextDueAt,
            row.DueNow,
            row.TargetsKnown,
            row.FinishedAt is null
                ? null
                : new CrawlCycleRecord(
                    row.StartedAt!.Value,
                    row.FinishedAt.Value,
                    row.Considered!.Value,
                    row.Probed!.Value,
                    row.Answered!.Value,
                    row.Failed!.Value,
                    row.Refused!.Value,
                    row.OptedOut!.Value,
                    row.Errored!.Value,
                    row.Listed!.Value,
                    row.Reviews!.Value,
                    row.Counted!.Value,
                    row.Unmeasurable!.Value,
                    row.Transitions!.Value,
                    row.Referrals!.Value));
    }

    public async Task<int> SweepAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM crawl_cycle WHERE finished_at < @before",
            new { before = before.ToUniversalTime() },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT to_regclass('public.crawl_cycle') IS NOT NULL",
            cancellationToken: cancellationToken));
    }

    /// <summary>The flattened join, before the nullable half is folded back into a record.</summary>
    private sealed class PulseRow
    {
        public DateTimeOffset? LastProbeAt { get; init; }
        public DateTimeOffset? NextDueAt { get; init; }
        public int DueNow { get; init; }
        public int TargetsKnown { get; init; }

        public DateTimeOffset? StartedAt { get; init; }
        public DateTimeOffset? FinishedAt { get; init; }
        public int? Considered { get; init; }
        public int? Probed { get; init; }
        public int? Answered { get; init; }
        public int? Failed { get; init; }
        public int? Refused { get; init; }
        public int? OptedOut { get; init; }
        public int? Errored { get; init; }
        public int? Listed { get; init; }
        public int? Reviews { get; init; }
        public int? Counted { get; init; }
        public int? Unmeasurable { get; init; }
        public int? Transitions { get; init; }
        public int? Referrals { get; init; }
    }
}
