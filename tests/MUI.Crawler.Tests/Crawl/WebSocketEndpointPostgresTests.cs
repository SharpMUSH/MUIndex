using System.Collections.Concurrent;
using System.Net;

using Dapper;

using MUI.Crawl;
using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// The MSSP <c>WEBSOCKET</c> variable, through the crawl loop and into <c>game_endpoint</c>.
/// </summary>
/// <remarks>
/// A declared address becomes an endpoint only once a handshake there completed (rule 1), and an
/// address that did not upgrade, opted out, or resolved somewhere we do not dial leaves nothing behind
/// on the game's record (rule 5).
/// </remarks>
public class WebSocketEndpointPostgresTests
{
    private static ScriptedProbe Declaring(string value) => new(target => Probes.Answered(
        host: target.Host,
        port: target.Port,
        mssp: Probes.Mssp(("NAME", "Multi-Users in Middle-earth"), ("WEBSOCKET", value)),
        banner: "Welcome to MUME"));

    private sealed class Handshakes(bool answer) : IWebSocketProbe
    {
        public ConcurrentQueue<(WebSocketAddress Address, IReadOnlyList<IPAddress> Addresses)> Asked { get; } = [];

        public Task<WebSocketHandshake> HandshakeAsync(
            WebSocketAddress address,
            IReadOnlyList<IPAddress> addresses,
            CancellationToken cancellationToken = default)
        {
            Asked.Enqueue((address, addresses));
            return Task.FromResult(answer ? WebSocketHandshake.Upgraded : new WebSocketHandshake(false, "HTTP 502"));
        }
    }

    [Test]
    public async Task ADeclaredAddressThatUpgradesIsAWebSocketEndpointWithItsPath()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed("mume.org", 4242));
        var handshakes = new Handshakes(answer: true);

        await CrawlCycles.Build(
                source, Declaring("wss://mume.org/ws-play/"), TimeProvider.System,
                options: CrawlCycles.Confirming(), webSockets: handshakes)
            .RunAsync();

        await using var connection = await source.OpenConnectionAsync();
        var row = await connection.QuerySingleAsync<(string Kind, string Path, string State)>(
            "SELECT kind, path, state FROM game_endpoint WHERE host = 'mume.org' AND port = 443");

        await Assert.That(row.Kind).IsEqualTo("wss");
        await Assert.That(row.Path).IsEqualTo("/ws-play/");
        await Assert.That(row.State).IsEqualTo("active");

        // On the same game as the telnet address it was declared by.
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(DISTINCT game_id) FROM game_endpoint WHERE host = 'mume.org'"))
            .IsEqualTo(1L);

        // And dialled at the address the scope guard ruled on, not at a second lookup.
        await Assert.That(handshakes.Asked.Single().Addresses.Single()).IsEqualTo(IPAddress.Parse("198.51.100.7"));
    }

    [Test]
    public async Task AnAddressThatDidNotUpgradeLeavesNothing()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed("mume.org", 4242));

        var report = await CrawlCycles.Build(
                source, Declaring("wss://mume.org/ws-play/"), TimeProvider.System,
                options: CrawlCycles.Confirming(), webSockets: new Handshakes(answer: false))
            .RunAsync();

        await Assert.That(report.Answered).IsEqualTo(1);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM game_endpoint WHERE kind IN ('websocket', 'wss')"))
            .IsEqualTo(0L);

        // The declaration itself is still the game's field, verbatim.
        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM game_field WHERE field = 'WEBSOCKET'"))
            .IsEqualTo(1L);
    }

    [Test]
    public async Task AnAddressThatResolvesInsideIsNeverDialled()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed("mume.org", 4242));
        var handshakes = new Handshakes(answer: true);

        await CrawlCycles.Build(
                source, Declaring("ws://metadata.example.org/"), TimeProvider.System,
                resolver: new FakeHostResolver().Resolving("metadata.example.org", "169.254.169.254"),
                options: CrawlCycles.Confirming(), webSockets: handshakes)
            .RunAsync();

        await Assert.That(handshakes.Asked).IsEmpty();
    }

    [Test]
    public async Task AnAddressThatOptedOutIsNeverDialled()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed("mume.org", 4242));
        await new NpgsqlCrawlOptOutRepository(source).RecordAsync(
            new CrawlOptOut
            {
                Host = "mume.org",
                Port = 443,
                Source = OptOutSource.Request,
                RecordedAt = DateTimeOffset.UtcNow,
                LastConfirmedAt = DateTimeOffset.UtcNow,
                Detail = "asked",
            },
            default);

        var handshakes = new Handshakes(answer: true);

        await CrawlCycles.Build(
                source, Declaring("wss://mume.org/ws-play/"), TimeProvider.System,
                options: CrawlCycles.Confirming(), webSockets: handshakes)
            .RunAsync();

        await Assert.That(handshakes.Asked).IsEmpty();
    }

    [Test]
    public async Task ADeclarationNamingTheGamesOwnTelnetAddressLeavesItTelnet()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed("mume.org", 4242));
        var handshakes = new Handshakes(answer: true);

        await CrawlCycles.Build(
                source, Declaring("ws://mume.org:4242/"), TimeProvider.System,
                options: CrawlCycles.Confirming(), webSockets: handshakes)
            .RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<string>(
                "SELECT kind FROM game_endpoint WHERE host = 'mume.org' AND port = 4242"))
            .IsEqualTo("telnet");
        await Assert.That(handshakes.Asked).IsEmpty();
    }
}
