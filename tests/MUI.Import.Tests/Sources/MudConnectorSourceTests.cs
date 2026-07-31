using MUI.Catalog.Persistence;
using MUI.Import.Sources;
using MUI.Import.Tests.Support;

namespace MUI.Import.Tests.Sources;

/// <summary>
/// The Mud Connector's Big List, read from a recorded fixture: six rows — three the site reached,
/// one it was refused by, and two with no address at all — plus a decoy link outside the table.
/// </summary>
/// <remarks>
/// The rule this file exists to hold shut is the tier one. TMC genuinely connects, and its
/// <c>Connect Status</c> column is a real result — but the page states no time for it, so it is read
/// by nothing here. An undated measurement imported as a measurement is a fabricated timestamp.
/// </remarks>
public class MudConnectorSourceTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static string Document() => Fixture.Read("mudconnect-biglist.html");

    private static IReadOnlyList<ImportedGame> Games() => MudConnectorSource.Parse(Document());

    [Test]
    public async Task ItIsAssertedBecauseTheListIsHandMaintained()
    {
        var (_, client) = FakeHttp.Serving();
        var source = MudConnectorSource.Create(client, new ManualTimeProvider(Start));

        // Spec §7.6 names it in the asserted tier: seeds discovery and endpoints only. No history,
        // no presence, no grace.
        await Assert.That(source.Tier).IsEqualTo(ImportTier.Asserted);
        await Assert.That(source.SourceName).IsEqualTo("The Mud Connector");
    }

    [Test]
    public async Task TheWholeCatalogueIsOnePageSoItIsReadAsABulkExport()
    {
        var etiquette = MudConnectorSource.DefaultEtiquette();

        await Assert.That(EtiquettePlanner.Decide(etiquette).Route).IsEqualTo(FetchRoute.BulkExport);
        await Assert.That(etiquette.ScrapeUri).IsNull();
        await Assert.That(etiquette.AttributionNote).IsNotNull();
    }

    [Test]
    public async Task EveryRowWithAnAddressBecomesOneGame()
    {
        var games = Games();

        await Assert.That(games.Count).IsEqualTo(4);
        await Assert.That(games.Select(game => game.Name))
            .Contains("/TG/MUD").And.Contains("3-Kingdoms");

        foreach (var game in games)
        {
            await Assert.That(game.Endpoints.Count).IsEqualTo(1);
            await Assert.That(game.SourceName).IsEqualTo("The Mud Connector");
        }
    }

    [Test]
    public async Task TheAddressComesFromTheSitesOwnConnectLink()
    {
        var game = Games().Single(candidate => candidate.Name == "3-Kingdoms");

        await Assert.That(game.Endpoints.Single())
            .IsEqualTo(new ImportedEndpoint("3k.org", 3000, EndpointKind.Telnet));
        await Assert.That(game.SourceKey).IsEqualTo("3k.org:3000");
    }

    [Test]
    public async Task AWebsiteComesOutOfTheSitesOwnRedirectorRatherThanTheAnchorText()
    {
        var game = Games().Single(candidate => candidate.Name == "/TG/MUD");

        await Assert.That(game.Fields["WEBSITE"]).IsEqualTo("http://mud.tgchan.org:27744");
        await Assert.That(game.Fields["NAME"]).IsEqualTo("/TG/MUD");
    }

    [Test]
    public async Task ARowWithNoAddressYieldsNothingRatherThanAnEmptyListing()
    {
        // "No Address Listed" is on the page with a name and no telnet link. A game we cannot reach
        // is not a crawl target, and minting one from a name is inventing an address.
        await Assert.That(Games().Select(game => game.Name)).DoesNotContain("No Address Listed");
    }

    [Test]
    public async Task ARowTheSiteCouldNotConnectToIsStillSeededAndStillCarriesNoVerdict()
    {
        // "Connect Refused" is a real result of a real connection attempt — with no time attached, so
        // it cannot be imported as one. The address is seeded like any other and the crawler finds
        // out for itself (§7.2), which is the only honest thing to do with an undated verdict.
        var game = Games().Single(candidate => candidate.Endpoints[0].Host == "Actofwarmud.com");

        await Assert.That(game.Presence).IsEmpty();
        await Assert.That(game.Availability).IsEmpty();
        await Assert.That(game.Fields.Keys).DoesNotContain("STATUS");
    }

    [Test]
    public async Task NoRowCarriesHistoryOfAnyKind()
    {
        foreach (var game in Games())
        {
            await Assert.That(game.Presence).IsEmpty();
            await Assert.That(game.Availability).IsEmpty();
        }
    }

    [Test]
    public async Task ALinkOutsideTheListIsNotARow()
    {
        // The page around the table is a site — a sidebar, a news panel, a login modal — and one of
        // its links is a telnet URL. Read unbounded, a row is whatever happens to look like one.
        await Assert.That(Games().SelectMany(game => game.Endpoints).Select(endpoint => endpoint.Host))
            .DoesNotContain("sidebar.example.org");
    }

    [Test]
    public async Task APageWithNoListAtAllYieldsNothingRatherThanThrowing()
    {
        await Assert.That(MudConnectorSource.Parse("<html><body>Down for maintenance.</body></html>"))
            .IsEmpty();
    }

    [Test]
    public async Task TheWholeSourceReadsThroughTheFetcherWithRobotsFirstAndFetchesOnePage()
    {
        var etiquette = MudConnectorSource.DefaultEtiquette();
        var (handler, client) = FakeHttp.Serving(
            (etiquette.RobotsUri.AbsoluteUri, "User-agent: *\n"),
            (etiquette.BulkExportUri!.AbsoluteUri, Document()));

        var fetcher = new DirectoryFetcher(client, etiquette, new ManualTimeProvider(Start));
        await fetcher.PrimeRobotsAsync(CancellationToken.None);

        var games = new List<ImportedGame>();
        await foreach (var game in new MudConnectorSource(fetcher).ReadAsync(CancellationToken.None))
        {
            games.Add(game);
        }

        await Assert.That(games.Count).IsEqualTo(4);

        // Six hundred and eighty-nine games for one GET, and no per-game page. That arithmetic is the
        // whole of the argument for reading this site as an export rather than scraping it.
        await Assert.That(handler.Requests.Count()).IsEqualTo(2);
        await Assert.That(handler.Requests[1].Uri).IsEqualTo(etiquette.BulkExportUri.AbsoluteUri);
    }
}
