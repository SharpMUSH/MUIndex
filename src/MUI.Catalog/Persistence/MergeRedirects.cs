using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// Where a merged-away game's URL now points (spec §7.3, migration 0018).
/// </summary>
/// <remarks>
/// <para>
/// <b>Separate from <see cref="IMergeLog"/> on purpose, and this is not the same question.</b> The log
/// is the audit trail — who absorbed whom, on what evidence, and whether it is still in force — and it
/// is written by whatever performs a merge. This is one lookup by slug that a read-only replica serving
/// pages needs, and it must not carry a write path with it.
/// </para>
/// <para>
/// <b>The absorbed game keeps its slug.</b> A merge is not a rename: nothing goes into
/// <c>game_slug_history</c>, because that table's promise is "a URL a game gave up", and this game gave
/// nothing up — it is still there, still probed, still holding its own row. So the redirect cannot be
/// found by asking the slug history, and this is the second thing the page has to ask.
/// </para>
/// </remarks>
public interface IMergeRedirects
{
    /// <summary>
    /// The slug of the game that absorbed <paramref name="slug"/>, or null when nothing did.
    /// </summary>
    /// <remarks>
    /// Null when the merge has been reverted, and null when the survivor is itself absorbed by
    /// something else — a chain is a redirect nobody can follow and a browser caches half of.
    /// </remarks>
    Task<string?> AbsorbedIntoAsync(string slug, CancellationToken cancellationToken = default);
}

/// <summary><see cref="IMergeRedirects"/> over <c>merge_log</c>.</summary>
public sealed class NpgsqlMergeRedirects(NpgsqlDataSource source) : IMergeRedirects
{
    public async Task<string?> AbsorbedIntoAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // The second NOT EXISTS is the chain guard, in SQL rather than in a loop: if the survivor has
        // itself been absorbed, this answers nothing rather than sending a reader to a page that will
        // send them somewhere else again. One hop or none — and a merge that would form a chain is a
        // pair for a person to look at, not a redirect to follow.
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT into_game.slug
              FROM merge_log m
              JOIN game absorbed ON absorbed.id = m.from_game_id
              JOIN game into_game ON into_game.id = m.into_game_id
             WHERE absorbed.slug = @slug
               AND m.reverted_at IS NULL
               AND NOT EXISTS (
                   SELECT 1 FROM merge_log onward
                    WHERE onward.from_game_id = m.into_game_id AND onward.reverted_at IS NULL)
            """,
            new { slug },
            cancellationToken: cancellationToken));
    }
}
