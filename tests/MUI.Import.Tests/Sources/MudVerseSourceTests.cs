using MUI.Catalog.Persistence;
using MUI.Import.Sources;
using MUI.Import.Tests.Support;

namespace MUI.Import.Tests.Sources;

/// <summary>
/// MudVerse game pages, read from recorded fixtures: one game the crawler reached and read MSSP from,
/// and one it reached with no MSSP to read — which is also the one carrying player reviews, in a
/// table sharing its class with the MSSP table.
/// </summary>
public class MudVerseSourceTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>"07/31/26 03:35 am GMT", as the page states it.</summary>
    private static readonly DateTimeOffset Tested = new(2026, 7, 31, 3, 35, 0, TimeSpan.Zero);

    private static readonly Uri GameUri = new("https://www.mudverse.com/game/671");

    private static string Page() => Fixture.Read("mudverse-game-671.html");

    private static string NoMssp() => Fixture.Read("mudverse-game-359.html");

    private static ImportedGame Game() => MudVerseSource.ReadGame("671", Page(), GameUri, "MudVerse")!;

    [Test]
    public async Task ItIsMeasuredBecauseTheSiteCrawlsHourly()
    {
        var (_, client) = FakeHttp.Serving();
        var source = MudVerseSource.Create(client, new ManualTimeProvider(Start));

        await Assert.That(source.Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(source.SourceName).IsEqualTo("MudVerse");
    }

    [Test]
    public async Task ItIsAScrapeAndIsRefusedUntilSomebodyHasWrittenToTheMaintainer()
    {
        // The gate MudStats used to sit behind, doing its job on the next source. Nobody has written
        // to MudVerse, so nothing here can fetch — and the run says so rather than running.
        await Assert.That(MudVerseSource.DefaultEtiquette().ContactedMaintainer).IsFalse();

        var refused = EtiquettePlanner.Decide(MudVerseSource.DefaultEtiquette());

        await Assert.That(refused.Route).IsEqualTo(FetchRoute.None);
        await Assert.That(refused.RefusedReason).IsEqualTo(EtiquettePlanner.MaintainerNotContacted);

        await Assert.That(EtiquettePlanner.Decide(MudVerseSource.DefaultEtiquette(contactedMaintainer: true)).Route)
            .IsEqualTo(FetchRoute.Scrape);
    }

    [Test]
    public async Task ItRateLimitsHardBecauseScrapingIsTheOnlyRouteItHas()
    {
        await Assert.That(MudVerseSource.DefaultEtiquette().MinimumInterval)
            .IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task TheSitemapYieldsTheGameListingsAndNothingElse()
    {
        var listings = MudVerseSource.Listings(Fixture.Read("mudverse-sitemap.xml"));

        // /games, /reviews, /read-review/150 and /write-a-review/671 are all in the sitemap and none
        // of them is a game. The repeated /game/671 is one listing, not two.
        await Assert.That(listings).IsEquivalentTo(new[] { "671", "359" });
    }

    [Test]
    public async Task TheAddressComesFromTheConnectionPanelAndNotFromTheDeclaredHostname()
    {
        var game = Game();

        // The MSSP block declares HOSTNAME too. The connection panel is what the site dialled, and a
        // measurement outranks a declaration even when the two agree.
        await Assert.That(game.Endpoints[0])
            .IsEqualTo(new ImportedEndpoint("darkhopemud.com", 6777, EndpointKind.Telnet));
        await Assert.That(game.Fields.Keys).DoesNotContain("HOSTNAME");
    }

    [Test]
    public async Task ATlsPortOnTheConnectionPanelIsAnEndpointBecauseTheSiteConnectedToIt()
    {
        var game = Game();

        // Distinct from an MSSP `SSL` claim, which the TinTin sources deliberately do NOT turn into
        // an endpoint: nobody had connected to that port. This one is in the panel of addresses the
        // crawler used.
        await Assert.That(game.Endpoints.Count).IsEqualTo(2);
        await Assert.That(game.Endpoints[1])
            .IsEqualTo(new ImportedEndpoint("darkhopemud.com", 6778, EndpointKind.Tls));
    }

    [Test]
    public async Task TheMsspPanelIsReadAndTheOwnerSubmittedPanelIsNot()
    {
        var game = Game();

        // The two panels disagree on this very page: the owner typed "Codebase: Other" into a form
        // and the crawler read the real thing off the wire.
        await Assert.That(game.Fields["CODEBASE"]).IsEqualTo("DikuMUD/Merc/Envy - Dark Hope MUD");
        await Assert.That(game.Fields.Values).DoesNotContain("Other");

        // The owner's "Player Count: 0-5" is a dropdown bucket and is not a count of anything.
        await Assert.That(game.Fields.Values).DoesNotContain("0-5");
    }

    [Test]
    public async Task ALinkedCellYieldsItsUrlAndNotItsLabel()
    {
        var game = Game();

        // The WEBSITE cell renders the word "Website" and the URL exists only in the href.
        await Assert.That(game.Fields["WEBSITE"]).IsEqualTo("https://darkhopemud.com");

        // CONTACT is a mailto: and is not a website; it is kept out of the linked-URL fields
        // entirely rather than stored as one.
        await Assert.That(game.Fields.Values).DoesNotContain("mailto:theo@darkhopemud.com");
    }

    [Test]
    public async Task ACapabilityIsDeclaredAndNeverMeasured()
    {
        var game = Game();

        await Assert.That(game.Fields[CapabilityFields.Declared("MCCP")]).IsEqualTo("1");
        await Assert.That(game.Fields[CapabilityFields.Declared("MXP")]).IsEqualTo("0");

        // The site read the claim; the game made it. No `.measured` field may come out of an import.
        await Assert.That(game.Fields.Keys.Any(CapabilityFields.IsMeasured)).IsFalse();
    }

    [Test]
    public async Task ACapabilityCellThatIsNotAYesOrANoIsAbsentRatherThanDenied()
    {
        var game = Game();

        // PLAYERKILLING reads "Restricted". It is not a boolean and is not coerced into one.
        await Assert.That(game.Fields.Keys).DoesNotContain(CapabilityFields.Declared("PLAYERKILLING"));
    }

    [Test]
    public async Task ThePlayerCountIsDatedByTheConnectionTheReadingCameFrom()
    {
        var sample = Game().Presence.Single();

        await Assert.That(sample.Count).IsEqualTo(1);
        await Assert.That(sample.At).IsEqualTo(Tested);
    }

    [Test]
    public async Task AnMsspBlockOlderThanTheLastSuccessfulConnectionYieldsNoCount()
    {
        // The page dates the MSSP block to the day and the connection to the minute. When they
        // disagree the block is older than the connection above it, by an amount the page does not
        // state — and dating a stale count to a fresh connection would be the invention.
        var stale = Page().Replace("MSSP Data - Crawled on 07/31/26", "MSSP Data - Crawled on 07/24/26",
            StringComparison.Ordinal);

        await Assert.That(MudVerseSource.ReadGame("671", stale, GameUri, "MudVerse")!.Presence).IsEmpty();
    }

    [Test]
    public async Task APageWithNoMsspDataYieldsAnAddressAndNothingElse()
    {
        var game = MudVerseSource.ReadGame("359", NoMssp(), new Uri("https://www.mudverse.com/game/359"),
            "MudVerse")!;

        await Assert.That(game.Name).IsEqualTo("Tirradyn");
        await Assert.That(game.Endpoints.Single())
            .IsEqualTo(new ImportedEndpoint("tirradyn.com", 9010, EndpointKind.Telnet));

        // The page says "No MSSP data available." and carries a Connection Tested stamp anyway. A
        // reachable host with nothing to read is a crawl target, not a zero.
        await Assert.That(game.Presence).IsEmpty();
        await Assert.That(game.Fields).IsEmpty();
    }

    [Test]
    public async Task APlayerReviewIsNotAnMsspVariable()
    {
        // The reviews table and the MSSP table share a class on this page. Read by class alone, this
        // game contributes review titles, author names and star ratings as MSSP readings.
        var mssp = MudVerseSource.Mssp(NoMssp());

        await Assert.That(mssp).IsEmpty();
        await Assert.That(mssp.Keys).DoesNotContain("Review Title");
    }

    [Test]
    public async Task ASnapshotYieldsNoAvailabilitySpans()
    {
        // Two instants — tested, and last successful — say the host answered then and nothing about
        // for how long. The daily averages the site publishes are not imported either: a day's mean
        // is a derived statistic and a presence sample is a reading somebody took at an instant.
        await Assert.That(Game().Availability).IsEmpty();
    }

    [Test]
    public async Task TheWholeSourceReadsTheSitemapThenEachGamePage()
    {
        var etiquette = MudVerseSource.DefaultEtiquette(contactedMaintainer: true);
        var (handler, client) = FakeHttp.Serving(
            (etiquette.RobotsUri.AbsoluteUri, "User-agent: *\nDisallow: /who/\n"),
            ("https://www.mudverse.com/sitemap.xml", Fixture.Read("mudverse-sitemap.xml")),
            ("https://www.mudverse.com/game/671", Page()),
            ("https://www.mudverse.com/game/359", NoMssp()));

        var fetcher = new DirectoryFetcher(client, etiquette, new ManualTimeProvider(Start));
        await fetcher.PrimeRobotsAsync(CancellationToken.None);

        var games = new List<ImportedGame>();
        await foreach (var game in new MudVerseSource(fetcher).ReadAsync(CancellationToken.None))
        {
            games.Add(game);
        }

        await Assert.That(games.Count).IsEqualTo(2);
        await Assert.That(games[0].Presence.Single().Count).IsEqualTo(1);

        // robots.txt, the sitemap, then one page per game and no more.
        await Assert.That(handler.Requests.Count()).IsEqualTo(4);
        await Assert.That(handler.Requests[0].Uri).IsEqualTo(etiquette.RobotsUri.AbsoluteUri);
        await Assert.That(handler.Requests[1].Uri).IsEqualTo("https://www.mudverse.com/sitemap.xml");
    }
}
