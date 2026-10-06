using Dapper;

using MUI.Crawl;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// What the crawl loop remembers about the plaintext <c>MSSP-REQUEST</c>, and what it asks next time.
/// </summary>
/// <remarks>
/// The probe decides how to send the line and reads what came back; these are about the memory
/// between dials (<c>crawl_target.mssp_route</c>), which is the whole of the "not again" in "try it,
/// and do not try it again on a game that does not answer".
/// </remarks>
public class MsspRoutePostgresTests
{
    private const string Host = "realms.example.org";
    private const int Port = 4000;

    [Test]
    public async Task AnAddressNobodyKnowsAnythingAboutIsGivenOneTrial()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        var probe = new ScriptedProbe(target => Probes.Answered(host: target.Host, port: target.Port));
        await CrawlCycles.Build(source, probe, TimeProvider.System).RunAsync();

        await Assert.That(probe.Asked.Single().PlaintextMssp).IsEqualTo(PlaintextMsspAsk.Trial);
    }

    [Test]
    public async Task AGameThatAnsweredIsAskedInTheSessionFromThenOn()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        var trial = new ScriptedProbe(target => AnsweredInPlaintext(target));
        await CrawlCycles.Build(source, trial, TimeProvider.System).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await RouteAsync(connection)).IsEqualTo("plaintext");

        // The report it sent is stored the way an option-70 report is: it is the same report.
        await Assert.That(await connection.ExecuteScalarAsync<string>(
                "SELECT value FROM game_field WHERE field = 'NAME' AND source = 'mssp'"))
            .IsEqualTo("Old Realms");

        await DueAgainAsync(connection);

        var next = new ScriptedProbe(target => AnsweredInPlaintext(target));
        await CrawlCycles.Build(source, next, TimeProvider.System).RunAsync();

        await Assert.That(next.Asked.Single().PlaintextMssp).IsEqualTo(PlaintextMsspAsk.Ask);
    }

    [Test]
    public async Task AGameThatDidNotAnswerIsNeverAskedAgain()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        var trial = new ScriptedProbe(target => Probes.Answered(host: target.Host, port: target.Port) with
        {
            PlaintextMssp = PlaintextMsspOutcome.Unanswered,
        });
        await CrawlCycles.Build(source, trial, TimeProvider.System).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await RouteAsync(connection)).IsEqualTo("none");
        await Assert.That(await connection.ExecuteScalarAsync<bool>(
                "SELECT mssp_route_at IS NOT NULL FROM crawl_target")).IsTrue();

        await DueAgainAsync(connection);

        var next = new ScriptedProbe(target => Probes.Answered(host: target.Host, port: target.Port));
        await CrawlCycles.Build(source, next, TimeProvider.System).RunAsync();

        await Assert.That(next.Asked.Single().PlaintextMssp).IsEqualTo(PlaintextMsspAsk.Never);
        await Assert.That(await RouteAsync(connection)).IsEqualTo("none");
    }

    [Test]
    public async Task AGameReportingOverOption70IsNeverSentTheRequest()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        var first = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "Old Realms"))));
        await CrawlCycles.Build(source, first, TimeProvider.System).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await RouteAsync(connection)).IsEqualTo("telnet");

        await DueAgainAsync(connection);

        // A session where the report went missing does not reopen the question.
        var next = new ScriptedProbe(target => Probes.Answered(host: target.Host, port: target.Port));
        await CrawlCycles.Build(source, next, TimeProvider.System).RunAsync();

        await Assert.That(next.Asked.Single().PlaintextMssp).IsEqualTo(PlaintextMsspAsk.Never);
        await Assert.That(await RouteAsync(connection)).IsEqualTo("telnet");
    }

    [Test]
    public async Task AFailedDialTeachesNothing()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        var probe = new ScriptedProbe(target => Probes.Failed(host: target.Host, port: target.Port));
        await CrawlCycles.Build(source, probe, TimeProvider.System).RunAsync();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await RouteAsync(connection)).IsNull();
    }

    [Test]
    public async Task TheSchemaRefusesARouteNobodyDated()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
                "UPDATE crawl_target SET mssp_route = 'none'"))
            .Throws<Npgsql.PostgresException>();
        await Assert.That(async () => await connection.ExecuteAsync(
                "UPDATE crawl_target SET mssp_route = 'gopher', mssp_route_at = now()"))
            .Throws<Npgsql.PostgresException>();
    }

    private static ProbeResult AnsweredInPlaintext(ProbeTarget target) =>
        Probes.Answered(
            host: target.Host,
            port: target.Port,
            mssp: Probes.Mssp(("NAME", "Old Realms"), ("PLAYERS", "4"))) with
        {
            MsspTransport = MsspTransport.PlaintextRequest,
            PlaintextMssp = PlaintextMsspOutcome.Answered,
        };

    private static Task<string?> RouteAsync(Npgsql.NpgsqlConnection connection) =>
        connection.ExecuteScalarAsync<string?>("SELECT mssp_route FROM crawl_target");

    private static Task DueAgainAsync(Npgsql.NpgsqlConnection connection) =>
        connection.ExecuteAsync("UPDATE crawl_target SET next_probe_at = now() - interval '1 hour'");
}
