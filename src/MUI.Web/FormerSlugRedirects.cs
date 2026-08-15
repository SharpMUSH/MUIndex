using MUI.Catalog;
using MUI.Web.Api;

using Microsoft.Net.Http.Headers;

namespace MUI.Web;

/// <summary>
/// A permanent redirect from a slug a game used to have to the page it has now (spec §5.7).
/// </summary>
/// <remarks>
/// <para>
/// <b>The URL a stranger is holding is <c>/g/{slug}</c>, not <c>/api/games/{key}</c>.</b> A bookmark,
/// a link in somebody's channel topic and a search-engine result all point at the page, so an alias
/// table that only redirected the API would leave the half that matters undone. This is the same
/// promise <see cref="ISlugHistory"/> keeps for the API, kept for the site.
/// </para>
/// <para>
/// <b>Middleware ahead of the Blazor route rather than a branch inside the page</b>, for two reasons.
/// The page is a component with no honest way to send a 301 — <c>NavigationManager.NavigateTo</c>
/// under static SSR produces a temporary redirect, and §5.7's promise is permanent. And a redirect is
/// a fact about a URL rather than about a game, so it belongs where URLs are decided; the page is
/// then reached only by slugs that are current, and renders "not found" only for slugs nobody ever
/// held.
/// </para>
/// <para>
/// <b>One indexed lookup, and only ever the history's.</b> Asking whether the game exists first would
/// double the reads on every page view for the sake of a case that is rare by construction — and it
/// would answer a question this does not need: a hit here means the slug asked for is *not* the
/// current one for the game that wore it, because the store's own query refuses to answer with the
/// slug it was given. A game that took back a name it used to have therefore resolves to itself and
/// does not bounce.
/// </para>
/// </remarks>
public static class FormerSlugRedirects
{
    /// <summary>The game page's route prefix, and the only path this looks at.</summary>
    public const string GamePrefix = "/g/";

    public static IApplicationBuilder UseFormerSlugRedirects(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            if (SlugOf(context.Request) is { } slug
                && await MovedToAsync(context, slug) is { } current)
            {
                context.Response.StatusCode = StatusCodes.Status301MovedPermanently;

                // The query string travels with it. Plain mode is a real second surface (§9), and
                // sending a reader who asked for ?plain=1 to the graphical page would be answering a
                // question they did not ask.
                context.Response.Headers[HeaderNames.Location] =
                    $"{GamePrefix}{Uri.EscapeDataString(current)}{context.Request.QueryString}";

                return;
            }

            await next(context);
        });
    }

    /// <summary>
    /// The slug a GET of a game page asked for, or null when this request is not one.
    /// </summary>
    /// <remarks>
    /// <c>HEAD</c> as well as <c>GET</c>: a link checker asking whether a URL still works is exactly
    /// the reader this promise is for. A trailing slash is tolerated because routing tolerates it, so
    /// <c>/g/corvid/</c> and <c>/g/corvid</c> cannot disagree about whether a URL has moved.
    /// </remarks>
    private static string? SlugOf(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return null;
        }

        if (!request.Path.HasValue
            || !request.Path.Value.StartsWith(GamePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var segment = request.Path.Value[GamePrefix.Length..].TrimEnd('/');

        // One segment only. /g/corvid/history is somebody else's route, present or future, and a
        // redirect that swallowed it would be this middleware deciding what the site's URLs mean.
        return segment.Length > 0 && !segment.Contains('/', StringComparison.Ordinal) ? segment : null;
    }

    /// <summary>
    /// Where this slug now points, or null when it is current, unknown, or names a game that is not
    /// there.
    /// </summary>
    /// <remarks>
    /// The last case is only reachable through <see cref="ConfiguredSlugHistory"/> — the table's own
    /// answer comes from a join and cannot name a game that does not exist. An operator's typo should
    /// leave the reader on a page that says "no game here", rather than on a permanent redirect their
    /// browser will cache to a page that says the same thing.
    /// </remarks>
    private static async Task<string?> MovedToAsync(HttpContext context, string slug)
    {
        var history = context.RequestServices.GetService<ISlugHistory>();

        if (history is null
            || await history.CurrentSlugAsync(slug, context.RequestAborted) is not { } current)
        {
            return null;
        }

        var games = context.RequestServices.GetRequiredService<IGameQueries>();

        return await games.FindAsync(current, context.RequestAborted) is null ? null : current;
    }
}
