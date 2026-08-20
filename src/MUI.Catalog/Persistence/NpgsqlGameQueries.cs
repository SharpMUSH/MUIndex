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
public sealed class NpgsqlGameQueries(NpgsqlDataSource source, IFieldRegistry? registry = null)
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

    private const int FeedLimit = 13;

    private const int ChangeLimit = 20;

    private const int ChangePerFieldLimit = 3;

    private const int RankingLimit = 20;

    /// <summary>
    /// The §5.1 ladder as a SQL parameter, generated from the enum so it cannot drift from it.
    /// </summary>
    private static readonly string[] SourceLadder = Enum.GetValues<FieldSource>()
        .OrderBy(FieldPrecedence.RankOf)
        .Select(SqlEnums.ToDb)
        .ToArray();

    private readonly IFieldRegistry _registry = registry ?? FieldRegistry.Instance;

    /// <summary>
    /// Overridable so a test can render a fixed frame. Everything time-dependent on this class reads
    /// it, and nothing calls <c>DateTimeOffset.UtcNow</c> directly.
    /// </summary>
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// The listing and its facets (spec §9), from one pass over one set of games.
    /// </summary>
    /// <remarks>
    /// The database narrows only on the archive toggle; every other facet is decided by
    /// <see cref="FacetedSearch"/> over <see cref="GameFacetRow"/> so the listing and its facet counts
    /// are always the same arithmetic — a separate `WHERE` here and `GROUP BY` there already
    /// disagreed once, for <c>band=archived</c>.
    /// </remarks>
    public async Task<GameListing> SearchAsync(
        GameFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var now = Clock();

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // Archived games leave the default listing (spec §7.5); requesting the archived band lifts
        // just that exclusion. Must lift `archived` only — not `excluded` or `unlisted`, which answer
        // different questions the archive checkbox doesn't ask.
        var includeArchived = filter.IncludeArchived || filter.Band is ActivityBand.Archived;

        var rows = (await connection.QueryAsync<GameRow>(new CommandDefinition(
            $"""
            SELECT g.id AS Id, g.slug AS Slug, g.name AS Name, g.tagline AS Tagline,
                   g.state AS State, g.is_claimed AS IsClaimed, g.last_reachable_at AS LastReachableAt,
                   g.first_seen_at AS FirstSeenAt
              FROM game g
             WHERE g.state NOT IN {NeverBrowsable}
               AND (@includeArchived OR g.state <> 'archived')
               AND {PublicG}
             ORDER BY g.name
            """,
            new { includeArchived },
            cancellationToken: cancellationToken))).ToList();

        if (rows.Count == 0)
        {
            return GameListing.Empty;
        }

        var ids = rows.Select(row => row.Id).ToArray();
        var fields = await FieldsForAsync(connection, ids, cancellationToken);
        var presence = await PresenceDigestAsync(connection, ids, now, cancellationToken);
        var tls = await TlsEndpointsAsync(connection, ids, cancellationToken);
        var icons = await IconsForAsync(connection, ids, cancellationToken);

        // Only where the order asks for it. It is an aggregate over the presence series of the whole
        // catalogue, and computing it for a listing sorted by name would be a scan nobody reads.
        var windows = SortWindows.Of(filter.Sort) is { } span
            ? await PlayersOverWindowAsync(connection, ids, span, now, cancellationToken)
            : [];

        // Unlike windows, always computed: trending is a facet now (Games list), not only a figure a
        // window sort happens to show, so a reader has to be able to filter on it without first
        // having to sort by a typical count.
        var dailyMedians = await DailyMediansAsync(connection, ids, now, cancellationToken);
        var growth = ids.ToDictionary(
            id => id,
            id => GrowthTrend.Of(dailyMedians.GetValueOrDefault(id, [])));

        var facetRows = new List<GameFacetRow>(rows.Count);

        foreach (var row in rows)
        {
            var forGame = fields.TryGetValue(row.Id, out var list) ? list : [];
            var digest = presence.TryGetValue(row.Id, out var found) ? found : PresenceDigest.None;
            var state = SqlEnums.ToLifecycleState(row.State);

            var codebase = Winner(forGame, "CODEBASE");
            var genre = Winner(forGame, "GENRE")?.Value;

            var summary = new GameSummary(
                row.Id,
                row.Slug,
                row.Name,
                row.Tagline,
                state,
                row.IsClaimed,
                digest.CountNow,
                codebase?.Value,
                MeasuredProtocolsOf(forGame),
                row.LastReachableAt,
                CountChip(digest, now),
                Chip(codebase, now),
                icons.Contains(row.Id),
                windows.GetValueOrDefault(row.Id),
                growth.GetValueOrDefault(row.Id),
                row.FirstSeenAt);

            facetRows.Add(new GameFacetRow(
                summary,
                BandOf(state, row.LastReachableAt, digest, now),
                FacetedSearch.LastSeenOf(row.LastReachableAt, now),
                TlsMeasured: tls.Contains(row.Id),
                Charset: NegotiatedCharset(forGame),
                Language: Winner(forGame, "LANGUAGE")?.Value,
                Codebase: summary.Codebase,
                Family: Winner(forGame, "FAMILY")?.Value,
                Genre: genre,

                // Winner's value, not the raw MSSP claim — an owner may add a flag their server never
                // sent, and the listing must read the correction.
                IsAdult: AdultContent.Declared(
                    genre, Winner(forGame, AdultContent.Field)?.Value),

                // Distinct from BandOf's `Quiet`: a measured zero all week vs. every count unreadable.
                Uncounted: digest.Uncounted,

                // From the availability series, never the presence one — a hole there can't tell an
                // hour we missed from an hour we could not reach (rule 2).
                Unreachable: FacetedSearch.NotReachedRecently(row.LastReachableAt, now),

                Growth: summary.Growth));
        }

        return FacetedSearch.Search(facetRows, filter);
    }

    /// <summary>A listing with no panel — the same query, projected.</summary>
    public async Task<IReadOnlyList<GameSummary>> ListAsync(
        GameFilter filter,
        CancellationToken cancellationToken = default) =>
        (await SearchAsync(filter, cancellationToken)).Games;

    /// <summary>
    /// The encoding CHARSET settled on, and never the game's MSSP claim about one.
    /// </summary>
    /// <remarks>
    /// Deliberately not the precedence winner: the winner would silently fall back to the game's own
    /// assertion when there's no handshake measurement, making a facet advertised as measured answer
    /// from declared data. Unmeasured games belong in the unknown bucket instead.
    /// </remarks>
    private static string? NegotiatedCharset(IReadOnlyList<GameField> fields) =>
        fields.FirstOrDefault(f =>
            string.Equals(f.Field, "CHARSET", StringComparison.Ordinal)
            && f.Source is FieldSource.Handshake)?.Value;

    /// <summary>
    /// The games we have completed a TLS connection to.
    /// </summary>
    /// <remarks>
    /// An endpoint row, not a capability claim: <c>capability.ssl.declared</c> only means a server
    /// configured a port, an endpoint of kind <c>tls</c> means a socket was opened. Nothing writes one
    /// yet (the crawler dials plaintext), so this returns empty and the facet doesn't render — the
    /// honest rendering of a measurement nobody has taken.
    /// </remarks>
    private static async Task<HashSet<Guid>> TlsEndpointsAsync(
        NpgsqlConnection connection,
        Guid[] ids,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT DISTINCT game_id
              FROM game_endpoint
             WHERE game_id = ANY(@ids) AND kind = 'tls' AND state <> 'gone'
            """,
            new { ids },
            cancellationToken: cancellationToken));

        return [.. rows];
    }

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

    /// <summary>
    /// What each game's counts added up to over one window — the basis the window sorts rank on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reads the daily rollup below the retention watermark and raw samples above it, same as
    /// <see cref="ActivityAsync"/>: the rollup survives retention dropping raw partitions (§5.2), and
    /// raw samples cover probes taken since the last completed day.
    /// </para>
    /// <para>
    /// The typical count is a median (same walk as <see cref="RankingsAsync"/>: summed frequencies in
    /// ascending order, first value whose running total reaches <c>ceil(n / 2.0)</c>), not a mean —
    /// a mean is pulled around by one busy evening. <c>ceil(n / 2.0)</c> rather than <c>(n + 1) / 2</c>
    /// because <c>sum()</c> over bigint returns exact <c>numeric</c>, so an even sample count needs the
    /// element after the true midpoint; this is pinned by equality with <c>percentile_disc</c>.
    /// </para>
    /// <para>
    /// A rolled-up day with no distribution is excluded from all three figures, not just one, so the
    /// printed tally matches what the median was taken over. A NULL count is excluded rather than read
    /// as a zero (rule 4) — an unparseable probe contributes to neither sum nor tally.
    /// </para>
    /// <para>
    /// The window's far end is snapped back to a whole UTC day, since the rollup buckets by day and a
    /// partial day can't be read — reading a few hours extra is preferred over discarding a day's
    /// evidence to make the label exact.
    /// </para>
    /// </remarks>
    private static async Task<Dictionary<Guid, PresenceWindow>> PlayersOverWindowAsync(
        NpgsqlConnection connection,
        Guid[] ids,
        TimeSpan window,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<WindowRow>(new CommandDefinition(
            """
            WITH boundary AS (
                SELECT coalesce(
                    (SELECT rolled_up_through FROM presence_rollup_state WHERE scope = 'day'),
                    '-infinity'::timestamptz) AS at
            ),
            -- Truncated in UTC on both sides of the wire, like every other bucket boundary here, so
            -- a session's TimeZone setting can never move the window's far end by a day.
            span AS (
                SELECT date_trunc('day', @from AT TIME ZONE 'UTC') AT TIME ZONE 'UTC' AS from_at
            ),
            -- One frequency table over both halves, so the median, tally and peak all describe the
            -- same set of probes.
            frequency AS (
                SELECT p.game_id, p.count AS value, count(*)::bigint AS times
                  FROM presence_sample p
                 WHERE p.game_id = ANY(@ids)
                   AND p.count IS NOT NULL
                   AND p.at >= (SELECT from_at FROM span)
                   AND p.at >= (SELECT at FROM boundary)
                 GROUP BY 1, 2

                UNION ALL

                SELECT r.game_id, e.key::int, sum(e.value::bigint)
                  FROM presence_rollup_day r
                  CROSS JOIN LATERAL jsonb_each_text(r.count_histogram) AS e(key, value)
                 WHERE r.game_id = ANY(@ids)
                   AND r.count_histogram IS NOT NULL
                   AND r.day >= (SELECT from_at FROM span)
                   AND r.day < (SELECT at FROM boundary)
                 GROUP BY 1, 2
            ),
            counted AS (
                SELECT game_id, value, sum(times) AS times
                  FROM frequency
                 GROUP BY 1, 2
            ),
            walked AS (
                SELECT game_id, value,
                       sum(times) OVER (PARTITION BY game_id ORDER BY value) AS running,
                       ceil(sum(times) OVER (PARTITION BY game_id) / 2.0)    AS half,
                       sum(times) OVER (PARTITION BY game_id)                AS samples,
                       max(value)  OVER (PARTITION BY game_id)               AS peak
                  FROM counted
            )
            SELECT game_id        AS GameId,
                   min(value)::int AS Median,
                   max(peak)::int  AS Peak,
                   max(samples)::int AS Samples
              FROM walked
             WHERE running >= half
             GROUP BY 1
            """,
            new { ids, from = (now - window).ToUniversalTime() },
            cancellationToken: cancellationToken));

        return rows.ToDictionary(
            r => r.GameId,
            r => new PresenceWindow(window, r.Median, r.Peak, r.Samples));
    }

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

    /// <summary>
    /// §9's referral neighbours, in both directions, resolved to games we can name.
    /// </summary>
    /// <remarks>
    /// Both directions come back in one pass with a <c>direction</c> column, same join read from both
    /// ends, so the two can't drift apart. <see cref="PublicG"/> applies here too — being named by
    /// somebody else's referral must not be a way onto a page the listing itself refuses. Deduplicated
    /// in code rather than <c>DISTINCT ON</c>, which can't see an output alias and would tie the query
    /// to the presentation's <c>ORDER BY</c>.
    /// </remarks>
    private static async Task<IReadOnlyList<ReferralNeighbour>> NeighboursAsync(
        NpgsqlConnection connection,
        Guid gameId,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<NeighbourRow>(new CommandDefinition(
            $"""
            SELECT g.slug AS Slug, g.name AS Name, e.to_host AS Host, e.to_port AS Port,
                   'lists' AS Direction, e.first_seen_at AS FirstSeenAt,
                   e.last_seen_at AS LastSeenAt, e.present AS Present
              FROM referral_edge e
              JOIN game_endpoint ep ON ep.host = e.to_host AND ep.port = e.to_port
              JOIN game g ON g.id = ep.game_id
             WHERE e.from_game_id = @gameId AND g.id <> @gameId AND {PublicG}
               AND g.state NOT IN {NeverBrowsable}

            UNION ALL

            SELECT g.slug, g.name, e.to_host, e.to_port,
                   'listed-by', e.first_seen_at, e.last_seen_at, e.present
              FROM game_endpoint ep
              JOIN referral_edge e ON e.to_host = ep.host AND e.to_port = ep.port
              JOIN game g ON g.id = e.from_game_id
             WHERE ep.game_id = @gameId AND g.id <> @gameId AND {PublicG}
               AND g.state NOT IN {NeverBrowsable}
            """,
            new { gameId },
            cancellationToken: cancellationToken));

        return [.. rows
            .DistinctBy(r => (r.Direction, r.Slug))
            .Select(r => new ReferralNeighbour(
                r.Slug,
                r.Name,
                r.Host,
                r.Port,
                r.Direction == "lists" ? ReferralDirection.Lists : ReferralDirection.ListedBy,
                new DateTimeOffset(DateTime.SpecifyKind(r.FirstSeenAt, DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(r.LastSeenAt, DateTimeKind.Utc)),
                r.Present))
            .OrderByDescending(n => n.Present)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The one projection of <c>game</c> every read of a single game starts from.
    /// </summary>
    /// <remarks>
    /// Written once so the three keys a game answers to (spec §5.7) cannot come back as three
    /// different rows. <see cref="Public"/> is baked in rather than appended per call site, so a
    /// lookup added on top of this projection cannot forget it.
    /// </remarks>
    private const string GameSelect =
        $"""
        SELECT id AS Id, slug AS Slug, name AS Name, tagline AS Tagline, state AS State,
               is_claimed AS IsClaimed, last_reachable_at AS LastReachableAt,
               excluded_reason AS ExcludedReason, first_seen_at AS FirstSeenAt
          FROM game
         WHERE {Public}
        """;

    public async Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<GameRow>(new CommandDefinition(
            GameSelect + " AND id = @id",
            new { id },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        var now = Clock();

        Guid[] ids = [row.Id];
        var fields = (await FieldsForAsync(connection, ids, cancellationToken))
            .GetValueOrDefault(row.Id, []);
        var digest = (await PresenceDigestAsync(connection, ids, now, cancellationToken))
            .GetValueOrDefault(row.Id, PresenceDigest.None);
        var codebase = Winner(fields, "CODEBASE");

        return new GameSummary(
            row.Id,
            row.Slug,
            row.Name,
            row.Tagline,
            SqlEnums.ToLifecycleState(row.State),
            row.IsClaimed,
            digest.CountNow,
            codebase?.Value,
            MeasuredProtocolsOf(fields),
            row.LastReachableAt,
            CountChip(digest, now),
            Chip(codebase, now),
            (await IconsForAsync(connection, ids, cancellationToken)).Contains(row.Id),
            FirstSeenAt: row.FirstSeenAt);
    }

    public async Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<GameRow>(new CommandDefinition(
            GameSelect + " AND slug = @slug",
            new { slug },
            cancellationToken: cancellationToken));

        return row is null ? null : await PageAsync(connection, row, cancellationToken);
    }

    /// <summary>
    /// The same page, found by the identifier that never moves.
    /// </summary>
    /// <remarks>
    /// One read of <c>game</c> and then the page, exactly as the slug route does — the two differ in
    /// their <c>WHERE</c> clause and in nothing else. Both columns are unique-indexed, so neither
    /// key is the expensive one, which is the property the API's advice to store the id depends on.
    /// </remarks>
    public async Task<GamePage?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<GameRow>(new CommandDefinition(
            GameSelect + " AND id = @id",
            new { id },
            cancellationToken: cancellationToken));

        return row is null ? null : await PageAsync(connection, row, cancellationToken);
    }

    /// <summary>Everything a game page is, assembled from a row whoever found it already has.</summary>
    private async Task<GamePage> PageAsync(
        NpgsqlConnection connection,
        GameRow row,
        CancellationToken cancellationToken)
    {
        var now = Clock();

        Guid[] ids = [row.Id];
        var fields = (await FieldsForAsync(connection, ids, cancellationToken))
            .GetValueOrDefault(row.Id, []);
        var digest = (await PresenceDigestAsync(connection, ids, now, cancellationToken))
            .GetValueOrDefault(row.Id, PresenceDigest.None);

        var intervals = await new NpgsqlAvailabilityStore(source).ForGameAsync(row.Id, cancellationToken);
        var endpoints = await new NpgsqlEndpointStore(source).ForGameAsync(row.Id, cancellationToken);
        var neighbours = await NeighboursAsync(connection, row.Id, cancellationToken);
        var changes = await new NpgsqlGameFieldStore(source)
            .ChangesAsync(row.Id, ChangeLimit, ChangePerFieldLimit, cancellationToken);
        var activity = await ActivityAsync(connection, row.Id, now, cancellationToken);

        var codebase = Winner(fields, "CODEBASE");

        var summary = new GameSummary(
            row.Id,
            row.Slug,
            row.Name,
            row.Tagline,
            SqlEnums.ToLifecycleState(row.State),
            row.IsClaimed,
            digest.CountNow,
            codebase?.Value,
            MeasuredProtocolsOf(fields),
            row.LastReachableAt,
            CountChip(digest, now),
            Chip(codebase, now),
            (await IconsForAsync(connection, ids, cancellationToken)).Contains(row.Id),
            FirstSeenAt: row.FirstSeenAt);

        return new GamePage(
            summary,
            Description: Winner(fields, "DESCRIPTION")?.Value,
            Endpoints: endpoints
                .Select(e => new GameEndpointView(
                    e.Host,
                    e.Port,
                    SqlEnums.ToDb(e.Kind),
                    TlsMeasured: e.Kind is EndpointKind.Tls,
                    e.FirstSeenAt,
                    e.LastSeenAt,
                    SqlEnums.ToDb(e.State)))
                .ToList(),
            ConnectScreen: Winner(fields, InternalFields.ConnectScreen)?.Value,
            ConnectScreenSuppressed: string.Equals(
                Winner(fields, InternalFields.ConnectScreenSuppressed)?.Value,
                "true",
                StringComparison.Ordinal),
            ReachableFraction: Reachability.FractionReachable(intervals, RecentlyReachable * 3, now),
            LongestOutage: Reachability.LongestOutage(intervals, RecentlyReachable * 3, now),
            Capabilities: CapabilitiesOf(fields),
            Activity: activity,
            Declared: DeclaredOf(fields, now),
            Changes: changes.Select(Describe).ToList(),
            Neighbours: neighbours,
            ConnectScreenCharset: ScreenCharset(fields),
            ExcludedReason: row.ExcludedReason,
            Reachable: QuickLinks.From(fields, _registry, now));
    }

    /// <summary>
    /// The encoding the connect screen was read with, or null when nothing needed saying.
    /// </summary>
    /// <remarks>
    /// Read from <c>charset.read</c> only, never the operator's <c>CHARSET</c> override — those are
    /// different facts, and an override naming an encoding this runtime doesn't have (which
    /// <c>WireEncoding.Override</c> ignores) would otherwise caption a screen with an encoding that
    /// was never actually applied. UTF-8 is suppressed since it's the ordinary case, not worth a
    /// caption.
    /// </remarks>
    private static string? ScreenCharset(IReadOnlyList<GameField> fields)
    {
        var charset = Winner(fields, InternalFields.CharsetRead)?.Value;

        return string.IsNullOrWhiteSpace(charset)
            || string.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase)
                ? null
                : charset;
    }

    public async Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default)
    {
        var now = Clock();
        var since = now - RecentlyReachable;

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // §9's three liveness feeds.
        var discovered = await connection.QueryAsync<FeedRow>(new CommandDefinition(
            $"""
            SELECT id AS Id, slug AS Slug, name AS Name, first_seen_at AS At, NULL AS Cause
              FROM game
             WHERE first_seen_at >= @since AND {Public}
               AND state NOT IN {NeverBrowsable}
             ORDER BY first_seen_at DESC
             LIMIT @limit
            """,
            new { since = since.ToUniversalTime(), limit = FeedLimit },
            cancellationToken: cancellationToken));

        var wentDark = await connection.QueryAsync<FeedRow>(new CommandDefinition(
            $"""
            SELECT g.id AS Id, g.slug AS Slug, g.name AS Name, a.from_at AS At, a.cause AS Cause
              FROM availability_interval a
              JOIN game g ON g.id = a.game_id
             WHERE a.to_at IS NULL AND a.state = 'unreachable' AND a.from_at >= @since
               AND {PublicG} AND g.state NOT IN {NeverBrowsable}
             ORDER BY a.from_at DESC
             LIMIT @limit
            """,
            new { since = since.ToUniversalTime(), limit = FeedLimit },
            cancellationToken: cancellationToken));

        // A game "came back" when a reachable interval opens exactly where an unreachable one closed.
        // That join is the whole reason availability is stored as intervals: on a sample series this
        // would be a scan for a transition that nothing recorded.
        var cameBack = await connection.QueryAsync<FeedRow>(new CommandDefinition(
            $"""
            SELECT g.id AS Id, g.slug AS Slug, g.name AS Name, a.from_at AS At, prev.cause AS Cause
              FROM availability_interval a
              JOIN game g ON g.id = a.game_id
              JOIN availability_interval prev
                ON prev.game_id = a.game_id AND prev.to_at = a.from_at AND prev.state <> 'reachable'
             WHERE a.state = 'reachable' AND a.from_at >= @since
               AND {PublicG} AND g.state NOT IN {NeverBrowsable}
             ORDER BY a.from_at DESC
             LIMIT @limit
            """,
            new { since = since.ToUniversalTime(), limit = FeedLimit },
            cancellationToken: cancellationToken));

        // The detail is only what the heading doesn't already say — a cause is a measurement and
        // stays, but restating "newly discovered" or "came back" in every row would be noise
        // (including for a screen reader).
        return new LivenessFeeds(
            discovered.Select(r => new FeedEntry(r.Id, r.Slug, r.Name, r.At, string.Empty)).ToList(),
            wentDark.Select(r => new FeedEntry(
                r.Id, r.Slug, r.Name, r.At, r.Cause ?? "unknown")).ToList(),
            cameBack.Select(r => new FeedEntry(r.Id, r.Slug, r.Name, r.At, string.Empty)).ToList());
    }

    /// <summary>The crawler status page's "recently updated" — the newest field transitions site-wide.</summary>
    /// <remarks>
    /// Ranked with a per-game cap before the overall limit, same two-stage shape
    /// <c>NpgsqlGameFieldStore.ChangesAsync</c> uses per field on one game's own page — a game whose
    /// <c>NAME</c> or <c>PLAYERNAMES</c> flaps every crawl still contributes one row per genuine
    /// transition, but cannot crowd the rest of the catalogue off a page meant to show what the
    /// instrument has been doing broadly.
    /// </remarks>
    public async Task<IReadOnlyList<RecentGameChange>> RecentFieldChangesAsync(
        int limit, int perGameLimit = 3, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<RecentChangeRow>(new CommandDefinition(
            $"""
            WITH ranked AS (
                SELECT c.game_id, g.slug, g.name, c.field, c.source, c.old_value, c.new_value, c.at, c.id,
                       ROW_NUMBER() OVER (PARTITION BY c.game_id ORDER BY c.at DESC, c.id DESC) AS rn
                  FROM field_change c
                  JOIN game g ON g.id = c.game_id
                 WHERE NOT (c.field = ANY(@internal) OR c.field LIKE @internalPrefix)
                   AND {PublicG} AND g.state NOT IN {NeverBrowsable}
            )
            SELECT game_id AS GameId, slug AS Slug, name AS Name, field AS Field, source AS Source,
                   old_value AS OldValue, new_value AS NewValue, at AS At
              FROM ranked
             WHERE rn <= @perGameLimit
             ORDER BY at DESC, id DESC
             LIMIT @limit
            """,
            new
            {
                limit,
                perGameLimit,
                @internal = InternalFields.ExactNames.ToArray(),
                internalPrefix = InternalFields.ConnectScreen + "%",
            },
            cancellationToken: cancellationToken));

        return [.. rows.Select(r => new RecentGameChange(
            r.GameId, r.Slug, r.Name, r.Field, SqlEnums.ToFieldSource(r.Source),
            r.OldValue, r.NewValue, r.At))];
    }

    private sealed class RecentChangeRow
    {
        public Guid GameId { get; init; }

        public string Slug { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string Field { get; init; } = string.Empty;

        public string Source { get; init; } = string.Empty;

        public string? OldValue { get; init; }

        public string NewValue { get; init; } = string.Empty;

        public DateTimeOffset At { get; init; }
    }

    /// <summary>
    /// Codebase share and protocol adoption over the listed games (spec §9).
    /// </summary>
    /// <remarks>
    /// Every figure is a count of <em>games</em>; this method has no access to presence at all, so a
    /// player total can never be computed here (§15.7 forbids publishing one). The protocol
    /// denominator is games with a completed session (<c>reachable</c> interval), read off
    /// availability rather than off capability rows — counting games that merely have capability rows
    /// would let a handshake that measured nothing quietly raise every share. Archived games are
    /// excluded (§7.5): a handshake from 2019 isn't evidence about what the hobby runs now.
    /// </remarks>
    public async Task<EcosystemDashboard> EcosystemAsync(CancellationToken cancellationToken = default)
    {
        var now = Clock();

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var totals = await connection.QuerySingleAsync<EcosystemTotalsRow>(new CommandDefinition(
            $"""
            SELECT
              (SELECT count(*)::int FROM game WHERE state NOT IN {ListedStates} AND {Public}) AS Listed,

              -- A completed session, which is what a measured capability is a capability of.
              (SELECT count(DISTINCT a.game_id)::int
                 FROM availability_interval a
                 JOIN game g ON g.id = a.game_id
                WHERE {PublicG} AND g.state NOT IN {ListedStates} AND a.state = 'reachable') AS Handshakes,

              -- Games whose MSSP report we hold — a different set, hence its own denominator.
              (SELECT count(DISTINCT f.game_id)::int
                 FROM game_field f
                 JOIN game g ON g.id = f.game_id
                WHERE {PublicG} AND g.state NOT IN {ListedStates} AND f.source = 'mssp') AS MsspReports,

              -- How stale the stalest handshake is, so the page can say how old the picture is.
              (SELECT min(f.last_confirmed_at)
                 FROM game_field f
                 JOIN game g ON g.id = f.game_id
                WHERE {PublicG} AND g.state NOT IN {ListedStates} AND f.source = 'handshake'
                  AND f.field LIKE 'capability.%.measured') AS OldestHandshake,

              -- The raw material of the curve this page cannot yet draw (§5.1's change ledger).
              (SELECT count(*)::int
                 FROM field_change c
                 JOIN game g ON g.id = c.game_id
                WHERE {PublicG} AND g.state NOT IN {ListedStates}
                  AND c.field LIKE 'capability.%.measured') AS CapabilityTransitions
            """,
            cancellationToken: cancellationToken));

        var codebases = (await connection.QueryAsync<string>(new CommandDefinition(
            $"""
            SELECT DISTINCT ON (f.game_id) f.value
              FROM game_field f
              JOIN game g ON g.id = f.game_id
             WHERE {PublicG} AND g.state NOT IN {ListedStates} AND f.field = 'CODEBASE' AND f.value <> ''
             ORDER BY f.game_id, array_position(@ladder::text[], f.source), f.last_confirmed_at DESC
            """,
            new { ladder = SourceLadder },
            cancellationToken: cancellationToken))).ToList();

        var capabilities = (await connection.QueryAsync<CapabilityTallyRow>(new CommandDefinition(
            $"""
            SELECT winner.field AS Field, winner.value AS Value, count(*)::int AS Games
              FROM (SELECT DISTINCT ON (f.game_id, f.field) f.field, f.value
                      FROM game_field f
                      JOIN game g ON g.id = f.game_id
                     WHERE {PublicG} AND g.state NOT IN {ListedStates} AND f.field LIKE 'capability.%'
                     ORDER BY f.game_id, f.field,
                              array_position(@ladder::text[], f.source), f.last_confirmed_at DESC) winner
             GROUP BY winner.field, winner.value
            """,
            new { ladder = SourceLadder },
            cancellationToken: cancellationToken))).ToList();

        return new EcosystemDashboard(
            now,
            totals.Listed,
            totals.Handshakes,
            totals.MsspReports,
            totals.OldestHandshake,
            totals.CapabilityTransitions,
            CodebaseUsage.Of(codebases, totals.Listed),
            ProtocolsOf(capabilities, totals.Handshakes, totals.MsspReports));
    }

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
        var now = Clock();

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
                .Where(row => GrowthTrend.Of(row.Days) == GrowthDirection.Up)
                .Select(row => new TrendingGame(
                    row.Game.Slug, row.Game.Name,
                    EarliestMedian: row.Days.MinBy(d => d.Day)!.Median,
                    LatestMedian: row.Days.MaxBy(d => d.Day)!.Median,
                    Change: GrowthTrend.ChangeFraction(row.Days)!.Value))
                .OrderByDescending(r => r.Change)
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

    /// <summary>
    /// One row per capability worth reporting, measured beside declared.
    /// </summary>
    /// <remarks>
    /// A capability nothing offered is reported as <b>unmeasured</b>, never nought per cent — derived
    /// from the tally, not compiled in, so a column starts reporting a share the day the first
    /// measurement lands (TLS is the standing example: the probe dials plaintext). §9's headline four
    /// are always listed, even unmeasured, since "not measured yet" is only visible if the row exists.
    /// </remarks>
    private static IReadOnlyList<ProtocolAdoption> ProtocolsOf(
        IReadOnlyList<CapabilityTallyRow> tallies,
        int handshakes,
        int msspReports)
    {
        var offered = new Dictionary<string, int>(StringComparer.Ordinal);
        var declined = new Dictionary<string, int>(StringComparer.Ordinal);
        var declared = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var tally in tallies)
        {
            if (CapabilityFields.NameOf(tally.Field) is not { } name)
            {
                continue;
            }

            var bucket = (CapabilityFields.IsMeasured(tally.Field), tally.Value) switch
            {
                (true, "true") => offered,
                (true, "false") => declined,
                (false, "true") => declared,
                _ => null,
            };

            if (bucket is null)
            {
                continue;
            }

            bucket[name] = bucket.GetValueOrDefault(name) + tally.Games;
        }

        // A protocol observed at all (either direction) gets a measurable column; one never observed
        // gets none — "0%" would be our own reach reported as the hobby's.
        var measurable = offered.Keys.Concat(declined.Keys).ToHashSet(StringComparer.Ordinal);

        var names = EcosystemProtocols.Headline
            .Concat(measurable.Concat(declared.Keys).Order(StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return names
            .Select(name => new ProtocolAdoption(
                name,
                measurable.Contains(name) ? offered.GetValueOrDefault(name) : null,
                declined.GetValueOrDefault(name),
                handshakes,
                declared.GetValueOrDefault(name),
                msspReports))
            .ToList();
    }

    /// <summary>
    /// One change as a sentence. An emptied value is <em>cleared</em>, and still an event.
    /// </summary>
    /// <remarks>
    /// Rendering the empty string as itself would print "FANDOM changed from Exalted to" and lose
    /// the event in the punctuation.
    /// </remarks>
    private static ChangeEntry Describe(FieldChange change) => new(
        change.At,
        change.OldValue is null
            ? $"{change.Field} recorded as {Spell(change.NewValue)} ({SqlEnums.ToDb(change.Source)})"
            : $"{change.Field} changed from {Spell(change.OldValue)} to {Spell(change.NewValue)} "
                + $"({SqlEnums.ToDb(change.Source)})");

    private static string Spell(string value) => value.Length == 0 ? "nothing" : value;

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
    /// Measured beside declared, never merged into one badge. A capability with no row on a side is
    /// <see cref="CapabilityState.Unknown"/> and renders as a dash — nothing said either way is not
    /// the same fact as "it is not there", and only one of them is a measurement.
    /// </summary>
    private static IReadOnlyList<CapabilityRow> CapabilitiesOf(IReadOnlyList<GameField> fields)
    {
        var rows = new List<CapabilityRow>();

        foreach (var capability in CapabilityFields.Names)
        {
            var measured = Winner(fields, CapabilityFields.Measured(capability));
            var declared = Winner(fields, CapabilityFields.Declared(capability));

            if (measured is null && declared is null)
            {
                continue;
            }

            DateTimeOffset? confirmed = (measured, declared) switch
            {
                ({ } m, { } d) => m.LastConfirmedAt > d.LastConfirmedAt ? m.LastConfirmedAt : d.LastConfirmedAt,
                ({ } m, null) => m.LastConfirmedAt,
                (null, { } d) => d.LastConfirmedAt,
                _ => null,
            };

            rows.Add(new CapabilityRow(capability, StateOf(measured), StateOf(declared), confirmed));
        }

        return rows;

        static CapabilityState StateOf(GameField? field) => field?.Value switch
        {
            "true" => CapabilityState.Present,
            "false" => CapabilityState.Absent,
            _ => CapabilityState.Unknown,
        };
    }

    private IReadOnlyDictionary<string, ProvenanceChip> DeclaredOf(
        IReadOnlyList<GameField> fields,
        DateTimeOffset now)
    {
        var chips = new Dictionary<string, ProvenanceChip>(StringComparer.Ordinal);

        foreach (var group in fields
            .Where(f => !f.Field.StartsWith(CapabilityFields.Prefix, StringComparison.Ordinal)
                && !InternalFields.IsInternal(f.Field)

                // A cleared field is a row with an empty value, not a missing row — filtered HERE,
                // before the ladder, because an absence must not win. Filtering after the winner was
                // chosen let a cleared `owner` row (which outranks `mssp`) silently drop the whole
                // group instead of exposing what was underneath — an owner editing a measurement by
                // the back door, which §8.5 forbids.
                && f.Value.Length > 0)
            .GroupBy(f => f.Field, StringComparer.Ordinal))
        {
            if (FieldPrecedence.Winner(group) is not { } winner)
            {
                continue;
            }

            chips[winner.Field.ToLowerInvariant()] = Chip(winner, now)!;
        }

        return chips;
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
            SELECT g.id AS GameId, recent.count AS CountNow, recent.at AS CountedAt,
                   recent.source AS CountSource, coalesce(week.nonzero, 0) AS NonZeroThisWeek,
                   coalesce(week.counted, 0) AS CountedThisWeek,
                   coalesce(week.uncountable, 0) AS UncountableThisWeek
              FROM unnest(@ids::uuid[]) AS g(id)
              LEFT JOIN LATERAL (
                   SELECT p.count, p.at, p.source
                     FROM presence_sample p
                    WHERE p.game_id = g.id AND p.at >= @nowFrom AND p.count IS NOT NULL
                    ORDER BY p.at DESC
                    LIMIT 1) recent ON true

              -- Three tallies, one scan: `count(p.count)` includes a measured nought;
              -- `count(*) FILTER (count IS NULL)` is answered-but-unreadable. A row exists only
              -- where a probe got far enough to try, so no tally here speaks for an hour we never
              -- measured (§5.4's third state, which names no cause).
              LEFT JOIN LATERAL (
                   SELECT count(*) FILTER (WHERE p.count > 0) AS nonzero,
                          count(p.count) AS counted,
                          count(*) FILTER (WHERE p.count IS NULL) AS uncountable
                     FROM presence_sample p
                    WHERE p.game_id = g.id AND p.at >= @weekFrom) week ON true
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
    /// The day-of-week × hour grid, in the three states an hour can be in (spec §5.4). A cell with
    /// samples but no counts is <em>probed and uncountable</em> — hatched, not empty — and a cell
    /// with no samples at all is the gap that means we could not reach the game.
    /// </summary>
    private static async Task<IReadOnlyList<ActivityCell>> ActivityAsync(
        NpgsqlConnection connection,
        Guid gameId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<ActivityRow>(new CommandDefinition(
            """
            WITH boundary AS (
                -- Where the raw table stops answering: below the watermark, retention may have
                -- dropped raw partitions (§5.2), so read the rollup; above it only raw rows exist
                -- (the rollup only consumes whole elapsed hours). No watermark means nothing has
                -- been rolled up yet, so -infinity reads raw for the whole window.
                SELECT coalesce(
                    (SELECT rolled_up_through FROM presence_rollup_state WHERE scope = 'hour'),
                    '-infinity'::timestamptz) AS at
            ),
            parts AS (
                SELECT extract(dow FROM at AT TIME ZONE 'UTC')::int AS day,
                       extract(hour FROM at AT TIME ZONE 'UTC')::int AS hour,
                       count(*)::bigint AS samples,
                       count(count)::bigint AS counted,
                       sum(count)::bigint AS total
                  FROM presence_sample
                 WHERE game_id = @gameId
                   AND at >= @from
                   AND at >= (SELECT at FROM boundary)
                 GROUP BY 1, 2

                UNION ALL

                -- The tally is summed, never averaged: a mean of hourly means would weight an hour
                -- probed once the same as an hour probed twelve times.
                SELECT extract(dow FROM r.hour AT TIME ZONE 'UTC')::int,
                       extract(hour FROM r.hour AT TIME ZONE 'UTC')::int,
                       sum(r.counted_samples + r.unmeasurable_samples)::bigint,
                       sum(r.counted_samples)::bigint,
                       sum(r.sum_count)::bigint
                  FROM presence_rollup_hour r
                 WHERE r.game_id = @gameId
                   AND r.hour >= @from
                   AND r.hour < (SELECT at FROM boundary)
                 GROUP BY 1, 2
            )
            SELECT day AS Day,
                   hour AS Hour,
                   sum(samples)::int AS Samples,
                   sum(counted)::int AS Counted,
                   CASE WHEN sum(counted) > 0
                        THEN sum(total)::float8 / sum(counted)
                   END AS Mean
              FROM parts
             GROUP BY 1, 2
            """,
            new { gameId, from = (now - ActivityWindow).ToUniversalTime() },
            cancellationToken: cancellationToken));

        var byCell = rows.ToDictionary(r => (r.Day, r.Hour));
        var cells = new List<ActivityCell>(7 * 24);

        for (var day = 0; day < 7; day++)
        {
            for (var hour = 0; hour < 24; hour++)
            {
                if (!byCell.TryGetValue((day, hour), out var row) || row.Samples == 0)
                {
                    cells.Add(new ActivityCell(day, hour, null, Probed: false));
                    continue;
                }

                cells.Add(new ActivityCell(
                    day,
                    hour,
                    row.Counted > 0 && row.Mean is { } mean ? (int)Math.Round(mean) : null,
                    Probed: true));
            }
        }

        return cells;
    }

    private static ActivityBand BandOf(
        LifecycleState state,
        DateTimeOffset? lastReachableAt,
        PresenceDigest digest,
        DateTimeOffset now)
    {
        if (state is LifecycleState.Archived)
        {
            return ActivityBand.Archived;
        }

        if (digest.CountNow > 0)
        {
            return ActivityBand.PlayersNow;
        }

        if (digest.NonZeroThisWeek)
        {
            return ActivityBand.ActiveThisWeek;
        }

        // Reachable recently but nobody counted, including a game every count of which was
        // unmeasurable — quiet, never dark. This band can't say which of the two it is; `Uncounted`
        // is its own facet for exactly that reason.
        return FacetedSearch.NotReachedRecently(lastReachableAt, now)
            ? ActivityBand.Dark
            : ActivityBand.Quiet;
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

    /// <summary>A referral edge with the game at its far end, as the query returns it.</summary>
    private sealed record NeighbourRow(
        string Slug,
        string Name,
        string Host,
        int Port,
        string Direction,

        // DateTime rather than DateTimeOffset: Npgsql hands a timestamptz back as a UTC DateTime,
        // and Dapper matches a record's constructor by exact parameter type.
        DateTime FirstSeenAt,
        DateTime LastSeenAt,
        bool Present);

    /// <summary>One game's window figures as the walk returns them.</summary>
    private sealed class WindowRow
    {
        public Guid GameId { get; init; }

        public int Median { get; init; }

        public int Peak { get; init; }

        public int Samples { get; init; }
    }

    private sealed class ActivityRow
    {
        public int Day { get; init; }

        public int Hour { get; init; }

        public int Samples { get; init; }

        public int Counted { get; init; }

        public double? Mean { get; init; }
    }

    private sealed class EcosystemTotalsRow
    {
        public int Listed { get; init; }

        public int Handshakes { get; init; }

        public int MsspReports { get; init; }

        public DateTimeOffset? OldestHandshake { get; init; }

        public int CapabilityTransitions { get; init; }
    }

    private sealed class CapabilityTallyRow
    {
        public string Field { get; init; } = string.Empty;

        public string Value { get; init; } = string.Empty;

        public int Games { get; init; }
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

    private sealed class FeedRow
    {
        public Guid Id { get; init; }

        public string Slug { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public DateTimeOffset At { get; init; }

        public string? Cause { get; init; }
    }
}
