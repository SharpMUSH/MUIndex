namespace MUI.Web;

/// <summary>
/// <c>/crawler</c> — the short URL the crawler announces to the servers it dials (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a URL a person types off a log line</b>, having just found an unfamiliar connection from
/// an address they did not recognise, and that is the whole argument for its existence: the string
/// travels over TTYPE and MNES, gets copied into a mail client by hand, and is read by somebody who
/// is mildly annoyed. <c>/about#about-crawler</c> is the honest address of the content and a poor
/// one to retype.
/// </para>
/// <para>
/// <b>A redirect rather than a page</b>, so there is one copy of what the crawler does and one place
/// to correct it. The section it lands on is generated from <c>ProbeOptions</c> — the name we
/// announce, the commands we send, the three ways to make us stop — and a second surface rendering
/// the same facts is a second surface that can fall behind them.
/// </para>
/// <para>
/// <b>302 and not 301</b>, unlike <see cref="FormerSlugRedirects"/>. That one implements a promise
/// the spec makes about a URL a stranger holds, and permanence is the promise; this says only where
/// the answer lives today. If the crawler section ever outgrows a section of <c>/about</c> and takes
/// a page of its own, a cached permanent redirect would be a thing readers could not undo and we
/// could not withdraw.
/// </para>
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

        // HEAD as well as GET: a link checker asking whether the address we published still works is
        // exactly the reader this route is for, and MapGet alone answers one with 405.
        endpoints.MapMethods(Path, [HttpMethods.Get, HttpMethods.Head], (HttpRequest request) =>
            // The query string travels with it, and the fragment goes last because that is where a
            // fragment goes: ?plain=1 is a real second surface (§9), and an admin who asked for the
            // plain rendering of the page that explains us should get it.
            TypedResults.Redirect($"/about{request.QueryString}#{Fragment}"));

        return endpoints;
    }
}
