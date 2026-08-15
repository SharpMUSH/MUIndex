using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler.Tests.Support;

using Microsoft.Extensions.Logging.Abstractions;

namespace MUI.Crawler.Tests;

/// <summary>
/// Spec §5.2 and §12 — the rollup runs on a schedule, and N replicas run one of it.
/// </summary>
/// <remarks>
/// The lease matters more here than it does for the crawl loop. Two crawlers are rude; two
/// maintenance passes are two <c>DROP TABLE</c>s racing for one partition and two watermarks
/// leapfrogging each other past what has actually been aggregated.
/// </remarks>
public class PresenceMaintenanceServicePostgresTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [Test]
    public async Task TheServiceRollsUpWhatTheCrawlerWrote()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var game = await SeedGameAsync(database);
        var samples = new NpgsqlPresenceStore(database.DataSource);
        var rollups = new NpgsqlPresenceRollupStore(database.DataSource);
        var at = DateTimeOffset.UtcNow.AddHours(-2);

        await new PresenceWriter(samples).WriteAsync(game, PresenceReading.Counted(4, FieldSource.Who), at);

        using var service = Service(database, out _);
        await service.StartAsync(CancellationToken.None);

        var rolled = await UntilAsync(async () =>
            (await rollups.ForGameAsync(game, PresenceGrain.Hour, at.AddHours(-1), at.AddHours(1))).Count > 0);

        await service.StopAsync(CancellationToken.None);

        await Assert.That(rolled).IsTrue();
        await Assert.That(await rollups.WatermarkAsync(PresenceGrain.Hour)).IsNotNull();
    }

    [Test]
    public async Task AReplicaThatCannotTakeTheLeaseDoesNothing()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var other = database.SecondPool();
        var game = await SeedGameAsync(database);
        var rollups = new NpgsqlPresenceRollupStore(database.DataSource);
        var at = DateTimeOffset.UtcNow.AddHours(-2);

        await new PresenceWriter(new NpgsqlPresenceStore(database.DataSource))
            .WriteAsync(game, PresenceReading.Counted(4, FieldSource.Who), at);

        // Somebody else is already the maintenance replica.
        await using var held = await AdvisoryLease.TryAcquireAsync(other, AdvisoryLease.PresenceMaintenanceKey);
        await Assert.That(held).IsNotNull();

        using var service = Service(database, out _);
        await service.StartAsync(CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(1));
        await service.StopAsync(CancellationToken.None);

        await Assert.That(await rollups.WatermarkAsync(PresenceGrain.Hour)).IsNull();
        await Assert.That(await rollups.ForGameAsync(game, PresenceGrain.Hour, at.AddHours(-1), at.AddHours(1)))
            .IsEmpty();
    }

    [Test]
    public async Task TheMaintenanceLeaseIsNotTheCrawlLease()
    {
        // One replica runs both, so they cannot compete for one key — and a deployment that has turned
        // the crawler off still needs its partitions made next month.
        await using var database = await PostgresFixture.MigratedAsync();
        await using var other = database.SecondPool();

        await using var crawl = await AdvisoryLease.TryAcquireAsync(database.DataSource, AdvisoryLease.CrawlKey);
        await using var maintenance = await AdvisoryLease.TryAcquireAsync(
            other, AdvisoryLease.PresenceMaintenanceKey);

        await Assert.That(crawl).IsNotNull();
        await Assert.That(maintenance).IsNotNull();
    }

    private static PresenceMaintenanceService Service(TestDatabase database, out PresenceMaintenanceOptions options)
    {
        options = new PresenceMaintenanceOptions
        {
            Interval = TimeSpan.FromMilliseconds(200),
            LeaseRetryInterval = TimeSpan.FromMilliseconds(200),
        };

        return new PresenceMaintenanceService(
            database.DataSource,
            new PresenceMaintenance(
                new NpgsqlPresenceStore(database.DataSource),
                new NpgsqlPresenceRollupStore(database.DataSource),
                options.Retention),
            options,
            TimeProvider.System,
            NullLogger<PresenceMaintenanceService>.Instance);
    }

    private static async Task<Guid> SeedGameAsync(TestDatabase database)
    {
        var id = Guid.CreateVersion7();

        await new NpgsqlGameStore(database.DataSource).InsertAsync(new GameRecord(
            id,
            "corvid",
            "Corvid",
            Tagline: null,
            LifecycleState.Active,
            IsClaimed: false,
            FirstSeenAt: DateTimeOffset.UtcNow.AddYears(-1),
            LastReachableAt: DateTimeOffset.UtcNow,
            ArchivedAt: null));

        return id;
    }

    /// <summary>Polls until the condition holds or patience runs out, so the test never sleeps a cycle.</summary>
    private static async Task<bool> UntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return false;
    }
}
