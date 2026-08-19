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
public sealed record IconCandidate(
    Guid GameId,
    string DeclaredUrl,
    string? CachedUrl,
    string? ETag,
    DateTimeOffset? FetchedAt);

/// <summary>
/// The icon cache (migration 0013).
/// </summary>
/// <remarks>
/// The one store here that holds no fact: the <c>ICON</c> field is the fact (an ordinary
/// <see cref="GameField"/> row), these are just bytes fetched from the URL it names. Emptying this
/// table loses nothing that can't be fetched again — a cache, not an exception to §7.5. A failed
/// fetch writes nothing (no row, no marker, no attempt counter): that we couldn't reach somebody's
/// web server is a fact about our afternoon, not their game (rule 5).
/// </remarks>
public interface IIconStore
{
    /// <summary>The icon we hold for a game, or null where we hold none.</summary>
    Task<GameIcon?> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default);

    /// <summary>Stores what we fetched, replacing whatever was there.</summary>
    Task UpsertAsync(GameIcon icon, CancellationToken cancellationToken = default);

    /// <summary>
    /// Games whose <c>ICON</c> field names a URL we have not fetched, or fetched longest ago.
    /// </summary>
    /// <remarks>
    /// A URL that changed since we cached it sorts first — we're serving from the wrong address.
    /// Everything else is oldest-first, so one slow game can't starve the rest.
    /// </remarks>
    Task<IReadOnlyList<IconCandidate>> DueAsync(
        int limit, DateTimeOffset staleBefore, CancellationToken cancellationToken = default);
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
                fetched_at   = EXCLUDED.fetched_at
            """,
            icon,
            cancellationToken: cancellationToken));
    }

    /// <remarks>
    /// Read through the same precedence the page uses, so an owner's override is the URL we fetch.
    /// <c>DISTINCT ON</c> with the source ordering does that in one pass.
    /// </remarks>
    public async Task<IReadOnlyList<IconCandidate>> DueAsync(
        int limit,
        DateTimeOffset staleBefore,
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
            )
            SELECT d.game_id AS GameId, d.url AS DeclaredUrl, i.source_url AS CachedUrl,
                   i.etag AS ETag, i.fetched_at AS FetchedAt
            FROM declared d
            LEFT JOIN game_icon i ON i.game_id = d.game_id
            WHERE i.game_id IS NULL
               OR i.source_url IS DISTINCT FROM d.url
               OR i.fetched_at < @staleBefore
            ORDER BY (i.source_url IS DISTINCT FROM d.url) DESC, i.fetched_at NULLS FIRST
            LIMIT @limit
            """,
            new { limit, staleBefore },
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

        public IconCandidate ToRecord() => new(GameId, DeclaredUrl, CachedUrl, ETag, FetchedAt);
    }
}
