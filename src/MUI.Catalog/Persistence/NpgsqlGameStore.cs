using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>The <c>game</c> table (spec §5, §5.7, §7.5).</summary>
public sealed class NpgsqlGameStore(NpgsqlDataSource source) : IGameStore
{
    // Aliased rather than relying on Dapper's underscore matching, which is a process-wide static
    // this library has no business setting on its host's behalf.
    private const string Columns = """
        id AS Id, slug AS Slug, name AS Name, tagline AS Tagline, state AS State,
        is_claimed AS IsClaimed, first_seen_at AS FirstSeenAt,
        last_reachable_at AS LastReachableAt, archived_at AS ArchivedAt,
        submitted_at AS SubmittedAt, corroborated_at AS CorroboratedAt,
        corroborated_by AS CorroboratedBy
        """;

    public async Task<GameRecord?> ByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM game WHERE id = @id",
            new { id },
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public async Task<GameRecord?> BySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM game WHERE slug = @slug",
            new { slug },
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public async Task InsertAsync(GameRecord game, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game (id, slug, name, tagline, state, is_claimed, first_seen_at,
                              last_reachable_at, archived_at, submitted_at)
            VALUES (@id, @slug, @name, @tagline, @state, @isClaimed, @firstSeenAt,
                    @lastReachableAt, @archivedAt, @submittedAt)
            """,
            new
            {
                id = game.Id,
                slug = game.Slug,
                name = game.Name,
                tagline = game.Tagline,
                state = SqlEnums.ToDb(game.State),
                isClaimed = game.IsClaimed,
                firstSeenAt = game.FirstSeenAt.ToUniversalTime(),
                lastReachableAt = game.LastReachableAt?.ToUniversalTime(),
                archivedAt = game.ArchivedAt?.ToUniversalTime(),
                submittedAt = game.SubmittedAt?.ToUniversalTime(),
            },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Publishes a submitted game on the strength of what a probe measured (spec §7.8).
    /// </summary>
    /// <remarks>
    /// <b>Write-once at the database, not by convention.</b> The <c>WHERE corroborated_at IS NULL</c>
    /// is what makes a second probe with richer signals leave the first record alone — a caller that
    /// forgot to check would otherwise rewrite the answer to "why was this published" every six
    /// hours, and two crawlers racing the same game would each think they were first.
    /// </remarks>
    public async Task CorroborateAsync(
        Guid id,
        DateTimeOffset at,
        IReadOnlyList<string> signals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signals);

        if (signals.Count == 0)
        {
            // Nothing measured it, so there is nothing to publish it on. The CHECK would refuse an
            // empty array anyway; refusing here means the caller never has to have known that.
            return;
        }

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE game
               SET corroborated_at = @at, corroborated_by = @signals
             WHERE id = @id AND corroborated_at IS NULL
            """,
            new { id, at = at.ToUniversalTime(), signals = signals.ToArray() },
            cancellationToken: cancellationToken));
    }

    public async Task ExcludeAsync(
        Guid id,
        string reason,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE game
               SET state = 'excluded', archived_at = NULL,
                   excluded_at = @at, excluded_reason = @reason
             WHERE id = @id
            """,
            new { id, at = at.ToUniversalTime(), reason = reason.Trim() },
            cancellationToken: cancellationToken));
    }

    public async Task IncludeAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // Straight back to active rather than to whatever it was: the crawl decides the rest on the
        // next probe, and guessing at a prior state would be inventing one.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE game
               SET state = 'active', excluded_at = NULL, excluded_reason = NULL
             WHERE id = @id AND state = 'excluded'
            """,
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task UnlistAsync(
        Guid id,
        Guid byUserId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // archived_at is cleared for the reason IncludeAsync clears excluded_at: the schema holds the
        // date and the state in step, and a game can be dark on the day its owner asks. Nothing else
        // about the row is touched — the history that made it dark is still true and still shown.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE game
               SET state = 'unlisted', archived_at = NULL,
                   excluded_at = NULL, excluded_reason = NULL,
                   unlisted_at = @at, unlisted_by = @byUserId
             WHERE id = @id
            """,
            new { id, at = at.ToUniversalTime(), byUserId },
            cancellationToken: cancellationToken));
    }

    public async Task RelistAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // Straight back to active, as IncludeAsync does and for the same reason: the next probe
        // decides the rest, and guessing at the state it held before would be inventing one.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE game
               SET state = 'active', unlisted_at = NULL, unlisted_by = NULL
             WHERE id = @id AND state = 'unlisted'
            """,
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task SetStateAsync(
        Guid id,
        LifecycleState state,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // **`AND state NOT IN ('excluded', 'unlisted')` is the load-bearing clause.** The sweeper and
        // the ingestor both call this, and both would otherwise walk one of those games back into the
        // listing — the sweeper by archiving it when it went dark, the ingestor by restoring it when
        // it answered. Refusing here rather than at each caller means a third caller written later
        // inherits it. Both states are left by their own method, which is where the thing this
        // signature cannot express lives: an argument for one, an account for the other.
        //
        // archived_at is set on the way in and cleared on the way out, because the schema's
        // game_archived_games_have_a_date constraint holds the two in step. Nothing else about the
        // row is touched: archiving changes presentation and never the record (§7.5).
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE game
               SET state = @state,
                   archived_at = CASE WHEN @state = 'archived' THEN @at ELSE NULL END,

                   -- Cleared on the way to any other state, because game_exclusion_is_explained and
                   -- game_unlisting_is_attributed hold each date and the state in step. Neither can
                   -- be put *on* from here, because there is nowhere in this signature to say why or
                   -- to name who asked.
                   excluded_at = NULL,
                   excluded_reason = NULL,
                   unlisted_at = NULL,
                   unlisted_by = NULL
             WHERE id = @id AND state NOT IN ('excluded', 'unlisted')
            """,
            new { id, state = SqlEnums.ToDb(state), at = at.ToUniversalTime() },
            cancellationToken: cancellationToken));
    }

    public async Task SetClaimedAsync(
        Guid id,
        bool isClaimed,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE game SET is_claimed = @isClaimed WHERE id = @id",
            new { id, isClaimed },
            cancellationToken: cancellationToken));
    }

    public async Task MarkReachableAsync(
        Guid id,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // GREATEST, because probes for a multi-port game are not serialised against each other and an
        // older answer arriving late must not walk this backwards.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE game SET last_reachable_at = GREATEST(last_reachable_at, @at) WHERE id = @id",
            new { id, at = at.ToUniversalTime() },
            cancellationToken: cancellationToken));
    }

    public async Task<string?> RenameAsync(
        Guid id,
        string name,
        string slug,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // One statement, so the retirement and the re-mint are one atomic act (see IGameStore). The
        // data-modifying CTEs all read the same snapshot, so `previous` sees the slug as it was
        // however Postgres orders them, and a CTE nobody selects from still runs exactly once.
        //
        // ON CONFLICT DO NOTHING keeps the earliest retirement of a slug a game has worn twice: the
        // row exists to say "this URL is spoken for and here is whose", and re-stamping it would
        // rewrite when a URL somebody bookmarked started redirecting.
        //
        // The answer is read from `previous` and NOT from the insert. A game that goes A -> B -> A -> B
        // retires a slug already in the table, so the insert is suppressed and RETURNING yields
        // nothing — which reported "the slug did not move" while it moved. What the caller asked is
        // what this game was at a moment ago; the row it already has is the record, not the write.
        //
        // The unique index on game.slug is the guard against re-minting a URL another game holds; it
        // raises here rather than letting two games answer to one address.
        return await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            """
            WITH previous AS (
                SELECT id, slug FROM game WHERE id = @id
            ), retired AS (
                INSERT INTO game_slug_history (slug, game_id, retired_at)
                SELECT slug, id, @at FROM previous WHERE slug <> @slug
                ON CONFLICT (slug) DO NOTHING
                RETURNING slug
            ), renamed AS (
                UPDATE game SET slug = @slug, name = @name
                 WHERE id = @id AND EXISTS (SELECT 1 FROM previous)
                RETURNING id
            )
            SELECT slug FROM previous WHERE slug <> @slug
            """,
            new { id, name, slug, at = at.ToUniversalTime() },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<GameRecord>> UnarchivedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM game WHERE state <> 'archived' ORDER BY first_seen_at",
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    private sealed class Row
    {
        public Guid Id { get; init; }

        public string Slug { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string? Tagline { get; init; }

        public string State { get; init; } = string.Empty;

        public bool IsClaimed { get; init; }

        public DateTimeOffset FirstSeenAt { get; init; }

        public DateTimeOffset? LastReachableAt { get; init; }

        public DateTimeOffset? ArchivedAt { get; init; }

        public DateTimeOffset? SubmittedAt { get; init; }

        public DateTimeOffset? CorroboratedAt { get; init; }

        public string[]? CorroboratedBy { get; init; }

        public GameRecord ToRecord() => new(
            Id, Slug, Name, Tagline, SqlEnums.ToLifecycleState(State), IsClaimed,
            FirstSeenAt, LastReachableAt, ArchivedAt, SubmittedAt, CorroboratedAt, CorroboratedBy);
    }
}
