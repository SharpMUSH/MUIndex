using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// One picture a connect screen draws, as the probe read it, ready to be stored (migration 0044).
/// </summary>
/// <remarks>
/// Positions are in the stored <c>connect_screen</c>'s cells. Either <see cref="Bytes"/> (a picture
/// sent inline) or <see cref="SourceUrl"/> (an MXP <c>IMAGE</c>'s address), never neither.
/// </remarks>
public sealed record ScreenPictureCapture(
    int Line,
    int Column,
    int Columns,
    int Rows,
    string? SourceUrl,
    string? ContentType,
    byte[]? Bytes);

/// <summary>Where a picture we hold is drawn, without its bytes — what a page needs to place it.</summary>
/// <param name="Version">
/// Changes when the bytes do, so the URL a page writes for the picture changes with it and a long
/// cache lifetime is safe.
/// </param>
public sealed record ScreenPicturePlacement(
    int Ordinal,
    int Line,
    int Column,
    int Columns,
    int Rows,
    string Version);

/// <summary>A picture's bytes, and the type read from them.</summary>
public sealed record ScreenPictureFile(string ContentType, byte[] Bytes, DateTimeOffset CapturedAt);

/// <summary>An address a connect screen named that is due a fetch.</summary>
/// <param name="Failures">How many fetches of this address have failed running, which sizes the back-off.</param>
public sealed record ScreenPictureDue(Guid GameId, int Ordinal, string SourceUrl, int Failures);

/// <summary>
/// The pictures a game's connect screen draws (migration 0044).
/// </summary>
/// <remarks>
/// A cache of the screen's pictures, never a fact of its own: the connect screen is the fact, and
/// every probe that reads it writes its pictures again through <see cref="ReplaceAsync"/>. A failed
/// fetch writes nothing about the game (rule 5) — only when we tried and when we will again, which no
/// page reads.
/// </remarks>
public interface IScreenPictureStore
{
    /// <summary>
    /// Makes the game's pictures exactly these, in this order.
    /// </summary>
    /// <remarks>
    /// An address the screen still names at the same place keeps the bytes already fetched for it and
    /// its back-off; a different address starts again. A picture the screen no longer draws is
    /// deleted. An empty list deletes them all — that is a screen read and found to draw none.
    /// </remarks>
    Task ReplaceAsync(
        Guid gameId,
        IReadOnlyList<ScreenPictureCapture> pictures,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default);

    /// <summary>The game's pictures we hold bytes for, in drawing order.</summary>
    Task<IReadOnlyList<ScreenPicturePlacement>> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default);

    /// <summary>One picture's bytes, or null where we hold none.</summary>
    Task<ScreenPictureFile?> FileAsync(Guid gameId, int ordinal, CancellationToken cancellationToken = default);

    /// <summary>Addresses never fetched, or whose next attempt has come round, oldest first.</summary>
    Task<IReadOnlyList<ScreenPictureDue>> DueAsync(int limit, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores what was fetched from <paramref name="sourceUrl"/>, when the row still names it.
    /// </summary>
    /// <remarks>
    /// Conditional on the address because a probe can replace the row while the fetch is in flight,
    /// and bytes from the old address must not be filed under the new one.
    /// </remarks>
    Task StoreFetchedAsync(
        Guid gameId,
        int ordinal,
        string sourceUrl,
        string contentType,
        byte[] bytes,
        DateTimeOffset at,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default);

    /// <summary>Records that a fetch of <paramref name="sourceUrl"/> produced nothing, and when to try again.</summary>
    Task RecordFailureAsync(
        Guid gameId,
        int ordinal,
        string sourceUrl,
        DateTimeOffset at,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IScreenPictureStore"/>
public sealed class NpgsqlScreenPictureStore(NpgsqlDataSource source) : IScreenPictureStore
{
    public async Task ReplaceAsync(
        Guid gameId,
        IReadOnlyList<ScreenPictureCapture> pictures,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pictures);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM screen_picture WHERE game_id = @gameId AND ordinal >= @count",
            new { gameId, count = pictures.Count },
            transaction,
            cancellationToken: cancellationToken));

        for (var ordinal = 0; ordinal < pictures.Count; ordinal++)
        {
            var picture = pictures[ordinal];

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO screen_picture (game_id, ordinal, line, col, columns, rows, source_url,
                                            content_type, bytes, captured_at)
                VALUES (@gameId, @ordinal, @Line, @Column, @Columns, @Rows, @SourceUrl,
                        @ContentType, @Bytes, @capturedAt)
                ON CONFLICT (game_id, ordinal) DO UPDATE SET
                    line        = EXCLUDED.line,
                    col         = EXCLUDED.col,
                    columns     = EXCLUDED.columns,
                    rows        = EXCLUDED.rows,
                    captured_at = EXCLUDED.captured_at,

                    -- Inline bytes are the picture as sent this time. An address keeps what was
                    -- fetched from it, and its back-off, only while it is still the same address.
                    content_type = CASE
                        WHEN EXCLUDED.bytes IS NOT NULL THEN EXCLUDED.content_type
                        WHEN screen_picture.source_url IS NOT DISTINCT FROM EXCLUDED.source_url
                            THEN screen_picture.content_type
                        END,
                    bytes = CASE
                        WHEN EXCLUDED.bytes IS NOT NULL THEN EXCLUDED.bytes
                        WHEN screen_picture.source_url IS NOT DISTINCT FROM EXCLUDED.source_url
                            THEN screen_picture.bytes
                        END,
                    attempted_at = CASE
                        WHEN screen_picture.source_url IS NOT DISTINCT FROM EXCLUDED.source_url
                            THEN screen_picture.attempted_at
                        END,
                    failures = CASE
                        WHEN screen_picture.source_url IS NOT DISTINCT FROM EXCLUDED.source_url
                            THEN screen_picture.failures
                        ELSE 0
                        END,
                    next_attempt_at = CASE
                        WHEN screen_picture.source_url IS NOT DISTINCT FROM EXCLUDED.source_url
                            THEN screen_picture.next_attempt_at
                        END,
                    source_url = EXCLUDED.source_url
                """,
                new
                {
                    gameId,
                    ordinal,
                    picture.Line,
                    picture.Column,
                    picture.Columns,
                    picture.Rows,
                    picture.SourceUrl,
                    ContentType = picture.Bytes is null ? null : picture.ContentType,
                    picture.Bytes,
                    capturedAt,
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScreenPicturePlacement>> ForGameAsync(
        Guid gameId, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<PlacementRow>(new CommandDefinition(
            """
            SELECT ordinal AS Ordinal, line AS Line, col AS Column, columns AS Columns, rows AS Rows,
                   md5(bytes) AS Version
            FROM screen_picture
            WHERE game_id = @gameId AND bytes IS NOT NULL
            ORDER BY ordinal
            """,
            new { gameId },
            cancellationToken: cancellationToken));

        return [.. rows.Select(row => new ScreenPicturePlacement(
            row.Ordinal, row.Line, row.Column, row.Columns, row.Rows, row.Version[..12]))];
    }

    public async Task<ScreenPictureFile?> FileAsync(
        Guid gameId, int ordinal, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<FileRow>(new CommandDefinition(
            """
            SELECT content_type AS ContentType, bytes AS Bytes, captured_at AS CapturedAt
            FROM screen_picture
            WHERE game_id = @gameId AND ordinal = @ordinal AND bytes IS NOT NULL
            """,
            new { gameId, ordinal },
            cancellationToken: cancellationToken));

        return row is null ? null : new ScreenPictureFile(row.ContentType, row.Bytes, row.CapturedAt);
    }

    public async Task<IReadOnlyList<ScreenPictureDue>> DueAsync(
        int limit, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<DueRow>(new CommandDefinition(
            """
            SELECT game_id AS GameId, ordinal AS Ordinal, source_url AS SourceUrl, failures AS Failures
            FROM screen_picture
            WHERE source_url IS NOT NULL AND (next_attempt_at IS NULL OR next_attempt_at <= @now)
            -- Never asked first, then whoever has waited longest; game and ordinal make a pass
            -- reproducible rather than the planner's choice of the day.
            ORDER BY next_attempt_at NULLS FIRST, game_id, ordinal
            LIMIT @limit
            """,
            new { limit, now },
            cancellationToken: cancellationToken));

        return [.. rows.Select(row => new ScreenPictureDue(row.GameId, row.Ordinal, row.SourceUrl, row.Failures))];
    }

    public async Task StoreFetchedAsync(
        Guid gameId,
        int ordinal,
        string sourceUrl,
        string contentType,
        byte[] bytes,
        DateTimeOffset at,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE screen_picture
            SET content_type = @contentType, bytes = @bytes, attempted_at = @at, failures = 0,
                next_attempt_at = @nextAttemptAt
            WHERE game_id = @gameId AND ordinal = @ordinal AND source_url = @sourceUrl
            """,
            new { gameId, ordinal, sourceUrl, contentType, bytes, at, nextAttemptAt },
            cancellationToken: cancellationToken));
    }

    public async Task RecordFailureAsync(
        Guid gameId,
        int ordinal,
        string sourceUrl,
        DateTimeOffset at,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // The bytes already held, if any, stay: a server that was there last week and is down today
        // still published the picture we have.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE screen_picture
            SET attempted_at = @at, failures = failures + 1, next_attempt_at = @nextAttemptAt
            WHERE game_id = @gameId AND ordinal = @ordinal AND source_url = @sourceUrl
            """,
            new { gameId, ordinal, sourceUrl, at, nextAttemptAt },
            cancellationToken: cancellationToken));
    }

    // Settable classes rather than the records, as IconStore's are: Dapper materialises by
    // constructor signature, and timestamptz arrives as DateTime.
    private sealed class PlacementRow
    {
        public int Ordinal { get; init; }

        public int Line { get; init; }

        public int Column { get; init; }

        public int Columns { get; init; }

        public int Rows { get; init; }

        public string Version { get; init; } = string.Empty;
    }

    private sealed class FileRow
    {
        public string ContentType { get; init; } = string.Empty;

        public byte[] Bytes { get; init; } = [];

        public DateTimeOffset CapturedAt { get; init; }
    }

    private sealed class DueRow
    {
        public Guid GameId { get; init; }

        public int Ordinal { get; init; }

        public string SourceUrl { get; init; } = string.Empty;

        public int Failures { get; init; }
    }
}
