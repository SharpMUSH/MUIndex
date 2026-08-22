namespace MUI.Discovery;

/// <summary>
/// Reading <see cref="IMergeLog"/> the way everything outside a merge wants it read: <em>which listing
/// is this game part of now</em> (spec §7.3).
/// </summary>
/// <remarks>
/// <para>
/// A merge is a redirect, so an absorbed game keeps its row, its endpoints and its crawl targets, and
/// keeps being probed for ever. Every caller that asks "are these two the same listing?" therefore has
/// to go through the log rather than through the game rows — <c>CatalogueBinder</c> before it opens a
/// duplicate review, and <see cref="IdentityMatcher"/> before it counts how many distinct listings
/// publish one connect screen.
/// </para>
/// <para>
/// <b>One hop is the whole walk.</b> <c>merge_log_no_chains</c> (migration 0018) refuses to record a
/// merge whose winner is itself absorbed, so a game is either a survivor or one step from one. This
/// does not loop, and a loop would be a schema failure rather than something to defend against here.
/// </para>
/// </remarks>
public static class MergeLookup
{
    /// <summary>
    /// The game <paramref name="gameId"/>'s page now answers as — itself when nothing absorbed it, or
    /// when whatever did was reverted.
    /// </summary>
    public static async Task<Guid> ListingOfAsync(this IMergeLog merges, Guid gameId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(merges);

        foreach (var record in await merges.ForGameAsync(gameId, ct))
        {
            // Only the absorbed side redirects: this game being a winner says nothing about where it
            // points, and merge_log_absorbed_once_idx means at most one row can match.
            if (record.IsInForce && record.FromGameId == gameId)
            {
                return record.IntoGameId;
            }
        }

        return gameId;
    }

    /// <summary>
    /// Whether these two ids are already one listing — the same game, or joined by a merge still in
    /// force, in either direction and including both having been absorbed into some third game.
    /// </summary>
    public static async Task<bool> AreOneListingAsync(
        this IMergeLog merges, Guid a, Guid b, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(merges);

        return a == b || await merges.ListingOfAsync(a, ct) == await merges.ListingOfAsync(b, ct);
    }
}
