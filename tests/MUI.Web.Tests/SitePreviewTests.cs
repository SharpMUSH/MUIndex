namespace MUI.Web.Tests;

/// <summary>
/// What a link to this site looks like everywhere that is not this site.
/// </summary>
/// <remarks>
/// A game page is shared far more than it's browsed to, rendered by somebody else's unfurler from
/// the head alone — so the five rules reach into the head exactly as they reach into the page (an
/// unlabelled count, a fabricated zero). The demo banner also can't follow: <c>MainLayout</c> writes
/// it into the body, which no unfurler renders, so the fixture has to say so in the metadata itself.
/// </remarks>
public class SitePreviewTests
{
    [Test]
    public async Task ACanonicalUrlDropsTheQueryThatDidNotChangeTheDocument()
    {
        // Both are the same document, not a second page — otherwise a search engine sees unbounded
        // near-duplicate URLs.
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/g/m-u-s-h?plain=1");

        var canonical = Head.Link(body, "canonical");

        await Assert.That(canonical).IsNotNull();
        await Assert.That(canonical!).EndsWith("/g/m-u-s-h");
        await Assert.That(canonical).DoesNotContain("plain");
    }

    [Test]
    public async Task ThePreviewUrlIsAbsoluteAndSoIsTheImage()
    {
        // An unfurler has no base to resolve a relative path against; a relative og:image is simply not fetched.
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/g/m-u-s-h");

        await Assert.That(Head.Meta(body, "og:url")!).StartsWith("http");
        await Assert.That(Head.Meta(body, "og:image")!).StartsWith("http");
        await Assert.That(Head.Meta(body, "twitter:image")!).StartsWith("http");
    }

    [Test]
    public async Task AGameDescriptionQuotesItsCountWithTheAgeOfTheMeasurement()
    {
        // Rule 1, with no room for a chip: the number and its age travel together or the number doesn't travel.
        await using var site = await SiteHost.StartAsync(
            measured: true, clock: FixedClock.AtFixtureNow());

        var body = await site.Client.GetStringAsync("/g/m-u-s-h");
        var description = Head.Meta(body, "og:description")!;

        await Assert.That(description).Contains("15 players");
        await Assert.That(description).Contains("measured");
        await Assert.That(description).Contains("4m ago");
    }

    [Test]
    public async Task ACountAGameDeclaredIsNotDescribedAsOneWeMeasured()
    {
        // Declared via MSSP, not measured by us — calling it "measured" would be rule 5's failure.
        await using var site = await SiteHost.StartAsync(
            measured: true, clock: FixedClock.AtFixtureNow());

        var body = await site.Client.GetStringAsync("/g/ashen-court");
        var description = Head.Meta(body, "og:description")!;

        await Assert.That(description).Contains("9 players");
        await Assert.That(description).Contains("declared");
        await Assert.That(description).DoesNotContain("measured");
    }

    [Test]
    public async Task AGameWhoseCountCannotBeReadIsNotDescribedAsEmpty()
    {
        // Rule 4: nothing in the answer is countable, so the honest preview omits the number rather
        // than rounding our parser's limit down to zero.
        await using var site = await SiteHost.StartAsync(measured: true);

        var body = await site.Client.GetStringAsync("/g/midnight-sun");
        var description = Head.Meta(body, "og:description")!;

        await Assert.That(description).DoesNotContain("0 players");
        await Assert.That(description).DoesNotContain("no players");
    }

    [Test]
    public async Task AMeasuredZeroIsACountAndIsPublished()
    {
        // The other half of rule 4: a measured zero is a fact about the game and must still publish.
        await using var site = await SiteHost.StartAsync(measured: true);

        var body = await site.Client.GetStringAsync("/g/eldertale");

        await Assert.That(Head.Meta(body, "og:description")!).Contains("0 players");
    }

    [Test]
    public async Task TheFixtureSaysSoInTheMetadataBecauseTheBannerCannotReachThere()
    {
        // The demo banner is body copy that no unfurler renders; without this, a pasted demo link is
        // indistinguishable from a measured one in the context where a reader can least check.
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/g/m-u-s-h");

        await Assert.That(Head.Meta(body, "og:description")!).StartsWith("Demo data");
        await Assert.That(Head.Meta(body, "description")!).StartsWith("Demo data");
    }

    [Test]
    public async Task NoStructuredDataIsPublishedOverTheFixture()
    {
        // JSON-LD is read by machines that won't read the disclaimer beside it, so absent is the only honest answer over invented data.
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/g/m-u-s-h");

        await Assert.That(Head.StructuredData(body)).IsEmpty();
    }

    [Test]
    public async Task AMeasuredGamePagePublishesStructuredData()
    {
        await using var site = await SiteHost.StartAsync(measured: true);

        var body = await site.Client.GetStringAsync("/g/m-u-s-h");
        var blocks = Head.StructuredData(body);

        await Assert.That(blocks).IsNotEmpty();
        await Assert.That(blocks[0]).Contains("VideoGame");
        await Assert.That(blocks[0]).Contains("M*U*S*H");
    }

    [Test]
    public async Task EveryIconTheHeadNamesIsActuallyServed()
    {
        // Reads paths off the document rather than restating them, so this doesn't rot the day one is renamed.
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/");

        string[] rels = ["icon", "apple-touch-icon", "manifest"];
        var named = rels.SelectMany(rel => Head.Links(body, rel)).ToList();

        await Assert.That(named).IsNotEmpty();

        foreach (var href in named)
        {
            var response = await site.Client.GetAsync(href);

            await Assert.That(response.IsSuccessStatusCode)
                .IsTrue()
                .Because($"the head names {href}");
        }
    }

    [Test]
    public async Task TheHeadColoursTheBrowserChromeForBothThemes()
    {
        // Neither theme is "the real one"; a single theme-color would hand mobile Safari one as the truth.
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/");

        await Assert.That(body).Contains("name=\"theme-color\"");
        await Assert.That(body).Contains("prefers-color-scheme: dark");
        await Assert.That(body).Contains("prefers-color-scheme: light");
    }

    [Test]
    public async Task EveryIconTheManifestNamesIsServedToo()
    {
        // A second list of the same files, read by a different consumer.
        await using var site = await SiteHost.StartAsync();

        var manifest = await site.Client.GetStringAsync("/site.webmanifest");
        using var document = System.Text.Json.JsonDocument.Parse(manifest);

        var icons = document.RootElement.GetProperty("icons").EnumerateArray().ToList();

        await Assert.That(icons).IsNotEmpty();

        foreach (var icon in icons)
        {
            var src = icon.GetProperty("src").GetString()!;
            var response = await site.Client.GetAsync(src);

            await Assert.That(response.IsSuccessStatusCode)
                .IsTrue()
                .Because($"the manifest names {src}");
        }
    }

    [Test]
    public async Task EveryPageHasADescriptionOfItsOwn()
    {
        // A repeated description is one a search engine discards.
        await using var site = await SiteHost.StartAsync();

        string[] paths = ["/", "/games", "/archive", "/rankings", "/reference", "/ecosystem", "/about"];
        var seen = new List<string>();

        foreach (var path in paths)
        {
            var body = await site.Client.GetStringAsync(path);
            var description = Head.Meta(body, "description");

            await Assert.That(description).IsNotNull().Because($"{path} has a description");
            seen.Add(description!);
        }

        await Assert.That(seen.Distinct().Count()).IsEqualTo(paths.Length);
    }

    [Test]
    public async Task BehindATrustedProxyTheCanonicalUrlKeepsTheSchemeTheReaderUsed()
    {
        // Behind TLS termination, Request.Scheme is http, so absolute URLs would otherwise name a
        // scheme the reader didn't use.
        await using var site = await SiteHost.StartAsync(
            new Dictionary<string, string?> { ["Submissions:TrustedProxyHops"] = "1" });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/g/m-u-s-h");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await site.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(Head.Link(body, "canonical")!).StartsWith("https://");
    }

    [Test]
    public async Task AForwardedSchemeNobodyVouchedForIsIgnored()
    {
        // Same rule as the submitter address: a header is trusted because the deployment configured
        // it, never because it arrived.
        await using var site = await SiteHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/g/m-u-s-h");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await site.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(Head.Link(body, "canonical")!).StartsWith("http://");
    }
}
