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

    /// <summary>The fixture's <em>Connection Tested</em> — the stamp that must NOT date a reading.</summary>
    private static readonly DateTimeOffset Tested = new(2026, 7, 31, 3, 35, 0, TimeSpan.Zero);

    /// <summary>The fixture's <em>Last Successful Connection</em> — the one the count belongs to.</summary>
    private static readonly DateTimeOffset Succeeded = new(2026, 7, 31, 2, 10, 0, TimeSpan.Zero);

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
            .IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(15));
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
    public async Task TheRecordIsAnchoredOnTheAddressTheDirectoryPublishesAndKeepsTheDeclaredOneAsAField()
    {
        var game = Game();

        // The Connectivity block below the panel reports testing this host and port, which is what
        // makes it the address to lead with. The game's own declared HOSTNAME is kept as a field and
        // sits at the bottom of the §5.1 ladder like any other imported value — where the two
        // disagree, that disagreement is the interesting fact.
        await Assert.That(game.Endpoints[0])
            .IsEqualTo(new ImportedEndpoint("darkhopemud.com", 6777, EndpointKind.Telnet));
        await Assert.That(game.Fields["HOSTNAME"]).IsEqualTo("darkhopemud.com");
    }

    [Test]
    public async Task ATlsPortInTheSamePanelIsSeededAsACandidateAndClaimsNothing()
    {
        var game = Game();

        // The page states one Connection Tested and one Last Successful Connection, both singular, so
        // it does not say this port was dialled — it says the directory publishes it as a way in.
        // §7.2 is what makes that safe: a candidate becomes a game by answering for itself.
        await Assert.That(game.Endpoints.Count).IsEqualTo(2);
        await Assert.That(game.Endpoints[1])
            .IsEqualTo(new ImportedEndpoint("darkhopemud.com", 6778, EndpointKind.Tls));

        // And it claims nothing about the game: no capability field is minted from it. That is the
        // difference from an MSSP `SSL` number, which lives inside a game's own reply and which the
        // TinTin sources refuse to turn into an endpoint for exactly the mirrored reason.
        await Assert.That(game.Fields.Keys).DoesNotContain(CapabilityFields.Declared("SSL"));
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
    public async Task ThePlayerCountIsDatedByTheLastSuccessfulConnectionAndNotByTheLastTest()
    {
        // The two stamps differ in this fixture on purpose. A failed test reads nothing, so a reading
        // belongs to the last connection that succeeded — and with the two equal, as they are on a
        // healthy game's live page, a parser reading the wrong one is indistinguishable from a parser
        // reading the right one.
        var sample = Game().Presence.Single();

        await Assert.That(sample.Count).IsEqualTo(1);
        await Assert.That(sample.At).IsEqualTo(Succeeded);
        await Assert.That(sample.At).IsNotEqualTo(Tested);
    }

    [Test]
    public async Task AnMsspBlockCrawledOnADifferentDayFromTheSuccessfulConnectionYieldsNoCount()
    {
        // The failed-test case, whole: connectivity was tested this morning and last succeeded
        // yesterday, and the MSSP block below is the one read yesterday. Dating that count to
        // yesterday would be right and dating it to this morning would be a fabrication — but the
        // page states the block's age only to the day, so the honest answer is no reading at all.
        var lateFailure = Page()
            .Replace(">07/31/26 02:10 am GMT<", ">07/30/26 11:40 pm GMT<", StringComparison.Ordinal);

        await Assert.That(MudVerseSource.ReadGame("671", lateFailure, GameUri, "MudVerse")!.Presence)
            .IsEmpty();
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
