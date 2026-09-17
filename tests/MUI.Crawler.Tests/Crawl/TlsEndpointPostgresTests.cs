using Dapper;

using MUI.Crawl;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// What the crawl loop does with a session that turned out to be TLS.
/// </summary>
/// <remarks>
/// Two facts, and they are different facts: the <em>endpoint</em> records how a reader reaches this
/// game, and the <em>target</em> records which door to knock on next time. The schema has carried
/// both since the beginning — <c>game_endpoint.kind</c> has had <c>tls</c> in its vocabulary and
/// <c>crawl_target.use_tls</c> has existed as a column — and nothing has ever written either, so the
/// TLS search facet returns an empty list to everyone who has ever tried it.
/// </remarks>
public class TlsEndpointPostgresTests
{
    [Test]
    public async Task AGameReadThroughTlsIsRecordedAsATlsEndpoint()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed("chatmud.com", 7443));

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "ChatMUD")),
            banner: "Welcome to ChatMUD",
            transport: ProbeTransport.Tls));

        await CrawlCycles.Build(source, probe, TimeProvider.System, options: CrawlCycles.Confirming()).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<string>(
                "SELECT kind FROM game_endpoint WHERE host = 'chatmud.com' AND port = 7443"))
            .IsEqualTo("tls");
    }

    [Test]
    public async Task ATargetFoundBehindTlsIsDialledAsTlsNextTime()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed("chatmud.com", 7443));

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "ChatMUD")),
            banner: "Welcome to ChatMUD",
            transport: ProbeTransport.Tls));

        await CrawlCycles.Build(source, probe, TimeProvider.System, options: CrawlCycles.Confirming()).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        // The point of remembering: without this the opportunistic retry is paid on every cycle,
        // for ever, at every TLS address in the registry.
        await Assert.That(await connection.ExecuteScalarAsync<bool>(
                "SELECT use_tls FROM crawl_target WHERE host = 'chatmud.com' AND port = 7443"))
            .IsTrue();

        await connection.ExecuteAsync("UPDATE crawl_target SET next_probe_at = now() - interval '1 hour'");

        var second = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "ChatMUD")),
            transport: ProbeTransport.Tls));

        await CrawlCycles.Build(source, second, TimeProvider.System, options: CrawlCycles.Confirming()).RunAsync();

        await Assert.That(second.Asked.Single().UseTls).IsTrue();
    }

    /// <summary>
    /// A game that stops speaking TLS stops being recorded as one.
    /// </summary>
    /// <remarks>
    /// The flag is a memory of a measurement, not a setting, so a later measurement overrules it.
    /// Left one-way, an address that moved back to an ordinary port would keep its stale endpoint
    /// kind and go on being dialled at a handshake nobody answers — and the probe's own fallback,
    /// which is what produced this result, would be re-run every cycle to no purpose.
    /// </remarks>
    [Test]
    public async Task AGameThatStopsSpeakingTlsIsNoLongerRecordedAsTls()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed("chatmud.com", 7443));

        var tls = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "ChatMUD")),
            transport: ProbeTransport.Tls));

        await CrawlCycles.Build(source, tls, TimeProvider.System, options: CrawlCycles.Confirming()).RunAsync();

        await using var connection = await source.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE crawl_target SET next_probe_at = now() - interval '1 hour'");

        var cleartext = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "ChatMUD")),
            transport: ProbeTransport.Telnet));

        await CrawlCycles.Build(source, cleartext, TimeProvider.System, options: CrawlCycles.Confirming()).RunAsync();

        await Assert.That(await connection.ExecuteScalarAsync<bool>(
                "SELECT use_tls FROM crawl_target WHERE host = 'chatmud.com' AND port = 7443"))
            .IsFalse();

        await Assert.That(await connection.ExecuteScalarAsync<string>(
                "SELECT kind FROM game_endpoint WHERE host = 'chatmud.com' AND port = 7443"))
            .IsEqualTo("telnet");
    }
}
