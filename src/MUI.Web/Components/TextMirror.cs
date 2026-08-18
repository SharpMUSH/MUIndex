using Microsoft.AspNetCore.Http;

namespace MUI.Web.Components;

/// <summary>
/// Which pages have a text mirror, and the URL that asks for it.
/// </summary>
/// <remarks>
/// <para>
/// The offer used to live at the bottom of each page that honoured <c>?plain=1</c>, so the page that
/// rendered the mirror was the page that advertised it and the two could not disagree. Moving the
/// offer into the bar breaks that: the header is rendered by the layout, above <c>@Body</c> and
/// before the routed page has rendered anything, so nothing the page knows can reach it. Under
/// static server rendering a cascade only runs downwards.
/// </para>
/// <para>
/// So the fact is stated here, once, against the request's own path — and
/// <c>SiteHeaderTests.EveryRoutablePageIsClassified</c> walks the assembly's route table to prove no
/// page has been added that this has never been asked about. An ungated link would be worse than no
/// link: <c>/account?plain=1</c> answers with the dashboard it always answers with, and an offer
/// that silently does nothing is a lie told on every page of the site.
/// </para>
/// </remarks>
public static class TextMirror
{
    /// <summary>The pages whose whole content is also written as text, by the path that reaches them.</summary>
    /// <remarks>
    /// Exact, not prefixed — <c>/games/random</c> is a redirect under <c>/games</c> and
    /// <c>/g/{slug}/mssp</c> is a scorecard under a game. Both are matched by any rule that reads a
    /// path as a section, and neither has a mirror. <c>/reference</c> is the one section here, and
    /// it is named as one below.
    /// </remarks>
    private static readonly string[] Mirrored =
    [
        "/", "/games", "/archive", "/find", "/ecosystem", "/rankings", "/about", "/submit",
    ];

    /// <summary>Whether this request is looking at a page that can be read as text instead.</summary>
    /// <remarks>
    /// A request already in plain mode is not offered it again. The page it is reading <em>is</em>
    /// the mirror, and a link back to itself in the site's chrome reads as a way out of something.
    /// </remarks>
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
    /// A query-only href, so it resolves against the page being read (see <c>App.razor</c> on why
    /// there is no <c>&lt;base&gt;</c>). The existing query rides along because a reader who has
    /// narrowed the listing to three facets and then asks for text has not asked to start again —
    /// the four footers that offered this were split between doing that and dropping the query on
    /// the floor, and the two that dropped it sent a filtered listing to the whole catalogue.
    /// </remarks>
    public static string Href(HttpContext? context) =>
        ListingLinks.With(context?.Request.QueryString.Value, "plain", "1");
}
