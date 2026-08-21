using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

public sealed partial class NpgsqlGameQueries
{
    private const int ChangeLimit = 20;

    private const int ChangePerFieldLimit = 3;

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

        var now = _time.GetUtcNow();

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
        var now = _time.GetUtcNow();

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

    private sealed class ActivityRow
    {
        public int Day { get; init; }

        public int Hour { get; init; }

        public int Samples { get; init; }

        public int Counted { get; init; }

        public double? Mean { get; init; }
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
}
