using Dapper;

using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// The <c>duplicate_review</c> table — spec §7.3's middle band.
/// </summary>
/// <remarks>
/// <b>This changes no presentational state at all.</b> It does not archive, hide, redirect or reorder
/// either game: both pages stay live and link to each other reciprocally, because a wrongly hidden
/// game is worse than a visible duplicate, and hiding one side would make this an unreviewed merge
/// wearing a review's name.
/// </remarks>
public sealed class NpgsqlDuplicateReviewRepository(NpgsqlDataSource source) : IDuplicateReviewRepository
{
    private const string Columns = """
        id AS Id, left_game_id AS LeftGameId, right_game_id AS RightGameId, score AS Score,
        signals::text AS SignalsJson, opened_at AS OpenedAt, resolved_at AS ResolvedAt,
        resolution AS Resolution
        """;

    /// <summary>
    /// Opens the pair, or returns the id of the one already open.
    /// </summary>
    /// <remarks>
    /// The pair is unordered and storage orders it, so "have we already opened this?" is one lookup
    /// rather than two racing into two rows for one pair.
    /// </remarks>
    public async Task<Guid> OpenAsync(
        Guid a,
        Guid b,
        IdentityScore score,
        DateTimeOffset at,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(score);

        if (a == b)
        {
            throw new ArgumentException("A game cannot be a suspected duplicate of itself.", nameof(b));
        }

        var (left, right) = a.CompareTo(b) < 0 ? (a, b) : (b, a);

        await using var connection = await source.OpenConnectionAsync(ct);

        var existing = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT id FROM duplicate_review
             WHERE left_game_id = @left AND right_game_id = @right AND resolved_at IS NULL
            """,
            new { left, right },
            cancellationToken: ct));

        if (existing is { } open)
        {
            return open;
        }

        var id = Guid.CreateVersion7();

        // ON CONFLICT against the partial unique index, so two workers opening the same pair at once
        // end up with one row and both learn its id.
        var written = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            INSERT INTO duplicate_review (id, left_game_id, right_game_id, score, signals, opened_at)
            VALUES (@id, @left, @right, @score, @signals::jsonb, @at)
            ON CONFLICT DO NOTHING
            RETURNING id
            """,
            new
            {
                id,
                left,
                right,
                score = score.Score,
                signals = IdentitySignals.ToJson(score.Signals),
                at = at.ToUniversalTime(),
            },
            cancellationToken: ct));

        return written ?? await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            SELECT id FROM duplicate_review
             WHERE left_game_id = @left AND right_game_id = @right AND resolved_at IS NULL
            """,
            new { left, right },
            cancellationToken: ct));
    }

    /// <summary>Every open pair this game is on either side of — the reciprocal link.</summary>
    public async Task<IReadOnlyList<DuplicateReview>> OpenPairsForAsync(Guid gameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"""
            SELECT {Columns}
              FROM duplicate_review
             WHERE (left_game_id = @gameId OR right_game_id = @gameId)
               AND resolved_at IS NULL
             ORDER BY opened_at DESC
            """,
            new { gameId },
            cancellationToken: ct));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    /// <summary>Closes the pair. The row is kept: a judgement is part of the record.</summary>
    public async Task ResolveAsync(
        Guid id, string resolution, DateTimeOffset at, CancellationToken ct, IUnitOfWork? unitOfWork = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resolution);

        // See NpgsqlMergeLog.RecordAsync's own comment -- the same join-or-open choice, for the same
        // caller (ReviewMergeService.MergeAsync).
        var shared = unitOfWork as NpgsqlUnitOfWork;

        NpgsqlConnection? owned = shared is null ? await source.OpenConnectionAsync(ct) : null;

        try
        {
            var connection = shared?.Connection ?? owned!;

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE duplicate_review
                   SET resolved_at = @at, resolution = @resolution
                 WHERE id = @id AND resolved_at IS NULL
                """,
                new { id, resolution, at = at.ToUniversalTime() },
                transaction: shared?.Transaction,
                cancellationToken: ct));
        }
        finally
        {
            if (owned is not null)
            {
                await owned.DisposeAsync();
            }
        }
    }

    private sealed class Row
    {
        public Guid Id { get; init; }

        public Guid LeftGameId { get; init; }

        public Guid RightGameId { get; init; }

        public double Score { get; init; }

        public string SignalsJson { get; init; } = "[]";

        public DateTimeOffset OpenedAt { get; init; }

        public DateTimeOffset? ResolvedAt { get; init; }

        public string? Resolution { get; init; }

        public DuplicateReview ToRecord() =>
            new(Id, LeftGameId, RightGameId, Score, SignalsJson, OpenedAt, ResolvedAt, Resolution);
    }
}
