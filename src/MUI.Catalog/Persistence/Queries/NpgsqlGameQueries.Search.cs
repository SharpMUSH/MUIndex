using System.Collections.Concurrent;

using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

public sealed partial class NpgsqlGameQueries
{
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

        // Archived games leave the default listing (spec §7.5); requesting the archived band lifts
        // just that exclusion. Must lift `archived` only — not `excluded` or `unlisted`, which answer
        // different questions the archive checkbox doesn't ask.
        var includeArchived = filter.IncludeArchived || filter.Band is ActivityBand.Archived;

        var rows = await CatalogueAsync(
            new CatalogueKey(includeArchived, SortWindows.Of(filter.Sort)), cancellationToken);

        return rows.Count == 0 ? GameListing.Empty : FacetedSearch.Search(rows, filter);
    }

    /// <summary>
    /// What a catalogue snapshot is keyed by — everything the assembled rows depend on, and nothing
    /// a facet selection can change.
    /// </summary>
    /// <remarks>
    /// The window is here because a window sort is the one thing that adds a query
    /// (<see cref="PlayersOverWindowAsync"/>); the archive toggle because it is the one predicate the
    /// database applies. Every other facet is arithmetic over the rows, so it cannot be part of the
    /// key — which is exactly why the key space is a handful of entries rather than the product of
    /// twelve facets.
    /// </remarks>
    private readonly record struct CatalogueKey(bool IncludeArchived, TimeSpan? Window);

    private sealed record CatalogueSnapshot(IReadOnlyList<GameFacetRow> Rows, DateTimeOffset TakenAt);

    /// <summary>
    /// How long an assembled catalogue is served before it is built again.
    /// </summary>
    /// <remarks>
    /// The crawler writes presence roughly hourly, so a minute of staleness is far inside the
    /// resolution of the data being shown; what it buys is the difference between assembling the
    /// catalogue once a minute and assembling it once per request. The figures embedded in a row —
    /// bands, the freshness chips, "last seen" — are computed against the instant the snapshot was
    /// taken rather than the instant it is served, which is the honest reading of a cached row and
    /// is bounded by this constant.
    /// </remarks>
    private static readonly TimeSpan CatalogueFreshness = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<CatalogueKey, Lazy<Task<CatalogueSnapshot>>> _catalogue = new();

    /// <summary>
    /// The whole catalogue as facet rows — from cache when one is fresh, otherwise built once and
    /// shared by everyone waiting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The row set costs seven queries over every game in the catalogue and does not vary with the
    /// filter, so without this every distinct query string re-ran all seven. That is not a
    /// theoretical concern: the listing's facets are combinable, so the URL space is the product of
    /// them all, and a crawler walking it generates a stream of URLs that are each unique and each
    /// identical in what they cost to answer. Caching the *rendered page* would never hit; caching
    /// the rows behind it always does.
    /// </para>
    /// <para>
    /// The <see cref="Lazy{T}"/> holds the <see cref="Task"/> rather than the result, which is what
    /// makes this single-flight: concurrent callers arriving on a cold key await one build instead
    /// of starting one each. Under the load this exists to survive, a cache that admitted a
    /// thundering herd on every expiry would be close to no cache at all.
    /// </para>
    /// <para>
    /// The build deliberately does not take the caller's <see cref="CancellationToken"/>. The work is
    /// shared, so honouring one caller's cancellation would abandon every other caller waiting on
    /// it — and the callers most likely to disconnect mid-request are precisely the automated ones
    /// arriving in bulk. A failed build is evicted rather than cached, so the next request retries
    /// instead of inheriting the exception for the rest of the freshness window.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<GameFacetRow>> CatalogueAsync(
        CatalogueKey key,
        CancellationToken cancellationToken)
    {
        var entry = Entry(key);
        var snapshot = await SnapshotAsync(key, entry, cancellationToken);

        if (_time.GetUtcNow() - snapshot.TakenAt < CatalogueFreshness)
        {
            return snapshot.Rows;
        }

        // Stale: drop this entry and take whatever the replacement builds. Whoever wins the race to
        // re-add does the work and everybody else awaits it. Removing by key *and value* matters —
        // a plain remove would discard a replacement another caller had already installed.
        //
        // Deliberately one rebuild rather than a loop that re-tests freshness: a snapshot is stamped
        // with the clock that a re-test would read, so a freshness of zero — or a clock that does not
        // advance, which is every test with a fixed one — would spin for ever rather than answer.
        // Serving rows one build old is the correct answer to that race anyway; nobody is waiting for
        // a *newer* catalogue than the one just assembled.
        Evict(key, entry);

        return (await SnapshotAsync(key, Entry(key), cancellationToken)).Rows;
    }

    private Lazy<Task<CatalogueSnapshot>> Entry(CatalogueKey key) =>
        _catalogue.GetOrAdd(
            key,
            k => new Lazy<Task<CatalogueSnapshot>>(
                () => BuildCatalogueAsync(k, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));

    private async Task<CatalogueSnapshot> SnapshotAsync(
        CatalogueKey key,
        Lazy<Task<CatalogueSnapshot>> entry,
        CancellationToken cancellationToken)
    {
        try
        {
            return await entry.Value.WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Evict(key, entry);
            throw;
        }
    }

    private void Evict(CatalogueKey key, Lazy<Task<CatalogueSnapshot>> entry) =>
        ((ICollection<KeyValuePair<CatalogueKey, Lazy<Task<CatalogueSnapshot>>>>)_catalogue)
            .Remove(new KeyValuePair<CatalogueKey, Lazy<Task<CatalogueSnapshot>>>(key, entry));

    private async Task<CatalogueSnapshot> BuildCatalogueAsync(
        CatalogueKey key,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var includeArchived = key.IncludeArchived;

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
            return new CatalogueSnapshot([], now);
        }

        var ids = rows.Select(row => row.Id).ToArray();
        var fields = await FieldsForAsync(connection, ids, cancellationToken);
        var presence = await PresenceDigestAsync(connection, ids, now, cancellationToken);
        var tls = await TlsEndpointsAsync(connection, ids, cancellationToken);
        var icons = await IconsForAsync(connection, ids, cancellationToken);

        // Only where the order asks for it. It is an aggregate over the presence series of the whole
        // catalogue, and computing it for a listing sorted by name would be a scan nobody reads.
        var windows = key.Window is { } span
            ? await PlayersOverWindowAsync(connection, ids, span, now, cancellationToken)
            : [];

        // Unlike windows, always computed: trending is a facet now (Games list), not only a figure a
        // window sort happens to show, so a reader has to be able to filter on it without first
        // having to sort by a typical count.
        var dailyMedians = await DailyMediansAsync(connection, ids, now, cancellationToken);
        var growth = ids.ToDictionary(
            id => id,
            id =>
            {
                var days = dailyMedians.GetValueOrDefault(id, []);

                return (Direction: GrowthTrend.Of(days), Players: GrowthTrend.ChangePlayers(days));
            });

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
                growth.GetValueOrDefault(row.Id).Direction,
                growth.GetValueOrDefault(row.Id).Players,
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

        return new CatalogueSnapshot(facetRows, now);
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

    /// <summary>One game's window figures as the walk returns them.</summary>
    private sealed class WindowRow
    {
        public Guid GameId { get; init; }

        public int Median { get; init; }

        public int Peak { get; init; }

        public int Samples { get; init; }
    }
}
