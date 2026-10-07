using Dapper;

using MUI.Crawl;
using MUI.Crawler.Tests.Support;

namespace MUI.Crawler.Tests;

/// <summary>
/// What the crawl loop remembers about whether an address answers <c>WHO</c>
/// (<c>crawl_target.who_answers_at</c>), and that the next dial is told.
/// </summary>
public class WhoAnswersPostgresTests
{
    private const string Host = "tapestries.example.org";
    private const int Port = 2069;

    [Test]
    public async Task AnAddressWhoseWhoCountedIsAskedWhoFromThenOn()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        var first = new ScriptedProbe(target => Probes.Answered(
            host: target.Host, port: target.Port, who: new WhoReading(WhoConfidence.Count, 412)));
        await CrawlCycles.Build(source, first, TimeProvider.System).RunAsync();

        await Assert.That(first.Asked.Single().WhoAnswers).IsFalse();

        await using var connection = await source.OpenConnectionAsync();

        await Assert.That(await KnownAsync(connection)).IsTrue();

        await DueAgainAsync(connection);

        var next = new ScriptedProbe(target => Probes.Answered(
            host: target.Host, port: target.Port, who: new WhoReading(WhoConfidence.Count, 405)));
        await CrawlCycles.Build(source, next, TimeProvider.System).RunAsync();

        await Assert.That(next.Asked.Single().WhoAnswers).IsTrue();
    }

    [Test]
    public async Task AnAddressWhoseLoginPromptAteWhoIsForgotten()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        await using var connection = await source.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE crawl_target SET who_answers_at = now() - interval '1 day'");

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host, port: target.Port, who: WhoReading.LoginPrompt));
        await CrawlCycles.Build(source, probe, TimeProvider.System).RunAsync();

        await Assert.That(probe.Asked.Single().WhoAnswers).IsTrue();
        await Assert.That(await KnownAsync(connection)).IsFalse();
    }

    private static Task<bool> KnownAsync(Npgsql.NpgsqlConnection connection) =>
        connection.ExecuteScalarAsync<bool>("SELECT who_answers_at IS NOT NULL FROM crawl_target");

    private static Task DueAgainAsync(Npgsql.NpgsqlConnection connection) =>
        connection.ExecuteAsync("UPDATE crawl_target SET next_probe_at = now() - interval '1 hour'");
}
