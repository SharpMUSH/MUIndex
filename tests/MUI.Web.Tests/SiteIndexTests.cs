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
        await Assert.That(robots).DoesNotContain("Disallow: /api/");
        await Assert.That(robots).Contains("Disallow: /mcp");
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
        await Assert.That(paths).Contains("/about/api");
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

    [Test]
    public async Task RobotsKeepsCrawlersOffTheClaimCeremonyButNotTheGamePage()
    {
        // A claim page is the same short "you need an account first" under a few thousand slugs, and
        // belongs to whoever is signed in — the same ground /account is excluded on. The game's own
        // page is the record and stays crawlable.
        await using var site = await SiteHost.StartAsync();

        var robots = await site.Client.GetStringAsync("/robots.txt");

        await Assert.That(robots).Contains("Disallow: /g/*/claim");

        // Compared as parsed lines: AppendLine writes Environment.NewLine, so a literal "\n" in the
        // needle makes this assertion vacuous on Windows, where the line ends "\r\n".
        var directives = robots.Split('\n').Select(line => line.Trim()).ToList();

        await Assert.That(directives).DoesNotContain("Disallow: /g/");
    }

    [Test]
    public async Task TheSitemapCarriesEveryLinkedPageAndNotTheOnesThatAreNotDocuments()
    {
        // /find and /crawler are in the header, in the front page's own start list, and were in no
        // sitemap — findable only by following a link from a page a crawler had to reach first.
        await using var site = await SiteHost.StartAsync();

        var document = XDocument.Parse(await site.Client.GetStringAsync("/sitemap.xml"));
        var paths = Paths(document);

        await Assert.That(paths).Contains("/find");
        await Assert.That(paths).Contains("/crawler");

        // The counterpart: a route that answers differently every time is not a document.
        await Assert.That(paths).DoesNotContain("/games/random");
        await Assert.That(paths).DoesNotContain("/account");
    }

    [Test]
    public async Task TheSitemapSubmitsTheCategoryListings()
    {
        // These are the pages IndexableFacet made indexable. Without them here they are reachable
        // only by crawling the facet panel, which is the thing a sitemap exists to avoid relying on.
        await using var site = await SiteHost.StartAsync(measured: true);

        var document = XDocument.Parse(await site.Client.GetStringAsync("/sitemap.xml"));
        var addresses = Addresses(document);

        await Assert.That(addresses.Any(a => a.Contains("/games?codebase=", StringComparison.Ordinal)))
            .IsTrue()
            .Because("the codebase facet has values in the fixture");

        // Every submitted category is a page that says it is that category — i.e. it is
        // self-canonical rather than pointing back at /games, which would make submitting it a
        // contradiction.
        foreach (var address in addresses.Where(a => a.Contains('?', StringComparison.Ordinal)))
        {
            var canonical = Head.Link(await site.Client.GetStringAsync(address), "canonical");

            await Assert.That(canonical).IsNotNull();
            await Assert.That(canonical!).EndsWith(address).Because($"{address} is submitted as its own page");
        }
    }

    [Test]
    public async Task TheFixtureSubmitsNoCategoryEitherBecauseItHasNoRealOnes()
    {
        // Same rule as the game URLs above: a sitemap has no field for "these are made up".
        await using var site = await SiteHost.StartAsync();

        var document = XDocument.Parse(await site.Client.GetStringAsync("/sitemap.xml"));

        await Assert.That(Addresses(document).Any(a => a.Contains('?', StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task LlmsTxtSendsAnAgentToTheApiAndTheTextMirrorRatherThanTheMarkup()
    {
        await using var site = await SiteHost.StartAsync();

        var text = await site.Client.GetStringAsync("/llms.txt");

        await Assert.That(text).StartsWith("# mu*index");
        await Assert.That(text).Contains("/api/openapi.json");
        await Assert.That(text).Contains("/api/dump/games.ndjson");
        await Assert.That(text).Contains("?plain=1");
    }

    [Test]
    public async Task LlmsTxtStatesTheRulesForReadingAValueAndTheLicenceItGoesOutUnder()
    {
        // An agent reading this is exactly the reader most likely to report a declared count as a
        // measured one, or an unreadable player list as nobody playing.
        await using var site = await SiteHost.StartAsync();

        var text = await site.Client.GetStringAsync("/llms.txt");

        await Assert.That(text).Contains("Declared");
        await Assert.That(text).Contains("Reachable, never uptime");
        await Assert.That(text).Contains("three states");

        // Read from configuration, so it cannot claim terms the dump does not go out under.
        await Assert.That(text).Contains("CC-BY-4.0");
    }

    [Test]
    public async Task LlmsTxtConfessesOverTheFixtureAndDoesNotWhenMeasured()
    {
        // No banner reaches this file, same argument as the preview metadata.
        await using var fixture = await SiteHost.StartAsync();
        await using var measured = await SiteHost.StartAsync(measured: true);

        await Assert.That(await fixture.Client.GetStringAsync("/llms.txt")).Contains("DEMO DATA");
        await Assert.That(await measured.Client.GetStringAsync("/llms.txt")).DoesNotContain("DEMO DATA");
    }

    [Test]
    public async Task LlmsTxtIsServedAsText()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync("/llms.txt");

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/plain");
    }

    private static IReadOnlyList<string> Locations(XDocument document) =>
        [.. document.Descendants(Sitemap + "loc").Select(l => l.Value)];

    private static IReadOnlyList<string> Paths(XDocument document) =>
        [.. Locations(document).Select(l => new Uri(l).AbsolutePath)];

    /// <summary>Path and query — what <see cref="Paths"/> drops, and what a category listing is.</summary>
    private static IReadOnlyList<string> Addresses(XDocument document) =>
        [.. Locations(document).Select(l => new Uri(l).PathAndQuery)];
}
