using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// Where a merged-away game's URL now points (spec §7.3, migration 0018).
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IMergeLog"/> on purpose: the log is the audit trail, written by whatever
/// performs a merge. This is one read-only lookup by slug, with no write path attached.
/// </para>
/// <para>
/// The absorbed game keeps its slug — a merge is not a rename, so nothing goes into
/// <c>game_slug_history</c> (that table's promise is "a URL a game gave up", and this game gave
/// nothing up). So the redirect can't be found via the slug history; this is the second thing the
/// page has to ask.
/// </para>
/// </remarks>
public interface IMergeRedirects
{
    /// <summary>
    /// The slug of the game that absorbed <paramref name="slug"/>, or null when nothing did.
    /// </summary>
    /// <remarks>
    /// Null when reverted, and null when the survivor isn't a page a reader can land on — an
    /// unclaimed submission, or (which the schema already refuses) a game itself absorbed further.
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

        // A 301 is cached by the reader's browser, so a redirect onto a 404 can't be undone by
        // reloading. Two cases refused here: the survivor is not public (an unclaimed submission),
        // decided on read rather than at merge time since claiming it later makes it a good
        // destination without a re-merge; and the survivor is itself absorbed further —
        // merge_log_no_chains already refuses to record that, but this is kept as a second line in
        // case that guarantee is ever dropped.
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT into_game.slug
              FROM merge_log m
              JOIN game absorbed ON absorbed.id = m.from_game_id
              JOIN game into_game ON into_game.id = m.into_game_id
             WHERE absorbed.slug = @slug
               AND m.reverted_at IS NULL
               AND (into_game.submitted_at IS NULL OR into_game.is_claimed)
               AND NOT EXISTS (
                   SELECT 1 FROM merge_log onward
                    WHERE onward.from_game_id = m.into_game_id AND onward.reverted_at IS NULL)
            """,
            new { slug },
            cancellationToken: cancellationToken));
    }
}
