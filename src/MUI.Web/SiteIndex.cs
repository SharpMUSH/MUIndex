using System.Globalization;
using System.Text;
using System.Xml;

using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Data;
using MUI.Web.Reference;

namespace MUI.Web;

/// <summary>
/// The two documents a crawler asks for before it asks for a page.
/// </summary>
/// <remarks>
/// <para>
/// A measurement nobody can find is a measurement nobody reads, and the parts of this catalogue
/// most worth finding are the parts least likely to be reached by following links. The archive is
/// the clearest case: spec §7.4 keeps every dark game's page alive for ever, which is the historical
/// record the incumbents threw away — and nothing on the live site links to most of it. The
/// reference section is the second: hand-written prose whose only inbound link is its own index.
/// </para>
/// <para>
/// <b>Endpoints and not files in <c>wwwroot</c>.</b> A sitemap enumerates the catalogue, so a static
/// one is a second copy of it that is wrong by the end of the first crawl cycle.
/// </para>
/// <para>
/// <b>Nothing invented is submitted.</b> Over the demo fixture the game URLs are left out entirely.
/// A sitemap is a request that somebody index these pages, it is read only by programs, and it has
/// no field in which to say the games are made up — the same reasoning that keeps the structured
/// data off a fixture page. What stays is the hand-written pages, which are ours and true either way.
/// </para>
/// </remarks>
public static class SiteIndex
{
    private const string SitemapPath = "/sitemap.xml";

    /// <summary>Routes that answer for machines rather than readers.</summary>
    public static WebApplication MapMuiSiteIndex(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/robots.txt", (HttpContext http) =>
            Results.Text(Robots(SiteUrls.Absolute(http, SitemapPath)), "text/plain; charset=utf-8"));

        app.MapGet(SitemapPath, async (HttpContext http, IGameQueries queries, CatalogueSource catalogue) =>
        {
            var games = catalogue.IsMeasured
                ? await queries.ListAsync(
                    new GameFilter { IncludeArchived = true },
                    http.RequestAborted)
                : [];

            // Through the API's writer for the ETag: a sitemap is re-fetched on a schedule by
            // clients that all send If-None-Match, and it changes only when the catalogue does.
            await ApiResponse.WriteTextAsync(http, Sitemap(http, games), "application/xml; charset=utf-8");
        });

        return app;
    }

    /// <summary>
    /// What a crawler may have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything, less the routes that are not documents. <c>/games/random</c> answers with a
    /// different game every time, so a crawler following it indexes nothing and comes back for ever;
    /// the account and claim routes belong to whoever is signed in; <c>/api</c> answers the same
    /// facts in a format built for a program that already knows it wants them.
    /// </para>
    /// <para>
    /// <b>No <c>Crawl-delay</c> and no rate advice.</b> This site's own crawler is polite because
    /// spec §11 makes it so, and asking for the same courtesy in a file the impolite ignore buys
    /// nothing. The facet permutations that would otherwise be the real cost are handled by the
    /// canonical link instead, which is the mechanism specified for it.
    /// </para>
    /// </remarks>
    private static string Robots(string sitemap)
    {
        var text = new StringBuilder();

        text.AppendLine("User-agent: *");
        text.AppendLine("Disallow: /games/random");
        text.AppendLine("Disallow: /account");
        text.AppendLine("Disallow: /api/");
        text.AppendLine("Allow: /");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Sitemap: {sitemap}");

        return text.ToString();
    }

    private static string Sitemap(HttpContext http, IReadOnlyList<GameSummary> games)
    {
        var output = new StringBuilder();

        // Through a writer that admits to being UTF-8. XmlWriter takes the encoding it declares from
        // the TextWriter it is given and ignores XmlWriterSettings.Encoding when there is one — and
        // a StringWriter says UTF-16, because a .NET string is. The bytes on the wire are UTF-8, so
        // the default spelling of this ships a document whose declaration contradicts its own
        // content: strict parsers reject it outright and the rest guess. Nothing in a rendered page
        // would ever show it.
        using (var text = new Utf8StringWriter(output))
        using (var xml = XmlWriter.Create(text, new XmlWriterSettings
        {
            Indent = true,
            OmitXmlDeclaration = false,
        }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

            foreach (var path in Pages)
            {
                Entry(xml, SiteUrls.Absolute(http, path), modified: null);
            }

            foreach (var document in ReferenceLibrary.Shipped.Documents)
            {
                Entry(xml, SiteUrls.Absolute(http, document.Path), modified: null);
            }

            foreach (var game in games)
            {
                // Archived games included, and with no marking that says so. Rule 3: archiving takes
                // a game out of the default listing and out of nothing else, and a sitemap that
                // dropped them would quietly make it out of the record too.
                Entry(xml, SiteUrls.Absolute(http, $"/g/{game.Slug}"), game.LastReachableAt);
            }

            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        return output.ToString();
    }

    /// <summary>
    /// The hand-written surfaces, in the order the header lists them.
    /// </summary>
    /// <remarks>
    /// No <c>changefreq</c> and no <c>priority</c>. Both are hints a crawler is free to ignore and
    /// every major one does; writing "hourly" beside a page would also be this site asserting
    /// something about itself that it had not measured, which is a habit worth not starting.
    /// </remarks>
    private static readonly string[] Pages =
    [
        "/",
        "/games",
        "/archive",
        "/rankings",
        "/reference",
        "/ecosystem",
        "/about",
        "/submit",
    ];

    private static void Entry(XmlWriter xml, string location, DateTimeOffset? modified)
    {
        xml.WriteStartElement("url");
        xml.WriteElementString("loc", location);

        if (modified is { } at)
        {
            // When we last reached the game, which is when what the page says last changed for a
            // reason. Not the render time — a page regenerated hourly from a three-year-old
            // measurement is three years old, and saying otherwise is a freshness claim nobody made.
            xml.WriteElementString("lastmod", at.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
        }

        xml.WriteEndElement();
    }

    /// <summary>A <see cref="StringWriter"/> that reports the encoding the response is actually in.</summary>
    private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
