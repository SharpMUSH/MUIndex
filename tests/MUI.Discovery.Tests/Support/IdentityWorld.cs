using MUI.Catalog;
using MUI.Crawl;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// A catalogue in memory, and the matcher pointed at it. Every identity test in this suite runs
/// against this and nothing else — no network, no database.
/// </summary>
public sealed class IdentityWorld
{
    private static readonly DateTimeOffset Then = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    public InMemoryGameDirectory Games { get; } = new();

    public InMemoryEndpointDirectory Endpoints { get; } = new();

    public InMemoryGameFieldStore Fields { get; } = new();

    public DiscoveryOptions Options { get; init; } = new();

    /// <summary>
    /// Null on every test that does not set it, matching every production caller that has not wired
    /// one yet — <see cref="IdentityWeights.ResolvedEndpoint"/> never fires and the rest of this suite
    /// is unaffected by its existence.
    /// </summary>
    public IHostResolver? Resolver { get; set; }

    public IdentityMatcher Matcher => new(Games, Endpoints, Fields, Fields, Options, Resolver);

    /// <summary>A game with the stored fields a probe will be scored against.</summary>
    public async Task<Guid> GameAsync(params (string Field, string Value)[] fields)
    {
        var id = Games.Add();

        foreach (var (field, value) in fields)
        {
            await Fields.UpsertAsync(new GameField(id, field, FieldSource.Mssp, value, Then, Then), None);
        }

        return id;
    }

    public Task EndpointAsync(Guid gameId, string host, int port) =>
        Endpoints.UpsertAsync(new KnownEndpoint(gameId, host, port, Then, Then), None);
}
