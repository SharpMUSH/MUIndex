using System.Reflection;

using Dapper;

using MUI.Catalog;
using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Tests;

/// <summary>
/// The <c>crawl_target</c> registry's contract, against the database that has to enforce it.
/// </summary>
/// <remarks>
/// <c>MUI.Discovery.Tests</c> asserts the same contract against an in-memory registry. That is not a
/// duplicate: a fake kinder than the real thing passes every test while production mints a second
/// target for the same machine, and these are the assertions only the real schema can make.
/// </remarks>
public class CrawlRegistryPostgresTests
{
    private static CrawlTarget Target(
        string host = "mud.example.org",
        int port = 4201,
        int depth = 0,
        DateTimeOffset? next = null) => new()
    {
        Id = Guid.CreateVersion7(),
        Host = host,
        Port = port,
        Depth = depth,
        NextProbeAt = next ?? DateTimeOffset.UtcNow,
        FirstSeenAt = DateTimeOffset.UtcNow,
    };

    [Test]
    public async Task NothingInTheRegistryCanRetireATarget()
    {
        // The worst regression this repository could ship: a game dark for two years must still be
        // probed weekly forever. The interface holds this by reflection; the table by having no
        // column for it.
        await using var database = await PostgresFixture.MigratedAsync();

        await using var connection = await database.DataSource.OpenConnectionAsync();

        var columns = (await connection.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'crawl_target'"))
            .ToList();

        await Assert.That(columns)
            .DoesNotContain(c => c.Contains("retire", StringComparison.OrdinalIgnoreCase)
                || c.Contains("enabled", StringComparison.OrdinalIgnoreCase)
                || c.Contains("deleted", StringComparison.OrdinalIgnoreCase)
                || c.Contains("disabled", StringComparison.OrdinalIgnoreCase));

        var methods = typeof(ICrawlTargetRepository)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name);

        await Assert.That(methods)
            .DoesNotContain(name => name.Contains("Delete", StringComparison.Ordinal)
                || name.Contains("Remove", StringComparison.Ordinal)
                || name.Contains("Retire", StringComparison.Ordinal));
    }

    [Test]
    public async Task OneMachineIsOneTargetHoweverTheHostIsSpelled()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var registry = new NpgsqlCrawlTargetRepository(database.DataSource);

        var first = await registry.AddAsync(Target("MUD.Example.ORG."), CancellationToken.None);
        var second = await registry.AddAsync(Target("mud.example.org"), CancellationToken.None);

        await Assert.That(second).IsEqualTo(first);

        await using var connection = await database.DataSource.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM crawl_target"))
            .IsEqualTo(1L);
    }

    [Test]
    public async Task AddingAKnownAddressNeverResetsItsSchedule()
    {
        // A second referral naming an address we already crawl must not drag it forward — exactly the
        // coupling §7.1 removes by giving every target its own next_probe_at.
        await using var database = await PostgresFixture.MigratedAsync();
        var registry = new NpgsqlCrawlTargetRepository(database.DataSource);

        var later = DateTimeOffset.UtcNow.AddDays(3);
        await registry.AddAsync(Target(next: later), CancellationToken.None);
        await registry.AddAsync(Target(next: DateTimeOffset.UtcNow), CancellationToken.None);

        var stored = await registry.ByAddressAsync("mud.example.org", 4201, CancellationToken.None);

        await Assert.That(stored!.NextProbeAt.ToUnixTimeSeconds()).IsEqualTo(later.ToUnixTimeSeconds());
    }

    [Test]
    public async Task ARepeatSightingMayOnlyLowerTheRecordedDepth()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var registry = new NpgsqlCrawlTargetRepository(database.DataSource);

        await registry.AddAsync(Target(depth: 3), CancellationToken.None);
        await registry.AddAsync(Target(depth: 1), CancellationToken.None);
        await registry.AddAsync(Target(depth: 4), CancellationToken.None);

        var stored = await registry.ByAddressAsync("mud.example.org", 4201, CancellationToken.None);

        await Assert.That(stored!.Depth).IsEqualTo(1);
    }

    [Test]
    public async Task AStatedCrawlDelayIsNotForgottenOnASilentProbe()
    {
        // A server that stated CRAWL DELAY once and then stopped publishing MSSP has not withdrawn the
        // request, and forgetting it would mean knocking more often at a server that asked us not to.
        await using var database = await PostgresFixture.MigratedAsync();
        var registry = new NpgsqlCrawlTargetRepository(database.DataSource);

        var id = await registry.AddAsync(Target(), CancellationToken.None);

        await registry.RecordAttemptAsync(
            id, DateTimeOffset.UtcNow, true, TimeSpan.FromDays(30), DateTimeOffset.UtcNow.AddDays(30),
            CancellationToken.None);
        await registry.RecordAttemptAsync(
            id, DateTimeOffset.UtcNow, true, null, DateTimeOffset.UtcNow.AddDays(30), CancellationToken.None);

        var stored = await registry.ByAddressAsync("mud.example.org", 4201, CancellationToken.None);

        await Assert.That(stored!.CrawlDelay).IsEqualTo(TimeSpan.FromDays(30));
    }

    [Test]
    public async Task FailuresAccrueAndOneSuccessClearsThem()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var registry = new NpgsqlCrawlTargetRepository(database.DataSource);

        var id = await registry.AddAsync(Target(), CancellationToken.None);
        var now = DateTimeOffset.UtcNow;

        await registry.RecordAttemptAsync(id, now, false, null, now, CancellationToken.None);
        await registry.RecordAttemptAsync(id, now, false, null, now, CancellationToken.None);

        await Assert.That((await registry.ByAddressAsync("mud.example.org", 4201, CancellationToken.None))!
            .ConsecutiveFailures).IsEqualTo(2);

        await registry.RecordAttemptAsync(id, now, true, null, now, CancellationToken.None);

        await Assert.That((await registry.ByAddressAsync("mud.example.org", 4201, CancellationToken.None))!
            .ConsecutiveFailures).IsEqualTo(0);
    }

    [Test]
    public async Task ATargetAlreadyAttributedToAGameIsNotSilentlyRepointed()
    {
        // Re-pointing an address at a different game is a merge. It is §7.3's business, logged and
        // reversible, and never a side effect of a crawl cycle.
        await using var database = await PostgresFixture.MigratedAsync();
        var registry = new NpgsqlCrawlTargetRepository(database.DataSource);

        var id = await registry.AddAsync(Target(), CancellationToken.None);
        var first = await SeedGameAsync(database.DataSource, "corvid");
        var second = await SeedGameAsync(database.DataSource, "magpie");

        await registry.AttachGameAsync(id, first, CancellationToken.None);
        await registry.AttachGameAsync(id, second, CancellationToken.None);

        var stored = await registry.ByAddressAsync("mud.example.org", 4201, CancellationToken.None);

        await Assert.That(stored!.GameId).IsEqualTo(first);
    }

    [Test]
    public async Task DueTakesTheOldestFirstAndThenTheShallowest()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var registry = new NpgsqlCrawlTargetRepository(database.DataSource);

        var now = DateTimeOffset.UtcNow;

        await registry.AddAsync(Target("deep.example.org", next: now.AddMinutes(-1), depth: 4), CancellationToken.None);
        await registry.AddAsync(Target("shallow.example.org", next: now.AddMinutes(-1)), CancellationToken.None);
        await registry.AddAsync(Target("later.example.org", next: now.AddHours(1)), CancellationToken.None);

        var due = await registry.DueAsync(now, 10, CancellationToken.None);

        await Assert.That(due).Count().IsEqualTo(2);
        await Assert.That(due[0].Host).IsEqualTo("shallow.example.org");
    }

    [Test]
    public async Task AnUncanonicalHostIsRefusedByTheDatabaseAndNotOnlyByUs()
    {
        // The write path normalises; the CHECK is what stops the day somebody adds a second write path
        // and forgets. Two spellings of one machine is double the traffic at somebody else's server.
        await using var database = await PostgresFixture.MigratedAsync();

        await using var connection = await database.DataSource.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO crawl_target (id, host, port, next_probe_at, first_seen_at)
                VALUES (@id, 'MUD.Example.ORG', 4201, now(), now())
                """,
                new { id = Guid.CreateVersion7() }))
            .Throws<PostgresException>();
    }

    private static async Task<Guid> SeedGameAsync(NpgsqlDataSource source, string slug)
    {
        var id = Guid.CreateVersion7();

        await using var connection = await source.OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO game (id, slug, name, state, first_seen_at)
            VALUES (@id, @slug, @slug, 'active', now())
            """,
            new { id, slug });

        return id;
    }

    /// <summary>
    /// Write-once, enforced by the insert rather than by a rule somebody has to remember. A referral
    /// naming an address AresCentral already gave us must not relabel where we first heard of it.
    /// </summary>
    [Test]
    public async Task ASecondChannelFindingAKnownAddressDoesNotRelabelIt()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var registry = new NpgsqlCrawlTargetRepository(database.DataSource);

        await registry.AddAsync(
            Target("relabel.example.org") with { DiscoveredVia = DiscoverySource.AresCentral },
            CancellationToken.None);

        await registry.AddAsync(
            Target("relabel.example.org") with { DiscoveredVia = DiscoverySource.Referral },
            CancellationToken.None);

        var stored = await registry.ByAddressAsync(
            "relabel.example.org", 4201, CancellationToken.None);

        await Assert.That(stored!.DiscoveredVia).IsEqualTo(DiscoverySource.AresCentral);
    }

    /// <summary>
    /// Every address in the registry today predates the column, and a guess would be exactly the
    /// accident §7.6 warned about. Unknown stays unknown, and the page renders nothing.
    /// </summary>
    [Test]
    public async Task AnAddressRecordedWithNoChannelStaysUnknown()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var registry = new NpgsqlCrawlTargetRepository(database.DataSource);

        await registry.AddAsync(Target("unknown.example.org"), CancellationToken.None);

        var stored = await registry.ByAddressAsync(
            "unknown.example.org", 4201, CancellationToken.None);

        await Assert.That(stored!.DiscoveredVia).IsNull();
    }
}
