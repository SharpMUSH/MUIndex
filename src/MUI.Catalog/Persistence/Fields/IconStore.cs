using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// A game's icon as we hold it: the bytes, what we determined they are, and where they came from.
/// </summary>
/// <param name="SourceUrl">
/// The URL these bytes came from, as the <c>ICON</c> field said it at the time — not re-read from the
/// field, which can have moved on since.
/// </param>
/// <param name="ContentType">
/// The type read from the bytes by <c>ImageHeader</c>, never one a far end claimed in a header.
/// </param>
public sealed record GameIcon(
    Guid GameId,
    string SourceUrl,
    string ContentType,
    int Width,
    int Height,
    byte[] Bytes,
    string? ETag,
    DateTimeOffset FetchedAt);

/// <summary>
/// A game whose icon is worth fetching: the URL its <c>ICON</c> field names, and what we last got.
/// </summary>
/// <param name="ETag">
/// What the far end gave us last time, so the next fetch can ask conditionally. Null where we hold
/// nothing or the server sent none.
/// </param>
/// <param name="Failures">
/// How many times in a row <see cref="DeclaredUrl"/> has failed us, and zero where it has not been
/// tried — what the caller sizes the next back-off from. Reset by the URL moving: a new address is a
/// new question, and the old address's luck says nothing about it.
/// </param>
public sealed record IconCandidate(
    Guid GameId,
    string DeclaredUrl,
    string? CachedUrl,
    string? ETag,
    DateTimeOffset? FetchedAt,
    int Failures = 0);

/// <summary>
/// A fetch that did not produce an icon, and when the next one is worth making (migration 0035).
/// </summary>
/// <remarks>
/// A fact about this site's afternoon, never about the game — it reaches no page, no API field and
/// no change feed, which is what keeps rule 5 intact while letting the queue move.
/// </remarks>
public sealed record IconAttempt(
    Guid GameId,
    string Url,
    DateTimeOffset AttemptedAt,
    int Failures,
    DateTimeOffset NextAttemptAt);

/// <summary>
/// The icon cache (migration 0013) and the retry bookkeeping beside it (migration 0035).
/// </summary>
/// <remarks>
/// The one store here that holds no fact: the <c>ICON</c> field is the fact (an ordinary
/// <see cref="GameField"/> row), these are just bytes fetched from the URL it names. Emptying either
/// table loses nothing that can't be fetched again — a cache, not an exception to §7.5. A failed
/// fetch writes nothing about the <em>game</em>: that we couldn't reach somebody's web server is a
/// fact about our afternoon, not their game (rule 5). It does write down that we tried, because a
/// queue with no record of an attempt cannot tell a candidate it just failed from one it has never
/// seen, and re-serves the same twenty for ever.
/// </remarks>
public interface IIconStore
{
    /// <summary>The icon we hold for a game, or null where we hold none.</summary>
    Task<GameIcon?> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores what we fetched, replacing whatever was there, and clears the game's failure record.
    /// </summary>
    /// <remarks>
    /// Clearing here rather than in the caller: a success that left the count standing would shorten
    /// nothing today and lengthen the next failure's back-off by everything that came before it.
    /// </remarks>
    Task UpsertAsync(GameIcon icon, CancellationToken cancellationToken = default);

    /// <summary>
    /// Games whose <c>ICON</c> field names a URL we have not fetched, or fetched longest ago, minus
    /// those whose last failure has not yet come round again.
    /// </summary>
    /// <remarks>
    /// A URL that changed since we cached it sorts first — we're serving from the wrong address.
    /// Everything else is by last-touched, oldest first, counting a failed attempt as a touch. That
    /// last clause is the whole point: ranking on what the cache holds alone ties every never-fetched
    /// game with every other, and a bounded pass over a tie is the same rows every time.
    /// </remarks>
    /// <param name="limit">How many to return, which is one pass's budget.</param>
    /// <param name="staleBefore">An icon fetched before this is worth asking about again.</param>
    /// <param name="now">
    /// The moment the back-off is measured against — a candidate whose <c>next_attempt_at</c> is
    /// still ahead of this is not offered.
    /// </param>
    /// <param name="cancellationToken">The caller's budget.</param>
    Task<IReadOnlyList<IconCandidate>> DueAsync(
        int limit,
        DateTimeOffset staleBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Records that a fetch produced nothing, and when to try again.</summary>
    Task RecordFailureAsync(IconAttempt attempt, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IIconStore"/>
public sealed class NpgsqlIconStore(NpgsqlDataSource source) : IIconStore
{
    public async Task<GameIcon?> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<IconRow>(
            new CommandDefinition(
                """
                SELECT game_id AS GameId, source_url AS SourceUrl, content_type AS ContentType,
                       width AS Width, height AS Height, bytes AS Bytes, etag AS ETag,
                       fetched_at AS FetchedAt
                FROM game_icon
                WHERE game_id = @gameId
                """,
                new { gameId },
                cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public async Task UpsertAsync(GameIcon icon, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(icon);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game_icon (game_id, source_url, content_type, width, height, bytes, etag,
                                   fetched_at)
            VALUES (@GameId, @SourceUrl, @ContentType, @Width, @Height, @Bytes, @ETag, @FetchedAt)
            ON CONFLICT (game_id) DO UPDATE SET
                source_url   = EXCLUDED.source_url,
                content_type = EXCLUDED.content_type,
                width        = EXCLUDED.width,
                height       = EXCLUDED.height,
                bytes        = EXCLUDED.bytes,
                etag         = EXCLUDED.etag,
                fetched_at   = EXCLUDED.fetched_at;

            DELETE FROM icon_attempt WHERE game_id = @GameId;
            """,
            icon,
            cancellationToken: cancellationToken));
    }

    public async Task RecordFailureAsync(
        IconAttempt attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO icon_attempt (game_id, url, attempted_at, failures, next_attempt_at)
            VALUES (@GameId, @Url, @AttemptedAt, @Failures, @NextAttemptAt)
            ON CONFLICT (game_id) DO UPDATE SET
                url             = EXCLUDED.url,
                attempted_at    = EXCLUDED.attempted_at,
                failures        = EXCLUDED.failures,
                next_attempt_at = EXCLUDED.next_attempt_at
            """,
            attempt,
            cancellationToken: cancellationToken));
    }

    /// <remarks>
    /// Read through the same precedence the page uses, so an owner's override is the URL we fetch.
    /// <c>DISTINCT ON</c> with the source ordering does that in one pass.
    /// </remarks>
    public async Task<IReadOnlyList<IconCandidate>> DueAsync(
        int limit,
        DateTimeOffset staleBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<CandidateRow>(new CommandDefinition(
            """
            WITH declared AS (
                SELECT DISTINCT ON (gf.game_id)
                       gf.game_id, gf.value AS url
                FROM game_field gf
                WHERE upper(gf.field) = 'ICON' AND gf.value <> ''
                ORDER BY gf.game_id,
                         CASE gf.source
                             WHEN 'staff' THEN 0
                             WHEN 'owner' THEN 1
                             WHEN 'mssp'  THEN 2
                             ELSE 3
                         END
            ),
            -- Only where it still describes the URL we would fetch. An attempt against an address
            -- the game has since moved off holds neither a useful failure count nor a back-off worth
            -- honouring, so it is joined away rather than reasoned around three times below.
            attempt AS (
                SELECT a.* FROM icon_attempt a JOIN declared d ON d.game_id = a.game_id
                WHERE a.url = d.url
            )
            SELECT d.game_id AS GameId, d.url AS DeclaredUrl, i.source_url AS CachedUrl,
                   i.etag AS ETag, i.fetched_at AS FetchedAt,
                   coalesce(a.failures, 0) AS Failures
            FROM declared d
            LEFT JOIN game_icon i ON i.game_id = d.game_id
            LEFT JOIN attempt a ON a.game_id = d.game_id
            WHERE (a.game_id IS NULL OR a.next_attempt_at <= @now)
              AND (i.game_id IS NULL
                   OR i.source_url IS DISTINCT FROM d.url
                   OR i.fetched_at < @staleBefore)
            -- Serving from an address the game has moved off is a small untruth on a live page, so
            -- it outranks everything; but only where we hold bytes at all, since a game we have
            -- never fetched is not being mis-served, it is simply unfetched.
            ORDER BY (i.game_id IS NOT NULL AND i.source_url IS DISTINCT FROM d.url) DESC,
                     -- Last touched, oldest first, where a failure counts as a touch. Without the
                     -- attempt half, every never-fetched game ties here with every other and LIMIT
                     -- returns the same rows every pass, for ever.
                     coalesce(a.attempted_at, i.fetched_at) NULLS FIRST,
                     -- Total, so a pass is reproducible and the tail of a tie is not the planner's
                     -- choice of the day.
                     d.game_id
            LIMIT @limit
            """,
            new { limit, staleBefore, now },
            cancellationToken: cancellationToken));

        return [.. rows.Select(row => row.ToRecord())];
    }

    /// <summary>
    /// Dapper's view of a row, which is not the record's.
    /// </summary>
    /// <remarks>
    /// A settable class, not the positional record: Dapper materialises by constructor signature and
    /// a <c>timestamptz</c> arrives as <c>DateTime</c>, so a record taking
    /// <see cref="DateTimeOffset"/> fails at run time.
    /// </remarks>
    private sealed class IconRow
    {
        public Guid GameId { get; init; }

        public string SourceUrl { get; init; } = string.Empty;

        public string ContentType { get; init; } = string.Empty;

        public int Width { get; init; }

        public int Height { get; init; }

        public byte[] Bytes { get; init; } = [];

        public string? ETag { get; init; }

        public DateTimeOffset FetchedAt { get; init; }

        public GameIcon ToRecord() =>
            new(GameId, SourceUrl, ContentType, Width, Height, Bytes, ETag, FetchedAt);
    }

    private sealed class CandidateRow
    {
        public Guid GameId { get; init; }

        public string DeclaredUrl { get; init; } = string.Empty;

        public string? CachedUrl { get; init; }

        public string? ETag { get; init; }

        public DateTimeOffset? FetchedAt { get; init; }

        public int Failures { get; init; }

        public IconCandidate ToRecord() =>
            new(GameId, DeclaredUrl, CachedUrl, ETag, FetchedAt, Failures);
    }
}
