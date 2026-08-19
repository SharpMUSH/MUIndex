using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Api;

using Microsoft.Net.Http.Headers;

namespace MUI.Web;

/// <summary>
/// A permanent redirect from a slug a game used to have to the page it has now (spec §5.7).
/// </summary>
/// <remarks>
/// Redirects the page, <c>/g/{slug}</c>, not just the API — the same promise <see cref="ISlugHistory"/>
/// keeps for the API, kept for the site, since that's what bookmarks and search results actually
/// hold. Implemented as middleware, not a component branch, because
/// <c>NavigationManager.NavigateTo</c> under static server rendering can only produce a temporary
/// redirect and §5.7's promise is permanent.
/// <b>A slug some game still wears is never a former slug, whatever the record says</b> — checked
/// here rather than assumed, since trusting a stale hand-written alias would let it permanently
/// redirect readers off a working game's page, a mistake a browser caches.
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

            // Query string travels with it — plain mode is a real second surface (§9).
            context.Response.Headers[HeaderNames.Location] =
                $"{GamePrefix}{Uri.EscapeDataString(current)}{context.Request.QueryString}";
        });
    }

    /// <summary>
    /// The slug a GET of a game page asked for, or null when this request is not one.
    /// </summary>
    /// <remarks><c>HEAD</c> as well as <c>GET</c>, for link checkers. A trailing slash is tolerated because routing tolerates it.</remarks>
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

        // One segment only — /g/corvid/claim/check is somebody else's route (§8.1).
        return segment.Length > 0 && !segment.Contains('/', StringComparison.Ordinal) ? segment : null;
    }

    /// <summary>
    /// Where this slug now points, or null when it is current, unknown, or names a game that is not
    /// there. <see cref="SlugDestination"/> holds the rule so this and the API can't drift apart.
    /// </summary>
    private static Task<string?> MovedToAsync(HttpContext context, string slug) =>
        SlugDestination.ForAsync(
            slug,
            context.RequestServices.GetService<IMergeRedirects>(),
            context.RequestServices.GetService<ISlugHistory>(),
            context.RequestServices.GetRequiredService<IGameQueries>(),
            context.RequestAborted);
}
