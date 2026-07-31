using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Import.Tests.Support;

namespace MUI.Import.Tests;

/// <summary>
/// The one pass an import makes: resolve identity, seed crawl targets, and — only for a game we
/// already know — write endpoints and fields. Spec §7.1, §7.2, §7.6.
/// </summary>
public class ImportPipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static ImportedGame Record(string host, int port, params (string Field, string Value)[] fields) => new()
    {
        SourceName = "Example Directory",
        SourceKey = $"{host}:{port}",
        Name = "Anachronism",
        SourceUri = new Uri("https://example.test/games/anachronism"),
        Endpoints = [new ImportedEndpoint(host, port, EndpointKind.Telnet)],
        Fields = fields.ToDictionary(field => field.Field, field => field.Value, StringComparer.Ordinal),
    };

    private static FakeSource Source(params ImportedGame[] games) =>
        new("Example Directory", ImportTier.Measured, FakeSource.ExportEtiquette("Example"), games);

    [Test]
    public async Task AnUnknownAddressBecomesACrawlTargetAndNotAGame()
    {
        var harness = Harness.Build(Now);

        var report = await harness.Pipeline.RunAsync(
            Source(Record("newgame.example", 4201)), CancellationToken.None);

        await Assert.That(report.GamesSeen).IsEqualTo(1);
        await Assert.That(report.Matched).IsEqualTo(0);
        await Assert.That(report.TargetsAdded).IsEqualTo(1);

        // Nothing was listed. A host becomes a game by answering for itself (spec §7.2), and this
        // pipeline has no method that could mint one.
        await Assert.That(await harness.Games.UnarchivedAsync()).IsEmpty();
        await Assert.That(harness.Endpoints.Endpoints).IsEmpty();
        await Assert.That(harness.Fields.Fields).IsEmpty();
    }

    [Test]
    public async Task ASeededTargetIsDueNowAndIsNotAnOperatorSeed()
    {
        var harness = Harness.Build(Now);

        await harness.Pipeline.RunAsync(Source(Record("newgame.example", 4201)), CancellationToken.None);

        var target = harness.Targets.Targets.Single();

        await Assert.That(target.NextProbeAt).IsEqualTo(Now);
        await Assert.That(target.FirstSeenAt).IsEqualTo(Now);
        await Assert.That(target.GameId).IsNull();

        // The security-relevant default. A stranger's list never exempts a host from §7.2's
        // resolved-address gate, and it stays that way by nothing here mentioning the flag.
        await Assert.That(target.IsOperatorSeed).IsFalse();
    }

    [Test]
    public async Task AHostSpelledInCapitalsIsOneTargetAndNotTwo()
    {
        var harness = Harness.Build(Now);

        await harness.Pipeline.RunAsync(
            Source(Record("Anachronism.Example", 4000), Record("anachronism.example.", 4000)),
            CancellationToken.None);

        await Assert.That(harness.Targets.Targets.Count()).IsEqualTo(1);
        await Assert.That(harness.Targets.Targets.Single().Host).IsEqualTo("anachronism.example");
    }

    [Test]
    public async Task AKnownAddressAttachesFieldsToTheGameWeAlreadyProbed()
    {
        var harness = Harness.Build(Now);
        var gameId = await harness.SeedProbedGameAsync("anachronism.example", 4000, Now.AddYears(-1));

        var report = await harness.Pipeline.RunAsync(
            Source(Record("anachronism.example", 4000, ("GENRE", "Fantasy"), ("LANGUAGE", "English"))),
            CancellationToken.None);

        await Assert.That(report.Matched).IsEqualTo(1);
        await Assert.That(report.FieldsWritten).IsEqualTo(2);

        var genre = harness.Fields.Fields.Single(field => field.Field == "GENRE");

        await Assert.That(genre.GameId).IsEqualTo(gameId);
        await Assert.That(genre.Source).IsEqualTo(FieldSource.ImportedMeasured);
        await Assert.That(genre.Value).IsEqualTo("Fantasy");
    }

    [Test]
    public async Task AnImportNeverOverwritesWhatAProbeMeasured()
    {
        var harness = Harness.Build(Now);
        var gameId = await harness.SeedProbedGameAsync("anachronism.example", 4000, Now.AddYears(-1));

        await harness.Fields.UpsertAsync(
            new GameField(gameId, "GENRE", FieldSource.Mssp, "Science Fiction", Now.AddYears(-1), Now.AddDays(-1)));

        await harness.Pipeline.RunAsync(
            Source(Record("anachronism.example", 4000, ("GENRE", "Fantasy"))), CancellationToken.None);

        // Two rows for one field, keyed by source, exactly as §5.1 requires — and the ladder, not the
        // clock, decides which is shown.
        var rows = harness.Fields.Fields.Where(field => field.Field == "GENRE").ToList();

        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(FieldPrecedence.Winner(rows)!.Source).IsEqualTo(FieldSource.Mssp);
        await Assert.That(FieldPrecedence.Winner(rows)!.Value).IsEqualTo("Science Fiction");
    }

    [Test]
    public async Task ReImportingTheSameValueConfirmsItRatherThanRecordingAChange()
    {
        var harness = Harness.Build(Now);
        await harness.SeedProbedGameAsync("anachronism.example", 4000, Now.AddYears(-1));

        var source = Source(Record("anachronism.example", 4000, ("GENRE", "Fantasy")));

        await harness.Pipeline.RunAsync(source, CancellationToken.None);
        var second = await harness.Pipeline.RunAsync(source, CancellationToken.None);

        await Assert.That(harness.Fields.Changes).IsEmpty();
        await Assert.That(second.FieldsWritten).IsEqualTo(0);
        await Assert.That(harness.Fields.Fields.Count()).IsEqualTo(1);
        await Assert.That(harness.Fields.Fields.Single().FirstSeenAt).IsEqualTo(Now);
    }

    [Test]
    public async Task AValueThatMovedRecordsOneChange()
    {
        var harness = Harness.Build(Now);
        await harness.SeedProbedGameAsync("anachronism.example", 4000, Now.AddYears(-1));

        await harness.Pipeline.RunAsync(
            Source(Record("anachronism.example", 4000, ("GENRE", "Fantasy"))), CancellationToken.None);
        await harness.Pipeline.RunAsync(
            Source(Record("anachronism.example", 4000, ("GENRE", "Horror"))), CancellationToken.None);

        var change = harness.Fields.Changes.Single();

        await Assert.That(change.OldValue).IsEqualTo("Fantasy");
        await Assert.That(change.NewValue).IsEqualTo("Horror");
        await Assert.That(change.Source).IsEqualTo(FieldSource.ImportedMeasured);
    }

    [Test]
    public async Task ReRunningAnImportWritesNoSecondCopyOfAnything()
    {
        var harness = Harness.Build(Now);
        await harness.SeedProbedGameAsync("anachronism.example", 4000, Now.AddYears(-1));

        var withHistory = Record("anachronism.example", 4000, ("GENRE", "Fantasy")) with
        {
            Presence = [new ImportedPresence(Now.AddHours(-1), 12)],
            Availability = [new ImportedAvailability(Now.AddDays(-9), Now.AddDays(-1), true)],
        };

        var source = Source(withHistory);

        await harness.Pipeline.RunAsync(source, CancellationToken.None);
        var second = await harness.Pipeline.RunAsync(source, CancellationToken.None);

        await Assert.That(second.TargetsAdded).IsEqualTo(0);
        await Assert.That(second.PresenceRows).IsEqualTo(0);
        await Assert.That(second.AvailabilityRows).IsEqualTo(0);
        await Assert.That(harness.Presence.Samples.Count()).IsEqualTo(1);
        await Assert.That(harness.Availability.Intervals.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task EveryImportedValueCarriesTheSiteThatSaidItAndTheDayWeReadIt()
    {
        var harness = Harness.Build(Now);
        await harness.SeedProbedGameAsync("anachronism.example", 4000, Now.AddYears(-1));

        await harness.Pipeline.RunAsync(
            Source(Record("anachronism.example", 4000, ("GENRE", "Fantasy"))), CancellationToken.None);

        await Assert.That(harness.Provenance.Rows).IsNotEmpty();

        foreach (var row in harness.Provenance.Rows)
        {
            await Assert.That(row.SourceName).IsEqualTo("Example Directory");
            await Assert.That(row.ImportedAt).IsEqualTo(Now);
            await Assert.That(row.SourceUri).IsEqualTo(new Uri("https://example.test/games/anachronism"));
            await Assert.That(row.Tier).IsEqualTo(ImportTier.Measured);
        }

        var contributions = await harness.Provenance.ContributionsAsync();

        await Assert.That(contributions.Single().SourceName).IsEqualTo("Example Directory");
    }

    [Test]
    public async Task AnImportedEndpointIsStaleRatherThanActiveBecauseWeHaveNotReachedItOurselves()
    {
        var harness = Harness.Build(Now);
        await harness.SeedProbedGameAsync("anachronism.example", 4000, Now.AddYears(-1));

        // The listing carries the address we know AND a second one we do not. The first is what makes
        // the record resolve to a game at all; the second is what this test is about.
        var record = Record("anachronism.example", 4000) with
        {
            Endpoints =
            [
                new ImportedEndpoint("anachronism.example", 4000, EndpointKind.Telnet),
                new ImportedEndpoint("anachronism.example", 4020, EndpointKind.Telnet),
            ],
        };

        await harness.Pipeline.RunAsync(Source(record), CancellationToken.None);

        var fresh = harness.Endpoints.Endpoints.Single(endpoint => endpoint.Port == 4020);

        await Assert.That(fresh.State).IsEqualTo(EndpointState.Stale);

        // And the address we HAD reached keeps the state our own probe gave it.
        var known = harness.Endpoints.Endpoints.Single(endpoint => endpoint.Port == 4000);

        await Assert.That(known.State).IsEqualTo(EndpointState.Active);
    }

    [Test]
    public async Task ADryRunReadsEverythingAndWritesNothing()
    {
        var harness = Harness.Build(Now);
        await harness.SeedProbedGameAsync("anachronism.example", 4000, Now.AddYears(-1));

        var withHistory = Record("anachronism.example", 4000, ("GENRE", "Fantasy")) with
        {
            Presence = [new ImportedPresence(Now.AddHours(-1), 12)],
        };

        var writer = new DryRunImportWriter();
        var report = await harness.Pipeline.DryRunAsync(Source(withHistory), writer, CancellationToken.None);

        await Assert.That(report.FieldsWritten).IsEqualTo(1);
        await Assert.That(report.PresenceRows).IsEqualTo(1);

        // Reported, and in the rehearsal's own hands — and in none of the real stores.
        await Assert.That(writer.Fields.Count()).IsEqualTo(1);
        await Assert.That(writer.Presence.Count()).IsEqualTo(1);
        await Assert.That(harness.Fields.Fields).IsEmpty();
        await Assert.That(harness.Presence.Samples).IsEmpty();
        await Assert.That(harness.Provenance.Rows).IsEmpty();
    }

    [Test]
    public async Task AnImportNeverSchedulesAProbe()
    {
        // The in-memory registry throws on RecordAttemptAsync, which is the crawler's method and not
        // an importer's (spec §7.1). A run that completes is a run that never called it.
        var harness = Harness.Build(Now);

        var report = await harness.Pipeline.RunAsync(
            Source(Record("newgame.example", 4201)), CancellationToken.None);

        await Assert.That(report.TargetsAdded).IsEqualTo(1);
    }
}
