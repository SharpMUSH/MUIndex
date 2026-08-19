using MUI.Web.Api;

using Microsoft.Net.Http.Headers;

namespace MUI.Web;

/// <summary>
/// Sends a listing request on to the URL that says only what was asked for.
/// </summary>
/// <remarks>
/// A browser submits every named control in the panel's GET form regardless of value (spec §9, no
/// script to omit blanks), so empty params exist by the time we see them; <see cref="ListingQuery"/>
/// holds the canonicalization rule, this applies it via middleware since a component can't answer
/// with a status line. <b>302, not 301</b>: which sort is default is a decision that may move, and a
/// cached permanent redirect couldn't be withdrawn. <b>Pages only</b> — §10's read API answers
/// whatever URL a consumer built rather than redirecting it.
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
    /// <remarks>HEAD as well as GET, for link checkers. A trailing slash is tolerated because routing tolerates it.</remarks>
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
