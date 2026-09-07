using System.Xml.Linq;

namespace MUI.Web.Tests;

/// <summary>
/// The two files a crawler asks for before it asks for anything else.
/// </summary>
/// <remarks>Rule 3 says an archived game's page survives; the sitemap is where that promise stops being internal. Both are endpoints, not static files, since a static one would be stale from the first crawl cycle.</remarks>
public class SiteIndexTests
{
    private static readonly XNamespace Sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";

    [Test]
    public async Task RobotsNamesTheSitemapWithAnAbsoluteUrl()
    {
        // The Sitemap directive takes a full URL; a relative one is silently skipped by a crawler.
        await using var site = await SiteHost.StartAsync();

        var robots = await site.Client.GetStringAsync("/robots.txt");

        await Assert.That(robots).Contains("Sitemap: http");
        await Assert.That(robots).Contains("/sitemap.xml");
    }

    [Test]
    public async Task RobotsKeepsCrawlersOffTheRoutesThatAreNotDocuments()
    {
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
        // Rule 3: Gaslight Row stopped answering in 2023 and its page is still the record it existed.
        await using var site = await SiteHost.StartAsync(measured: true);

        var document = XDocument.Parse(await site.Client.GetStringAsync("/sitemap.xml"));
        var paths = Paths(document);

        await Assert.That(paths).Contains("/g/m-u-s-h");
        await Assert.That(paths).Contains("/g/gaslight-row");
    }

    [Test]
    public async Task TheFixtureSubmitsNoGameUrls()
    {
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
        // A StringWriter declares UTF-16 (since a .NET string is), so the obvious spelling ships
        // UTF-8 bytes under an encoding="utf-16" declaration.
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
