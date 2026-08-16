using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Api;

namespace MUI.Web;

/// <summary>
/// Where a slug somebody is holding should send them — across <em>both</em> tables that can move one
/// (spec §5.7, §7.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two records move a URL and they compose, which is the whole reason this is one function.</b> A
/// rename retires a slug into <c>game_slug_history</c>; a merge points a game at another game. Each
/// was handled where it was introduced — history in the middleware and the API, merges in the
/// middleware only — and neither knew about the other. That produced two failures the moment the
/// first real merges were recorded:
/// </para>
/// <para>
/// A game renamed and <em>then</em> absorbed stranded every URL it used to have. The history said
/// "that slug belongs to this game", the game was no longer a public read, the guard against
/// redirecting onto a 404 fired correctly, and the redirect was refused — so five URLs that had
/// worked that morning answered 404, which is §5.7's "for ever" broken by §7.3. The guard was right
/// and the answer was wrong: the destination exists, one hop further on.
/// </para>
/// <para>
/// And the API did not know about merges at all, so <c>/g/{slug}</c> sent a reader on while
/// <c>/api/games/{key}</c> answered 404 for the same game — one promise with two answers, and the
/// consumer following the API would have concluded the game was gone.
/// </para>
/// <para>
/// <b>The hops are bounded by construction and this does not walk a chain.</b> A merge whose survivor
/// is itself absorbed cannot be recorded (<c>merge_log_no_chains</c>), and a former slug points at a
/// game rather than at another slug, so there is nothing to walk: history is one hop, merge is one
/// hop, and the two together are two.
/// </para>
/// </remarks>
internal static class SlugDestination
{
    /// <summary>
    /// The slug to redirect <paramref name="slug"/> to, or null when it is current, unknown, or names
    /// a game that is not there.
    /// </summary>
    public static async Task<string?> ForAsync(
        string slug,
        IMergeRedirects? merges,
        ISlugHistory? history,
        IGameQueries games,
        CancellationToken cancellationToken)
    {
        if (await AbsorbedAsync(merges, slug, cancellationToken) is { } survivor)
        {
            return survivor;
        }

        if (history is null
            || await history.CurrentSlugAsync(slug, cancellationToken) is not { } current
            || string.Equals(current, slug, StringComparison.Ordinal))
        {
            return null;
        }

        // The former slug's game has since been absorbed. Sending the reader to the survivor is the
        // only answer that keeps both promises at once; sending them to `current` would be a redirect
        // onto a page the catalogue no longer offers, and refusing is the 404 this exists to stop.
        if (await AbsorbedAsync(merges, current, cancellationToken) is { } onward)
        {
            return onward;
        }

        // A slug some game still wears is never a former slug, whatever the record says, and a record
        // naming a game that is not there is not a destination. Both are only reachable through a
        // hand-written alias — the table's own query excludes live slugs and cannot name a missing
        // game — and a reader pays for either in a permanent redirect their browser caches.
        return await games.FindAsync(slug, cancellationToken) is not null
            || await games.FindAsync(current, cancellationToken) is null
            ? null
            : current;
    }

    private static async Task<string?> AbsorbedAsync(
        IMergeRedirects? merges,
        string slug,
        CancellationToken cancellationToken) =>
        merges is not null
        && await merges.AbsorbedIntoAsync(slug, cancellationToken) is { } survivor
        && !string.Equals(survivor, slug, StringComparison.Ordinal)
            ? survivor
            : null;
}
