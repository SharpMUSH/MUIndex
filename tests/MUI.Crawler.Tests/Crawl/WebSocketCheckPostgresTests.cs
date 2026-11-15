using Dapper;

using MUI.Crawl;
using MUI.Crawler.Tests.Support;

namespace MUI.Crawler.Tests;

/// <summary>
/// The check of a game's declared WebSocket URL: dialled through the gates, written beside the
/// declaration, and spaced out.
/// </summary>
public class WebSocketCheckPostgresTests
{
    private const string Host = "socket-realm.example.org";
    private const int Port = 4000;
    private const string Url = "wss://play.socket-realm.example.org/ws";

    private static readonly Func<ProbeTarget, ProbeResult> Declaring = Answering(target =>
        Probes.Answered(host: target.Host, port: target.Port, banner: "Socket Realm\r\nName: ", transport: ProbeTransport.WebSocket));

    [Test]
    public async Task ADeclaredUrlThatAnswersTelnetIsMeasuredBesideTheDeclaration()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        var probe = new ScriptedProbe(Declaring);
        await CrawlCycles.Build(source, probe, TimeProvider.System).RunAsync();

        var check = probe.Asked.Single(target => target.WebSocket is not null);

        await Assert.That(check.WebSocket!.ToString()).IsEqualTo(Url);
        await Assert.That(check.AsksNothing).IsTrue();

        // The guard's answer, so the dial reaches what was ruled on.
        await Assert.That(check.Addresses.Select(a => a.ToString())).IsEquivalentTo(["198.51.100.7"]);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await FieldAsync(connection, "capability.websocket.measured", "handshake")).IsEqualTo("true");
        await Assert.That(await FieldAsync(connection, "capability.websocket.declared", "mssp")).IsEqualTo("true");
        await Assert.That(await FieldAsync(connection, "WEBSOCKET", "mssp")).IsEqualTo(Url);
        await Assert.That(await CheckedAsync(connection)).IsTrue();
    }

    [Test]
    public async Task TheCheckWaitsItsIntervalBeforeDiallingAgain()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        await CrawlCycles.Build(source, new ScriptedProbe(Declaring), TimeProvider.System).RunAsync();

        await using var connection = await source.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE crawl_target SET next_probe_at = now() - interval '1 hour'");

        var again = new ScriptedProbe(Declaring);
        await CrawlCycles.Build(source, again, TimeProvider.System).RunAsync();

        await Assert.That(again.Asked.Count(target => target.WebSocket is not null)).IsEqualTo(0);
        await Assert.That(again.Asked.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ARefusedUrlIsMeasuredFalse()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        var probe = new ScriptedProbe(Answering(target =>
            Probes.Failed(host: target.Host, port: target.Port, cause: DialFailureCause.Refused)));
        await CrawlCycles.Build(source, probe, TimeProvider.System).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await FieldAsync(connection, "capability.websocket.measured", "handshake")).IsEqualTo("false");
    }

    /// <summary>
    /// A timeout is the path from here, not the game: nothing is written, but the check is spent.
    /// </summary>
    [Test]
    public async Task ATimeoutWritesNothingAboutTheGame()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        var probe = new ScriptedProbe(Answering(target =>
            Probes.Failed(host: target.Host, port: target.Port, cause: DialFailureCause.Timeout)));
        await CrawlCycles.Build(source, probe, TimeProvider.System).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await FieldAsync(connection, "capability.websocket.measured", "handshake")).IsNull();
        await Assert.That(await CheckedAsync(connection)).IsTrue();

        // And the game's own reachability is untouched by a second door that did not open.
        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM availability_interval WHERE state <> 'reachable'")).IsEqualTo(0);
    }

    /// <summary>
    /// The URL is the game's own and goes through the scope guard like a referral: one resolving
    /// inside a private network is never dialled, and no check is recorded — we declined.
    /// </summary>
    [Test]
    public async Task AUrlResolvingSomewherePrivateIsNeverDialled()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        var resolver = new FakeHostResolver().Resolving("play.socket-realm.example.org", "10.0.0.5");
        var probe = new ScriptedProbe(Declaring);
        await CrawlCycles.Build(source, probe, TimeProvider.System, resolver).RunAsync();

        await Assert.That(probe.Asked.Count(target => target.WebSocket is not null)).IsEqualTo(0);

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await FieldAsync(connection, "capability.websocket.measured", "handshake")).IsNull();
        await Assert.That(await CheckedAsync(connection)).IsFalse();
    }

    /// <summary>The game's own probe declares the URL; the check answers with <paramref name="check"/>.</summary>
    private static Func<ProbeTarget, ProbeResult> Answering(Func<ProbeTarget, ProbeResult> check) =>
        target => target.WebSocket is not null
            ? check(target)
            : Probes.Answered(
                host: target.Host,
                port: target.Port,
                mssp: Probes.Mssp(("NAME", "Socket Realm"), ("PLAYERS", "3"), ("WEBSOCKET", Url)));

    private static Task<string?> FieldAsync(Npgsql.NpgsqlConnection connection, string field, string source) =>
        connection.ExecuteScalarAsync<string?>(
            "SELECT value FROM game_field WHERE field = @field AND source = @source",
            new { field, source });

    private static Task<bool> CheckedAsync(Npgsql.NpgsqlConnection connection) =>
        connection.ExecuteScalarAsync<bool>("SELECT websocket_checked_at IS NOT NULL FROM crawl_target");
}
