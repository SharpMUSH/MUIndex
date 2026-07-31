using MUI.Import.Sources;
using MUI.Import.Tests.Support;

namespace MUI.Import.Tests;

/// <summary>
/// Spec §7.6: "the about page names every source we ingested", and every imported value carries its
/// originating site.
/// </summary>
/// <remarks>
/// The list is derived from the registry rather than written beside it, so a source that is being
/// read and is not being credited is not a state this code can reach.
/// </remarks>
public class AttributionTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static SourceRegistry Registry()
    {
        var (_, client) = FakeHttp.Serving();
        var time = new ManualTimeProvider(Start);

        return new SourceRegistry([
            TinTinMsspCrawlerSource.Create(client, time),
            MudStatsSource.Create(client, time),
        ]);
    }

    [Test]
    public async Task EverySourceIsCreditedWithAnAttributionUri()
    {
        var attributions = Registry().Attributions();

        await Assert.That(attributions.Count).IsEqualTo(2);

        foreach (var attribution in attributions)
        {
            await Assert.That(attribution.SourceName).IsNotEmpty();
            await Assert.That(attribution.Uri.Scheme).IsEqualTo("https");
            await Assert.That(attribution.Entitlement).IsNotEmpty();
        }
    }

    [Test]
    public async Task ASourceIsCreditedEvenWhileItIsRefusedPermissionToFetch()
    {
        // MudStats is gated on somebody having emailed its maintainer, and is credited regardless:
        // the about page names what we ingest, and a refused source is a fact about us rather than a
        // reason to leave a site off the list once it does run.
        var attribution = Registry().Attributions().Single(row => row.SourceName == "MudStats");

        await Assert.That(attribution.Route).IsEqualTo(FetchRoute.None);
        await Assert.That(attribution.Note).IsNotNull();
    }

    [Test]
    public async Task TheTierIsSpelledOutInTheWordsTheSpecUses()
    {
        var tintin = Registry().Attributions().Single(row => row.SourceName.StartsWith("TinTin", StringComparison.Ordinal));

        await Assert.That(tintin.Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(tintin.Entitlement).Contains("half weight");
    }

    [Test]
    public async Task ARunSkipsAndSaysWhyRatherThanAttemptingASourceItMayNotFetch()
    {
        var harness = Harness.Build(Start);
        var runner = new ImportRunner(Registry(), harness.Pipeline);

        var run = await runner.RunAsync(new ImportRunOptions { Only = ["MudStats"] }, CancellationToken.None);

        await Assert.That(run.Reports).IsEmpty();
        await Assert.That(run.Skipped.Single()).Contains(EtiquettePlanner.MaintainerNotContacted);
    }
}
