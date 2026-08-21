using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

public sealed partial class NpgsqlGameQueries
{
    private const int RankingLimit = 20;

    /// <summary>
    /// The rankings (spec §9) — measured data only, and every basis stated on the record so the page
    /// and the plain surface cannot describe the same table two ways.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The busiest table ranks on a <em>median of measured concurrent counts</em>. A NULL count
    /// (unparseable probe, §5.4) is excluded rather than read as a zero (rule 4) — otherwise a game
    /// whose <c>DOING</c> header we can't parse would sink to the bottom while running perfectly well.
    /// A measured zero is a count and stays in.
    /// </para>
    /// <para>
    /// Eligibility needs both clauses: <see cref="MinimumRankingSamples"/> floors how many probes a
    /// median may be taken over, and <see cref="RankingSpans.MinimumDays"/> floors how much of the
    /// window they came from — without the second, a game probed hard for one weekend would rank in
    /// a table describing a quarter.
    /// </para>
    /// <para>
    /// The second table is the current unbroken run of reachability — one open interval per game
    /// (§5.3) — and carries the date the spell began rather than a duration.
    /// </para>
    /// </remarks>
    public async Task<Rankings> RankingsAsync(
        RankingSpan span = RankingSpan.Week,
        CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var listed = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT count(*)::int FROM game WHERE state NOT IN {ListedStates} AND {Public}",
            cancellationToken: cancellationToken));

        // Read from the rollup rather than raw samples: §5.2 lets retention drop raw once rolled up,
        // so reading raw here would silently shorten the window as a deployment ages. Reproduces
        // `percentile_disc(0.5)` exactly via `ceil(n / 2.0)` — not `(n + 1) / 2` — because `sum()`
        // over bigint returns exact `numeric`; the naive integer form shipped as a median one element
        // too high on every even sample count, caught by `PresenceHistogramPostgresTests`. Buckets
        // without a distribution are excluded by the JOIN, so `samples` and `days` describe the same
        // set the median was taken from.
        var busiest = (await connection.QueryAsync<BusiestRow>(new CommandDefinition(
            $"""
            WITH bucket AS (
                SELECT r.game_id, g.slug, g.name, r.counted_samples, r.max_count, r.count_histogram
                  FROM presence_rollup_day r
                  JOIN game g ON g.id = r.game_id
                 WHERE r.day >= @from AND r.count_histogram IS NOT NULL
                   AND g.state NOT IN {ListedStates} AND {PublicG}),
            eligible AS (
                SELECT game_id, slug, name,
                       sum(counted_samples)::int AS samples,
                       max(max_count) AS peak,
                       count(*)::int AS days
                  FROM bucket
                 GROUP BY game_id, slug, name
                HAVING sum(counted_samples) >= @minimum AND count(*) >= @minimumDays),
            frequency AS (
                SELECT b.game_id, e.key::int AS value, sum(e.value::bigint) AS times
                  FROM bucket b
                  JOIN eligible el ON el.game_id = b.game_id
                  CROSS JOIN LATERAL jsonb_each_text(b.count_histogram) AS e(key, value)
                 GROUP BY b.game_id, e.key::int),
            walked AS (
                SELECT game_id, value,
                       sum(times) OVER (PARTITION BY game_id ORDER BY value) AS running,
                       ceil(sum(times) OVER (PARTITION BY game_id) / 2.0) AS half
                  FROM frequency),
            median AS (
                SELECT game_id, min(value)::int AS median
                  FROM walked
                 WHERE running >= half
                 GROUP BY game_id)
            SELECT e.slug AS Slug, e.name AS Name, m.median AS Median, e.peak AS Peak,
                   e.samples AS Samples, e.days AS Days,
                   (count(*) OVER ())::int AS Eligible
              FROM eligible e
              JOIN median m ON m.game_id = e.game_id
             ORDER BY m.median DESC, e.peak DESC, e.name
             LIMIT @limit
            """,
            new
            {
                from = DayAlignedStart(now, span),
                minimum = MinimumRankingSamples,
                minimumDays = span.MinimumDays(),
                limit = RankingLimit,
            },
            cancellationToken: cancellationToken))).ToList();

        var spells = (await connection.QueryAsync<SpellRow>(new CommandDefinition(
            $"""
            SELECT g.slug AS Slug, g.name AS Name, a.from_at AS Since
              FROM availability_interval a
              JOIN game g ON g.id = a.game_id
             WHERE a.to_at IS NULL AND a.state = 'reachable' AND g.state NOT IN {ListedStates}
               AND {PublicG}
             ORDER BY a.from_at
             LIMIT @limit
            """,
            new { limit = RankingLimit },
            cancellationToken: cancellationToken))).ToList();

        var trending = await TrendingThisWeekAsync(connection, now, cancellationToken);

        return new Rankings(
            now,
            span.Window(),
            MinimumRankingSamples,
            listed,
            busiest.Count == 0 ? 0 : busiest[0].Eligible,
            busiest
                .Select(r => new BusiestGame(r.Slug, r.Name, r.Median, r.Peak, r.Samples, r.Days))
                .ToList(),
            spells.Select(r => new ReachableSpell(r.Slug, r.Name, r.Since)).ToList(),
            trending)
        {
            Span = span,
        };
    }

    private sealed class BusiestRow
    {
        public string Slug { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public int Median { get; init; }

        public int Peak { get; init; }

        public int Samples { get; init; }

        /// <summary>Day buckets the samples came from — the coverage half of the eligibility rule.</summary>
        public int Days { get; init; }

        public int Eligible { get; init; }
    }

    private sealed class SpellRow
    {
        public string Slug { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public DateTimeOffset Since { get; init; }
    }

    /// <summary>
    /// The games trending up over their measured history — the same classification the listing row's
    /// own arrow uses (<see cref="GrowthTrend.Of"/>), so a game the board calls trending is exactly
    /// one whose arrow agrees, never a decliner or a steady game caught by a bare sort on the number.
    /// </summary>
    private async Task<IReadOnlyList<TrendingGame>> TrendingThisWeekAsync(
        NpgsqlConnection connection, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var eligible = (await connection.QueryAsync<EligibleGameRow>(new CommandDefinition(
            $"SELECT id AS Id, slug AS Slug, name AS Name FROM game WHERE state NOT IN {ListedStates} AND {Public}",
            cancellationToken: cancellationToken))).ToList();

        var ids = eligible.Select(g => g.Id).ToArray();
        var dailyMedians = await DailyMediansAsync(connection, ids, now, cancellationToken);

        return
        [
            .. eligible
                .Select(g => (Game: g, Days: dailyMedians.GetValueOrDefault(g.Id, [])))
                // Selected on the fraction, ranked on the players. The band is what keeps a big game's
                // two-player wobble off the board at all; the ordering is what stops a game that gained
                // two players outranking one that gained fifty once both are on it.
                .Select(row => (row.Game, row.Days, Change: GrowthTrend.ChangeFraction(row.Days)))
                .Where(row => row.Change > GrowthTrend.SteadyBand)
                .Select(row => new TrendingGame(
                    row.Game.Slug, row.Game.Name,
                    EarliestMedian: row.Days.MinBy(d => d.Day)!.Median,
                    LatestMedian: row.Days.MaxBy(d => d.Day)!.Median,
                    ChangePlayers: GrowthTrend.ChangePlayers(row.Days)!.Value))
                .OrderByDescending(r => r.ChangePlayers)
                .ThenByDescending(r => r.LatestMedian)
                .ThenBy(r => r.Name, StringComparer.Ordinal)
                .Take(RankingLimit),
        ];
    }

    private sealed class EligibleGameRow
    {
        public Guid Id { get; init; }

        public string Slug { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;
    }

    /// <summary>
    /// The first day bucket a span covers: midnight UTC, <c>days - 1</c> whole days before today.
    /// </summary>
    /// <remarks>
    /// Aligned to the bucket rather than rolled back from the instant, since half a day bucket can't
    /// be read out of the median's grain — the page says "the last N days" and means N buckets.
    /// </remarks>
    internal static DateTimeOffset DayAlignedStart(DateTimeOffset now, RankingSpan span)
    {
        var utc = now.ToUniversalTime();
        var today = new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);

        return today.AddDays(-(span.Days() - 1));
    }
}
