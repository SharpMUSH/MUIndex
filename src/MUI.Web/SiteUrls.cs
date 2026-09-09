namespace MUI.Web;

/// <summary>
/// Where this site is, as an absolute URL, taken from the request that is being answered.
/// </summary>
/// <remarks>
/// <b>Nothing here is configured, and that is deliberate.</b> This project has no hardcoded public
/// hostname; a deployment told its own name would emit a wrong canonical URL on every mirror, preview
/// environment, and developer's laptop. Four surfaces need this (canonical link, Open Graph, sitemap,
/// RSS self-link), each read from the request rather than duplicating the logic.
/// <b>The scheme is only as trustworthy as the proxy configuration</b> — behind a TLS-terminating
/// proxy, <c>Request.Scheme</c> is <c>http</c> unless <see cref="Submissions.SubmitterAddress"/> has
/// unwound <c>X-Forwarded-Proto</c> under its trusted-hop gate.
/// </remarks>
public static class SiteUrls
{
    /// <summary>Scheme, authority and path base — with no trailing slash, so a path can be appended.</summary>
    public static Uri OriginOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;

        return new Uri($"{request.Scheme}://{request.Host}{request.PathBase}");
    }

    /// <summary>
    /// Scheme and authority as text, or the empty string where this request cannot supply them.
    /// </summary>
    /// <remarks>
    /// For the markup this site emits to be pasted somewhere else — the badge snippet, on the game
    /// page and the owner dashboard — where a rooted path would name the <em>other</em> server.
    /// <b>Answers empty rather than throwing.</b> A headless component render is handed an
    /// <see cref="HttpContext"/> with no <c>Host</c>, and <see cref="OriginOf"/> builds a
    /// <see cref="Uri"/> from <c>"://"</c> and throws; a snippet that comes out relative in a test
    /// harness is the right degradation, an exception during render is not.
    /// </remarks>
    public static string OriginTextOf(HttpContext? context) =>
        context is { Request.Host.HasValue: true }
            ? OriginOf(context).ToString().TrimEnd('/')
            : string.Empty;

    /// <summary>An absolute URL for a rooted path on this site.</summary>
    /// <remarks>
    /// Concatenated rather than resolved through <see cref="Uri"/>'s relative-reference rules, which
    /// discard the path base: <c>new Uri(new Uri("https://h/mui"), "/games")</c> is
    /// <c>https://h/games</c>, wrong wherever the site isn't mounted at the root.
    /// </remarks>
    public static string Absolute(HttpContext context, string path)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(path);

        var origin = OriginOf(context).ToString().TrimEnd('/');

        return path.StartsWith('/') ? origin + path : $"{origin}/{path}";
    }

    /// <summary>
    /// The one URL this document should be indexed under.
    /// </summary>
    /// <remarks>
    /// <b>The query string is dropped, and that is the whole point.</b> <c>?plain=1</c> is the same
    /// document (spec §9), and the facet panel's GET form makes every filter/sort combination another
    /// URL — left uncanonicalised, an unbounded supply of near-duplicates for a search crawler.
    /// </remarks>
    public static string CanonicalOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Absolute(context, context.Request.Path.Value ?? "/");
    }
}
