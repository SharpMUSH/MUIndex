using MUI.Catalog.Persistence;
using MUI.Import.Sources;
using MUI.Import.Tests.Support;

namespace MUI.Import.Tests.Sources;

/// <summary>
/// The TinTin++ MSDP crawler page, read from a recorded fixture: five real records trimmed out of the
/// live page, chosen so that every shape it has is here — a plain one whose only address is an IP,
/// one with a website link, one whose player count the crawler itself painted as invalid, one sharing
/// a host with three other entries at other ports, and one MUSH.
/// </summary>
public class TinTinMsdpCrawlerSourceTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>"19 Jan 2024 10:39 CET" — the page's own stamp, in the zone it states.</summary>
    private static readonly DateTimeOffset Generated = new(2024, 1, 19, 10, 39, 0, TimeSpan.FromHours(1));

    private static string Document() => Fixture.Read("tintin-msdp-mudlist.html");

    private static IReadOnlyList<ImportedGame> Games() => TinTinMsdpCrawlerSource.Parse(Document());

    [Test]
    public async Task ItIsMeasuredBecauseTheSiteActuallyProbes()
    {
        var (_, client) = FakeHttp.Serving();
        var source = TinTinMsdpCrawlerSource.Create(client, new ManualTimeProvider(Start));

        await Assert.That(source.Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(source.SourceName).IsEqualTo("TinTin++ MSDP Mud Crawler");
    }

    [Test]
    public async Task ItIsReadAsABulkExportAndNeedsNoContactedMaintainerGate()
    {
        var etiquette = TinTinMsdpCrawlerSource.DefaultEtiquette();

        await Assert.That(EtiquettePlanner.Decide(etiquette).Route).IsEqualTo(FetchRoute.BulkExport);
        await Assert.That(etiquette.ScrapeUri).IsNull();
        await Assert.That(etiquette.AttributionNote).IsNotNull();
    }

    [Test]
    public async Task ItIsADifferentPageFromItsMsspSiblingAndSaysSoInEveryPlaceThatMatters()
    {
        var msdp = TinTinMsdpCrawlerSource.DefaultEtiquette();
        var mssp = TinTinMsspCrawlerSource.DefaultEtiquette();

        await Assert.That(msdp.SourceName).IsNotEqualTo(mssp.SourceName);
        await Assert.That(msdp.BulkExportUri).IsNotEqualTo(mssp.BulkExportUri);

        // Same site, so the same robots.txt governs both and the same courtesy floor applies.
        await Assert.That(msdp.RobotsUri).IsEqualTo(mssp.RobotsUri);
        await Assert.That(msdp.MinimumInterval).IsEqualTo(mssp.MinimumInterval);
    }

    [Test]
    public async Task EveryRecordIsReadWithItsAddress()
    {
        var games = Games();

        await Assert.That(games.Count).IsEqualTo(5);
        await Assert.That(games.Select(game => game.Name))
            .Contains("ChaosMUD").And.Contains("4Dimensions").And.Contains("Arx: After the Reckoning");

        foreach (var game in games)
        {
            await Assert.That(game.Endpoints).IsNotEmpty();
            await Assert.That(game.SourceName).IsEqualTo("TinTin++ MSDP Mud Crawler");
            await Assert.That(game.SourceUri!.AbsoluteUri)
                .IsEqualTo("https://tintin.mudhalla.net/protocols/msdp/mudlist.html");
        }
    }

    [Test]
    public async Task ThePageIsReadAtItsOwnLabelWidthAndNotTheMsspOnes()
    {
        // The whole difference between the two pages, and the reason the width is an argument with no
        // default: read at the MSSP page's width this page does not fail, it yields truncated labels
        // and values sliced early. "CONFIGURABLE_VARIABLES" is what forced it wider.
        var misread = TinTinCrawlerSource.Parse(
            Document(),
            "wrong width",
            new Uri("https://tintin.mudhalla.net/protocols/msdp/mudlist.html"),
            TinTinCrawlerTable.MsspLabelWidth);

        await Assert.That(misread.Select(game => game.Name)).DoesNotContain("ChaosMUD");
    }

    [Test]
    public async Task AnAddressThatIsOnlyEverAnIpIsStillSeeded()
    {
        // ChaosMUD declares its HOSTNAME as a bare address, and this page carries no per-mud link, so
        // there is no name to prefer over it. It is a candidate like any other and becomes a game by
        // answering for itself (§7.2).
        var game = Games().Single(candidate => candidate.Name == "ChaosMUD");

        await Assert.That(game.Endpoints.Single())
            .IsEqualTo(new ImportedEndpoint("170.187.150.187", 1111, EndpointKind.Telnet));
    }

    [Test]
    public async Task FourGamesOnOneHostStayFourGames()
    {
        // godwars.net runs four separate games on four ports, and each is its own record. The source
        // key carries the port for exactly that reason.
        var game = Games().Single(candidate => candidate.Name == "Moments of Hatred");

        await Assert.That(game.SourceKey).IsEqualTo("godwars.net:3500");
        await Assert.That(game.Endpoints.Single().Port).IsEqualTo(3500);
    }

    [Test]
    public async Task AWebsiteComesFromItsCellsLinkAndNeverFromTheAnchorText()
    {
        var game = Games().Single(candidate => candidate.Name == "4Dimensions");

        // The cell renders the game's name and the URL exists only in the href.
        await Assert.That(game.Fields["WEBSITE"]).IsEqualTo("http://4dimensions.org");
        await Assert.That(game.Fields["CODEBASE"]).IsEqualTo("CircleMUD");
        await Assert.That(game.Fields["CREATED"]).IsEqualTo("1996");
    }

    [Test]
    public async Task AValueTheCrawlerItselfPaintedAsInvalidIsNotImported()
    {
        var game = Games().Single(candidate => candidate.Name == "4Dimensions");

        // "United States" against the page's country-name taxonomy, painted bright red by its own
        // legend. Relaying a source's data minus the source's caveat is what this refuses to do.
        await Assert.That(game.Fields.ContainsKey("LOCATION")).IsFalse();
        await Assert.That(game.Fields["LANGUAGE"]).IsEqualTo("English");
    }

    [Test]
    public async Task ThePlayerCountIsDatedByThePagesOwnGenerationStampInTheZoneItStates()
    {
        await Assert.That(TinTinCrawlerTable.GeneratedAt(Document())).IsEqualTo(Generated);

        var game = Games().Single(candidate => candidate.Name == "Arx: After the Reckoning");
        var sample = game.Presence.Single();

        await Assert.That(sample.Count).IsEqualTo(82);
        await Assert.That(sample.At).IsEqualTo(Generated);
    }

    [Test]
    public async Task AnOldSnapshotIsDatedWhenItWasTakenAndNeverWhenItWasRead()
    {
        // The live page has not been regenerated since January 2024. A reading two years old is still
        // a reading; putting it in this week of the heatmap would be the fabrication.
        foreach (var game in Games())
        {
            foreach (var sample in game.Presence)
            {
                await Assert.That(sample.At).IsEqualTo(Generated);
                await Assert.That(sample.At).IsLessThan(Start);
            }
        }
    }

    [Test]
    public async Task ACountTheCrawlerItselfFlaggedAsInvalidIsNotImportedAsZero()
    {
        // The 7th Plane's PLAYERS reads 0 and the crawler painted it invalid — the same bogus zero the
        // MSSP page carries for the same host. A count the source marked bad is not a measured zero.
        var game = Games().Single(candidate => candidate.Name == "The 7th Plane");

        await Assert.That(game.Presence).IsEmpty();
    }

    [Test]
    public async Task AMeasuredZeroIsARealReadingAndIsImportedAsOne()
    {
        // 4Dimensions reads 0 with no invalid flag on it. §5.4: a measured zero is a filled cell, not
        // an absence — we got in and nobody was there.
        var game = Games().Single(candidate => candidate.Name == "4Dimensions");

        await Assert.That(game.Presence.Single().Count).IsEqualTo(0);
    }

    [Test]
    public async Task ASnapshotYieldsNoAvailabilitySpans()
    {
        foreach (var game in Games())
        {
            await Assert.That(game.Availability).IsEmpty();
        }
    }

    [Test]
    public async Task APlayerCountIsNeverInventedWhenThePageCarriesNoReadableStamp()
    {
        // An unrecognised zone abbreviation costs the page its presence rows rather than guessing an
        // offset, because a guess puts the reading in the wrong hour of the heatmap.
        var unknownZone = Document().Replace("10:39 CET", "10:39 XYZ", StringComparison.Ordinal);

        foreach (var game in TinTinMsdpCrawlerSource.Parse(unknownZone))
        {
            await Assert.That(game.Presence).IsEmpty();
        }
    }

    [Test]
    public async Task AGameCannotForgeARecordByDrawingABoxInItsOwnConnectScreen()
    {
        // Both crawler pages interleave the crawler's frames with the login banners of the games it
        // dialled, and a banner is text the game controls completely. A narrow box carrying another
        // game's HOSTNAME and PORT must not be read as a record: that is a fabricated player count
        // attached to somebody else's listing. Only a full-width frame opens one.
        var forged = Document().Replace(
            "MSDP statistics for 46 MUDs",
            "┌──────────────┐\n"
            + "│                  PLAYERS                              9999                  HOSTNAME"
            + "                   4dimensions.org│\n"
            + "│                     PORT                              6000                      NAME"
            + "                          Not Real│\n"
            + "└──────────────┘\nMSDP statistics for 46 MUDs",
            StringComparison.Ordinal);

        var games = TinTinMsdpCrawlerSource.Parse(forged);

        await Assert.That(games.Count).IsEqualTo(5);
        await Assert.That(games.Select(game => game.Name)).DoesNotContain("Not Real");
        await Assert.That(games.SelectMany(game => game.Presence).Select(sample => sample.Count))
            .DoesNotContain(9999);
    }

    [Test]
    public async Task TheWholeSourceReadsThroughTheFetcherWithRobotsFirst()
    {
        var etiquette = TinTinMsdpCrawlerSource.DefaultEtiquette();
        var (handler, client) = FakeHttp.Serving(
            (etiquette.RobotsUri.AbsoluteUri, "User-agent: *\nCrawl-delay: 0\n"),
            (etiquette.BulkExportUri!.AbsoluteUri, Document()));

        var fetcher = new DirectoryFetcher(client, etiquette, new ManualTimeProvider(Start));
        await fetcher.PrimeRobotsAsync(CancellationToken.None);

        var games = new List<ImportedGame>();
        await foreach (var game in new TinTinMsdpCrawlerSource(fetcher).ReadAsync(CancellationToken.None))
        {
            games.Add(game);
        }

        await Assert.That(games.Count).IsEqualTo(5);

        // One request for the whole listing, which is why it is a bulk export rather than a scrape.
        await Assert.That(handler.Requests.Count()).IsEqualTo(2);
        await Assert.That(handler.Requests[0].Uri).IsEqualTo(etiquette.RobotsUri.AbsoluteUri);
        await Assert.That(handler.Requests[1].Uri).IsEqualTo(etiquette.BulkExportUri.AbsoluteUri);
    }
}
