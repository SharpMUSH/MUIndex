using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace MUI.Crawler;

/// <summary>
/// The shape every advisory-lease-gated hosted service shares (spec §12): loop, hold or wait for the
/// lease, run one pass, delay, retry on failure, release the lease on the way out.
/// </summary>
/// <remarks>
/// <see cref="CrawlerService"/>, <see cref="DnsClaimSweeper"/>,
/// <see cref="PresenceMaintenanceService"/> and <see cref="I3Service"/> each wrote this control flow
/// out by hand; this is that shape, once. Never lets an exception escape the loop — a hosted service
/// that faults takes the whole host down with it — re-checks the lease every cycle rather than
/// trusting a connection it took once, and does every wait through the injected
/// <see cref="TimeProvider"/> so a test can drive it without sleeping.
/// <para>
/// A subclass keeps its own <c>Enabled</c> flag and options type — deliberately not unified; see
/// <see cref="CrawlerServiceCollectionExtensions"/> for why — and its own <c>ExecuteAsync</c> override
/// that checks it, logs its own disabled-mode line, calls its own <c>Validate()</c>, and then calls
/// <see cref="RunLeaseLoopAsync"/> to do the rest. It supplies the actual work of one pass through
/// <see cref="RunPassAsync"/>, and may hook the two things that are genuinely per-service rather than
/// shared: a schema-readiness wait before the lease loop can do anything
/// (<see cref="SchemaReadyAsync"/>, used by <see cref="DnsClaimSweeper"/> and
/// <see cref="PresenceMaintenanceService"/>, both of which can start beside a fresh, unmigrated
/// database) and a one-time action the first time a lease is (re)acquired
/// (<see cref="OnLeaseAcquiredAsync"/>, used only by <see cref="CrawlerService"/> to apply migrations
/// and plant seeds before its first cycle).
/// </para>
/// </remarks>
public abstract class LeasedBackgroundService(
    NpgsqlDataSource source,
    long lockKey,
    TimeSpan leaseRetryInterval,
    TimeProvider time,
    ILogger logger) : BackgroundService
{
    /// <summary>
    /// The database the lease and the pass it gates both use.
    /// </summary>
    /// <remarks>
    /// A property rather than reading the primary constructor's <c>source</c> parameter directly from
    /// a subclass: a parameter used in a subclass body as well as passed up to this base constructor
    /// is captured twice (CS9107) — once here, once there. Reading it back through this property
    /// keeps one copy.
    /// </remarks>
    protected NpgsqlDataSource Source { get; } = source;

    /// <summary>The clock every wait in the loop goes through, for the same reason as <see cref="Source"/>.</summary>
    protected TimeProvider Time { get; } = time;

    /// <summary>
    /// Whether the schema this pass needs exists yet. True by default: most passes need nothing a
    /// migration has not already created by the time they can take their lease.
    /// </summary>
    protected virtual Task<bool> SchemaReadyAsync(CancellationToken cancellationToken) =>
        Task.FromResult(true);

    /// <summary>How long to wait before asking <see cref="SchemaReadyAsync"/> again.</summary>
    protected virtual TimeSpan SchemaWaitInterval => leaseRetryInterval;

    /// <summary>Logged once, the first time in a row <see cref="SchemaReadyAsync"/> says no.</summary>
    protected virtual string SchemaWaitingMessage => "The schema is not applied yet; waiting for the migration run";

    /// <summary>
    /// Runs once, the first time this replica's lease is acquired — before the first pass under it —
    /// and again the next time a lost lease is retaken. A no-op unless a subclass overrides it.
    /// </summary>
    protected virtual Task OnLeaseAcquiredAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Runs one pass, and returns how long to wait before the next one.</summary>
    protected abstract Task<TimeSpan> RunPassAsync(CancellationToken cancellationToken);

    /// <summary>Logged when a held lease no longer answers <c>true</c> to <c>IsHeldAsync</c>.</summary>
    protected abstract string LeaseLostMessage { get; }

    /// <summary>Logged once while another replica holds the lease.</summary>
    protected abstract string LeaseWaitingMessage { get; }

    /// <summary>Logged when a pass throws, before retrying after <paramref name="leaseRetryInterval"/>.</summary>
    protected abstract string FailureMessage { get; }

    /// <summary>
    /// The lease loop. A subclass's own <c>ExecuteAsync</c> calls this after deciding whether to run
    /// at all.
    /// </summary>
    protected async Task RunLeaseLoopAsync(CancellationToken stoppingToken)
    {
        AdvisoryLease? lease = null;
        var announced = false;
        var waitingForSchema = false;
        var acquired = false;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (lease is not null && !await lease.IsHeldAsync(stoppingToken))
                    {
                        // The session that held the lock has gone. Believing otherwise is how two
                        // replicas end up doing the same work at once.
                        logger.LogWarning(LeaseLostMessage);
                        await lease.DisposeAsync();
                        lease = null;
                        acquired = false;
                    }

                    lease ??= await AdvisoryLease.TryAcquireAsync(Source, lockKey, stoppingToken);

                    if (lease is null)
                    {
                        if (!announced)
                        {
                            logger.LogInformation(LeaseWaitingMessage);
                            announced = true;
                        }

                        await Task.Delay(leaseRetryInterval, Time, stoppingToken);
                        continue;
                    }

                    announced = false;

                    // Migrations may be being applied right now by whichever replica holds the crawl
                    // lease — a schema-gated pass starts beside that, not after it.
                    if (!await SchemaReadyAsync(stoppingToken))
                    {
                        if (!waitingForSchema)
                        {
                            logger.LogInformation(SchemaWaitingMessage);
                            waitingForSchema = true;
                        }

                        await Task.Delay(SchemaWaitInterval, Time, stoppingToken);
                        continue;
                    }

                    waitingForSchema = false;

                    if (!acquired)
                    {
                        await OnLeaseAcquiredAsync(stoppingToken);
                        acquired = true;
                    }

                    var next = await RunPassAsync(stoppingToken);

                    await Task.Delay(next, Time, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    // Never fault out of here. A pass that threw is a pass to retry, and a hosted
                    // service that faults takes the web tier down with it.
                    logger.LogError(error, FailureMessage);

                    try
                    {
                        await Task.Delay(leaseRetryInterval, Time, stoppingToken);
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
