using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Api;

namespace MUI.Web;

/// <summary>
/// Where a slug somebody is holding should send them — across <em>both</em> tables that can move one
/// (spec §5.7, §7.3).
/// </summary>
/// <remarks>
/// <b>Two records move a URL and they compose, which is the whole reason this is one function.</b> A
/// rename retires a slug into <c>game_slug_history</c>; a merge points a game at another game. Handled
/// separately, a game renamed and then absorbed stranded every URL it used to have — the history
/// pointed at a game that was no longer a public read, the anti-404 guard correctly refused, and
/// working URLs answered 404 instead of following one more hop to the merge survivor. The API not
/// knowing about merges at all meant <c>/g/{slug}</c> and <c>/api/games/{key}</c> disagreed about
/// whether the same game existed.
/// <b>The hops are bounded by construction.</b> A merge whose survivor is itself absorbed can't be
/// recorded (<c>merge_log_no_chains</c>), and a former slug points at a game, not another slug — history
/// is one hop, merge is one hop, the two together are two, and nothing here walks a chain.
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

        // The former slug's game has since been absorbed; send the reader on to the survivor rather
        // than to `current`, which the catalogue no longer offers.
        if (await AbsorbedAsync(merges, current, cancellationToken) is { } onward)
        {
            return onward;
        }

        // A slug some game still wears is never a former slug, and a record naming a missing game is
        // not a destination — a reader would otherwise pay for either in a redirect their browser caches.
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
