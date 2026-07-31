using System.Reflection;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Import.Tests.Support;

namespace MUI.Import.Tests;

/// <summary>
/// <b>Spec §7.6's two tiers — the pin.</b>
/// </summary>
/// <remarks>
/// Not "we are careful not to write history for an asserted source": the object that would do the
/// writing does not exist on that path. The pipeline below is handed an asserted source whose
/// <see cref="ImportedGame"/> is <em>full</em> of presence and availability rows, and zero of them
/// reach a store — while the same record through a measured source writes all of them, labelled as
/// imported, and yields exactly half of our own grace for the same measured time.
/// </remarks>
public class HistoryTierTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private const string Host = "anachronism.example";

    private const int Port = 4000;

    /// <summary>A record stuffed with history it may or may not be entitled to.</summary>
    private static ImportedGame Stuffed(string sourceName) => new()
    {
        SourceName = sourceName,
        SourceKey = "anachronism",
        Name = "Anachronism",
        Endpoints = [new ImportedEndpoint(Host, Port, EndpointKind.Telnet)],
        Fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["GENRE"] = "Fantasy" },
        Presence =
        [
            new ImportedPresence(Now.AddHours(-3), 24),
            new ImportedPresence(Now.AddHours(-2), 31),
            new ImportedPresence(Now.AddHours(-1), 18),
        ],
        Availability =
        [
            new ImportedAvailability(Now.AddYears(-4), Now.AddYears(-1), true),
            new ImportedAvailability(Now.AddYears(-1), Now.AddDays(-1), false),
        ],
    };

    [Test]
    public async Task AnAssertedSourceStuffedWithHistoryWritesNoneOfItAndIsCountedForTrying()
    {
        var harness = Harness.Build(Now);
        await harness.SeedProbedGameAsync(Host, Port, Now);

        var source = new FakeSource("The MUD Connector", ImportTier.Asserted,
            FakeSource.ExportEtiquette("TheMudConnector"), [Stuffed("The MUD Connector")]);

        var report = await harness.Pipeline.RunAsync(source, CancellationToken.None);

        await Assert.That(harness.Presence.Samples).IsEmpty();
        await Assert.That(harness.Availability.Intervals).IsEmpty();
        await Assert.That(report.PresenceRows).IsEqualTo(0);
        await Assert.That(report.AvailabilityRows).IsEqualTo(0);

        // Three presence readings plus two spans, refused and counted. A silent refusal reads
        // identically to a source that offered nothing, and only one of those is worth an email.
        await Assert.That(report.Rejected).IsEqualTo(5);
        await Assert.That(report.Notes.Any(note => note.Contains("asserted source", StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task AnAssertedSourceStillSeedsDiscoveryAndEndpoints()
    {
        var harness = Harness.Build(Now);
        var gameId = await harness.SeedProbedGameAsync(Host, Port, Now);

        var source = new FakeSource("The MUD Connector", ImportTier.Asserted,
            FakeSource.ExportEtiquette("TheMudConnector"), [Stuffed("The MUD Connector")]);

        var report = await harness.Pipeline.RunAsync(source, CancellationToken.None);

        await Assert.That(harness.Targets.Targets.Count()).IsEqualTo(1);
        await Assert.That(harness.Endpoints.Endpoints.Count(endpoint => endpoint.GameId == gameId)).IsEqualTo(1);
        await Assert.That(report.FieldsWritten).IsEqualTo(1);
    }

    [Test]
    public async Task TheAssertedSinkHoldsNothingItCouldWriteWith()
    {
        // The rule is enforced by construction: this type has no store, no writer and no clock, so it
        // cannot write history even if every caller forgets that it must not.
        var constructors = typeof(AssertedHistorySink).GetConstructors();

        await Assert.That(constructors.Length).IsEqualTo(1);
        await Assert.That(constructors[0].GetParameters().Length).IsEqualTo(0);
        await Assert.That(typeof(AssertedHistorySink)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Length)
            .IsEqualTo(0);
    }

    [Test]
    public async Task TheTierChoosesTheSinkAndNothingElseDoes()
    {
        var writer = new DryRunImportWriter();
        var provenance = new InMemoryImportProvenanceStore();

        await Assert.That(HistorySink.For(ImportTier.Asserted, writer, provenance, Now))
            .IsTypeOf<AssertedHistorySink>();
        await Assert.That(HistorySink.For(ImportTier.Measured, writer, provenance, Now))
            .IsTypeOf<MeasuredHistorySink>();
    }

    [Test]
    public async Task AMeasuredSourceWritesItsPresenceLabelledAsImportedAndNeverAsOurOwn()
    {
        var harness = Harness.Build(Now);
        var gameId = await harness.SeedProbedGameAsync(Host, Port, Now);

        var source = new FakeSource("MudStats", ImportTier.Measured,
            FakeSource.ExportEtiquette("MudStats"), [Stuffed("MudStats")]);

        var report = await harness.Pipeline.RunAsync(source, CancellationToken.None);

        await Assert.That(report.PresenceRows).IsEqualTo(3);
        await Assert.That(report.Rejected).IsEqualTo(0);
        await Assert.That(harness.Presence.Samples.Count()).IsEqualTo(3);

        foreach (var sample in harness.Presence.Samples)
        {
            await Assert.That(sample.GameId).IsEqualTo(gameId);
            await Assert.That(sample.Source).IsEqualTo(FieldSource.ImportedMeasured);

            // §11's histograms need a per-player WHO read, which an import never had. An empty object
            // here would say we looked and found nothing.
            await Assert.That(sample.Aggregates).IsNull();
            await Assert.That(sample.Reason).IsNull();
        }
    }

    [Test]
    public async Task AnImportedAvailabilitySpanIsNeverLeftOpenAndCarriesNoInventedCause()
    {
        var harness = Harness.Build(Now);
        await harness.SeedProbedGameAsync(Host, Port, Now);

        var stuffed = Stuffed("MudStats") with
        {
            // The source's export ends with the span still running.
            Availability = [new ImportedAvailability(Now.AddDays(-30), null, true)],
            Presence = [],
        };

        var source = new FakeSource("MudStats", ImportTier.Measured,
            FakeSource.ExportEtiquette("MudStats"), [stuffed]);

        await harness.Pipeline.RunAsync(source, CancellationToken.None);

        var interval = harness.Availability.Intervals.Single();

        await Assert.That(interval.To).IsEqualTo(Now);
        await Assert.That(interval.State).IsEqualTo(AvailabilityState.Reachable);
        await Assert.That(interval.Cause).IsEqualTo(FailureCause.None);
    }

    [Test]
    public async Task AnUnreachableImportedSpanCarriesNoCauseBecauseWeDidNotMeasureOne()
    {
        var harness = Harness.Build(Now);
        await harness.SeedProbedGameAsync(Host, Port, Now);

        var source = new FakeSource("MudStats", ImportTier.Measured,
            FakeSource.ExportEtiquette("MudStats"), [Stuffed("MudStats")]);

        await harness.Pipeline.RunAsync(source, CancellationToken.None);

        var down = harness.Availability.Intervals.Single(
            interval => interval.State is AvailabilityState.Unreachable);

        // `timeout` or `dns` here would be our invention about somebody else's socket, in a game's
        // public reachability history.
        await Assert.That(down.Cause).IsEqualTo(FailureCause.None);
    }

    [Test]
    public async Task FourImportedYearsEarnTheGraceOfTwoOfOurOwn()
    {
        var harness = Harness.Build(Now);
        var gameId = await harness.SeedProbedGameAsync(Host, Port, Now);

        var stuffed = Stuffed("MudStats") with
        {
            Availability = [new ImportedAvailability(Now.AddYears(-4), Now, true)],
            Presence = [],
        };

        var source = new FakeSource("MudStats", ImportTier.Measured,
            FakeSource.ExportEtiquette("MudStats"), [stuffed]);

        await harness.Pipeline.RunAsync(source, CancellationToken.None);

        var imported = harness.Availability.CumulativeImportedMeasuredReachable(gameId);

        // Computed the way ArchiveSweeper computes it — two sums into ArchivePolicy.GraceFor — rather
        // than by a calculator of this test's own, which would agree with itself and with nothing that
        // ships. Four imported years credit two, and two years of anybody's time is 182 days of grace.
        var importedGrace = ArchivePolicy.GraceFor(TimeSpan.Zero, imported);
        var ourGrace = ArchivePolicy.GraceFor(imported / 2);

        await Assert.That(importedGrace).IsEqualTo(ourGrace);
        await Assert.That(importedGrace).IsGreaterThan(ArchivePolicy.Floor);
    }

    [Test]
    public async Task AnAssertedSourcesFourYearsEarnNoGraceAtAll()
    {
        var harness = Harness.Build(Now);
        var gameId = await harness.SeedProbedGameAsync(Host, Port, Now);

        var stuffed = Stuffed("The MUD Connector") with
        {
            Availability = [new ImportedAvailability(Now.AddYears(-4), Now, true)],
            Presence = [],
        };

        var source = new FakeSource("The MUD Connector", ImportTier.Asserted,
            FakeSource.ExportEtiquette("TheMudConnector"), [stuffed]);

        await harness.Pipeline.RunAsync(source, CancellationToken.None);

        var imported = harness.Availability.CumulativeImportedMeasuredReachable(gameId);

        await Assert.That(imported).IsEqualTo(TimeSpan.Zero);
        await Assert.That(ArchivePolicy.GraceFor(TimeSpan.Zero, imported)).IsEqualTo(ArchivePolicy.Floor);
    }
}
