using System.Runtime.CompilerServices;

using MUI.Catalog;
using MUI.Catalog.Persistence;

namespace MUI.Import.Tests.Support;

/// <summary>A source that yields a fixed list and records whether anybody enumerated it.</summary>
internal sealed class FakeSource(
    string sourceName,
    ImportTier tier,
    ImportEtiquette etiquette,
    IReadOnlyList<ImportedGame> games) : IDirectorySource
{
    public string SourceName { get; } = sourceName;

    public ImportTier Tier { get; } = tier;

    public ImportEtiquette Etiquette { get; } = etiquette;

    public bool Enumerated { get; private set; }

    public async IAsyncEnumerable<ImportedGame> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Enumerated = true;

        foreach (var game in games)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return game;

            await Task.Yield();
        }
    }

    public static ImportEtiquette ExportEtiquette(string name) => new()
    {
        SourceName = name,
        AttributionUri = new Uri($"https://{name.ToLowerInvariant()}.test/"),
        BulkExportUri = new Uri($"https://{name.ToLowerInvariant()}.test/dump.json"),
        RobotsUri = new Uri($"https://{name.ToLowerInvariant()}.test/robots.txt"),
        UserAgent = ImporterIdentity.UserAgent,
    };
}

/// <summary>Every store an import writes through, plus a pipeline wired to them.</summary>
internal sealed record Harness(
    InMemoryCrawlTargetRepository Targets,
    InMemoryGameStore Games,
    InMemoryEndpointStore Endpoints,
    InMemoryGameFieldStore Fields,
    InMemoryPresenceStore Presence,
    InMemoryImportedAvailabilityWriter Availability,
    InMemoryImportProvenanceStore Provenance,
    ImportPipeline Pipeline)
{
    public static Harness Build(DateTimeOffset now)
    {
        var targets = new InMemoryCrawlTargetRepository();
        var games = new InMemoryGameStore();
        var endpoints = new InMemoryEndpointStore();
        var fields = new InMemoryGameFieldStore();
        var presence = new InMemoryPresenceStore();
        var availability = new InMemoryImportedAvailabilityWriter();
        var provenance = new InMemoryImportProvenanceStore();

        return new Harness(targets, games, endpoints, fields, presence, availability, provenance,
            new ImportPipeline(targets, games, endpoints, fields, presence, availability, provenance,
                new ManualTimeProvider(now)));
    }

    /// <summary>A game we have already probed for ourselves, with one endpoint we know.</summary>
    public async Task<Guid> SeedProbedGameAsync(string host, int port, DateTimeOffset at)
    {
        var gameId = Guid.NewGuid();

        await Games.InsertAsync(
            new GameRecord(gameId, "anachronism", "Anachronism", null, LifecycleState.Active, false, at, at),
            CancellationToken.None);

        await Endpoints.UpsertAsync(
            new GameEndpoint(gameId, host, port, EndpointKind.Telnet, at, at, EndpointState.Active),
            CancellationToken.None);

        return gameId;
    }
}
