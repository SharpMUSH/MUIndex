using MUI.Import.Tests.Support;

namespace MUI.Import.Tests;

/// <summary>
/// Spec §7.6: honour <c>robots.txt</c>, and rate-limit hard. Driven entirely by an injected clock, so
/// the suite is deterministic and instant.
/// </summary>
public class RobotsAndRateLimitTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static ImportEtiquette Etiquette(TimeSpan minimum) => new()
    {
        SourceName = "Example Directory",
        AttributionUri = new Uri("https://example.test/"),
        RobotsUri = new Uri("https://example.test/robots.txt"),
        ApiUri = new Uri("https://example.test/api"),
        UserAgent = ImporterIdentity.UserAgent,
        MinimumInterval = minimum,
    };

    [Test]
    public async Task AnEmptyDisallowForbidsNothing()
    {
        var policy = RobotsPolicy.Parse("User-agent: *\nDisallow:\n");

        await Assert.That(policy.Allows("/anything", ImporterIdentity.UserAgent)).IsTrue();
    }

    [Test]
    public async Task TheLongestMatchingRuleWinsAndAllowBeatsDisallowAtEqualLength()
    {
        var policy = RobotsPolicy.Parse("""
            User-agent: *
            Disallow: /private/
            Allow: /private/public/
            """);

        await Assert.That(policy.Allows("/private/secret", ImporterIdentity.UserAgent)).IsFalse();
        await Assert.That(policy.Allows("/private/public/list", ImporterIdentity.UserAgent)).IsTrue();
    }

    [Test]
    public async Task AGroupNamingUsSpecificallyBeatsTheWildcard()
    {
        var policy = RobotsPolicy.Parse("""
            User-agent: *
            Disallow: /

            User-agent: MUIndex-Importer
            Disallow: /admin/
            """);

        await Assert.That(policy.Allows("/list", ImporterIdentity.UserAgent)).IsTrue();
        await Assert.That(policy.Allows("/admin/", ImporterIdentity.UserAgent)).IsFalse();
    }

    [Test]
    public async Task CommentsAndWildcardsAreUnderstood()
    {
        var policy = RobotsPolicy.Parse("""
            # a comment
            User-agent: *
            Disallow: /*.json$   # and a trailing one
            """);

        await Assert.That(policy.Allows("/dumps/games.json", ImporterIdentity.UserAgent)).IsFalse();
        await Assert.That(policy.Allows("/dumps/games.jsonp", ImporterIdentity.UserAgent)).IsTrue();
    }

    [Test]
    public async Task NothingMayBeFetchedUntilRobotsHasBeenRead()
    {
        var gate = new PolitenessGate(Etiquette(TimeSpan.FromSeconds(5)), new ManualTimeProvider(Start));

        await Assert.That(gate.RobotsAdopted).IsFalse();
        await Assert.That(gate.MayFetch("/api/games")).IsFalse();

        gate.AdoptRobots(RobotsPolicy.AllowAll);

        await Assert.That(gate.RobotsAdopted).IsTrue();
        await Assert.That(gate.MayFetch("/api/games")).IsTrue();
    }

    [Test]
    public async Task TheSecondFetchWaitsOutTheRemainderOfTheInterval()
    {
        var time = new ManualTimeProvider(Start);
        var gate = new PolitenessGate(Etiquette(TimeSpan.FromSeconds(5)), time);
        gate.AdoptRobots(RobotsPolicy.AllowAll);

        await Assert.That(gate.WaitFor(Start)).IsEqualTo(TimeSpan.Zero);

        await gate.EnterAsync(CancellationToken.None);

        await Assert.That(gate.LastFetchAt).IsEqualTo(Start);

        time.Advance(TimeSpan.FromSeconds(2));

        await Assert.That(gate.WaitFor(time.GetUtcNow())).IsEqualTo(TimeSpan.FromSeconds(3));

        time.Advance(TimeSpan.FromSeconds(3));

        await Assert.That(gate.WaitFor(time.GetUtcNow())).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task ALongerCrawlDelayInRobotsReplacesOurConfiguredMinimum()
    {
        var gate = new PolitenessGate(Etiquette(TimeSpan.FromSeconds(5)), new ManualTimeProvider(Start));
        gate.AdoptRobots(RobotsPolicy.Parse("User-agent: *\nCrawl-delay: 30\n"));

        await Assert.That(gate.EffectiveInterval).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task AShorterCrawlDelayDoesNotLicenceUsToGoFaster()
    {
        var gate = new PolitenessGate(Etiquette(TimeSpan.FromSeconds(5)), new ManualTimeProvider(Start));

        // The real tintin.mudhalla.net robots.txt says exactly this, and it does not mean "hammer us".
        gate.AdoptRobots(RobotsPolicy.Parse("User-agent: *\nCrawl-delay: 0\n"));

        await Assert.That(gate.EffectiveInterval).IsEqualTo(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task AContentFetchBeforeRobotsIsRefused()
    {
        var (handler, client) = FakeHttp.Serving(("https://example.test/api", "{}"));
        var fetcher = new DirectoryFetcher(client, Etiquette(TimeSpan.FromSeconds(5)), new ManualTimeProvider(Start));

        await Assert.That(async () =>
                await fetcher.GetStringAsync(new Uri("https://example.test/api"), CancellationToken.None))
            .Throws<EtiquetteViolationException>();

        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task RobotsIsTheFirstRequestWeEverMakeToASiteAndEveryRequestSaysWhoWeAre()
    {
        var (handler, client) = FakeHttp.Serving(
            ("https://example.test/robots.txt", "User-agent: *\nDisallow:\n"),
            ("https://example.test/api", "{}"));
        var fetcher = new DirectoryFetcher(client, Etiquette(TimeSpan.FromSeconds(5)), new ManualTimeProvider(Start));

        await fetcher.PrimeRobotsAsync(CancellationToken.None);
        await fetcher.GetStringAsync(new Uri("https://example.test/api"), CancellationToken.None);

        await Assert.That(handler.Requests[0].Uri).IsEqualTo("https://example.test/robots.txt");
        await Assert.That(handler.Requests[1].Uri).IsEqualTo("https://example.test/api");

        foreach (var request in handler.Requests)
        {
            await Assert.That(request.UserAgent).IsNotNull();
            await Assert.That(ImporterIdentity.SelfIdentifies(request.UserAgent!)).IsTrue();
        }
    }

    [Test]
    public async Task ADisallowedPathIsNeverFetched()
    {
        var (handler, client) = FakeHttp.Serving(
            ("https://example.test/robots.txt", "User-agent: *\nDisallow: /api/\n"),
            ("https://example.test/api/games", "{}"));
        var fetcher = new DirectoryFetcher(client, Etiquette(TimeSpan.FromSeconds(5)), new ManualTimeProvider(Start));
        await fetcher.PrimeRobotsAsync(CancellationToken.None);

        await Assert.That(async () =>
                await fetcher.GetStringAsync(new Uri("https://example.test/api/games"), CancellationToken.None))
            .Throws<EtiquetteViolationException>();

        await Assert.That(handler.Requests.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task TheScrapeUriIsNeverFetchedWhileAnApiIsConfigured()
    {
        var etiquette = Etiquette(TimeSpan.FromSeconds(5)) with
        {
            ScrapeUri = new Uri("https://example.test/list"),
            ContactedMaintainer = true,
        };
        var (handler, client) = FakeHttp.Serving(
            ("https://example.test/robots.txt", "User-agent: *\nDisallow:\n"),
            ("https://example.test/list", "<html></html>"));
        var fetcher = new DirectoryFetcher(client, etiquette, new ManualTimeProvider(Start));
        await fetcher.PrimeRobotsAsync(CancellationToken.None);

        await Assert.That(async () =>
                await fetcher.GetStringAsync(new Uri("https://example.test/list"), CancellationToken.None))
            .Throws<EtiquetteViolationException>();

        await Assert.That(handler.Requests.Any(request => request.Uri == "https://example.test/list")).IsFalse();
    }

    [Test]
    public async Task AMissingRobotsFileMeansAllowAllRatherThanRefuseAll()
    {
        // mudstats.com genuinely answers 404 here, which is the ordinary case for a small site.
        var (_, client) = FakeHttp.Serving(("https://example.test/api", "{}"));
        var fetcher = new DirectoryFetcher(client, Etiquette(TimeSpan.FromSeconds(5)), new ManualTimeProvider(Start));

        await fetcher.PrimeRobotsAsync(CancellationToken.None);

        await Assert.That(fetcher.Gate.RobotsAdopted).IsTrue();
        await Assert.That(await fetcher.GetStringAsync(new Uri("https://example.test/api"), CancellationToken.None))
            .IsEqualTo("{}");
    }

    [Test]
    public async Task ARobotsCrawlDelayIsAdoptedByTheGate()
    {
        var (_, client) = FakeHttp.Serving(
            ("https://example.test/robots.txt", "User-agent: *\nCrawl-delay: 45\n"));
        var fetcher = new DirectoryFetcher(client, Etiquette(TimeSpan.FromSeconds(5)), new ManualTimeProvider(Start));

        await fetcher.PrimeRobotsAsync(CancellationToken.None);

        await Assert.That(fetcher.Gate.EffectiveInterval).IsEqualTo(TimeSpan.FromSeconds(45));
    }

    [Test]
    public async Task APageThatIsNotThereIsNullRatherThanTheEndOfTheRun()
    {
        // Found by running the importer against the live site: MudStats' index links 144 worlds and
        // one of them, /World/TheChattingZone, answers "404 No such world." On GetStringAsync that
        // one stale link ended the whole import at the hundredth page.
        var (_, client) = FakeHttp.Serving(("https://example.test/api/present", "{}"));
        var fetcher = new DirectoryFetcher(client, Etiquette(TimeSpan.Zero), new ManualTimeProvider(Start));

        await fetcher.PrimeRobotsAsync(CancellationToken.None);

        await Assert.That(
                await fetcher.TryGetStringAsync(new Uri("https://example.test/api/gone"), CancellationToken.None))
            .IsNull();
        await Assert.That(
                await fetcher.TryGetStringAsync(new Uri("https://example.test/api/present"), CancellationToken.None))
            .IsEqualTo("{}");
    }

    [Test]
    public async Task ASiteAskingUsToStopIsNotAPageThatIsNotThere()
    {
        // The distinction the whole method rests on. 404 means the page does not exist; 429, 503 or a
        // reset mean we are being asked to stop or the site is unwell — and swallowing those would
        // walk a struggling server to the end of its index and report a catalogue full of holes.
        var handler = new FakeHttp.Handler(new Dictionary<string, (System.Net.HttpStatusCode, string)>
        {
            ["https://example.test/api/busy"] = (System.Net.HttpStatusCode.TooManyRequests, "slow down"),
        });

        var fetcher = new DirectoryFetcher(
            new HttpClient(handler), Etiquette(TimeSpan.Zero), new ManualTimeProvider(Start));

        await fetcher.PrimeRobotsAsync(CancellationToken.None);

        await Assert.That(async () =>
                await fetcher.TryGetStringAsync(new Uri("https://example.test/api/busy"), CancellationToken.None))
            .Throws<HttpRequestException>();
    }

    [Test]
    public async Task AMissingPageIsRefusedByTheEtiquetteJustAsAPresentOneWouldBe()
    {
        // TryGetStringAsync is the same fetch with one status handled differently. It is not a way
        // round robots.txt, the route, or the rate limit.
        var (handler, client) = FakeHttp.Serving(
            ("https://example.test/robots.txt", "User-agent: *\nDisallow: /api/\n"));

        var fetcher = new DirectoryFetcher(client, Etiquette(TimeSpan.Zero), new ManualTimeProvider(Start));
        await fetcher.PrimeRobotsAsync(CancellationToken.None);

        await Assert.That(async () =>
                await fetcher.TryGetStringAsync(new Uri("https://example.test/api/gone"), CancellationToken.None))
            .Throws<EtiquetteViolationException>();
        await Assert.That(handler.Requests.Any(request => request.Uri.Contains("/api/", StringComparison.Ordinal)))
            .IsFalse();
    }
}
