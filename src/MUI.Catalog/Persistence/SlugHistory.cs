using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// Every slug a game has ever had (spec §5.7), and where it points now.
/// </summary>
/// <remarks>
/// <para>
/// A game's id is immutable and its slug is not, because games rename themselves — so a slug that
/// once worked has to keep working for ever, exactly as an archived game's page does. A URL is a
/// thing somebody else is holding.
/// </para>
/// <para>
/// <b>A row names a game, not another slug.</b> That is what makes a chain free and a loop
/// impossible: a game renamed twice has two rows, both pointing at it, so its oldest URL resolves to
/// its current one in one join and there is no edge between two slugs for a cycle to be made of.
/// </para>
/// <para>
/// <b>Nothing filters on lifecycle state here, and that is deliberate.</b> Archiving is a
/// presentation change (§7.5): an archived game's former URLs keep working exactly as its page
/// does.
/// </para>
/// </remarks>
public interface ISlugHistoryStore
{
    /// <summary>
    /// The slug <paramref name="formerSlug"/> now redirects to, or null when nothing ever wore it.
    /// </summary>
    /// <remarks>
    /// Never returns <paramref name="formerSlug"/> itself. A game may take back a name it used to
    /// have, which leaves a row pointing at a slug that is current again, and a caller that answered
    /// with it would send a browser round a redirect loop for ever.
    /// </remarks>
    Task<string?> CurrentSlugAsync(string formerSlug, CancellationToken cancellationToken = default);

    /// <summary>
    /// The game that used to wear <paramref name="slug"/>, or null if none did.
    /// </summary>
    /// <remarks>
    /// The half of the uniqueness question <c>game.slug</c> cannot answer. Minting asks both: a slug
    /// somebody's bookmark still points at is taken, even though no game currently wears it — unless
    /// the game asking is the one that retired it, which is entitled to its own old URL back.
    /// </remarks>
    Task<Guid?> RetiredByAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Every slug this game has worn and given up, newest retirement first.</summary>
    Task<IReadOnlyList<SlugRetirement>> ForGameAsync(
        Guid gameId, CancellationToken cancellationToken = default);
}

/// <summary>One slug a game used to have, and when it stopped being current (spec §5.7).</summary>
public sealed record SlugRetirement(string Slug, Guid GameId, DateTimeOffset RetiredAt);

/// <summary>The <c>game_slug_history</c> table (spec §5.7).</summary>
/// <remarks>
/// Read-only. The rows are written by <see cref="IGameStore.RenameAsync"/>, in the same statement
/// that re-mints the slug — an alias table nothing writes to is the promise §5.7 makes with a schema
/// under it and no keeper.
/// </remarks>
public sealed class NpgsqlSlugHistoryStore(NpgsqlDataSource source) : ISlugHistoryStore
{
    public async Task<string?> CurrentSlugAsync(
        string formerSlug, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // g.slug <> h.slug is the loop guard, and it belongs in the query rather than in a caller:
        // a game that took its old name back leaves a row pointing at a slug that is current again,
        // and "redirect to yourself" is the one answer no reader can recover from.
        return await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            """
            SELECT g.slug
              FROM game_slug_history h
              JOIN game g ON g.id = h.game_id
             WHERE h.slug = @formerSlug AND g.slug <> h.slug
            """,
            new { formerSlug },
            cancellationToken: cancellationToken));
    }

    public async Task<Guid?> RetiredByAsync(string slug, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT game_id FROM game_slug_history WHERE slug = @slug",
            new { slug },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<SlugRetirement>> ForGameAsync(
        Guid gameId, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT slug AS Slug, game_id AS GameId, retired_at AS RetiredAt
              FROM game_slug_history
             WHERE game_id = @gameId
             ORDER BY retired_at DESC
            """,
            new { gameId },
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    private sealed class Row
    {
        public string Slug { get; init; } = string.Empty;

        public Guid GameId { get; init; }

        public DateTimeOffset RetiredAt { get; init; }

        public SlugRetirement ToRecord() => new(Slug, GameId, RetiredAt);
    }
}
