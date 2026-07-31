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

    private const int FeedLimit = 10;

    private const int ChangeLimit = 20;

    private readonly IFieldRegistry _registry = registry ?? FieldRegistry.Instance;

    /// <summary>
    /// Overridable so a test can render a fixed frame. Everything time-dependent on this class reads
    /// it, and nothing calls <c>DateTimeOffset.UtcNow</c> directly.
    /// </summary>
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    public async Task<IReadOnlyList<GameSummary>> ListAsync(
        GameFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var now = Clock();

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var capabilityFields = filter.MeasuredProtocols
            .Select(CapabilityFields.Measured)
            .ToArray();

        var rows = (await connection.QueryAsync<GameRow>(new CommandDefinition(
            """
            SELECT g.id AS Id, g.slug AS Slug, g.name AS Name, g.tagline AS Tagline,
                   g.state AS State, g.is_claimed AS IsClaimed, g.last_reachable_at AS LastReachableAt
              FROM game g
             WHERE (@includeArchived OR g.state <> 'archived')
               AND (@text IS NULL OR g.name ILIKE @text)
               AND (cardinality(@capabilityFields::text[]) = 0 OR (
                       SELECT count(DISTINCT f.field)
                         FROM game_field f
                        WHERE f.game_id = g.id
                          AND f.field = ANY(@capabilityFields)
                          AND f.value = 'true') = cardinality(@capabilityFields::text[]))
             ORDER BY g.name
            """,
            new
            {
                includeArchived = filter.IncludeArchived,
                text = string.IsNullOrWhiteSpace(filter.Text) ? null : $"%{filter.Text.Trim()}%",
                capabilityFields,
            },
            cancellationToken: cancellationToken))).ToList();

        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(row => row.Id).ToArray();
        var fields = await FieldsForAsync(connection, ids, cancellationToken);
        var presence = await PresenceDigestAsync(connection, ids, now, cancellationToken);

        var summaries = new List<GameSummary>(rows.Count);

        foreach (var row in rows)
        {
            var forGame = fields.TryGetValue(row.Id, out var list) ? list : [];
            var digest = presence.TryGetValue(row.Id, out var found) ? found : PresenceDigest.None;
            var state = SqlEnums.ToLifecycleState(row.State);
            var band = BandOf(state, row.LastReachableAt, digest, now);

            if (filter.Band is { } wanted && band != wanted)
            {
                continue;
            }

            summaries.Add(new GameSummary(
                row.Id,
                row.Slug,
                row.Name,
                row.Tagline,
                state,
                row.IsClaimed,
                digest.CountNow,
                Winner(forGame, "CODEBASE")?.Value,
                MeasuredProtocolsOf(forGame)));
        }

        return summaries;
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
            MeasuredProtocolsOf(fields));

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
            SELECT g.id AS GameId, recent.count AS CountNow, coalesce(week.n, 0) AS NonZeroThisWeek
              FROM unnest(@ids::uuid[]) AS g(id)
              LEFT JOIN LATERAL (
                   SELECT p.count
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

        return rows.ToDictionary(r => r.GameId, r => new PresenceDigest(r.CountNow, r.NonZeroThisWeek > 0));
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

    private sealed record PresenceDigest(int? CountNow, bool NonZeroThisWeek)
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

    private sealed class FeedRow
    {
        public string Slug { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public DateTimeOffset At { get; init; }

        public string? Cause { get; init; }
    }
}
