namespace MUI.Web;

/// <summary>
/// Where this site is, as an absolute URL, taken from the request that is being answered.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is configured, and that is deliberate.</b> This project has no hardcoded public
/// hostname — <c>ProbeOptions.InfoUrl</c> ships pointing at <c>muindex.example</c> and the about
/// page treats "still the default" as *unconfigured* rather than as an address. A deployment that
/// had to be told its own name before it could emit a canonical URL would emit a wrong one on every
/// mirror, every preview environment and every developer's laptop, which is worse than none: a
/// canonical URL naming a host the reader is not on tells a search engine to index somewhere else.
/// </para>
/// <para>
/// Four surfaces need this — the canonical link, the Open Graph preview, the sitemap and the RSS
/// feed's self-link — and they had begun to answer it separately. The feed was first and got it
/// right; a second copy is a second chance to forget the path base.
/// </para>
/// <para>
/// <b>The scheme is only as trustworthy as the proxy configuration.</b> Behind something that
/// terminates TLS, <c>Request.Scheme</c> is <c>http</c> unless <c>X-Forwarded-Proto</c> has been
/// unwound — which <see cref="Submissions.SubmitterAddress"/> does, under the same trusted-hop gate
/// it applies to the client address, and only when a deployment has said how many proxies are in
/// front of it.
/// </para>
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

    /// <summary>An absolute URL for a rooted path on this site.</summary>
    /// <remarks>
    /// Concatenated rather than resolved through <see cref="Uri"/>'s relative-reference rules,
    /// because those discard the path base: <c>new Uri(new Uri("https://h/mui"), "/games")</c> is
    /// <c>https://h/games</c>, which is a page that does not exist wherever the site is not mounted
    /// at the root.
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
    /// <b>The query string is dropped, and that is the whole point of the method.</b>
    /// <c>?plain=1</c> is the same document rendered for a text browser — spec §9 is explicit that
    /// it is the same view models and not a second page — and the facet panel is a GET form, so
    /// every combination of filters and sorts is another URL for the listing. Left uncanonicalised
    /// that is an unbounded supply of near-duplicates for a crawler to spend its budget on, and the
    /// duplicate that gets indexed is whichever one it happened to find first.
    /// </remarks>
    public static string CanonicalOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Absolute(context, context.Request.Path.Value ?? "/");
    }
}
