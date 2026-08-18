using MUI.Web.Localization;

namespace MUI.Web;

/// <summary>
/// <c>/crawler</c> — the short URL the crawler announces to the servers it dials (spec §11).
/// </summary>
/// <remarks>
/// A short URL a person types off a log line after spotting an unfamiliar connection — travels over
/// TTYPE and MNES, gets copied by hand. A redirect rather than a page keeps one copy of what the
/// crawler does. <b>302, not 301</b>, unlike <see cref="FormerSlugRedirects"/>: this says only where
/// the answer lives today, and a cached permanent redirect would be un-withdrawable if the crawler
/// section ever became its own page.
/// </remarks>
public static class CrawlerContact
{
    /// <summary>The path announced to servers. Short because it is typed by hand.</summary>
    public const string Path = "/crawler";

    /// <summary>The <c>id</c> <c>About.razor</c> gives the crawler section's heading.</summary>
    public const string Fragment = "about-crawler";

    public static IEndpointRouteBuilder MapMuiCrawlerContact(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // HEAD as well as GET, for link checkers — MapGet alone answers HEAD with 405.
        endpoints.MapMethods(Path, [HttpMethods.Get, HttpMethods.Head], (HttpContext context) =>
            // Query string and locale both travel with the redirect; ?plain=1 is a real second
            // surface (§9).
            TypedResults.Redirect(LocaleRouting.Link(
                context.LocaleOf().Tag,
                $"/about{context.Request.QueryString}#{Fragment}")));

        return endpoints;
    }
}
