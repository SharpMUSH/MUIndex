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
    /// Null when the merge has been reverted, and null when the survivor is not a page a reader can
    /// actually land on — an unclaimed submission, or (which the schema refuses, so this is the second
    /// line rather than the first) a game itself absorbed by something else.
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

        // A 301 is cached by the reader's browser, so a redirect onto a page that answers 404 is a
        // mistake they cannot undo by reloading. Two ways that could happen, and both are refused here:
        //
        // The survivor is not public. An unclaimed submission is a game the listing and the lookup both
        // decline to show (§8), so redirecting onto it lands a reader on "no game here". Decided on
        // read rather than refused at the merge, because claiming the survivor makes it a perfectly
        // good destination and nothing should have to be re-merged for that.
        // FormerSlugRedirects already holds this line for a renamed game; this is the same line.
        //
        // The survivor is itself absorbed. merge_log_no_chains refuses to record that, so this is a
        // second line and not the guarantee — kept because the guarantee is a trigger somebody could
        // drop, and because "one hop or none" should be legible where the hop is taken.
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
