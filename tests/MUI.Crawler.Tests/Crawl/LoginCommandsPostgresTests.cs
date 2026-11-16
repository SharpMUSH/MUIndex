using Dapper;

using MUI.Crawl;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// What the crawl loop remembers about whether <c>INFO</c> and <c>VERSION</c> answer at an address
/// (<c>crawl_target.login_commands_*</c>, migration 0046), and that the next dial is told.
/// </summary>
public class LoginCommandsPostgresTests
{
    private const string Host = "fs.example.tw";
    private const int Port = 5555;

    /// <summary>What <c>fs.twkang.net:5555</c> answers to <c>INFO</c>: its name prompt, again.</summary>
    private static ProbeResult NamePrompt(ProbeTarget target) => Probes.Answered(
        host: target.Host,
        port: target.Port,
        who: new WhoReading(WhoConfidence.Count, 12),
        info: "好吧﹐那麼請重新輸入您的英文名字﹕") with { InfoAsked = target.AskLoginCommands };

    [Test]
    public async Task AnAddressThatNeverAnswersIsLeftAloneAfterThreeSessions()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        await using var connection = await source.OpenConnectionAsync();

        for (var session = 0; session < LoginCommands.Patience; session++)
        {
            var probe = new ScriptedProbe(NamePrompt);
            await CrawlCycles.Build(source, probe, TimeProvider.System).RunAsync();

            await Assert.That(probe.Asked.Single().AskLoginCommands).IsTrue();

            await DueAgainAsync(connection);
        }

        await Assert.That(await UnansweredAsync(connection)).IsEqualTo(LoginCommands.Patience);
        await Assert.That(await QuietAsync(connection)).IsTrue();

        var next = new ScriptedProbe(NamePrompt);
        await CrawlCycles.Build(source, next, TimeProvider.System).RunAsync();

        await Assert.That(next.Asked.Single().AskLoginCommands).IsFalse();

        // Not asking is not a miss: the memory is exactly as it was.
        await Assert.That(await UnansweredAsync(connection)).IsEqualTo(LoginCommands.Patience);
    }

    [Test]
    public async Task ARetrialThatAnswersEndsTheQuiet()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var source = database.DataSource;

        await CrawlCycles.SeedAsync(source, new CrawlSeed(Host, Port));

        await using var connection = await source.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "UPDATE crawl_target SET login_commands_unanswered = 3, login_commands_quiet_at = now() - interval '31 days'");

        var probe = new ScriptedProbe(target => Probes.Answered(
            host: target.Host,
            port: target.Port,
            info: "### Begin INFO 1\r\nName: Mortal Realms\r\nConnected: 7\r\n### End INFO\r\n")
            with { InfoAsked = target.AskLoginCommands });
        await CrawlCycles.Build(source, probe, TimeProvider.System).RunAsync();

        await Assert.That(probe.Asked.Single().AskLoginCommands).IsTrue();
        await Assert.That(await UnansweredAsync(connection)).IsEqualTo(0);
        await Assert.That(await QuietAsync(connection)).IsFalse();
    }

    private static Task<int> UnansweredAsync(Npgsql.NpgsqlConnection connection) =>
        connection.ExecuteScalarAsync<int>("SELECT login_commands_unanswered FROM crawl_target");

    private static Task<bool> QuietAsync(Npgsql.NpgsqlConnection connection) =>
        connection.ExecuteScalarAsync<bool>("SELECT login_commands_quiet_at IS NOT NULL FROM crawl_target");

    private static Task DueAgainAsync(Npgsql.NpgsqlConnection connection) =>
        connection.ExecuteAsync("UPDATE crawl_target SET next_probe_at = now() - interval '1 hour'");
}
