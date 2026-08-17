using MUI.Web.Api;

using Microsoft.Net.Http.Headers;

namespace MUI.Web;

/// <summary>
/// Sends a listing request on to the URL that says only what was asked for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a redirect and not a fix at the form.</b> A browser submits every named control in a GET
/// form, and the panel has nine <c>&lt;select&gt;</c>s that are usually on "any"; no markup says
/// "submit this one only if it holds something", and the panel deliberately runs no script (spec
/// §9). So the empty parameters exist by the time we see them, and the honest thing to do with a URL
/// that means the same as a shorter one is to say so. <see cref="ListingQuery"/> holds the rule; this
/// is the two lines that apply it.
/// </para>
/// <para>
/// <b>Middleware rather than a branch inside the page</b>, for the same reason
/// <see cref="FormerSlugRedirects"/> is: a component cannot answer with a status line, and this one
/// should cost nothing beyond the redirect — no catalogue read, no facet count, no render of a page
/// nobody is going to be shown.
/// </para>
/// <para>
/// <b>302 and not 301.</b> Half of what this drops is the default sort, and which order is default
/// is a decision of ours that may move. A permanent redirect asserting that <c>?sort=players</c> is
/// redundant is one a reader's browser caches and neither of us can withdraw — the same argument
/// <see cref="CrawlerContact"/> makes for its own.
/// </para>
/// <para>
/// <b>Pages only.</b> §10's read API is handed whatever URL a consumer built and answers it: a
/// client library that follows the redirect has spent a round trip for nothing, and one that does
/// not — a perfectly ordinary way to write one — reads a bodiless 302 as a failure.
/// </para>
/// </remarks>
public static class CanonicalListingUrls
{
    /// <summary>The two surfaces with a GET form of their own, and the only paths this looks at.</summary>
    private static readonly string[] Listings = ["/games", "/archive"];

    public static IApplicationBuilder UseCanonicalListingUrls(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var request = context.Request;
            var query = request.QueryString.Value ?? string.Empty;
            var canonical = ListingQuery.Canonical(query);

            if (!IsListingPage(request)
                || string.Equals(canonical, query, StringComparison.Ordinal))
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers[HeaderNames.Location] = request.Path.ToUriComponent() + canonical;
        });
    }

    /// <summary>
    /// A GET or HEAD of one of the listing pages.
    /// </summary>
    /// <remarks>
    /// HEAD as well as GET, because a link checker asking whether a URL still works is a caller this
    /// should answer the same way as a reader. A trailing slash is tolerated because routing
    /// tolerates it, so <c>/games/</c> and <c>/games</c> cannot disagree about which URLs are
    /// equivalent.
    /// </remarks>
    private static bool IsListingPage(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && request.Path.HasValue
        && Array.Exists(
            Listings,
            path => string.Equals(
                request.Path.Value!.TrimEnd('/'),
                path,
                StringComparison.OrdinalIgnoreCase));
}
