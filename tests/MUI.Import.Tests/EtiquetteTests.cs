using MUI.Import.Tests.Support;

namespace MUI.Import.Tests;

/// <summary>
/// Spec §7.6's closing paragraph and §11's identification rule, as code rather than prose: ask for a
/// bulk export or a documented API in preference to scraping, do not scrape a site whose maintainer
/// nobody emailed, and never arrive anonymously. Every case here is a refusal the code makes on its
/// own.
/// </summary>
public class EtiquetteTests
{
    private static ImportEtiquette Base() => new()
    {
        SourceName = "Example Directory",
        AttributionUri = new Uri("https://example.test/"),
        RobotsUri = new Uri("https://example.test/robots.txt"),
        UserAgent = ImporterIdentity.UserAgent,
    };

    [Test]
    public async Task ABulkExportBeatsBothAnApiAndAScrape()
    {
        var etiquette = Base() with
        {
            BulkExportUri = new Uri("https://example.test/dumps/games.json"),
            ApiUri = new Uri("https://example.test/api/games"),
            ScrapeUri = new Uri("https://example.test/list"),
            ContactedMaintainer = true,
        };

        var decision = EtiquettePlanner.Decide(etiquette);

        await Assert.That(decision.Route).IsEqualTo(FetchRoute.BulkExport);
        await Assert.That(decision.Uri).IsEqualTo(new Uri("https://example.test/dumps/games.json"));
    }

    [Test]
    public async Task ADocumentedApiBeatsAScrape()
    {
        var etiquette = Base() with
        {
            ApiUri = new Uri("https://example.test/api/games"),
            ScrapeUri = new Uri("https://example.test/list"),
            ContactedMaintainer = true,
        };

        await Assert.That(EtiquettePlanner.Decide(etiquette).Route).IsEqualTo(FetchRoute.Api);
    }

    [Test]
    public async Task TheScrapeUriIsRefusedOutrightWhenAnApiExists()
    {
        var etiquette = Base() with
        {
            ApiUri = new Uri("https://example.test/api/games"),
            ScrapeUri = new Uri("https://example.test/list"),
            ContactedMaintainer = true,
        };

        await Assert.That(EtiquettePlanner.MayFetch(etiquette, new Uri("https://example.test/list"))).IsFalse();
        await Assert.That(EtiquettePlanner.MayFetch(etiquette, new Uri("https://example.test/api/games/2")))
            .IsTrue();
    }

    [Test]
    public async Task ARouteRootDoesNotAuthoriseAPathThatMerelySharesItsPrefix()
    {
        var etiquette = Base() with { ApiUri = new Uri("https://example.test/api") };

        await Assert.That(EtiquettePlanner.MayFetch(etiquette, new Uri("https://example.test/api/games")))
            .IsTrue();
        await Assert.That(EtiquettePlanner.MayFetch(etiquette, new Uri("https://example.test/apiary/secret")))
            .IsFalse();
        await Assert.That(EtiquettePlanner.MayFetch(etiquette, new Uri("https://elsewhere.test/api/games")))
            .IsFalse();
    }

    [Test]
    public async Task ScrapingIsOffUntilSomebodyHasEmailedTheMaintainer()
    {
        var etiquette = Base() with { ScrapeUri = new Uri("https://example.test/list") };

        var decision = EtiquettePlanner.Decide(etiquette);

        await Assert.That(decision.Route).IsEqualTo(FetchRoute.None);
        await Assert.That(decision.RefusedReason).IsEqualTo(EtiquettePlanner.MaintainerNotContacted);
    }

    [Test]
    public async Task ScrapingIsAllowedOnceTheMaintainerHasBeenContactedAndThereIsNoBetterRoute()
    {
        var etiquette = Base() with
        {
            ScrapeUri = new Uri("https://example.test/list"),
            ContactedMaintainer = true,
        };

        var decision = EtiquettePlanner.Decide(etiquette);

        await Assert.That(decision.Route).IsEqualTo(FetchRoute.Scrape);
        await Assert.That(EtiquettePlanner.MayFetch(etiquette, new Uri("https://example.test/list/page/2")))
            .IsTrue();
    }

    [Test]
    public async Task AnAnonymousUserAgentIsRefusedBeforeAnythingElseIsConsidered()
    {
        var etiquette = Base() with
        {
            UserAgent = "Mozilla/5.0",
            BulkExportUri = new Uri("https://example.test/dumps/games.json"),
        };

        var decision = EtiquettePlanner.Decide(etiquette);

        await Assert.That(decision.Route).IsEqualTo(FetchRoute.None);
        await Assert.That(decision.RefusedReason).IsEqualTo(EtiquettePlanner.AnonymousUserAgent);
    }

    [Test]
    public async Task OurUserAgentNamesUsAndSaysWhereToReadAboutUs()
    {
        await Assert.That(ImporterIdentity.SelfIdentifies(ImporterIdentity.UserAgent)).IsTrue();
        await Assert.That(ImporterIdentity.UserAgent).Contains(ImporterIdentity.Product);
        await Assert.That(ImporterIdentity.UserAgent).Contains(ImporterIdentity.InfoUrl);
    }

    [Test]
    public async Task ASourceWithNoRouteAtAllIsRefusedRatherThanGuessedAt()
    {
        var decision = EtiquettePlanner.Decide(Base());

        await Assert.That(decision.Route).IsEqualTo(FetchRoute.None);
        await Assert.That(decision.RefusedReason).IsEqualTo(EtiquettePlanner.NothingConfigured);
    }

    [Test]
    public async Task APipelineRefusesToRunASourceItMayNotFetchEvenAsARehearsal()
    {
        var harness = Harness.Build(new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero));
        var gated = Base() with { ScrapeUri = new Uri("https://example.test/list") };
        var source = new FakeSource("Example Directory", ImportTier.Asserted, gated, []);

        await Assert.That(async () => await harness.Pipeline.RunAsync(source, CancellationToken.None))
            .Throws<EtiquetteViolationException>();

        // A dry run is not a licence either — and the source was never enumerated, so nothing was read.
        await Assert.That(async () => await harness.Pipeline.DryRunAsync(source, CancellationToken.None))
            .Throws<EtiquetteViolationException>();
        await Assert.That(source.Enumerated).IsFalse();
    }
}
