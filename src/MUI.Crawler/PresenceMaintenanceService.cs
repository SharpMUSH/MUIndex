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
    /// Independent of <see cref="CrawlerOptions.Enabled"/> on purpose. A pure web replica still wants
    /// the partitions to exist next month, and the advisory lock already guarantees that N replicas
    /// with this on run one pass between them.
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
    /// Short, and separate from <see cref="LeaseRetryInterval"/>, because the two are different
    /// situations. A replica that lost the lease has nothing to do for minutes; a replica whose
    /// database is being migrated by another replica right now is seconds from being able to work,
    /// and waiting out a lease interval would leave the first hour after every fresh deployment
    /// unrolled for no reason.
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
/// <para>
/// The same shape as <see cref="CrawlerService"/> and for the same reason: N web replicas must run
/// exactly one of these. Two of them would be two <c>DROP TABLE</c>s racing for one partition and two
/// watermarks leapfrogging each other. It takes its <b>own</b> key rather than the crawl lease's, so
/// that a deployment that has turned the crawler off still keeps its partitions made and its rollups
/// current, and so that a long crawl cycle never delays a rollup.
/// </para>
/// <para>
/// It never lets an exception escape <c>ExecuteAsync</c> — a hosted service that faults takes the web
/// tier with it, and no rollup is worth the site — and every wait goes through the injected
/// <see cref="TimeProvider"/>, so a test drives it without sleeping through an hour.
/// </para>
/// </remarks>
public sealed class PresenceMaintenanceService(
    NpgsqlDataSource source,
    PresenceMaintenance maintenance,
    PresenceMaintenanceOptions options,
    TimeProvider time,
    ILogger<PresenceMaintenanceService> logger) : BackgroundService
{
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

                    // The migrations may be being applied right now by whichever replica holds the
                    // crawl lease — this service starts beside that, not after it. Asking is one
                    // catalogue lookup; not asking would mean a 42P01 in the log on every fresh
                    // deployment and the first hour going unrolled while this backed off.
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

                    await maintenance.RunAsync(time.GetUtcNow(), stoppingToken);

                    await Task.Delay(options.Interval, time, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    // A pass that threw is a pass to retry. Nothing has been deleted that the next one
                    // cannot decide again, because retention runs last and is clamped to what the
                    // rollups have consumed.
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
