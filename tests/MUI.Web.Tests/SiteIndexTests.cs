using System.Xml.Linq;

namespace MUI.Web.Tests;

/// <summary>
/// The two files a crawler asks for before it asks for anything else.
/// </summary>
/// <remarks>
/// <para>
/// A game page nobody can find is a measurement nobody reads, and the archive is the part of this
/// catalogue no incumbent kept — so it is the part least likely to be indexed from links alone and
/// the part most worth submitting deliberately. Rule 3 says an archived game's page survives; the
/// sitemap is where that promise stops being internal.
/// </para>
/// <para>
/// Both are endpoints rather than files in <c>wwwroot</c>, because the sitemap enumerates the
/// catalogue and a static one would be a second copy of it, stale from the first crawl cycle.
/// </para>
/// </remarks>
public class SiteIndexTests
{
    private static readonly XNamespace Sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";

    [Test]
    public async Task RobotsNamesTheSitemapWithAnAbsoluteUrl()
    {
        // The Sitemap directive is specified as taking a full URL, and a crawler that reads a
        // relative one simply skips it — silently, which is the failure mode that survives for years.
        await using var site = await SiteHost.StartAsync();

        var robots = await site.Client.GetStringAsync("/robots.txt");

        await Assert.That(robots).Contains("Sitemap: http");
        await Assert.That(robots).Contains("/sitemap.xml");
    }

    [Test]
    public async Task RobotsKeepsCrawlersOffTheRoutesThatAreNotDocuments()
    {
        // /games/random answers a different game every time, so a crawler following it indexes
        // nothing and re-fetches for ever. The account and claim routes are somebody's session.
        await using var site = await SiteHost.StartAsync();

        var robots = await site.Client.GetStringAsync("/robots.txt");

        await Assert.That(robots).Contains("Disallow: /games/random");
        await Assert.That(robots).Contains("Disallow: /account");
    }

    [Test]
    public async Task TheSitemapIsWellFormedAndAbsolute()
    {
        await using var site = await SiteHost.StartAsync();

        var xml = await site.Client.GetStringAsync("/sitemap.xml");
        var document = XDocument.Parse(xml);

        var locations = Locations(document);

        await Assert.That(locations).IsNotEmpty();
        await Assert.That(locations.All(l => l.StartsWith("http", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task TheSitemapCarriesTheHandWrittenPages()
    {
        await using var site = await SiteHost.StartAsync();

        var document = XDocument.Parse(await site.Client.GetStringAsync("/sitemap.xml"));
        var paths = Paths(document);

        await Assert.That(paths).Contains("/");
        await Assert.That(paths).Contains("/games");
        await Assert.That(paths).Contains("/archive");
        await Assert.That(paths).Contains("/reference");
    }

    [Test]
    public async Task EveryReferencePageIsInTheSitemap()
    {
        // The reference section is hand-written prose with no inbound links from anywhere but its
        // own index, which is exactly the shape a crawler under-visits.
        await using var site = await SiteHost.StartAsync();

        var document = XDocument.Parse(await site.Client.GetStringAsync("/sitemap.xml"));
        var paths = Paths(document);

        foreach (var reference in MUI.Web.Reference.ReferenceLibrary.Shipped.Documents)
        {
            await Assert.That(paths).Contains(reference.Path);
        }
    }

    [Test]
    public async Task ArchivedGamesAreListedBesideLiveOnes()
    {
        // Rule 3, on the one surface where omission is invisible. Gaslight Row stopped answering in
        // 2023 and its page is still the record of it having existed.
        await using var site = await SiteHost.StartAsync(measured: true);

        var document = XDocument.Parse(await site.Client.GetStringAsync("/sitemap.xml"));
        var paths = Paths(document);

        await Assert.That(paths).Contains("/g/m-u-s-h");
        await Assert.That(paths).Contains("/g/gaslight-row");
    }

    [Test]
    public async Task TheFixtureSubmitsNoGameUrls()
    {
        // A sitemap is a list of pages we are asking a search engine to index, and there is no way
        // to attach "these games are invented" to an entry in it. The static pages are ours and
        // true either way; the game URLs are not.
        await using var site = await SiteHost.StartAsync();

        var document = XDocument.Parse(await site.Client.GetStringAsync("/sitemap.xml"));
        var paths = Paths(document);

        await Assert.That(paths).Contains("/about");
        await Assert.That(paths.Any(p => p.StartsWith("/g/", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task AGamesLastModifiedIsWhenWeLastReachedIt()
    {
        await using var site = await SiteHost.StartAsync(measured: true);

        var document = XDocument.Parse(await site.Client.GetStringAsync("/sitemap.xml"));

        var entry = document.Descendants(Sitemap + "url")
            .Single(u => u.Element(Sitemap + "loc")!.Value.EndsWith("/g/m-u-s-h", StringComparison.Ordinal));

        var modified = entry.Element(Sitemap + "lastmod");

        await Assert.That(modified).IsNotNull();
        await Assert.That(DateTimeOffset.Parse(modified!.Value, System.Globalization.CultureInfo.InvariantCulture))
            .IsEqualTo(MUI.Web.Fixtures.FixtureGameQueries.Now.AddMinutes(-4));
    }

    [Test]
    public async Task TheSitemapIsServedAsXml()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync("/sitemap.xml");

        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/xml");
    }

    [Test]
    public async Task TheSitemapDeclaresTheEncodingItIsActuallyIn()
    {
        // XmlWriter takes the encoding it declares from the writer it was handed, and a StringWriter
        // says UTF-16 because a .NET string is — so the obvious spelling of this ships UTF-8 bytes
        // under an encoding="utf-16" declaration. A strict parser rejects the document and a lenient
        // one guesses, and no test that reads the response as a string can see either happen. Read
        // as bytes here for exactly that reason.
        await using var site = await SiteHost.StartAsync();

        var bytes = await site.Client.GetByteArrayAsync("/sitemap.xml");
        var declaration = System.Text.Encoding.UTF8.GetString(bytes)[..Math.Min(64, bytes.Length)];

        await Assert.That(declaration).Contains("utf-8", StringComparison.OrdinalIgnoreCase);
        await Assert.That(declaration).DoesNotContain("utf-16", StringComparison.OrdinalIgnoreCase);

        using var parsed = new System.IO.MemoryStream(bytes);

        await Assert.That(XDocument.Load(parsed).Root!.Name.LocalName).IsEqualTo("urlset");
    }

    private static IReadOnlyList<string> Locations(XDocument document) =>
        [.. document.Descendants(Sitemap + "loc").Select(l => l.Value)];

    private static IReadOnlyList<string> Paths(XDocument document) =>
        [.. Locations(document).Select(l => new Uri(l).AbsolutePath)];
}
