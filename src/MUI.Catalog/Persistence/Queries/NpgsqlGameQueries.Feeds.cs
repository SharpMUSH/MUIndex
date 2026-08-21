using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

public sealed partial class NpgsqlGameQueries
{
    private const int FeedLimit = 11;

    public async Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
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

    private sealed class FeedRow
    {
        public Guid Id { get; init; }

        public string Slug { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public DateTimeOffset At { get; init; }

        public string? Cause { get; init; }
    }
    /// <summary>The crawler status page's "recently updated" — the newest field transitions site-wide.</summary>
    /// <remarks>
    /// Ranked with a per-game cap before the overall limit, same two-stage shape
    /// <c>NpgsqlGameFieldStore.ChangesAsync</c> uses per field on one game's own page — a game whose
    /// <c>NAME</c> or <c>PLAYERNAMES</c> flaps every crawl still contributes one row per genuine
    /// transition, but cannot crowd the rest of the catalogue off a page meant to show what the
    /// instrument has been doing broadly.
    /// <para>
    /// Bounded to <see cref="FacetedSearch.RecentlyReachable"/> before ranking — <c>field_change</c> is
    /// append-only and never pruned (rule 3), so an unbounded window function would scan every
    /// transition ever written, for every listed game, on every render of a page that only ever shows
    /// a handful of rows.
    /// </para>
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
                 WHERE c.at >= @since
                   AND NOT (c.field = ANY(@internal) OR c.field LIKE @internalPrefix)
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
                since = (_time.GetUtcNow() - FacetedSearch.RecentlyReachable).ToUniversalTime(),
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
}
