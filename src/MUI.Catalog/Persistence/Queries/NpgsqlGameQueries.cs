using System.Globalization;

using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// <see cref="IGameQueries"/> against PostgreSQL.
/// </summary>
/// <remarks>
/// Returns view models rather than rows, so assembly logic isn't repeated (and drifting) between this
/// and the plain-text surface. The §5.1 precedence ladder is never rewritten as SQL `CASE` logic: rows
/// come back per source and <see cref="FieldPrecedence.Winner"/> picks, using the declared order of
/// <see cref="FieldSource"/> as the one spelling of the ladder. The two aggregate reads instead sort
/// by <c>array_position(@ladder, source)</c> using <see cref="SourceLadder"/>, generated from the same
/// enum order, for the same reason.
/// </remarks>
public sealed partial class NpgsqlGameQueries(
    NpgsqlDataSource source, IFieldRegistry? registry = null, TimeProvider? time = null)
    : IGameQueries
{
    /// <summary>
    /// Whether a game is offered as a game of its own on a public surface — vouched for (spec §8,
    /// migration 0010) and not absorbed by a merge (spec §7.3, migration 0018).
    /// </summary>
    /// <remarks>
    /// A game is public if nobody submitted it, if it has been claimed, or if a probe has
    /// corroborated it (§7.8, migration 0022) — waiting on a claim alone would strand a game whose
    /// operator has no reason to know this site exists. A game absorbed by a merge in force is never
    /// offered separately: the absorbed game keeps its rows, only the listing stops presenting it as
    /// a second game.
    /// <para>
    /// Composed once here rather than added at each call site: a predicate written out per query is
    /// one that gets missed on the next query someone adds — which happened once, via
    /// <c>JOIN game g</c> call sites that didn't repeat the direct-table check, letting an unclaimed
    /// submission stay off the listing but show up in the rankings.
    /// </para>
    /// </remarks>
    private const string Public =
        $"((submitted_at IS NULL OR is_claimed OR corroborated_at IS NOT NULL) AND {NotAbsorbed})";

    /// <summary>
    /// The states a game is counted and listed in.
    /// </summary>
    /// <remarks>
    /// <c>archived</c> (stopped answering, reversed by the next successful probe), <c>excluded</c>
    /// (not a game, reversed only by a person), and <c>unlisted</c> (operator asked out, reversed by
    /// either — §11) are three different statements, not synonyms. All three keep the page, URL,
    /// history and crawl; only the listing withholds them. Named once so a count added later can't
    /// silently omit one.
    /// </remarks>
    private const string ListedStates = "('archived', 'excluded', 'unlisted')";

    /// <summary>
    /// The states no browsing surface may reach, whatever it is asking about.
    /// </summary>
    /// <remarks>
    /// <c>archived</c> is deliberately not included — the archive is a browsable section in its own
    /// right (§7.5). <c>excluded</c> and <c>unlisted</c> mean nothing that walks the site may arrive
    /// here; only the address does. Needed because <c>Public</c> alone says who vouched for a game,
    /// not its lifecycle state — the liveness feeds and referral neighbours read it directly and
    /// would otherwise keep linking to a game the listing had withdrawn.
    /// </remarks>
    private const string NeverBrowsable = "('excluded', 'unlisted')";

    /// <summary>The same rule where the table is aliased.</summary>
    private const string PublicG =
        $"((g.submitted_at IS NULL OR g.is_claimed OR g.corroborated_at IS NOT NULL) AND {NotAbsorbedG})";

    /// <summary>
    /// No merge is pointing this game at another one right now.
    /// </summary>
    /// <remarks>
    /// <c>NOT EXISTS</c> rather than <c>NOT IN</c>: the partial index
    /// <c>merge_log_absorbed_once_idx</c> is on <c>(from_game_id) WHERE reverted_at IS NULL</c>, which
    /// is exactly this predicate's shape, and <c>NOT IN</c> against a subquery would not use it.
    /// </remarks>
    private const string NotAbsorbed =
        "NOT EXISTS (SELECT 1 FROM merge_log m WHERE m.from_game_id = game.id AND m.reverted_at IS NULL)";

    /// <summary>The same rule where the table is aliased.</summary>
    private const string NotAbsorbedG =
        "NOT EXISTS (SELECT 1 FROM merge_log m WHERE m.from_game_id = g.id AND m.reverted_at IS NULL)";

    /// <summary>
    /// The heatmap's window (spec §5.2), which is the same 56 days retention floors itself at —
    /// one constant, because a window drawn wider than the retention protecting it would read off
    /// the end of what is kept.
    /// </summary>
    public static readonly TimeSpan ActivityWindow = PresenceRetentionOptions.HeatmapWindow;

    /// <summary>
    /// §5.2's "reachable recently", which separates <c>quiet</c> from <c>dark</c>.
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="FacetedSearch"/> and the demo fixture so the threshold can't drift
    /// between implementations.
    /// </remarks>
    public static readonly TimeSpan RecentlyReachable = FacetedSearch.RecentlyReachable;

    /// <summary>§5.2's "active this week".</summary>
    public static readonly TimeSpan ThisWeek = TimeSpan.FromDays(7);

    /// <summary>
    /// The window the busiest ranking is measured over, and named on the page.
    /// </summary>
    /// <remarks>
    /// A week: a MU* has a weekly shape, and a shorter window would rank Saturday's games above
    /// Tuesday's. <see cref="RankingSpan"/> offers longer windows beside it.
    /// </remarks>
    public static readonly TimeSpan RankingWindow = RankingSpan.Week.Window();

    /// <summary>
    /// How many counted samples a game needs before it can be ranked.
    /// </summary>
    /// <remarks>
    /// A day's worth of hourly probes — fewer would rank a lucky evening probe rather than the game.
    /// Shared with <see cref="SortWindows.MinimumSamples"/> so the listing's average sorts use the
    /// same floor.
    /// </remarks>
    public const int MinimumRankingSamples = SortWindows.MinimumSamples;

    private readonly IFieldRegistry _registry = registry ?? FieldRegistry.Instance;

    /// <summary>
    /// So a test can render a fixed frame. Everything time-dependent on this class reads it, and
    /// nothing calls <see cref="DateTimeOffset.UtcNow"/> directly.
    /// </summary>
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private static GameField? Winner(IReadOnlyList<GameField> fields, string name) =>
        FieldPrecedence.Winner(fields.Where(f => string.Equals(f.Field, name, StringComparison.Ordinal)));

    private static IReadOnlyList<string> MeasuredProtocolsOf(IReadOnlyList<GameField> fields) =>
        fields
            .Where(f => CapabilityFields.IsMeasured(f.Field)
                && string.Equals(f.Value, "true", StringComparison.Ordinal))
            .Select(f => CapabilityFields.CapabilityOf(f.Field))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Which of these games we hold icon bytes for.
    /// </summary>
    /// <remarks>
    /// A flag, never the bytes — the image itself is served separately from <c>/g/{slug}/icon</c>.
    /// <b>False carries no cause and must never be given one</b>: a game whose <c>ICON</c> names
    /// nothing, one whose web server we could not reach, and an empty cache are indistinguishable
    /// here on purpose — only the first is a fact about the game, and rule 5 forbids publishing the
    /// other two as though they were.
    /// </remarks>
    private static async Task<HashSet<Guid>> IconsForAsync(
        NpgsqlConnection connection,
        Guid[] ids,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT game_id FROM game_icon WHERE game_id = ANY(@ids)",
            new { ids },
            cancellationToken: cancellationToken));

        return [.. rows];
    }

    private static async Task<Dictionary<Guid, List<GameField>>> FieldsForAsync(
        NpgsqlConnection connection,
        Guid[] ids,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<FieldRow>(new CommandDefinition(
            """
            SELECT game_id AS GameId, field AS Field, source AS Source, value AS Value,
                   first_seen_at AS FirstSeenAt, last_confirmed_at AS LastConfirmedAt
              FROM game_field
             WHERE game_id = ANY(@ids)
            """,
            new { ids },
            cancellationToken: cancellationToken));

        return rows
            .Select(r => new GameField(
                r.GameId, r.Field, SqlEnums.ToFieldSource(r.Source), r.Value, r.FirstSeenAt, r.LastConfirmedAt))
            .GroupBy(f => f.GameId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private sealed class FieldRow
    {
        public Guid GameId { get; init; }

        public string Field { get; init; } = string.Empty;

        public string Source { get; init; } = string.Empty;

        public string Value { get; init; } = string.Empty;

        public DateTimeOffset FirstSeenAt { get; init; }

        public DateTimeOffset LastConfirmedAt { get; init; }
    }

    /// <summary>
    /// The presence facts a listing needs, in one pass: the current count, whether anybody was
    /// counted this week, and — separately — whether the week holds any readable count at all.
    /// </summary>
    /// <remarks>
    /// The last pair distinguishes a measured zero from an unreadable one — a game at nought all
    /// week and a game whose every <c>WHO</c> failed to parse must not collapse into one activity
    /// band.
    /// </remarks>
    private async Task<Dictionary<Guid, PresenceDigest>> PresenceDigestAsync(
        NpgsqlConnection connection,
        Guid[] ids,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // "Now" is the field registry's own window for PLAYERS, so a count on a page and a count in
        // the API age out at the same moment and neither invents its own idea of fresh.
        var nowWindow = _registry.Find("PLAYERS")?.ExpectedRefresh ?? TimeSpan.FromHours(2);

        // The sample's own instant and source come back with the count, since the label (§10.1) must
        // describe the row the number actually came from, not a re-derived guess.
        var rows = await connection.QueryAsync<DigestRow>(new CommandDefinition(
            """
            -- Two grouped passes over the window, joined to the id list — deliberately NOT a
            -- correlated LATERAL per game. `presence_sample` is written in probe order, so one
            -- game's week is scattered across the partition rather than gathered: a per-game
            -- bitmap heap scan re-read the same pages once per game, 71 pages to collect 77 rows,
            -- and the listing asks for the whole catalogue at once. Measured on production
            -- (935 games): 89,499 buffers and 58 ms for the LATERAL form against 3,928 buffers
            -- and 5.4 ms for this one, same rows out — the scan is shared instead of repeated.
            WITH recent AS (
                -- `DISTINCT ON` + `ORDER BY game_id, at DESC` is the set-based spelling of the
                -- per-game `ORDER BY at DESC LIMIT 1` this replaces: one row per game, the newest.
                SELECT DISTINCT ON (p.game_id) p.game_id, p.count, p.at, p.source
                  FROM presence_sample p
                 WHERE p.game_id = ANY(@ids) AND p.at >= @nowFrom AND p.count IS NOT NULL
                 ORDER BY p.game_id, p.at DESC
            ),

            -- Three tallies, one scan: `count(p.count)` includes a measured nought;
            -- `count(*) FILTER (count IS NULL)` is answered-but-unreadable. A row exists only
            -- where a probe got far enough to try, so no tally here speaks for an hour we never
            -- measured (§5.4's third state, which names no cause).
            week AS (
                SELECT p.game_id,
                       count(*) FILTER (WHERE p.count > 0) AS nonzero,
                       count(p.count) AS counted,
                       count(*) FILTER (WHERE p.count IS NULL) AS uncountable
                  FROM presence_sample p
                 WHERE p.game_id = ANY(@ids) AND p.at >= @weekFrom
                 GROUP BY p.game_id
            )

            -- Still driven off `unnest`, so a game with no sample in either window keeps its row
            -- and reaches the `coalesce`s below — the aggregate-over-nothing the LATERAL used to
            -- supply is now an absent group, and a LEFT JOIN says the same thing.
            SELECT g.id AS GameId, recent.count AS CountNow, recent.at AS CountedAt,
                   recent.source AS CountSource, coalesce(week.nonzero, 0) AS NonZeroThisWeek,
                   coalesce(week.counted, 0) AS CountedThisWeek,
                   coalesce(week.uncountable, 0) AS UncountableThisWeek
              FROM unnest(@ids::uuid[]) AS g(id)
              LEFT JOIN recent ON recent.game_id = g.id
              LEFT JOIN week   ON week.game_id   = g.id
            """,
            new
            {
                ids,
                nowFrom = (now - nowWindow).ToUniversalTime(),
                weekFrom = (now - ThisWeek).ToUniversalTime(),
            },
            cancellationToken: cancellationToken));

        return rows.ToDictionary(
            r => r.GameId,
            r => new PresenceDigest(
                r.CountNow,
                r.NonZeroThisWeek > 0,
                r.CountedAt,
                r.CountSource is { } source ? SqlEnums.ToFieldSource(source) : null,
                r.CountedThisWeek > 0,
                r.UncountableThisWeek > 0));
    }

    private sealed class DigestRow
    {
        public Guid GameId { get; init; }

        public int? CountNow { get; init; }

        public DateTimeOffset? CountedAt { get; init; }

        public string? CountSource { get; init; }

        public long NonZeroThisWeek { get; init; }

        public long CountedThisWeek { get; init; }

        public long UncountableThisWeek { get; init; }
    }

    /// <summary>
    /// The count as a labelled fact, or null where there is no count to label.
    /// </summary>
    /// <remarks>
    /// Staleness is asked of the registry rather than assumed, even though the digest can't currently
    /// produce a stale sample — a <c>false</c> compiled in here would be a second opinion on the
    /// window declared once in spec §5.6, wrong the day that window moves.
    /// </remarks>
    private ProvenanceChip? CountChip(PresenceDigest digest, DateTimeOffset now) =>
        digest is { CountNow: { } count, CountedAt: { } at, CountSource: { } source }
            ? new ProvenanceChip(
                count.ToString(CultureInfo.InvariantCulture),
                source,
                at,
                _registry.IsStale("PLAYERS", at, now))
            : null;

    /// <summary>A field as a labelled fact, or null where nothing has ever set it.</summary>
    private ProvenanceChip? Chip(GameField? field, DateTimeOffset now) => field is null
        ? null
        : new ProvenanceChip(
            field.Value,
            field.Source,
            field.LastConfirmedAt,
            _registry.IsStale(field.Field, field.LastConfirmedAt, now));

    /// <summary>
    /// Every day a game has enough presence samples for its own median, over the last
    /// <see cref="GrowthTrend.Span"/> — what <see cref="GrowthTrend.Of"/> fits a trend line through.
    /// </summary>
    /// <remarks>
    /// One frequency table per (game, day) rather than per game — the same walk
    /// <see cref="PlayersOverWindowAsync"/> uses to find one window's median, run once per day instead
    /// of once over the whole span, so a day the crawler barely touched a game is absent rather than
    /// diluting a pooled figure or being read as a zero (rule 4). Raw samples cover today, since the
    /// day rollup only reaches back through <c>presence_rollup_state</c>'s high-water mark; older days
    /// read the rollup's own histogram, which survives retention dropping raw partitions (§5.2).
    /// </remarks>
    private static async Task<Dictionary<Guid, List<DailyMedian>>> DailyMediansAsync(
        NpgsqlConnection connection, Guid[] ids, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<DailyMedianRow>(new CommandDefinition(
            """
            WITH boundary AS (
                SELECT coalesce(
                    (SELECT rolled_up_through FROM presence_rollup_state WHERE scope = 'day'),
                    '-infinity'::timestamptz) AS at
            ),
            span AS (
                SELECT date_trunc('day', @from AT TIME ZONE 'UTC') AT TIME ZONE 'UTC' AS from_at
            ),
            frequency AS (
                SELECT p.game_id,
                       date_trunc('day', p.at AT TIME ZONE 'UTC') AT TIME ZONE 'UTC' AS day,
                       p.count AS value, count(*)::bigint AS times
                  FROM presence_sample p
                 WHERE p.game_id = ANY(@ids)
                   AND p.count IS NOT NULL
                   AND p.at >= (SELECT from_at FROM span)
                   AND p.at >= (SELECT at FROM boundary)
                 GROUP BY 1, 2, 3

                UNION ALL

                SELECT r.game_id, r.day, e.key::int, e.value::bigint
                  FROM presence_rollup_day r
                  CROSS JOIN LATERAL jsonb_each_text(r.count_histogram) AS e(key, value)
                 WHERE r.game_id = ANY(@ids)
                   AND r.count_histogram IS NOT NULL
                   AND r.day >= (SELECT from_at FROM span)
                   AND r.day < (SELECT at FROM boundary)
            ),
            counted AS (
                SELECT game_id, day, value, sum(times) AS times
                  FROM frequency
                 GROUP BY 1, 2, 3
            ),
            walked AS (
                SELECT game_id, day, value,
                       sum(times) OVER (PARTITION BY game_id, day ORDER BY value) AS running,
                       ceil(sum(times) OVER (PARTITION BY game_id, day) / 2.0)    AS half,
                       sum(times) OVER (PARTITION BY game_id, day)                AS samples
                  FROM counted
            )
            SELECT game_id        AS GameId,
                   day             AS Day,
                   min(value)::int AS Median,
                   max(samples)::int AS Samples
              FROM walked
             WHERE running >= half
             GROUP BY 1, 2
            HAVING max(samples) >= @minimumSamplesPerDay
            """,
            new
            {
                ids,
                from = (now - GrowthTrend.Span).ToUniversalTime(),
                minimumSamplesPerDay = GrowthTrend.MinimumSamplesPerDay,
            },
            cancellationToken: cancellationToken));

        return rows
            .GroupBy(r => r.GameId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .Select(r => new DailyMedian(DateOnly.FromDateTime(r.Day.UtcDateTime), r.Median, r.Samples))
                    .ToList());
    }

    private sealed class DailyMedianRow
    {
        public Guid GameId { get; init; }

        public DateTimeOffset Day { get; init; }

        public int Median { get; init; }

        public int Samples { get; init; }
    }

    private sealed class GameRow
    {
        public Guid Id { get; init; }

        public string Slug { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string? Tagline { get; init; }

        public string State { get; init; } = string.Empty;

        public bool IsClaimed { get; init; }

        public DateTimeOffset? LastReachableAt { get; init; }

        /// <summary>
        /// Why an editor ruled this out (migration 0024). Null on the listing's own query, which does
        /// not select it — the listing has no excluded games in it to explain.
        /// </summary>
        public string? ExcludedReason { get; init; }

        /// <summary>When we first saw this address, for <see cref="GameSort.Discovered"/>.</summary>
        public DateTimeOffset? FirstSeenAt { get; init; }

        /// <summary>
        /// Which channel first told us about this game's address (migration 0033). Text rather than
        /// the enum: a spelling this build does not know is a line we omit, not a page that fails.
        /// </summary>
        public string? DiscoveredVia { get; init; }
    }

    private sealed record PresenceDigest(
        int? CountNow,
        bool NonZeroThisWeek,
        DateTimeOffset? CountedAt = null,
        FieldSource? CountSource = null,

        /// <summary>Any sample this week carried a number — <b>a measured nought included</b>.</summary>
        bool CountedThisWeek = false,

        /// <summary>Any sample this week answered and carried no number (§5.4's middle state).</summary>
        bool UncountableThisWeek = false)
    {
        /// <summary>
        /// We hold presence rows for the week and not one of them is readable.
        /// </summary>
        /// <remarks>
        /// <b>Both halves are load-bearing.</b> Without <see cref="CountedThisWeek"/> this would
        /// catch a game measured at nought all week, which is a count and the opposite fact. Without
        /// <see cref="UncountableThisWeek"/> it would catch a game we hold nothing at all for, which
        /// is not measured — and naming a cause for that is the one thing rule 2 forbids.
        /// </remarks>
        public bool Uncounted => UncountableThisWeek && !CountedThisWeek;

        public static readonly PresenceDigest None = new(null, false);
    }
}
