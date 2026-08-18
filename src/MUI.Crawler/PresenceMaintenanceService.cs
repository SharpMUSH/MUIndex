using MUI.Catalog;
using MUI.Catalog.Persistence;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace MUI.Crawler;

/// <summary>
/// Everything a deployment owns about presence maintenance (spec §5.2, §12, §15.4).
/// </summary>
/// <remarks>
/// The retention policy proper is <see cref="PresenceRetentionOptions"/>, which belongs to the
/// catalogue rather than to a scheduler; what is here is when the pass runs and which lock it
/// competes for.
/// </remarks>
public sealed record PresenceMaintenanceOptions
{
    /// <summary>
    /// Whether this deployable runs the maintenance pass at all.
    /// </summary>
    /// <remarks>
    /// Independent of <see cref="CrawlerOptions.Enabled"/>: a pure web replica still wants next
    /// month's partitions to exist, and the advisory lock already keeps N replicas to one pass.
    /// </remarks>
    public bool Enabled { get; init; } = true;

    /// <summary>Which advisory lock the maintenance pass competes for.</summary>
    public long AdvisoryLockKey { get; init; } = AdvisoryLease.PresenceMaintenanceKey;

    /// <summary>
    /// How often a pass runs. Hourly, because the finest grain it produces is an hour and rolling one
    /// up more often than it can change is work for nothing.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>How long a replica that could not take the lock waits before asking again.</summary>
    public TimeSpan LeaseRetryInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long to wait when the schema is not there yet.
    /// </summary>
    /// <remarks>
    /// Short and separate from <see cref="LeaseRetryInterval"/>: a replica whose database is mid
    /// migration is seconds from ready, not minutes, and waiting out a lease interval would leave the
    /// first hour after every fresh deployment unrolled for no reason.
    /// </remarks>
    public TimeSpan SchemaWait { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long presence is kept, at each grain. Keeps everything by default (§15.4).</summary>
    public PresenceRetentionOptions Retention { get; init; } = new();

    public void Validate()
    {
        if (Interval <= TimeSpan.Zero || LeaseRetryInterval <= TimeSpan.Zero || SchemaWait <= TimeSpan.Zero)
        {
            throw new ArgumentException("Maintenance intervals have to be positive.");
        }

        Retention.Validate();
    }
}

/// <summary>
/// The rollup, partition and retention pass, as an in-process <c>BackgroundService</c> gated on a
/// Postgres advisory lock (spec §5.2, §12).
/// </summary>
/// <remarks>
/// The same shape as <see cref="CrawlerService"/> and for the same reason: N web replicas must run
/// exactly one of these, or two would race to <c>DROP TABLE</c> the same partition. Its own key
/// rather than the crawl lease's, so a deployment with the crawler off still keeps its rollups
/// current. Never lets an exception escape <c>ExecuteAsync</c>, and every wait goes through the
/// injected <see cref="TimeProvider"/> so a test drives it without sleeping through an hour.
/// </remarks>
public sealed class PresenceMaintenanceService(
    NpgsqlDataSource source,
    PresenceMaintenance maintenance,
    PresenceMaintenanceOptions options,
    TimeProvider time,
    ILogger<PresenceMaintenanceService> logger,
    // §9's adoption curves ride this pass rather than a hosted service of their own: the scarce
    // thing is the advisory lease, and two locks for one pass is worse than one pass doing two
    // things. Optional, so a deployment without the ecosystem read side still keeps its rollups.
    IEcosystemSnapshots? snapshots = null,
    IGameQueries? ecosystem = null) : BackgroundService
{
    /// <summary>
    /// Records today's protocol adoption, and never lets that failure cost the rollups.
    /// </summary>
    /// <remarks>
    /// Caught separately rather than reaching the pass's own handler: the rollups are why this
    /// service exists, a curve is a nice graph, and a dashboard-read failure should cost a missing
    /// chart point, not a retried maintenance pass.
    /// </remarks>
    private async Task SnapshotAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (snapshots is null || ecosystem is null)
        {
            return;
        }

        try
        {
            var dashboard = await ecosystem.EcosystemAsync(cancellationToken);

            await snapshots.RecordAsync(now, dashboard.Protocols, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Today's ecosystem snapshot was not recorded");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Presence maintenance is disabled in configuration");
            return;
        }

        options.Validate();

        AdvisoryLease? lease = null;
        var announced = false;
        var waiting = false;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (lease is not null && !await lease.IsHeldAsync(stoppingToken))
                    {
                        logger.LogWarning("The presence maintenance lease was lost; asking again");
                        await lease.DisposeAsync();
                        lease = null;
                    }

                    lease ??= await AdvisoryLease.TryAcquireAsync(
                        source, options.AdvisoryLockKey, stoppingToken);

                    if (lease is null)
                    {
                        if (!announced)
                        {
                            logger.LogInformation(
                                "Another replica holds the presence maintenance lease; this one will keep asking");
                            announced = true;
                        }

                        await Task.Delay(options.LeaseRetryInterval, time, stoppingToken);
                        continue;
                    }

                    announced = false;

                    // Migrations may be being applied right now by whichever replica holds the crawl
                    // lease — this service starts beside that, not after it.
                    if (!await maintenance.SchemaReadyAsync(stoppingToken))
                    {
                        if (!waiting)
                        {
                            logger.LogInformation(
                                "The presence schema is not applied yet; waiting for the migration run");
                            waiting = true;
                        }

                        await Task.Delay(options.SchemaWait, time, stoppingToken);
                        continue;
                    }

                    waiting = false;

                    var now = time.GetUtcNow();

                    await maintenance.RunAsync(now, stoppingToken);
                    await SnapshotAsync(now, stoppingToken);

                    await Task.Delay(options.Interval, time, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    // A pass that threw is a pass to retry: retention runs last and is clamped to
                    // what the rollups have consumed, so nothing is lost that the next pass can't
                    // decide again.
                    logger.LogError(error, "The presence maintenance pass failed; retrying");

                    try
                    {
                        await Task.Delay(options.LeaseRetryInterval, time, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            if (lease is not null)
            {
                await lease.DisposeAsync();
            }
        }
    }
}
