using Microsoft.AspNetCore.Http;

namespace MUI.Web.Components;

/// <summary>
/// Which pages have a text mirror, and the URL that asks for it.
/// </summary>
/// <remarks>
/// The header offering this link is rendered by the layout, above <c>@Body</c> and before the routed
/// page runs, so the page itself cannot inform it — under static server rendering a cascade only
/// runs downwards. So the fact is stated here, once, against the request's path, and
/// <c>SiteHeaderTests.EveryRoutablePageIsClassified</c> walks the route table to prove no page was
/// added that this was never asked about.
/// </remarks>
public static class TextMirror
{
    /// <summary>The pages whose whole content is also written as text, by the path that reaches them.</summary>
    /// <remarks>
    /// Exact, not prefixed: <c>/games/random</c> and <c>/g/{slug}/mssp</c> would both match a
    /// prefix rule that reads a path as a section, and neither has a mirror.
    /// </remarks>
    private static readonly string[] Mirrored =
    [
        "/", "/games", "/archive", "/find", "/ecosystem", "/rankings", "/about",
        "/submit", "/crawler",
    ];

    /// <summary>Whether this request is looking at a page that can be read as text instead.</summary>
    /// <remarks>A request already in plain mode is not offered it again — the page it's reading <em>is</em> the mirror.</remarks>
    public static bool Offers(HttpContext? context) =>
        context is not null
        && !Truthy.Is(context.Request.Query["plain"])
        && Offers(context.Request.Path.Value);

    /// <summary>The same question asked of a path alone, which is what the tests can enumerate.</summary>
    public static bool Offers(string? path)
    {
        var trimmed = path?.TrimEnd('/');

        if (string.IsNullOrEmpty(trimmed))
        {
            trimmed = "/";
        }

        if (Mirrored.Contains(trimmed, StringComparer.Ordinal))
        {
            return true;
        }

        // The reference section: its index and every article under it, and nothing else nests there.
        if (trimmed == "/reference" || trimmed.StartsWith("/reference/", StringComparison.Ordinal))
        {
            return true;
        }

        // One game — /g/{slug} and not the two pages that hang off it. Counted rather than prefixed,
        // because /g/foo/mssp and /g/foo/claim are both "under" the game and neither is mirrored.
        return trimmed.StartsWith("/g/", StringComparison.Ordinal)
            && trimmed.AsSpan(3).IndexOf('/') < 0
            && trimmed.Length > 3;
    }

    /// <summary>
    /// This page, asked for as text — carrying whatever question it was already answering.
    /// </summary>
    /// <remarks>
    /// A query-only href, resolving against the page being read (see <c>App.razor</c> on why there
    /// is no <c>&lt;base&gt;</c>). The existing query rides along, or a reader who narrowed a
    /// listing by facets and asked for text would get the whole catalogue instead.
    /// </remarks>
    public static string Href(HttpContext? context) =>
        ListingLinks.With(context?.Request.QueryString.Value, "plain", "1");
}
