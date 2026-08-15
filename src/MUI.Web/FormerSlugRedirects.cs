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
/// <b>Middleware rather than a branch inside the page</b>, because a component has no honest way to
/// send a 301: <c>NavigationManager.NavigateTo</c> under static server rendering produces a
/// temporary redirect, and §5.7's promise is permanent.
/// </para>
/// <para>
/// <b>A slug some game still wears is never a former slug, whatever the record says</b> — and that is
/// checked here rather than assumed of the record. <see cref="ISlugHistoryStore"/>'s own query knows
/// which slugs are live (<c>g.slug &lt;&gt; h.slug</c>) and <see cref="ConfiguredSlugHistory"/>
/// cannot, so trusting the answer would let one stale hand-written alias permanently redirect
/// readers off a working game's page — a mistake a browser caches and a reader cannot undo. The
/// guard costs a lookup only when an alias actually matches, which is rare by construction: the
/// table answers for a slug that has been retired, and configuration for the handful an operator
/// wrote down.
/// </para>
/// <para>
/// It cannot be done the other way round — letting the route answer and rewriting its 404 — because
/// the response has already started by the time a Razor Components endpoint returns one. Measured,
/// not assumed.
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
            if (SlugOf(context.Request) is not { } slug
                || await MovedToAsync(context, slug) is not { } current)
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status301MovedPermanently;

            // The query string travels with it. Plain mode is a real second surface (§9), and
            // sending a reader who asked for ?plain=1 to the graphical page would be answering a
            // question they did not ask.
            context.Response.Headers[HeaderNames.Location] =
                $"{GamePrefix}{Uri.EscapeDataString(current)}{context.Request.QueryString}";
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

        // One segment only. /g/corvid/claim/check is somebody else's route (§8.1), and a redirect
        // that swallowed it would be this middleware deciding what the site's URLs mean.
        return segment.Length > 0 && !segment.Contains('/', StringComparison.Ordinal) ? segment : null;
    }

    /// <summary>
    /// Where this slug now points, or null when it is current, unknown, or names a game that is not
    /// there.
    /// </summary>
    /// <remarks>
    /// Both refusals matter and both are only reachable through <see cref="ConfiguredSlugHistory"/>,
    /// because the table's answer comes from a join that already excludes a live slug and cannot name
    /// a game that does not exist. A hand-written alias can do either, and the reader pays for it in
    /// a permanent redirect their browser caches: off a page that works in the first case, onto a
    /// page that says "no game here" in the second.
    /// </remarks>
    private static async Task<string?> MovedToAsync(HttpContext context, string slug)
    {
        var history = context.RequestServices.GetService<ISlugHistory>();

        if (history is null
            || await history.CurrentSlugAsync(slug, context.RequestAborted) is not { } current
            || string.Equals(current, slug, StringComparison.Ordinal))
        {
            return null;
        }

        var games = context.RequestServices.GetRequiredService<IGameQueries>();

        return await games.FindAsync(slug, context.RequestAborted) is not null
            || await games.FindAsync(current, context.RequestAborted) is null
            ? null
            : current;
    }
}
