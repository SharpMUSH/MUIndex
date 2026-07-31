using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>
    /// The registry the application composes, not one written beside it.
    /// </summary>
    /// <remarks>
    /// This is the difference between asserting that five sources are tiered correctly and asserting
    /// that <em>the five sources this build reads</em> are. A hand-written list here would keep
    /// passing while a source was registered in DI with the wrong tier, or left out of DI entirely —
    /// and the tier is the one thing in this assembly whose being wrong is worse than the source
    /// being skipped.
    /// </remarks>
    private static SourceRegistry Registry() =>
        new ServiceCollection()
            .AddSingleton<TimeProvider>(new ManualTimeProvider(Start))
            .AddMuiImporters()
            .BuildServiceProvider()
            .GetRequiredService<SourceRegistry>();

    [Test]
    public async Task EverySourceIsCreditedWithAnAttributionUri()
    {
        var attributions = Registry().Attributions();

        await Assert.That(attributions.Count).IsEqualTo(5);

        foreach (var attribution in attributions)
        {
            await Assert.That(attribution.SourceName).IsNotEmpty();
            await Assert.That(attribution.Uri.Scheme).IsEqualTo("https");
            await Assert.That(attribution.Entitlement).IsNotEmpty();
            await Assert.That(attribution.Note).IsNotNull();
        }
    }

    [Test]
    public async Task ASourceIsCreditedEvenWhileItIsRefusedPermissionToFetch()
    {
        // MudVerse is gated on somebody having emailed its maintainer, and is credited regardless:
        // the about page names what we ingest, and a refused source is a fact about us rather than a
        // reason to leave a site off the list once it does run.
        var attribution = Registry().Attributions().Single(row => row.SourceName == "MudVerse");

        await Assert.That(attribution.Route).IsEqualTo(FetchRoute.None);
        await Assert.That(attribution.Note).IsNotNull();
    }

    [Test]
    public async Task ASourceWhoseGateHasBeenSatisfiedIsCreditedWithTheRouteItWillActuallyUse()
    {
        var attribution = Registry().Attributions().Single(row => row.SourceName == "MudStats");

        await Assert.That(attribution.Route).IsEqualTo(FetchRoute.Scrape);
    }

    [Test]
    public async Task TheTierIsSpelledOutInTheWordsTheSpecUses()
    {
        var attributions = Registry().Attributions();

        var tintin = attributions.Single(row => row.SourceName.StartsWith("TinTin++ MSSP", StringComparison.Ordinal));

        await Assert.That(tintin.Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(tintin.Entitlement).Contains("half weight");

        var tmc = attributions.Single(row => row.SourceName == "The Mud Connector");

        await Assert.That(tmc.Tier).IsEqualTo(ImportTier.Asserted);
        await Assert.That(tmc.Entitlement).Contains("no history, no presence, no grace");
    }

    [Test]
    public async Task EveryMeasuredSourceIsOneThatActuallyConnectsToTheGames()
    {
        // The one mis-tiering that is worse than skipping a source: an asserted list filed as
        // measured earns archive grace nobody observed. Both directions are named here so that
        // adding a source has to make a deliberate choice rather than inherit one.
        var byName = Registry().Attributions().ToDictionary(row => row.SourceName, StringComparer.Ordinal);

        await Assert.That(byName["TinTin++ MSSP Mud Crawler"].Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(byName["TinTin++ MSDP Mud Crawler"].Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(byName["MudStats"].Tier).IsEqualTo(ImportTier.Measured);
        await Assert.That(byName["MudVerse"].Tier).IsEqualTo(ImportTier.Measured);

        // Hand-maintained. It does connect, but publishes no time for the result, so nothing it says
        // is importable as a measurement.
        await Assert.That(byName["The Mud Connector"].Tier).IsEqualTo(ImportTier.Asserted);
    }

    [Test]
    public async Task ARunSkipsAndSaysWhyRatherThanAttemptingASourceItMayNotFetch()
    {
        var harness = Harness.Build(Start);
        var runner = new ImportRunner(Registry(), harness.Pipeline);

        var run = await runner.RunAsync(new ImportRunOptions { Only = ["MudVerse"] }, CancellationToken.None);

        await Assert.That(run.Reports).IsEmpty();
        await Assert.That(run.Skipped.Single()).Contains(EtiquettePlanner.MaintainerNotContacted);
    }
}
