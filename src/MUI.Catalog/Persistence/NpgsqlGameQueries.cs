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
    /// <summary>The heatmap's window (spec §5.2).</summary>
    public static readonly TimeSpan ActivityWindow = TimeSpan.FromDays(56);

    /// <summary>§5.2's "reachable recently", which separates <c>quiet</c> from <c>dark</c>.</summary>
    public static readonly TimeSpan RecentlyReachable = TimeSpan.FromDays(30);

    /// <summary>§5.2's "active this week".</summary>
    public static readonly TimeSpan ThisWeek = TimeSpan.FromDays(7);

    /// <summary>
    /// The window the busiest ranking is measured over, and named on the page.
    /// </summary>
    /// <remarks>
    /// A week, because a MU* has a weekly shape — §5.2's heatmap is a day × hour grid for exactly that
    /// reason — and a ranking over anything shorter would rank Saturday's games above Tuesday's.
    /// </remarks>
    public static readonly TimeSpan RankingWindow = TimeSpan.FromDays(7);

    /// <summary>
    /// How many counted samples a game needs before it can be ranked.
    /// </summary>
    /// <remarks>
    /// A day's worth of hourly probes. A median over three samples is not a median, and a game found
    /// on Friday would otherwise take the top of the table off one lucky evening probe — which is
    /// ranking our crawl schedule rather than the game.
    /// </remarks>
    public const int MinimumRankingSamples = 24;

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
        var includeArchived = filter.IncludeArchived || filter.Band is ActivityBand.Archived;

        var rows = (await connection.QueryAsync<GameRow>(new CommandDefinition(
            """
            SELECT g.id AS Id, g.slug AS Slug, g.name AS Name, g.tagline AS Tagline,
                   g.state AS State, g.is_claimed AS IsClaimed, g.last_reachable_at AS LastReachableAt
              FROM game g
             WHERE (@includeArchived OR g.state <> 'archived')
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

        var facetRows = new List<GameFacetRow>(rows.Count);

        foreach (var row in rows)
        {
            var forGame = fields.TryGetValue(row.Id, out var list) ? list : [];
            var digest = presence.TryGetValue(row.Id, out var found) ? found : PresenceDigest.None;
            var state = SqlEnums.ToLifecycleState(row.State);

            var summary = new GameSummary(
                row.Id,
                row.Slug,
                row.Name,
                row.Tagline,
                state,
                row.IsClaimed,
                digest.CountNow,
                Winner(forGame, "CODEBASE")?.Value,
                MeasuredProtocolsOf(forGame),
                row.LastReachableAt,
                digest.CountAt);

            facetRows.Add(new GameFacetRow(
                summary,
                BandOf(state, row.LastReachableAt, digest, now),
                FacetedSearch.LastSeenOf(row.LastReachableAt, now),
                TlsMeasured: tls.Contains(row.Id),
                Charset: NegotiatedCharset(forGame),
                Language: Winner(forGame, "LANGUAGE")?.Value,
                Codebase: summary.Codebase,
                Family: Winner(forGame, "FAMILY")?.Value,
                Genre: Winner(forGame, "GENRE")?.Value));
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

    public async Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<GameRow>(new CommandDefinition(
            """
            SELECT id AS Id, slug AS Slug, name AS Name, tagline AS Tagline, state AS State,
                   is_claimed AS IsClaimed, last_reachable_at AS LastReachableAt
              FROM game
             WHERE id = @id
            """,
            new { id },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        Guid[] ids = [row.Id];
        var fields = (await FieldsForAsync(connection, ids, cancellationToken))
            .GetValueOrDefault(row.Id, []);
        var digest = (await PresenceDigestAsync(connection, ids, Clock(), cancellationToken))
            .GetValueOrDefault(row.Id, PresenceDigest.None);

        return new GameSummary(
            row.Id,
            row.Slug,
            row.Name,
            row.Tagline,
            SqlEnums.ToLifecycleState(row.State),
            row.IsClaimed,
            digest.CountNow,
            Winner(fields, "CODEBASE")?.Value,
            MeasuredProtocolsOf(fields),
            row.LastReachableAt,
            digest.CountAt);
    }

    public async Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default)
    {
        var now = Clock();

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<GameRow>(new CommandDefinition(
            """
            SELECT id AS Id, slug AS Slug, name AS Name, tagline AS Tagline, state AS State,
                   is_claimed AS IsClaimed, last_reachable_at AS LastReachableAt
              FROM game
             WHERE slug = @slug
            """,
            new { slug },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        Guid[] ids = [row.Id];
        var fields = (await FieldsForAsync(connection, ids, cancellationToken))
            .GetValueOrDefault(row.Id, []);
        var digest = (await PresenceDigestAsync(connection, ids, now, cancellationToken))
            .GetValueOrDefault(row.Id, PresenceDigest.None);

        var intervals = await new NpgsqlAvailabilityStore(source).ForGameAsync(row.Id, cancellationToken);
        var endpoints = await new NpgsqlEndpointStore(source).ForGameAsync(row.Id, cancellationToken);
        var changes = await new NpgsqlGameFieldStore(source).ChangesAsync(row.Id, ChangeLimit, cancellationToken);
        var activity = await ActivityAsync(connection, row.Id, now, cancellationToken);

        var summary = new GameSummary(
            row.Id,
            row.Slug,
            row.Name,
            row.Tagline,
            SqlEnums.ToLifecycleState(row.State),
            row.IsClaimed,
            digest.CountNow,
            Winner(fields, "CODEBASE")?.Value,
            MeasuredProtocolsOf(fields),
            row.LastReachableAt,
            digest.CountAt);

        return new GamePage(
            summary,
            Description: Winner(fields, "DESCRIPTION")?.Value,
            Endpoints: endpoints
                .Select(e => new GameEndpointView(
                    e.Host, e.Port, SqlEnums.ToDb(e.Kind), TlsMeasured: e.Kind is EndpointKind.Tls))
                .ToList(),
            ConnectScreen: Winner(fields, "connect_screen")?.Value,
            ConnectScreenSuppressed: string.Equals(
                Winner(fields, "connect_screen_suppressed")?.Value, "true", StringComparison.Ordinal),
            ReachableFraction: Reachability.FractionReachable(intervals, RecentlyReachable * 3, now),
            LongestOutage: Reachability.LongestOutage(intervals, RecentlyReachable * 3, now),
            Capabilities: CapabilitiesOf(fields),
            Activity: activity,
            Declared: DeclaredOf(fields, now),
            Changes: changes.Select(Describe).ToList());
    }

    public async Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default)
    {
        var now = Clock();
        var since = now - RecentlyReachable;

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // §9's three liveness feeds — the differentiator no incumbent can publish, because none of
        // them measured continuously enough to know when a game came back.
        var discovered = await connection.QueryAsync<FeedRow>(new CommandDefinition(
            """
            SELECT slug AS Slug, name AS Name, first_seen_at AS At, NULL AS Cause
              FROM game
             WHERE first_seen_at >= @since
             ORDER BY first_seen_at DESC
             LIMIT @limit
            """,
            new { since = since.ToUniversalTime(), limit = FeedLimit },
            cancellationToken: cancellationToken));

        var wentDark = await connection.QueryAsync<FeedRow>(new CommandDefinition(
            """
            SELECT g.slug AS Slug, g.name AS Name, a.from_at AS At, a.cause AS Cause
              FROM availability_interval a
              JOIN game g ON g.id = a.game_id
             WHERE a.to_at IS NULL AND a.state = 'unreachable' AND a.from_at >= @since
             ORDER BY a.from_at DESC
             LIMIT @limit
            """,
            new { since = since.ToUniversalTime(), limit = FeedLimit },
            cancellationToken: cancellationToken));

        // A game "came back" when a reachable interval opens exactly where an unreachable one closed.
        // That join is the whole reason availability is stored as intervals: on a sample series this
        // would be a scan for a transition that nothing recorded.
        var cameBack = await connection.QueryAsync<FeedRow>(new CommandDefinition(
            """
            SELECT g.slug AS Slug, g.name AS Name, a.from_at AS At, prev.cause AS Cause
              FROM availability_interval a
              JOIN game g ON g.id = a.game_id
              JOIN availability_interval prev
                ON prev.game_id = a.game_id AND prev.to_at = a.from_at AND prev.state <> 'reachable'
             WHERE a.state = 'reachable' AND a.from_at >= @since
             ORDER BY a.from_at DESC
             LIMIT @limit
            """,
            new { since = since.ToUniversalTime(), limit = FeedLimit },
            cancellationToken: cancellationToken));

        return new LivenessFeeds(
            discovered.Select(r => new FeedEntry(r.Slug, r.Name, r.At, "first seen")).ToList(),
            wentDark.Select(r => new FeedEntry(
                r.Slug, r.Name, r.At, $"unreachable · {r.Cause ?? "unknown"} · we keep knocking")).ToList(),
            cameBack.Select(r => new FeedEntry(r.Slug, r.Name, r.At, "answered again")).ToList());
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
            """
            SELECT
              (SELECT count(*)::int FROM game WHERE state <> 'archived') AS Listed,

              -- A completed session, which is what a measured capability is a capability of.
              (SELECT count(DISTINCT a.game_id)::int
                 FROM availability_interval a
                 JOIN game g ON g.id = a.game_id
                WHERE g.state <> 'archived' AND a.state = 'reachable') AS Handshakes,

              -- Games whose MSSP report we hold. A different set from the one above, and the whole
              -- reason the declared column carries its own denominator.
              (SELECT count(DISTINCT f.game_id)::int
                 FROM game_field f
                 JOIN game g ON g.id = f.game_id
                WHERE g.state <> 'archived' AND f.source = 'mssp') AS MsspReports,

              -- How stale the stalest handshake in this snapshot is, so the page can say how old the
              -- picture is rather than implying it is of this minute.
              (SELECT min(f.last_confirmed_at)
                 FROM game_field f
                 JOIN game g ON g.id = f.game_id
                WHERE g.state <> 'archived' AND f.source = 'handshake'
                  AND f.field LIKE 'capability.%.measured') AS OldestHandshake,

              -- The raw material of the curve this page cannot yet draw (§5.1's change ledger).
              (SELECT count(*)::int
                 FROM field_change c
                 JOIN game g ON g.id = c.game_id
                WHERE g.state <> 'archived'
                  AND c.field LIKE 'capability.%.measured') AS CapabilityTransitions
            """,
            cancellationToken: cancellationToken));

        var codebases = (await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT DISTINCT ON (f.game_id) f.value
              FROM game_field f
              JOIN game g ON g.id = f.game_id
             WHERE g.state <> 'archived' AND f.field = 'CODEBASE' AND f.value <> ''
             ORDER BY f.game_id, array_position(@ladder::text[], f.source), f.last_confirmed_at DESC
            """,
            new { ladder = SourceLadder },
            cancellationToken: cancellationToken))).ToList();

        var capabilities = (await connection.QueryAsync<CapabilityTallyRow>(new CommandDefinition(
            """
            SELECT winner.field AS Field, winner.value AS Value, count(*)::int AS Games
              FROM (SELECT DISTINCT ON (f.game_id, f.field) f.field, f.value
                      FROM game_field f
                      JOIN game g ON g.id = f.game_id
                     WHERE g.state <> 'archived' AND f.field LIKE 'capability.%'
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
            CodebasesOf(codebases, totals.Listed),
            ProtocolsOf(capabilities, totals.Handshakes, totals.MsspReports));
    }

    /// <summary>
    /// The rankings (spec §9) — measured data only, and every basis stated on the record so the page
    /// and the plain surface cannot describe the same table two ways.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The busiest table ranks on a <em>median of measured concurrent counts</em> over
    /// <see cref="RankingWindow"/>. A NULL count is a probe that got in and could not read a number
    /// (§5.4) and is excluded rather than read as a zero, which is rule 4 in the one place it would be
    /// most tempting to break: a game whose <c>DOING</c> header we cannot parse would otherwise sink
    /// to the bottom of a league table while running perfectly well. A measured zero is a count and
    /// stays in.
    /// </para>
    /// <para>
    /// The second table is the current unbroken run of reachability, which is one open interval per
    /// game and therefore arithmetic over a handful of rows (§5.3). It carries the date the spell
    /// began rather than a duration, because a spell cannot be longer than we have been watching.
    /// </para>
    /// </remarks>
    public async Task<Rankings> RankingsAsync(CancellationToken cancellationToken = default)
    {
        var now = Clock();

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var listed = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM game WHERE state <> 'archived'",
            cancellationToken: cancellationToken));

        var busiest = (await connection.QueryAsync<BusiestRow>(new CommandDefinition(
            """
            WITH counted AS (
                SELECT g.slug, g.name,
                       percentile_disc(0.5) WITHIN GROUP (ORDER BY p.count) AS median,
                       max(p.count) AS peak,
                       count(*)::int AS samples
                  FROM presence_sample p
                  JOIN game g ON g.id = p.game_id
                 WHERE p.at >= @from AND p.count IS NOT NULL AND g.state <> 'archived'
                 GROUP BY g.slug, g.name
                HAVING count(*) >= @minimum)
            SELECT slug AS Slug, name AS Name, median AS Median, peak AS Peak, samples AS Samples,
                   (count(*) OVER ())::int AS Eligible
              FROM counted
             ORDER BY median DESC, peak DESC, name
             LIMIT @limit
            """,
            new
            {
                from = (now - RankingWindow).ToUniversalTime(),
                minimum = MinimumRankingSamples,
                limit = RankingLimit,
            },
            cancellationToken: cancellationToken))).ToList();

        var spells = (await connection.QueryAsync<SpellRow>(new CommandDefinition(
            """
            SELECT g.slug AS Slug, g.name AS Name, a.from_at AS Since
              FROM availability_interval a
              JOIN game g ON g.id = a.game_id
             WHERE a.to_at IS NULL AND a.state = 'reachable' AND g.state <> 'archived'
             ORDER BY a.from_at
             LIMIT @limit
            """,
            new { limit = RankingLimit },
            cancellationToken: cancellationToken))).ToList();

        return new Rankings(
            now,
            RankingWindow,
            MinimumRankingSamples,
            listed,
            busiest.Count == 0 ? 0 : busiest[0].Eligible,
            busiest.Select(r => new BusiestGame(r.Slug, r.Name, r.Median, r.Peak, r.Samples)).ToList(),
            spells.Select(r => new ReachableSpell(r.Slug, r.Name, r.Since)).ToList());
    }

    /// <summary>
    /// Codebase values folded to families and counted. The denominator is the games that told us,
    /// never the listing — a game we could not identify is not a game running something else.
    /// </summary>
    private static CodebaseUsage CodebasesOf(IReadOnlyList<string> values, int listed)
    {
        var families = values
            .Select(CodebaseFamily.Of)
            .Where(family => family.Length > 0)
            .GroupBy(family => family, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MeasuredShare(
                // The spelling the most games used, so one game's stray capitalisation does not name
                // the family. Ordinal breaks the tie, so the label is the same on every render.
                group.GroupBy(spelling => spelling, StringComparer.Ordinal)
                    .OrderByDescending(spellings => spellings.Count())
                    .ThenBy(spellings => spellings.Key, StringComparer.Ordinal)
                    .First().Key,
                group.Count(),
                values.Count))
            .OrderByDescending(share => share.Count)
            .ThenBy(share => share.Label, StringComparer.Ordinal)
            .ToList();

        return new CodebaseUsage(families, values.Count, listed - values.Count);
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

    private static ChangeEntry Describe(FieldChange change) => new(
        change.At,
        change.OldValue is null
            ? $"{change.Field} recorded as {change.NewValue} ({SqlEnums.ToDb(change.Source)})"
            : $"{change.Field} changed from {change.OldValue} to {change.NewValue} ({SqlEnums.ToDb(change.Source)})");

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
                && !InternalFields.IsInternal(f.Field))
            .GroupBy(f => f.Field, StringComparer.Ordinal))
        {
            if (FieldPrecedence.Winner(group) is not { } winner)
            {
                continue;
            }

            chips[winner.Field.ToLowerInvariant()] = new ProvenanceChip(
                winner.Value,
                winner.Source,
                winner.LastConfirmedAt,
                _registry.IsStale(winner.Field, winner.LastConfirmedAt, now));
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

        var rows = await connection.QueryAsync<DigestRow>(new CommandDefinition(
            """
            SELECT g.id AS GameId, recent.count AS CountNow, recent.at AS CountAt,
                   coalesce(week.n, 0) AS NonZeroThisWeek
              FROM unnest(@ids::uuid[]) AS g(id)
              LEFT JOIN LATERAL (
                   SELECT p.count, p.at
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
            r => new PresenceDigest(r.CountNow, r.CountAt, r.NonZeroThisWeek > 0));
    }

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
            SELECT extract(dow FROM at AT TIME ZONE 'UTC')::int AS Day,
                   extract(hour FROM at AT TIME ZONE 'UTC')::int AS Hour,
                   count(*)::int AS Samples,
                   count(count)::int AS Counted,
                   avg(count)::float8 AS Mean
              FROM presence_sample
             WHERE game_id = @gameId AND at >= @from
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

    private sealed record PresenceDigest(int? CountNow, DateTimeOffset? CountAt, bool NonZeroThisWeek)
    {
        public static readonly PresenceDigest None = new(null, null, false);
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

        public DateTimeOffset? CountAt { get; init; }

        public long NonZeroThisWeek { get; init; }
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
        public string Slug { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public DateTimeOffset At { get; init; }

        public string? Cause { get; init; }
    }
}
