using MUI.Catalog.Persistence;
using MUI.Import.Sources;
using MUI.Import.Tests.Support;

namespace MUI.Import.Tests.Sources;

/// <summary>
/// MudStats world pages, read from a recorded fixture.
/// </summary>
/// <remarks>
/// The trap this file exists to hold shut: the page puts a field's label and its value in
/// <em>separate elements</em>, so a regex anchored on <c>Address:</c> captures the whitespace between
/// two closing tags and every field on the page comes back empty. The ids are what is read.
/// </remarks>
public class MudStatsSourceTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly Uri WorldUri = new("https://mudstats.com/World/4Dimensions");

    private static string Page() => Fixture.Read("mudstats-world-4dimensions.html");

    [Test]
    public async Task ItIsMeasuredBecauseTheSitePings()
    {
        var (_, client) = FakeHttp.Serving();
        var source = MudStatsSource.Create(client, new ManualTimeProvider(Start));

        await Assert.That(source.Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(source.SourceName).IsEqualTo("MudStats");
    }

    [Test]
    public async Task ItIsAScrapeThatMayNowRunBecauseItsMaintainerHasBeenApproached()
    {
        // The gate was satisfied, not removed. This one source's default is true because it is true
        // in the world; the mechanism is unchanged and still refuses every directory nobody has
        // written to — see MudVerseSourceTests for the source it is currently holding back.
        await Assert.That(MudStatsSource.DefaultEtiquette().ContactedMaintainer).IsTrue();
        await Assert.That(EtiquettePlanner.Decide(MudStatsSource.DefaultEtiquette()).Route)
            .IsEqualTo(FetchRoute.Scrape);
    }

    [Test]
    public async Task TheGateStillBitesTheMomentTheFactStopsBeingTrue()
    {
        // The same etiquette with the one fact retracted, so what is under test is the gate and not
        // a source that happens to be past it.
        var refused = EtiquettePlanner.Decide(MudStatsSource.DefaultEtiquette(contactedMaintainer: false));

        await Assert.That(refused.Route).IsEqualTo(FetchRoute.None);
        await Assert.That(refused.RefusedReason).IsEqualTo(EtiquettePlanner.MaintainerNotContacted);
    }

    [Test]
    public async Task PermissionToRunIsNotPermissionToHurry()
    {
        var etiquette = MudStatsSource.DefaultEtiquette();

        // Every other obligation §7.6 states is enforced here rather than remembered: the rate limit,
        // the bound on how much of the site one run touches, the user agent that names us with an
        // info URL, the robots.txt read before the first content fetch, and the attribution.
        await Assert.That(etiquette.MinimumInterval).IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(15));
        await Assert.That(MudStatsSource.Create(FakeHttp.Serving().Client, new ManualTimeProvider(Start))
            .MaxWorlds).IsLessThanOrEqualTo(500);
        await Assert.That(ImporterIdentity.SelfIdentifies(etiquette.UserAgent)).IsTrue();
        await Assert.That(etiquette.RobotsUri.AbsoluteUri).IsEqualTo("https://mudstats.com/robots.txt");
        await Assert.That(etiquette.AttributionNote).IsNotNull();
    }

    [Test]
    public async Task ItRateLimitsHardBecauseScrapingIsTheOnlyRouteItHas()
    {
        // One page per world at fifteen seconds apart. Spec §7.6: rate-limit hard where scraping is
        // the only option.
        await Assert.That(MudStatsSource.DefaultEtiquette().MinimumInterval)
            .IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task TheIndexYieldsItsWorldSlugsInOrderWithoutRepeats()
    {
        var slugs = MudStatsSource.Slugs(Fixture.Read("mudstats-index.html"));

        await Assert.That(slugs.Count).IsEqualTo(12);
        await Assert.That(slugs[0]).IsEqualTo("NannyMUD");
        await Assert.That(slugs.Distinct().Count()).IsEqualTo(slugs.Count);
    }

    [Test]
    public async Task AWorldPageYieldsItsAddressFromTheValueElementAndNotFromBesideTheLabel()
    {
        var game = MudStatsSource.ReadWorld("4Dimensions", Page(), WorldUri, "MudStats");

        await Assert.That(game).IsNotNull();
        await Assert.That(game!.Endpoints.Single())
            .IsEqualTo(new ImportedEndpoint("4dimensions.org", 6000, EndpointKind.Telnet));
    }

    [Test]
    public async Task AWorldPageYieldsItsTitleItsCodebaseAndItsWebsite()
    {
        var game = MudStatsSource.ReadWorld("4Dimensions", Page(), WorldUri, "MudStats")!;

        await Assert.That(game.Name).IsEqualTo("4 Dimensions");
        await Assert.That(game.SourceKey).IsEqualTo("4Dimensions");
        await Assert.That(game.Fields["CODEBASE"]).IsEqualTo("CircleMUD");
        await Assert.That(game.Fields["WEBSITE"]).IsEqualTo("http://4dimensions.org");
    }

    [Test]
    public async Task ThePlayerCountIsDatedByTheAgeThePageStatesBesideIt()
    {
        var sample = MudStatsSource.ReadPresence(Page(), Start);

        await Assert.That(sample).IsNotNull();
        await Assert.That(sample!.Count).IsEqualTo(1);

        // "1 (9 minutes ago)". The reading is nine minutes old, and dating it to now would be nine
        // minutes of fiction in a series that is read by the hour.
        await Assert.That(sample.At).IsEqualTo(Start - TimeSpan.FromMinutes(9));
    }

    [Test]
    public async Task ACountWithNoStatedAgeYieldsNoSampleAtAll()
    {
        var undated = Page().Replace("<small>(9 minutes ago)</small>", string.Empty, StringComparison.Ordinal);

        await Assert.That(MudStatsSource.ReadPresence(undated, Start)).IsNull();
    }

    [Test]
    public async Task AnAgeInMonthsIsRefusedRatherThanRoundedToThirtyDays()
    {
        var stale = Page().Replace("(9 minutes ago)", "(4 months ago)", StringComparison.Ordinal);

        // A month is not a duration. Rounding one puts a reading in the wrong week of the heatmap.
        await Assert.That(MudStatsSource.ReadPresence(stale, Start)).IsNull();
    }

    [Test]
    public async Task APageWithNoReadableAddressYieldsNothingRatherThanAGuess()
    {
        var broken = Page().Replace("4dimensions.org 6000", "coming soon", StringComparison.Ordinal);

        await Assert.That(MudStatsSource.ReadWorld("4Dimensions", broken, WorldUri, "MudStats")).IsNull();
    }

    [Test]
    public async Task AnUnknownFieldIsNotStoredAsTheWordUnknown()
    {
        var game = MudStatsSource.ReadWorld("4Dimensions", Page(), WorldUri, "MudStats")!;

        // The page spells an absent value "(Unknown)", which is not a value.
        await Assert.That(game.Fields.Values).DoesNotContain("(Unknown)");
    }

    [Test]
    public async Task TheWholeSourceWalksTheIndexThenEachWorldPage()
    {
        var etiquette = MudStatsSource.DefaultEtiquette();
        var (handler, client) = FakeHttp.Serving(
            ("https://mudstats.com/", "<a href=\"/World/4Dimensions\">4 Dimensions</a>"),
            ("https://mudstats.com/World/4Dimensions", Page()));

        var time = new ManualTimeProvider(Start);
        var fetcher = new DirectoryFetcher(client, etiquette, time);
        await fetcher.PrimeRobotsAsync(CancellationToken.None);

        var games = new List<ImportedGame>();
        await foreach (var game in new MudStatsSource(fetcher, time).ReadAsync(CancellationToken.None))
        {
            games.Add(game);
        }

        await Assert.That(games.Count).IsEqualTo(1);
        await Assert.That(games[0].Presence.Single().Count).IsEqualTo(1);
        await Assert.That(games[0].Availability).IsEmpty();

        // robots.txt (404, allow-all), the index, then one world page.
        await Assert.That(handler.Requests.Count()).IsEqualTo(3);
        await Assert.That(handler.Requests[0].Uri).IsEqualTo("https://mudstats.com/robots.txt");
    }
}
