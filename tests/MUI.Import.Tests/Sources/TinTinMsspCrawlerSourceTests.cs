using MUI.Catalog.Persistence;
using MUI.Import.Sources;
using MUI.Import.Tests.Support;

namespace MUI.Import.Tests.Sources;

/// <summary>
/// The TinTin++ MSSP crawler page, read from a recorded fixture: five real records trimmed out of the
/// live page, chosen so that every shape it has is here — a plain one, one with a website link, one
/// with no <c>HOSTNAME</c> at all, one with MSSP's array notation in <c>PORT</c>, and one carrying a
/// value the crawler itself painted as invalid.
/// </summary>
public class TinTinMsspCrawlerSourceTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Generated = new(2025, 7, 7, 19, 25, 0, TimeSpan.FromHours(-5));

    private static IReadOnlyList<ImportedGame> Games() =>
        TinTinMsspCrawlerSource.Parse(Fixture.Read("tintin-mssp-mudlist.html"));

    [Test]
    public async Task ItIsMeasuredBecauseTheSiteActuallyProbes()
    {
        var (_, client) = FakeHttp.Serving();
        var source = TinTinMsspCrawlerSource.Create(client, new ManualTimeProvider(Start));

        await Assert.That(source.Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(source.SourceName).IsEqualTo("TinTin++ MSSP Mud Crawler");
    }

    [Test]
    public async Task ItIsReadAsABulkExportAndNeedsNoContactedMaintainerGate()
    {
        var etiquette = TinTinMsspCrawlerSource.DefaultEtiquette();

        await Assert.That(EtiquettePlanner.Decide(etiquette).Route).IsEqualTo(FetchRoute.BulkExport);
        await Assert.That(etiquette.ScrapeUri).IsNull();
        await Assert.That(etiquette.AttributionUri).IsNotNull();
    }

    [Test]
    public async Task EveryRecordInTheFixtureIsReadWithItsAddress()
    {
        var games = Games();

        await Assert.That(games.Count).IsEqualTo(5);
        await Assert.That(games.Select(game => game.Name))
            .Contains("ChaosMUD").And.Contains("4Dimensions").And.Contains("Alter Aeon");

        foreach (var game in games)
        {
            await Assert.That(game.Endpoints).IsNotEmpty();
            await Assert.That(game.SourceName).IsEqualTo("TinTin++ MSSP Mud Crawler");
        }
    }

    [Test]
    public async Task ARecordWithNoDeclaredHostnameStillYieldsTheAddressTheCrawlerDialled()
    {
        // The 7th Plane declares no MSSP HOSTNAME — 48 of the 115 entries on the live page do not —
        // and the address is recoverable only from the record's own per-mud link.
        var game = Games().Single(candidate => candidate.Name == "The 7th Plane");

        await Assert.That(game.Endpoints.Select(endpoint => endpoint.Host)).Contains("7thplane.net");
        await Assert.That(game.Endpoints.First().Port).IsEqualTo(8888);
        await Assert.That(game.Endpoints.First().Kind).IsEqualTo(EndpointKind.Telnet);
    }

    [Test]
    public async Task MsspArrayNotationInPortBecomesOneEndpointPerPort()
    {
        var game = Games().Single(candidate => candidate.Name == "Alter Aeon");
        var ports = game.Endpoints.Select(endpoint => endpoint.Port).Distinct().Order().ToList();

        await Assert.That(ports).IsEquivalentTo(new[] { 23, 3000, 3010, 3224 });
    }

    [Test]
    public async Task AWebsiteComesFromItsOwnCellsLinkAndNotFromTheLinesFirstOne()
    {
        // Alter Aeon's ICON cell sits on the same rendered line as its WEBSITE and holds a PNG, so a
        // reader taking the first href on the line stores a banner image as the game's website.
        var game = Games().Single(candidate => candidate.Name == "Alter Aeon");

        await Assert.That(game.Fields["WEBSITE"]).IsEqualTo("http://www.alteraeon.com");
        await Assert.That(game.Fields["ICON"]).IsEqualTo("http://www.alteraeon.com/banners/AA-64x64.png");
    }

    [Test]
    public async Task ACapabilityRelayedOutOfSomebodyElsesMsspReadIsDeclaredAndNeverMeasured()
    {
        var game = Games().Single(candidate => candidate.Name == "4Dimensions");

        await Assert.That(game.Fields.ContainsKey(CapabilityFields.Declared("MXP"))).IsTrue();
        await Assert.That(game.Fields[CapabilityFields.Declared("MXP")]).IsEqualTo("1");

        // The whole point of the tier split, one level down: TinTin measured that the server answered;
        // the server declared what was in the answer. No .measured field may come out of an import.
        await Assert.That(game.Fields.Keys.Any(CapabilityFields.IsMeasured)).IsFalse();
    }

    [Test]
    public async Task AValueTheCrawlerItselfPaintedAsInvalidIsNotImported()
    {
        var game = Games().Single(candidate => candidate.Name == "Abandoned Realms");

        // The page's legend defines bright red as invalid data, and this record's GAMEPLAY is in it.
        // Relaying a source's data minus the source's own caveat is what this refuses to do.
        await Assert.That(game.Fields.ContainsKey("GAMEPLAY")).IsFalse();
        await Assert.That(game.Fields["GENRE"]).IsEqualTo("Fantasy");
    }

    [Test]
    public async Task ThePlayerCountIsDatedByThePagesOwnGenerationStamp()
    {
        await Assert.That(TinTinCrawlerTable.GeneratedAt(Fixture.Read("tintin-mssp-mudlist.html")))
            .IsEqualTo(Generated);

        var game = Games().Single(candidate => candidate.Name == "Alter Aeon");
        var sample = game.Presence.Single();

        await Assert.That(sample.Count).IsEqualTo(63);
        await Assert.That(sample.At).IsEqualTo(Generated);
    }

    [Test]
    public async Task ACountTheCrawlerItselfFlaggedAsInvalidIsNotImportedAsZero()
    {
        // The 7th Plane's PLAYERS reads 0 and the crawler painted it invalid. A count the source
        // marked bad is not a measured zero, and importing it as one would put "we got in and nobody
        // was there" on a game's page on the strength of a reading its own author distrusted.
        var game = Games().Single(candidate => candidate.Name == "The 7th Plane");

        await Assert.That(game.Presence).IsEmpty();
    }

    [Test]
    public async Task AMeasuredZeroIsARealReadingAndIsImportedAsOne()
    {
        // The same record with the crawler's invalid flag lifted off the cell. §5.4: a measured zero
        // is a filled cell, not an absence — we got in and nobody was there.
        var cleared = Fixture.Read("tintin-mssp-mudlist.html").Replace(
            "<span style='color:#F55'>                                         0 </span>",
            "<span style='color:#FFF'>                                         0 </span>",
            StringComparison.Ordinal);

        var game = TinTinMsspCrawlerSource.Parse(cleared).Single(candidate => candidate.Name == "The 7th Plane");

        await Assert.That(game.Presence.Single().Count).IsEqualTo(0);
    }

    [Test]
    public async Task ASnapshotYieldsNoAvailabilitySpans()
    {
        foreach (var game in Games())
        {
            // The page says the host answered at one instant and nothing about for how long. A span
            // invented around it would credit archive grace nobody measured.
            await Assert.That(game.Availability).IsEmpty();
        }
    }

    [Test]
    public async Task APlayerCountIsNeverInventedWhenThePageCarriesNoGenerationStamp()
    {
        var undated = Fixture.Read("tintin-mssp-mudlist.html")
            .Replace("generated on", "assembled around", StringComparison.Ordinal);

        foreach (var game in TinTinMsspCrawlerSource.Parse(undated))
        {
            // Dating somebody else's measurement to the moment we read the page would put a
            // fabricated timestamp into the day × hour heatmap.
            await Assert.That(game.Presence).IsEmpty();
        }
    }

    [Test]
    public async Task TheWholeSourceReadsThroughTheFetcherWithRobotsFirst()
    {
        var etiquette = TinTinMsspCrawlerSource.DefaultEtiquette();
        var (handler, client) = FakeHttp.Serving(
            (etiquette.RobotsUri.AbsoluteUri, "User-agent: *\nCrawl-delay: 0\n"),
            (etiquette.BulkExportUri!.AbsoluteUri, Fixture.Read("tintin-mssp-mudlist.html")));

        var fetcher = new DirectoryFetcher(client, etiquette, new ManualTimeProvider(Start));
        await fetcher.PrimeRobotsAsync(CancellationToken.None);

        var games = new List<ImportedGame>();
        await foreach (var game in new TinTinMsspCrawlerSource(fetcher).ReadAsync(CancellationToken.None))
        {
            games.Add(game);
        }

        await Assert.That(games.Count).IsEqualTo(5);
        await Assert.That(handler.Requests.Count()).IsEqualTo(2);
        await Assert.That(handler.Requests[0].Uri).IsEqualTo(etiquette.RobotsUri.AbsoluteUri);

        // One request for the whole listing, which is why it is a bulk export rather than a scrape.
        await Assert.That(handler.Requests[1].Uri).IsEqualTo(etiquette.BulkExportUri.AbsoluteUri);
    }
}
