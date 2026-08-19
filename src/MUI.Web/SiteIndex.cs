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
/// The archive (spec §7.4 keeps every dark game's page alive forever) and the reference section are
/// the parts of this catalogue least likely to be reached by following links, and most worth finding.
/// <b>Endpoints, not files in <c>wwwroot</c></b> — a static sitemap is a second copy wrong by the end
/// of the first crawl cycle.
/// <b>Nothing invented is submitted.</b> Over the demo fixture, game URLs are left out entirely — a
/// sitemap has no field to say the games are made up.
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

            // Through the API's writer for the ETag: re-fetched on a schedule by clients that send
            // If-None-Match.
            await ApiResponse.WriteTextAsync(http, Sitemap(http, games), "application/xml; charset=utf-8");
        });

        return app;
    }

    /// <summary>
    /// What a crawler may have.
    /// </summary>
    /// <remarks>
    /// Everything but the routes that aren't documents: <c>/games/random</c> answers differently
    /// every time, and account/claim routes belong to whoever is signed in.
    /// <b>No <c>Crawl-delay</c> and no rate advice</b> — the facet permutations that would otherwise
    /// be the real cost are handled by the canonical link instead.
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

        // Through a writer that admits to being UTF-8: XmlWriter takes its declared encoding from the
        // TextWriter, and a plain StringWriter reports UTF-16, contradicting the UTF-8 bytes actually
        // sent — strict parsers reject that outright.
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
                // Archived games included, unmarked — rule 3: archiving removes a game from the
                // default listing and nothing else.
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
    /// <remarks>No <c>changefreq</c> or <c>priority</c> — both are hints every major crawler ignores, and writing "hourly" would be an unmeasured claim about itself.</remarks>
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
            // When we last reached the game, not the render time — a page regenerated hourly from a
            // three-year-old measurement is still three years old.
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
