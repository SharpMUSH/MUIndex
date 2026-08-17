using System.Globalization;

using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// <see cref="IGameQueries"/> against PostgreSQL — the read side the site was built against a
/// fixture to wait for.
/// </summary>
/// <remarks>
/// <para>
/// Returns view models rather than rows, for the same reason the plain-text surface exists: if a page
/// had to assemble a fact from three tables, the plain renderer would have to repeat that assembly
/// and the two would drift.
/// </para>
/// <para>
/// The §5.1 precedence ladder is <b>not</b> rewritten in SQL here. Rows come back per source and
/// <see cref="FieldPrecedence.Winner"/> picks, so the ladder has exactly one spelling — the declared
/// order of <see cref="FieldSource"/> — and a `CASE source WHEN …` in a query cannot drift from it.
/// </para>
/// <para>
/// The two aggregate reads are the one exception, and they keep the rule while breaking the shape:
/// the ecosystem dashboard resolves a winner per <c>(game, field)</c> across the whole catalogue, and
/// dragging every capability row of every game into memory to do it would be a scan for a page. They
/// use <c>DISTINCT ON … ORDER BY array_position(@ladder, source)</c> instead — where
/// <see cref="SourceLadder"/> is generated from the enum's declared order, so the ladder still has
/// exactly one spelling and a hand-written <c>CASE</c> still cannot drift from it.
/// </para>
/// </remarks>
public sealed class NpgsqlGameQueries(NpgsqlDataSource source, IFieldRegistry? registry = null)
    : IGameQueries
{
    /// <summary>
    /// Whether a game is offered as a game of its own on a public surface — vouched for (spec §8,
    /// migration 0010) and not absorbed by a merge (spec §7.3, migration 0018).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A game is public if nobody submitted it, if it has been claimed, or if a probe has shown
    /// it to be a game.</b> Anything the crawler found for itself is listed on sight exactly as §7.1
    /// says; an address a stranger handed us waits until the server itself corroborates it (§7.8,
    /// migration 0022) or somebody proves they run it.
    /// </para>
    /// <para>
    /// <b>The third clause is not a loosening of the second, it is the second one's missing half.</b>
    /// Waiting for a claim meant waiting for an operator who had no reason to know this site exists,
    /// and it was measured: of 432 games, exactly one was excluded by this rule, having answered
    /// every probe for a fortnight with its engine's name and sixty-seven connected players.
    /// </para>
    /// <para>
    /// <b>And a game absorbed by a merge in force is not offered separately.</b> That is the whole
    /// public effect of a merge: nothing moves between the two games, the absorbed one keeps every row
    /// it ever had, and the reads stop presenting it as a second game. Reverting clears the pointer
    /// and the listing has it back, with no rows to carry either way.
    /// </para>
    /// <para>
    /// It is one constant because it has to hold on <em>every</em> read, and the count of reads is
    /// larger than it looks: the listing, the faceted search, all three liveness feeds, both halves of
    /// the rankings, six separate subqueries behind the ecosystem dashboard, and both lookups. The
    /// first cut of the submission half covered the six queries that name <c>game</c> directly and
    /// missed every one that reaches it through <c>JOIN game g</c> — so an unclaimed submission stayed
    /// off the listing and turned up in the rankings. A predicate written out per query is a predicate
    /// that will be forgotten on the next query somebody adds, and the failure mode is a game on a
    /// public page that nobody vouched for. <b>The merge half is composed in here rather than added at
    /// call sites for exactly that reason</b> — its failure mode is the same shape, a duplicate the
    /// listing hides and the rankings show.
    /// </para>
    /// </remarks>
    private const string Public =
        $"((submitted_at IS NULL OR is_claimed OR corroborated_at IS NOT NULL) AND {NotAbsorbed})";

    /// <summary>
    /// The states a game is counted and listed in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three states withhold a game from the listing and no two of them are the same statement.</b>
    /// <c>archived</c> says it stopped answering and is reversed by the next probe that gets an
    /// answer; <c>excluded</c> says we decided it is not a game for players and is reversed only by a
    /// person; <c>unlisted</c> says the people who run it asked to come out, and is reversed by
    /// either of them (§11). All three keep the page, the URL, the history and the crawl. Named once
    /// here so that a count added later cannot quietly include one of them — the same reasoning as
    /// <c>Public</c> below, which arrived after a lookup had already forgotten it once.
    /// </para>
    /// <para>
    /// <b>It was named once and then written out eleven times</b>, which is how a constant fails at
    /// the job it was added for. Every predicate now reads it, so the state added next is added here.
    /// </para>
    /// </remarks>
    private const string ListedStates = "('archived', 'excluded', 'unlisted')";

    /// <summary>
    /// The states no browsing surface may reach, whatever it is asking about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>`archived` is deliberately not in here, and that is the whole distinction.</b> The archive
    /// is a browsable section in its own right (§7.5) with its own facet and its own feed entry when
    /// a game comes back. The other two are not sections: "we decided this is not a game somebody can
    /// play" and "the people who run it asked to come out" both mean *nothing that walks the site
    /// arrives here*. Only the address does.
    /// </para>
    /// <para>
    /// <b>It exists because the listing was not the only way in.</b> The liveness feeds and the
    /// referral neighbours both read <c>Public</c> alone, which says who vouched for a game and
    /// nothing about its lifecycle — so a game taken out of the listing went on being offered as a
    /// *newly discovered* entry and as a link on its neighbour's page. Excluding it from the listing
    /// and then linking to it from the listing's own pages is not excluding it.
    /// </para>
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

    /// <summary>§5.2's "reachable recently", which separates <c>quiet</c> from <c>dark</c>.</summary>
    public static readonly TimeSpan RecentlyReachable = TimeSpan.FromDays(30);

    /// <summary>§5.2's "active this week".</summary>
    public static readonly TimeSpan ThisWeek = TimeSpan.FromDays(7);

    /// <summary>
    /// The window the busiest ranking is measured over, and named on the page.
    /// </summary>
    /// <remarks>
    /// A week, because a MU* has a weekly shape — §5.2's heatmap is a day × hour grid for exactly that
    /// reason — and a ranking over anything shorter would rank Saturday's games above Tuesday's. It
    /// is the floor and the default rather than the only window: <see cref="RankingSpan"/> offers a
    /// month and a quarter beside it, which the day rollup's distribution made affordable.
    /// </remarks>
    public static readonly TimeSpan RankingWindow = RankingSpan.Week.Window();

    /// <summary>
    /// How many counted samples a game needs before it can be ranked.
    /// </summary>
    /// <remarks>
    /// A day's worth of hourly probes. A median over three samples is not a median, and a game found
    /// on Friday would otherwise take the top of the table off one lucky evening probe — which is
    /// ranking our crawl schedule rather than the game. It is <see cref="SortWindows.MinimumSamples"/>
    /// rather than a second literal, because the listing's average sorts put the same floor under the
    /// same kind of statistic and two constants would eventually disagree about how much evidence is
    /// enough.
    /// </remarks>
    public const int MinimumRankingSamples = SortWindows.MinimumSamples;

    private const int FeedLimit = 10;

    private const int ChangeLimit = 20;

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
    /// <para>
    /// The database narrows on the one thing that is not a facet — the archive toggle — and
    /// everything else is decided by <see cref="FacetedSearch"/> over <see cref="GameFacetRow"/>.
    /// That is deliberate rather than laziness about SQL: a facet count has to be measured against
    /// the same set the listing came from, and a <c>WHERE</c> clause that filtered here beside a
    /// <c>GROUP BY</c> that counted there would be two answers to one question. Sharing the
    /// arithmetic also means the demo fixture and this class cannot disagree about what a filter
    /// means, which they already did for <c>band=archived</c>.
    /// </para>
    /// <para>
    /// The cost is a pass over the unarchived catalogue and its fields per listing request — the
    /// same order as before, since <c>FieldsForAsync</c> already read every field of every listed
    /// game. The point at which that stops being affordable is aggregation in the database, and the
    /// counts would then need pinning against the listing rather than being the same arithmetic by
    /// construction.
    /// </para>
    /// </remarks>
    public async Task<GameListing> SearchAsync(
        GameFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var now = Clock();

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // Archived games leave the default listing and nothing else (spec §7.5) — and asking for the
        // archived band is asking for them, so it lifts the exclusion by itself. Without that the one
        // facet value naming the archive returned nothing at all, while the fixture returned the
        // archive: one filter, two answers, and only one of them was tested.
        //
        // **The toggle lifts `archived` and only `archived`**, which it did not until now: the
        // predicate read `@includeArchived OR state NOT IN (archived, excluded)`, so a reader ticking
        // "show me the archive" was also handed every stock `Your MUD Name` instance an editor had
        // ruled out. Each of the other two states answers a question the archive checkbox does not
        // ask — and for `unlisted` there is no checkbox to add, because a game that asked to come out
        // of the listing is not served by a control that puts it back in.
        var includeArchived = filter.IncludeArchived || filter.Band is ActivityBand.Archived;

        var rows = (await connection.QueryAsync<GameRow>(new CommandDefinition(
            $"""
            SELECT g.id AS Id, g.slug AS Slug, g.name AS Name, g.tagline AS Tagline,
                   g.state AS State, g.is_claimed AS IsClaimed, g.last_reachable_at AS LastReachableAt
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
                windows.GetValueOrDefault(row.Id));

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

                // Whichever source won each variable, since an owner correcting their own MSSP is
                // the case the field table exists for — an owner may add the flag their server
                // never sent, and the listing has to read the correction rather than the original.
                IsAdult: AdultContent.Declared(
                    genre, Winner(forGame, AdultContent.Field)?.Value)));
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
    /// Deliberately not the precedence winner. <c>CHARSET</c> is one of the few fields both a
    /// handshake and MSSP write, so the winner is the handshake's <em>when there is one</em> and
    /// silently the game's own assertion when there is not — which would make a facet advertised as
    /// measured answer from the declared column for every server that never negotiates, without
    /// saying so anywhere. Games with no measurement belong in the unknown bucket, which is a
    /// different answer and an honest one.
    /// </remarks>
    private static string? NegotiatedCharset(IReadOnlyList<GameField> fields) =>
        fields.FirstOrDefault(f =>
            string.Equals(f.Field, "CHARSET", StringComparison.Ordinal)
            && f.Source is FieldSource.Handshake)?.Value;

    /// <summary>
    /// The games we have completed a TLS connection to.
    /// </summary>
    /// <remarks>
    /// An endpoint row, not a capability claim. <c>capability.ssl.declared</c> exists and says only
    /// that somebody typed <c>SSL 4202</c> into their configuration; an endpoint of kind <c>tls</c>
    /// says a socket was opened. Nothing writes one yet — <c>CatalogueBinder</c> records what it
    /// dialled and the crawler dials plaintext — so this comes back empty and the facet does not
    /// render at all, which is the honest rendering of a measurement nobody has taken. It becomes a
    /// real facet the day the crawler takes it, with no change here.
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
    /// <para>
    /// The flag and never the bytes. A listing row needs to know which of two elements to draw — the
    /// game's own picture, served from this origin, or the monogram — and 500 images in a listing
    /// query would be a hundred megabytes to answer a yes-or-no question per row. The image itself
    /// arrives on its own request at <c>/g/{slug}/icon</c>, which is cached for a day and served with
    /// the type we determined from the bytes.
    /// </para>
    /// <para>
    /// <b>False carries no cause and must never be given one.</b> A game whose <c>ICON</c> names
    /// nothing, one whose web server we could not reach, and every game on a deployment with an empty
    /// cache are indistinguishable here on purpose: only the first is a fact about the game, and rule
    /// 5 forbids publishing the other two as though they were.
    /// </para>
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
    /// <b>Read from the daily rollup below the watermark and from raw samples above it</b>, exactly as
    /// <see cref="ActivityAsync"/> reads the hourly pair, and for the same two reasons. The rollup is
    /// the copy that survives retention dropping raw partitions (§5.2), so reading raw alone would
    /// silently shorten the ninety-day window on any deployment that has ever configured retention;
    /// and the rollup consumes only whole elapsed days, so reading it alone would leave out every
    /// probe taken since midnight — which on a seven-day window is a seventh of the evidence and on a
    /// listing sorted at nine in the evening is the part a reader most expects to be in there.
    /// </para>
    /// <para>
    /// <b>The typical count is a median, walked out of migration 0019's distributions</b>, and it is
    /// the same walk <see cref="RankingsAsync"/> does — summed frequencies in ascending count order,
    /// and the first value whose running total reaches <c>ceil(n / 2.0)</c>. A mean was the obvious
    /// thing to compute from <c>sum_count</c> and is the wrong statistic: it is pulled around by the
    /// one evening a game was linked from somewhere, which is exactly what 0019 was written about.
    /// The number that comes out is a count a server actually reported and never the average of two.
    /// </para>
    /// <para>
    /// <c>ceil(n / 2.0)</c> and not <c>(n + 1) / 2</c> — <c>sum()</c> over a bigint returns
    /// <c>numeric</c>, so the division is exact and an even sample count asks for element 15.5, which
    /// no row satisfies until the one after the median. That off-by-one shipped once already on the
    /// rankings and is pinned there by equality with <c>percentile_disc</c>.
    /// </para>
    /// <para>
    /// <b>A rolled-up day with no distribution is excluded from all three figures rather than from
    /// one</b>, so the tally printed on the row is the tally the median was taken over. Those are the
    /// buckets 0019 could not rebuild on a deployment that had already dropped raw; counting their
    /// samples while their counts could not reach the walk would publish a basis the arithmetic never
    /// used. Raw samples above the watermark are individual counts and always participate.
    /// </para>
    /// <para>
    /// <b>A NULL count is excluded rather than read as a zero</b> (rule 4). A probe that got in and
    /// could not read a number is §5.4's middle state; it contributes to neither the sum nor the
    /// tally, so a game whose <c>DOING</c> header is past our parser has no average rather than an
    /// average of nothing. The <c>HAVING</c> drops such a game from the result entirely, and the
    /// listing puts it below the break with a sentence saying which of the two it is.
    /// </para>
    /// <para>
    /// <b>The far end of the window is snapped back to a whole UTC day</b>, so the window covers at
    /// least the span it names and never less. The rollup is bucketed by day and a bucket is all or
    /// nothing: a cutoff at midday would drop the whole day it fell in, which on a seven-day window
    /// is a seventh of the evidence discarded to make the label exact. Of the two errors available,
    /// reading a few hours more than the label says is the one that does not throw away measurements
    /// we took — and the label reads "7 days" rather than "168 hours" for that reason.
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
            -- One frequency table over both halves: a raw sample is one occurrence of the count it
            -- read, and a rolled-up day is however many occurrences its distribution records. The
            -- median, the tally and the peak all come off this, so they cannot describe three
            -- different sets of probes.
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
    /// §9's referral neighbours, in both directions, resolved to games we can name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reverse index migration 0006 created for exactly this question had no reader until now.
    /// Both arrows come back in one pass with a <c>direction</c> column rather than as two queries,
    /// because they are the same join read from the two ends and two statements would drift.
    /// </para>
    /// <para>
    /// <b><see cref="PublicG"/> applies here as everywhere.</b> An unclaimed submission is off every
    /// public surface, and a neighbour list is a public surface — being named by somebody else's
    /// referral must not be a way onto a page that the listing itself refuses.
    /// </para>
    /// <para>
    /// Deduplicated by game and direction, because a game answering on two ports would otherwise be
    /// named twice for one relationship — the edge is about the game rather than about which of its
    /// addresses somebody wrote down. Done here rather than with <c>DISTINCT ON</c>, which cannot
    /// see an output alias and would tie the query to an <c>ORDER BY</c> the presentation owns.
    /// </para>
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
    /// different rows — the key is the only thing that differs between them, and it is a literal at
    /// each call site rather than a string this class assembles.
    /// <para>
    /// <b><see cref="Public"/> is baked in rather than appended per call site</b>, so a lookup added
    /// on top of this projection cannot forget it. That is not hypothetical: this constant and the
    /// visibility rule arrived on separate branches, and the id lookup written against the constant
    /// would have served an unclaimed submission to anyone who had its identifier while the slug
    /// lookup beside it refused.
    /// </para>
    /// </remarks>
    private const string GameSelect =
        $"""
        SELECT id AS Id, slug AS Slug, name AS Name, tagline AS Tagline, state AS State,
               is_claimed AS IsClaimed, last_reachable_at AS LastReachableAt,
               excluded_reason AS ExcludedReason
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
            (await IconsForAsync(connection, ids, cancellationToken)).Contains(row.Id));
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
        var changes = await new NpgsqlGameFieldStore(source).ChangesAsync(row.Id, ChangeLimit, cancellationToken);
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
            (await IconsForAsync(connection, ids, cancellationToken)).Contains(row.Id));

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
            ExcludedReason: row.ExcludedReason);
    }

    /// <summary>
    /// The encoding the connect screen was read with, or null when nothing needed saying.
    /// </summary>
    /// <remarks>
    /// <b>Read from <c>charset.read</c> and from nothing else — never from the <c>CHARSET</c>
    /// override an operator typed.</b> Those are two different facts and only one of them is about
    /// this screen: the override is what somebody asked for, and <c>charset.read</c> is what the
    /// probe applied. They come apart whenever the override names an encoding this runtime does not
    /// have, which <c>WireEncoding.Override</c> ignores — reading the raw staff row would then
    /// caption a Latin-1 screen "read as not-an-encoding", a sentence about the screen that is not
    /// true of it. A row exists here only where the encoding was determined, so an unusable override
    /// correctly produces no caption at all rather than a confident wrong one.
    ///
    /// UTF-8 is suppressed because it is the ordinary case and worth no caption: there is nothing
    /// surprising to tell a reader, and a line of provenance on every screen in the catalogue would
    /// bury the thirteen where it matters.
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

        // §9's three liveness feeds — the differentiator no incumbent can publish, because none of
        // them measured continuously enough to know when a game came back.
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

        return new LivenessFeeds(
            discovered.Select(r => new FeedEntry(r.Id, r.Slug, r.Name, r.At, "first seen")).ToList(),
            wentDark.Select(r => new FeedEntry(
                r.Id, r.Slug, r.Name, r.At,
                $"unreachable · {r.Cause ?? "unknown"} · we keep knocking")).ToList(),
            cameBack.Select(r => new FeedEntry(r.Id, r.Slug, r.Name, r.At, "answered again")).ToList());
    }

    /// <summary>
    /// Codebase share and protocol adoption over the listed games (spec §9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every figure here is a count of <em>games</em>, and nothing sums a player count. §15.7 withholds
    /// the absolute "how many people play MU*" number because a share over the measured set survives
    /// the unclaimed and unreachable biases and a total does not — so this method has no access to
    /// presence at all, which is the cheapest way to keep a total from ever being computed here.
    /// </para>
    /// <para>
    /// <b>The protocol denominator is games we have completed a session with, and it is read off
    /// availability rather than off the capability rows themselves.</b> A <c>reachable</c> interval
    /// means a probe of ours got in and finished (a session that answered and could not finish is
    /// <c>degraded</c>, §5.3), which is exactly the set a protocol share is a share of. Counting games
    /// that <em>have</em> capability rows instead would define the denominator out of the numerator:
    /// a game whose handshake completed and offered nothing measurable would drop out of the bottom of
    /// the fraction and quietly raise every share on the page.
    /// </para>
    /// <para>
    /// Archived games are excluded, which is the one presentation change archiving makes (§7.5). A
    /// game that stopped answering in 2019 is a fact about 2019 and its handshake is not evidence
    /// about what the hobby runs now.
    /// </para>
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

              -- Games whose MSSP report we hold. A different set from the one above, and the whole
              -- reason the declared column carries its own denominator.
              (SELECT count(DISTINCT f.game_id)::int
                 FROM game_field f
                 JOIN game g ON g.id = f.game_id
                WHERE {PublicG} AND g.state NOT IN {ListedStates} AND f.source = 'mssp') AS MsspReports,

              -- How stale the stalest handshake in this snapshot is, so the page can say how old the
              -- picture is rather than implying it is of this minute.
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
    /// The busiest table ranks on a <em>median of measured concurrent counts</em> over the
    /// <paramref name="span"/> asked for. A NULL count is a probe that got in and could not read a
    /// number (§5.4) and is excluded rather than read as a zero, which is rule 4 in the one place it
    /// would be most tempting to break: a game whose <c>DOING</c> header we cannot parse would
    /// otherwise sink to the bottom of a league table while running perfectly well. A measured zero
    /// is a count and stays in.
    /// </para>
    /// <para>
    /// Eligibility has two clauses and needs both. <see cref="MinimumRankingSamples"/> is a floor on
    /// how many probes a median may be taken over; <see cref="RankingSpans.MinimumDays"/> is a floor
    /// on how much of the window they came from. Without the second, a game probed hard for a
    /// weekend clears the first and ranks in a table describing a quarter.
    /// </para>
    /// <para>
    /// The second table is the current unbroken run of reachability, which is one open interval per
    /// game and therefore arithmetic over a handful of rows (§5.3). It carries the date the spell
    /// began rather than a duration, because a spell cannot be longer than we have been watching.
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

        // The median comes out of `presence_rollup_day`'s distribution (migration 0019) rather than
        // out of `presence_sample`, because §5.2 lets retention drop raw partitions once they have
        // been rolled up and a ranking read off raw would quietly shorten its own window as a
        // deployment aged — silently, and worst at the widest span, which is the one whose whole
        // point is depth. The rollup is the copy that outlives raw.
        //
        // It is the same number. `walked` reproduces `percentile_disc(0.5)` exactly: the summed
        // frequencies are walked in ascending count order and the first value whose running total
        // reaches ceil(n / 2) is taken — an observed count, and never the average of two.
        //
        // `ceil(n / 2.0)` and not `(n + 1) / 2`, which is the same arithmetic in every language this
        // codebase is written in and not in this one: `sum()` over a bigint returns **numeric**, so
        // the division is exact rather than integer and an even number of samples asks for element
        // 15.5 — which no row satisfies until the one after the median. It shipped as a median one
        // element too high on every game with an even sample count, and
        // `PresenceHistogramPostgresTests` caught it by asserting equality with `percentile_disc`
        // over distributions chosen for exactly this off-by-one.
        //
        // Buckets without a distribution are excluded by the JOIN rather than counted as empty. On a
        // deployment that dropped raw before 0019 they are the buckets the migration could not
        // rebuild, and `samples` and `days` are computed over the same set the median was taken
        // from — so the basis printed on the page is always the basis of the arithmetic.
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

        return new Rankings(
            now,
            span.Window(),
            MinimumRankingSamples,
            listed,
            busiest.Count == 0 ? 0 : busiest[0].Eligible,
            busiest
                .Select(r => new BusiestGame(r.Slug, r.Name, r.Median, r.Peak, r.Samples, r.Days))
                .ToList(),
            spells.Select(r => new ReachableSpell(r.Slug, r.Name, r.Since)).ToList())
        {
            Span = span,
        };
    }

    /// <summary>
    /// The first day bucket a span covers: midnight UTC, <c>days - 1</c> whole days before today.
    /// </summary>
    /// <remarks>
    /// A span of seven therefore covers seven day buckets — six complete and today's, which is still
    /// filling. Aligning to the bucket rather than rolling back from the instant is forced by the
    /// grain the median is summed at: half of a day bucket cannot be read out of it, so a window
    /// that asked for one would either drop the oldest day or take all of it and describe itself
    /// wrongly. The page says "the last N days" and means N buckets.
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
    /// <para>
    /// A capability nothing offered is reported as <b>unmeasured</b> rather than as nought per cent,
    /// and that is derived from the tally rather than compiled in: if no listed game has ever been
    /// observed to offer a protocol, the honest reading is that our handshake does not reach it — TLS
    /// is the standing example, because the probe dials plain telnet and TLS is not a telnet option.
    /// The day the first measurement lands the column starts reporting a share on its own.
    /// </para>
    /// <para>
    /// §9's headline four are listed whether or not anything is known about them, because "we have not
    /// measured TLS yet" is only visible if the row exists. Everything else appears when there is
    /// something to say, including capabilities no registry lists — a server naming a protocol we do
    /// not carry a column for is still a measurement (see <c>FieldObservations</c>).
    /// </para>
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

        // A protocol we have observed at all — in either direction — has a measurable column. One we
        // have never observed has none, and saying "0%" of it would be our own reach reported as the
        // hobby's.
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
    /// A field an owner clears keeps its row and gains a change entry — nothing is ever deleted, and
    /// the withdrawal of a fact is as much a thing that happened as its arrival. Rendering the empty
    /// string as itself would print "FANDOM changed from Exalted to" and lose the event in the
    /// punctuation.
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

                // A cleared field is a row with an empty value, not a missing row (see
                // OwnerEnrichment) — so it is filtered HERE, before the ladder, because an empty
                // row is an absence and an absence does not get to win.
                //
                // Filtering after the winner was chosen silenced whatever was underneath: `owner`
                // outranks `mssp` for enrichment fields, so a cleared owner row won its group and
                // then dropped the group for being empty. A game publishing its own unofficial
                // FANDOM could have had it removed from the page, the plain surface and the API by
                // its owner typing a space into a box — an owner editing a measurement by the back
                // door, which §8.5 forbids outright.
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
    /// The three presence facts a listing needs, in one pass: the current count, whether anybody was
    /// counted this week, and whether anything was probed at all.
    /// </summary>
    private async Task<Dictionary<Guid, PresenceDigest>> PresenceDigestAsync(
        NpgsqlConnection connection,
        Guid[] ids,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // "Now" is the field registry's own window for PLAYERS, so a count on a page and a count in
        // the API age out at the same moment and neither invents its own idea of fresh.
        var nowWindow = _registry.Find("PLAYERS")?.ExpectedRefresh ?? TimeSpan.FromHours(2);

        // The sample's own instant and source come back with the count, because a count is published
        // with a label on it (§10.1) and the label has to describe the row the number came from —
        // `who` is a reading of ours, `mssp` is the game's own claim about itself, and re-deriving
        // either from anything else here would be inventing it.
        var rows = await connection.QueryAsync<DigestRow>(new CommandDefinition(
            """
            SELECT g.id AS GameId, recent.count AS CountNow, recent.at AS CountedAt,
                   recent.source AS CountSource, coalesce(week.n, 0) AS NonZeroThisWeek
              FROM unnest(@ids::uuid[]) AS g(id)
              LEFT JOIN LATERAL (
                   SELECT p.count, p.at, p.source
                     FROM presence_sample p
                    WHERE p.game_id = g.id AND p.at >= @nowFrom AND p.count IS NOT NULL
                    ORDER BY p.at DESC
                    LIMIT 1) recent ON true
              LEFT JOIN LATERAL (
                   SELECT count(*) AS n
                     FROM presence_sample p
                    WHERE p.game_id = g.id AND p.at >= @weekFrom AND p.count > 0) week ON true
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
                r.CountSource is { } source ? SqlEnums.ToFieldSource(source) : null));
    }

    /// <summary>
    /// The count as a labelled fact, or null where there is no count to label.
    /// </summary>
    /// <remarks>
    /// Staleness is asked of the registry under <c>PLAYERS</c> rather than assumed, even though the
    /// digest only returns a sample inside that same window and so cannot presently produce a stale
    /// one. The window is declared in exactly one place (spec §5.6); a <c>false</c> compiled in here
    /// would be a second opinion about it, and would be wrong the day the window moves.
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
                -- Where the raw table stops being the copy that answers. The hourly rollup has
                -- consumed everything below its watermark, and §5.2 lets retention drop those raw
                -- partitions afterwards — so reading raw alone loses the far end of the grid the
                -- moment a deployment configures any retention at all. Above the watermark only the
                -- raw rows exist: the rollup consumes whole elapsed hours, so the newest hours are
                -- always ahead of it, and reading the rollup alone would render the probe we took
                -- ten minutes ago as an hour nobody measured.
                --
                -- No watermark means nothing has ever been rolled up, and -infinity makes that the
                -- read it used to be: raw for the whole window, rollup for none of it.
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

        // Reachable recently but nobody counted — including a game every one of whose counts was
        // unmeasurable. Quiet, never dark: being uncountable is not being absent.
        return lastReachableAt is { } reachable && now - reachable <= RecentlyReachable
            ? ActivityBand.Quiet
            : ActivityBand.Dark;
    }

    private sealed record PresenceDigest(
        int? CountNow,
        bool NonZeroThisWeek,
        DateTimeOffset? CountedAt = null,
        FieldSource? CountSource = null)
    {
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
