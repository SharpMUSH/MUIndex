using Microsoft.Net.Http.Headers;

namespace MUI.Web;

/// <summary>
/// Says that a page may not be stored, because every page names the reader it was rendered for.
/// </summary>
/// <remarks>
/// The layout's header is either "Sign in" or the account link, and the claim surface renders the
/// whole ceremony or "you need an account first" depending on who asked — so a document from this
/// site is an answer about one reader at one moment, not a resource. Answered with no cache
/// directive, as it was, such a document is eligible for the browser's back/forward cache: an
/// operator who opened a claim page signed out, signed in, and pressed Back was handed the
/// signed-out render again, complete with its sign-in link, and could loop through that for ever.
/// That was reported from the live site and reproduces in both Chromium and Firefox.
/// <b>Documents only.</b> The read API, the badges, the icons and the fingerprinted assets each say
/// for themselves how long their answer keeps, and an answer that already carries a directive is
/// left exactly as it was — a blanket rule over the whole pipeline would make the immutable
/// stylesheet unstorable too, which is the opposite of what a fingerprint is for.
/// </remarks>
public static class PageCacheHeaders
{
    /// <summary>
    /// Marks every HTML document unstorable, on the way out.
    /// </summary>
    /// <remarks>
    /// Registered early so it wraps everything that can render markup — the pages, the not-found
    /// page, and the redirects in between — and decided in <c>OnStarting</c> rather than up front,
    /// because which of those answered is not known until the response is about to be written.
    /// </remarks>
    public static IApplicationBuilder UseMuiPageCacheHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                var response = ((HttpContext)state).Response;

                if (IsDocument(response) && !response.Headers.ContainsKey(HeaderNames.CacheControl))
                {
                    // no-store rather than no-cache: no-cache permits the store and asks for
                    // revalidation, and a back/forward restore revalidates nothing.
                    response.Headers[HeaderNames.CacheControl] = "no-store";
                }

                return Task.CompletedTask;
            }, context);

            await next(context);
        });
    }

    /// <summary>
    /// Whether this response is a page somebody is reading.
    /// </summary>
    /// <remarks>
    /// Read off the content type rather than the path: the plain-text mirror, the not-found page and
    /// the localized redirects are all documents reached by paths this could not enumerate, and a
    /// page added tomorrow must not have to be added here too. A bodiless answer — the 302 a facet
    /// form canonicalizes to, a 401 from the sign-in endpoint — has no content type and is left
    /// alone; there is nothing in it to replay.
    /// </remarks>
    private static bool IsDocument(HttpResponse response) =>
        response.ContentType is { } type
        && type.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
}
