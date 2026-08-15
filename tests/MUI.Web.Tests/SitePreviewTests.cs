namespace MUI.Web.Tests;

/// <summary>
/// What a link to this site looks like everywhere that is not this site.
/// </summary>
/// <remarks>
/// <para>
/// A game page is shared far more often than it is browsed to, and every one of those shares is
/// rendered by somebody else's unfurler from the head of the document. That makes the head a
/// <em>surface</em> in the sense spec §9 means it, and the five rules reach into it exactly as they
/// reach into the page: a preview that quotes a count without its age has published an unlabelled
/// fact, and a preview that says "0 players" for a game we could not count has fabricated one.
/// </para>
/// <para>
/// It is also the surface where the demo banner cannot follow. <c>MainLayout</c> writes "nothing
/// here was measured" into the body, and no unfurler renders the body — so the fixture has to say
/// so in the metadata itself or not at all.
/// </para>
/// </remarks>
public class SitePreviewTests
{
    [Test]
    public async Task ACanonicalUrlDropsTheQueryThatDidNotChangeTheDocument()
    {
        // ?plain=1 is the same document rendered for a text browser, and a facet permutation is a
        // question about the listing rather than a second page. Both would otherwise present a
        // search engine with an unbounded supply of near-duplicate URLs for one document.
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
        // An unfurler has no base to resolve a relative path against; it has the URL it was handed
        // and the bytes at the end of it. A relative og:image is simply not fetched.
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/g/m-u-s-h");

        await Assert.That(Head.Meta(body, "og:url")!).StartsWith("http");
        await Assert.That(Head.Meta(body, "og:image")!).StartsWith("http");
        await Assert.That(Head.Meta(body, "twitter:image")!).StartsWith("http");
    }

    [Test]
    public async Task AGameDescriptionQuotesItsCountWithTheAgeOfTheMeasurement()
    {
        // Rule 1, in the one place there is no room for a chip: the number and how old it is travel
        // together or the number does not travel. M*U*S*H answers a pre-login WHO four minutes
        // before the fixture's clock.
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
        // Ashen Court publishes PLAYERS in MSSP and answers no pre-login WHO. Calling that
        // "measured" in a preview is rule 5's failure in miniature — our inability to check it,
        // republished as a fact we established.
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
        // Rule 4. Midnight Sun answers and nothing in the answer is countable; the honest preview
        // omits the number rather than rounding our parser's limit down to zero players.
        await using var site = await SiteHost.StartAsync(measured: true);

        var body = await site.Client.GetStringAsync("/g/midnight-sun");
        var description = Head.Meta(body, "og:description")!;

        await Assert.That(description).DoesNotContain("0 players");
        await Assert.That(description).DoesNotContain("no players");
    }

    [Test]
    public async Task AMeasuredZeroIsACountAndIsPublished()
    {
        // The other half of rule 4, so omitting the unknown case does not quietly swallow the
        // measured one. We got into Eldertale and nobody was there; that is a fact about the game.
        await using var site = await SiteHost.StartAsync(measured: true);

        var body = await site.Client.GetStringAsync("/g/eldertale");

        await Assert.That(Head.Meta(body, "og:description")!).Contains("0 players");
    }

    [Test]
    public async Task TheFixtureSaysSoInTheMetadataBecauseTheBannerCannotReachThere()
    {
        // The demo banner is body copy and an unfurler renders none of the body. Without this, a
        // pasted demo link is indistinguishable from a measured one in the one context where the
        // reader has the least ability to check — which is the mechanism this project exists to
        // replace, reproduced.
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/g/m-u-s-h");

        await Assert.That(Head.Meta(body, "og:description")!).StartsWith("Demo data");
        await Assert.That(Head.Meta(body, "description")!).StartsWith("Demo data");
    }

    [Test]
    public async Task NoStructuredDataIsPublishedOverTheFixture()
    {
        // JSON-LD is read by machines that will not read the disclaimer beside it, so there is no
        // way to ship it honestly from invented data. Absent is the only correct answer.
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
        // A head that names a file nobody shipped is a broken tab icon and a broken home-screen
        // install, and neither shows up in any other test. Reading the paths off the document
        // rather than restating them here is what keeps this from rotting the day one is renamed.
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
        // The stylesheet's whole position is that neither theme is the real one. A single
        // theme-color would hand mobile Safari one of them as the truth.
        await using var site = await SiteHost.StartAsync();

        var body = await site.Client.GetStringAsync("/");

        await Assert.That(body).Contains("name=\"theme-color\"");
        await Assert.That(body).Contains("prefers-color-scheme: dark");
        await Assert.That(body).Contains("prefers-color-scheme: light");
    }

    [Test]
    public async Task EveryIconTheManifestNamesIsServedToo()
    {
        // The manifest is a second list of the same files, read by a different consumer, and
        // nothing else in the suite opens it.
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
        // A description repeated across a site is one a search engine discards, and a page with
        // none is summarised from whatever text happens to be first in the body.
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
        // The site terminates plain HTTP behind something that terminated TLS, so Request.Scheme is
        // http and every absolute URL it generates — canonical, og:url, the sitemap, the RSS feed's
        // own self-link — names a scheme the reader did not use and some consumers will not follow.
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
        // The same rule the submitter address is under: a header is trusted because a deployment
        // said how many proxies are in front of it, never because it arrived.
        await using var site = await SiteHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/g/m-u-s-h");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await site.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(Head.Link(body, "canonical")!).StartsWith("http://");
    }
}
