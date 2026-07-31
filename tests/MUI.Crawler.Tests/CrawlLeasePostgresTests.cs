using MUI.Crawler.Tests.Support;

namespace MUI.Crawler.Tests;

/// <summary>
/// Spec §12 — N web replicas run exactly one crawler.
/// </summary>
/// <remarks>
/// The crawler is in-process with the web tier, so without this gate a three-replica deployment is
/// three crawlers: three times the traffic at every game in the catalogue, three presence writes
/// racing for one <c>(game_id, at)</c> key, and a politeness contract broken three ways. Asserted
/// against a real PostgreSQL because an advisory lock is a database behaviour and there is nothing to
/// fake about it that would still be a test.
/// </remarks>
public class CrawlLeasePostgresTests
{
    [Test]
    public async Task OnlyOneReplicaCanHoldTheLease()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var second = database.SecondPool();

        await using var first = await CrawlLease.TryAcquireAsync(database.DataSource, CrawlLease.DefaultKey);
        var loser = await CrawlLease.TryAcquireAsync(second, CrawlLease.DefaultKey);

        await Assert.That(first).IsNotNull();
        await Assert.That(loser).IsNull();
    }

    [Test]
    public async Task ReleasingTheLeaseLetsTheNextReplicaTakeIt()
    {
        // The failure mode that matters: a replica that stops crawling must not leave the crawl
        // stopped for everyone. Disposal closes the session, which is also what a killed process does.
        await using var database = await PostgresFixture.MigratedAsync();
        await using var second = database.SecondPool();

        var first = await CrawlLease.TryAcquireAsync(database.DataSource, CrawlLease.DefaultKey);
        await Assert.That(first).IsNotNull();
        await first!.DisposeAsync();

        await using var next = await CrawlLease.TryAcquireAsync(second, CrawlLease.DefaultKey);

        await Assert.That(next).IsNotNull();
    }

    [Test]
    public async Task AHeldLeaseSaysSoAndADisposedOneDoesNot()
    {
        // The loop asks this every cycle rather than trusting a connection it took once. A replica
        // that believes it holds a lock it lost is a second crawler.
        await using var database = await PostgresFixture.MigratedAsync();

        var lease = await CrawlLease.TryAcquireAsync(database.DataSource, CrawlLease.DefaultKey);
        await Assert.That(lease).IsNotNull();
        await Assert.That(await lease!.IsHeldAsync()).IsTrue();

        await lease.DisposeAsync();

        await Assert.That(await lease.IsHeldAsync()).IsFalse();
    }

    [Test]
    public async Task TwoDeploymentsSharingADatabaseCanUseDifferentKeys()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var second = database.SecondPool();

        await using var mine = await CrawlLease.TryAcquireAsync(database.DataSource, CrawlLease.DefaultKey);
        await using var theirs = await CrawlLease.TryAcquireAsync(second, CrawlLease.DefaultKey + 1);

        await Assert.That(mine).IsNotNull();
        await Assert.That(theirs).IsNotNull();
    }
}
