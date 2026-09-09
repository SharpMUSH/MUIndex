using System.Globalization;
using System.Text;
using System.Xml;

using MUI.Catalog;
using Microsoft.Extensions.Options;

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

    /// <summary>The convention's fixed address. See <see cref="Llms"/> for what this is and is not.</summary>
    private const string LlmsPath = "/llms.txt";

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

            // The category listings, taken from the same facet pass the panel draws, so a URL here
            // is one the site itself links to and one that returns rows. Over the fixture there are
            // no real categories to submit, same rule as the games above.
            var categories = catalogue.IsMeasured
                ? Categories(await queries.SearchAsync(Listing, http.RequestAborted))
                : [];

            // Through the API's writer for the ETag: re-fetched on a schedule by clients that send
            // If-None-Match.
            await ApiResponse.WriteTextAsync(
                http, Sitemap(http, games, categories), "application/xml; charset=utf-8");
        });

        app.MapGet(
            LlmsPath,
            (HttpContext http, CatalogueSource catalogue, IOptions<DatasetLicenceOptions> licence) =>
                Results.Text(
                    Llms(http, catalogue.IsMeasured, licence.Value),
                    "text/plain; charset=utf-8"));

        return app;
    }

    /// <summary>The filter a bare <c>/games</c> uses, whose facets are the categories worth submitting.</summary>
    /// <remarks>
    /// <see cref="GameFilter.IncludeAdult"/> written the listing surface's way rather than the
    /// record's, the same as <see cref="IndexableFacet"/>'s baseline — a sitemap must not advertise
    /// a category whose page would come back with different rows.
    /// </remarks>
    private static readonly GameFilter Listing = new() { IncludeAdult = false };

    /// <summary>
    /// Every faceted listing that has a page of its own and something on it.
    /// </summary>
    /// <remarks>
    /// A value nothing matches is skipped — the panel keeps a selected value visible at zero so it
    /// can be undone, and submitting that would be advertising an empty page. So is the unknown
    /// token: "games whose codebase we could not read" is a real question and not a category
    /// anybody searches for by name.
    /// </remarks>
    private static IReadOnlyList<string> Categories(GameListing listing) =>
    [
        .. IndexableFacet.Dimensions
            .SelectMany(key => listing.Facets
                .Where(group => string.Equals(group.Key, key, StringComparison.Ordinal))
                .SelectMany(group => group.Values
                    .Where(value => value is { IsUnknown: false, Count: > 0 })
                    .Select(value => "/games" + IndexableFacet.Query(
                        new IndexableFacet.Category(key, value.Token))))),
    ];

    /// <summary>
    /// What a crawler may have.
    /// </summary>
    /// <remarks>
    /// Everything but the routes that aren't documents: <c>/games/random</c> answers differently
    /// every time, and account/claim routes belong to whoever is signed in.
    /// Faceted listings remain crawlable. Canonical links guide indexing; they do not prevent
    /// fetching, so serving these URLs efficiently is the application's responsibility.
    /// </remarks>
    private static string Robots(string sitemap)
    {
        var text = new StringBuilder();

        text.AppendLine("User-agent: *");
        text.AppendLine("Disallow: /games/random");
        text.AppendLine("Disallow: /account");
        text.AppendLine("Disallow: /mcp");
        text.AppendLine("Disallow: /metrics");

        // A claim page is the ceremony for one game's operator, not a page about the game: signed
        // out it is the same short "you need an account first" under every one of a few thousand
        // slugs. Excluded on the same ground as /account — it belongs to whoever is signed in —
        // rather than as a crawl-budget trim. The game's own page is unaffected.
        text.AppendLine("Disallow: /g/*/claim");

        text.AppendLine("Allow: /");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Sitemap: {sitemap}");

        return text.ToString();
    }

    /// <summary>
    /// Where an agent should read this site instead of scraping its pages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is not a ranking signal and is not published as one.</b> Google has said no Search
    /// system reads <c>llms.txt</c>, and no major model provider documents it as a citation input;
    /// an analysis of 137k sites in 2026 found almost none of these files were fetched at all. It is
    /// here because the thing it is *for* is true of this site independently: there is a plain-text
    /// rendering of every page and a documented, versioned JSON API, and an agent that finds either
    /// gets better answers with less of somebody's bandwidth than one parsing the HTML. If the
    /// convention dies, this file was still the shortest honest description of how to read the site.
    /// </para>
    /// <para>
    /// <b>English, deliberately.</b> Every other surface localizes; this one addresses a program, in
    /// the language the field's own conventions are written in, and a translated copy at one fixed
    /// address would have to pick a language anyway.
    /// </para>
    /// <para>
    /// The licence is read from configuration rather than restated, so this cannot claim terms the
    /// dump does not go out under. Over the fixture it says so in the first line, for the same
    /// reason the preview metadata does: this file is read where no banner reaches.
    /// </para>
    /// </remarks>
    private static string Llms(HttpContext http, bool measured, DatasetLicenceOptions licence)
    {
        var text = new StringBuilder();

        text.AppendLine("# mu*index");
        text.AppendLine();

        if (!measured)
        {
            text.AppendLine("> DEMO DATA — no database is configured on this deployment, so every "
                + "game below is a fixture and nothing here was measured. Do not cite it.");
            text.AppendLine();
        }

        text.AppendLine("> A directory of the MU* hobby — MUSHes, MUDs, MUCKs and MOOs. Every value "
            + "carries how it was obtained and when it was last confirmed. Values we took by "
            + "connecting to a game are labelled measured; values a game published about itself are "
            + "labelled declared; the two are shown side by side and never merged into one number.");
        text.AppendLine();

        text.AppendLine("Prefer the JSON API to parsing these pages: it is versioned, documented, "
            + "and every value in it carries its source and the instant it was last confirmed. "
            + "Failing that, append `?plain=1` to any page for a text rendering of the same facts, "
            + "wrapped to 80 columns, with every state written as a word rather than drawn as a "
            + "colour or a cell shape.");
        text.AppendLine();

        text.AppendLine("## Data");
        text.AppendLine();
        Link(text, "OpenAPI contract", ApiRoutes.OpenApi, "every endpoint below, machine-readable");
        Link(text, "API guide", ApiRoutes.Documentation, "what each endpoint is for, and the limits on it");
        Link(text, "Games", ApiRoutes.Games, "the catalogue, with the same facets the listing page offers");
        Link(text, "Bulk export", ApiRoutes.Dump, "the whole catalogue as one JSON document");
        Link(text, "Bulk export, by line", ApiRoutes.DumpLines, "the same, one game per line, for a loader");
        text.AppendLine();

        text.AppendLine("## Pages");
        text.AppendLine();
        Link(text, "Games", "/games", "every game we have reached, faceted on what we measured");
        Link(text, "Rankings", "/rankings", "busiest and most reachable, computed from measurements");
        Link(text, "Ecosystem", "/ecosystem", "codebase share and protocol adoption, as shares never totals");
        Link(text, "Reference", "/reference", "written pages on the codebases, clients and protocols");
        Link(text, "Archive", "/archive", "games that went dark, still probed, nothing deleted");
        Link(text, "Crawler", "/crawler", "what the crawler did, and what it is due to do");
        Link(text, "About", "/about", "how the catalogue is built, and how an operator opts out");
        text.AppendLine();

        text.AppendLine("## Reading a value correctly");
        text.AppendLine();
        text.AppendLine("- **Measured** means we connected and observed it. **Declared** means the "
            + "game published it about itself and we did not verify it. **Derived** means it is a "
            + "classification of ours. Reporting a declared value as measured misstates it.");
        text.AppendLine("- **Reachable, never uptime.** We measure a socket from one vantage point "
            + "at intervals. A game with a routing problem to our host is unreachable and perfectly "
            + "alive; we did not measure whether it was up.");
        text.AppendLine("- **An hour has three states.** Counted (including a measured zero); "
            + "reached but publishing no count we could read; and no measurement at all. The third "
            + "names no cause — it covers an hour we could not reach and an hour we never probed "
            + "alike. An unreadable player list is never zero players.");
        text.AppendLine("- **No absolute population figure is published.** Per-codebase and "
            + "per-protocol shares are; totals are not. Do not sum the counts into one.");
        text.AppendLine("- **There are no votes, ratings or reviews here,** so nothing on this site "
            + "measures a game's quality and nothing here should be cited as ranking one.");
        text.AppendLine();

        text.AppendLine("## Licence");
        text.AppendLine();
        text.AppendLine(licence.LicenceUrl is { } url
            ? $"The published data is {licence.LicenceName} ({licence.LicenceId}), {url}."
            : $"The published data is {licence.LicenceName} ({licence.LicenceId}).");
        text.AppendLine($"Attribute it to {licence.Attribution}.");

        return text.ToString();

        void Link(StringBuilder to, string name, string path, string note) =>
            to.AppendLine(CultureInfo.InvariantCulture,
                $"- [{name}]({SiteUrls.Absolute(http, path)}): {note}");
    }

    private static string Sitemap(
        HttpContext http, IReadOnlyList<GameSummary> games, IReadOnlyList<string> categories)
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

            // The category listings. No lastmod: the page is a query over the whole catalogue, so
            // the honest answer is "whenever any of these games was last reached", which is a
            // different date from any one row's and not one this loop holds.
            foreach (var category in categories)
            {
                Entry(xml, SiteUrls.Absolute(http, category), modified: null);
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
        "/find",
        "/archive",
        "/rankings",
        "/reference",
        "/ecosystem",
        "/crawler",
        "/about",
        ApiRoutes.Documentation,
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
