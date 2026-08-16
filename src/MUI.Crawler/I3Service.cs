using MUI.Catalog;
using MUI.Discovery;
using MUI.I3;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace MUI.Crawler;

/// <summary>
/// Opens a connection to the Intermud-3 sidecar for the length of one pass.
/// </summary>
/// <remarks>
/// <b>Per pass, not per process, and that is the simplification the whole service rests on.</b> The
/// long-lived thing is the sidecar's own TCP session to the router — it holds the password, the
/// mudlist and the reconnect machinery, and it is the piece that must survive. Ours is a socket to
/// localhost that costs nothing to make and nothing to lose, so there is no reconnect logic here, no
/// backoff, and no half-open connection to detect: a pass either connects or does not, and the next
/// one tries again.
/// </remarks>
public sealed class I3GatewayFactory(GatewayOptions options, ILoggerFactory? loggers = null)
{
    public async Task<GatewayClient> ConnectAsync(CancellationToken cancellationToken)
    {
        var client = new GatewayClient(options, loggers?.CreateLogger<GatewayClient>());
        try
        {
            await client.ConnectAsync(cancellationToken);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }
}

/// <summary>What a deployment owns about the Intermud-3 pass.</summary>
public sealed record I3ServiceOptions
{
    /// <summary>
    /// Whether this deployable runs the I3 pass at all. <b>Off by default</b>, unlike the crawler.
    /// </summary>
    /// <remarks>
    /// The pass needs a sidecar that is not part of the default deployment — it sits behind a compose
    /// profile, because joining I3 registers a name on somebody else's network permanently and must
    /// never happen as a side effect of <c>compose up</c>. A default of true would make every
    /// deployment without that container log a connection failure every few minutes for a feature
    /// nobody asked for.
    /// </remarks>
    public bool Enabled { get; init; }

    /// <summary>Which advisory lock the I3 pass competes for.</summary>
    public long AdvisoryLockKey { get; init; } = AdvisoryLease.I3Key;

    /// <summary>
    /// How often a pass runs.
    /// </summary>
    /// <remarks>
    /// Five minutes, which is about the mudlist and not about the counts. The router pushes mudlist
    /// deltas continuously and the gateway caches them, so a pass is reading a local cache and then
    /// asking a handful of muds; the per-mud floor in <see cref="I3Options.AskEvery"/> is what
    /// actually bounds what we send, and it is half an hour.
    /// </remarks>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a replica that could not take the lock waits before asking again.</summary>
    public TimeSpan LeaseRetryInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Where the sidecar is, and the key it expects.</summary>
    public GatewayOptions Gateway { get; init; } = new();

    /// <summary>How hard to lean on the network once connected.</summary>
    public I3Options Pass { get; init; } = new();

    public void Validate()
    {
        if (Interval <= TimeSpan.Zero || LeaseRetryInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The I3 pass needs a positive interval and lease retry interval.");
        }

        if (Enabled && string.IsNullOrWhiteSpace(Gateway.ApiKey))
        {
            // Refused at startup rather than discovered as an authentication failure every five
            // minutes. The gateway's own configuration ships example keys; a deployment that turned
            // this on without setting one has not finished, and saying so once is kinder than a log
            // that repeats forever.
            throw new InvalidOperationException(
                "The I3 pass is enabled but no gateway API key is configured.");
        }
    }
}

/// <summary>
/// The Intermud-3 pass, as an in-process <c>BackgroundService</c> gated on a Postgres advisory lock
/// (spec §12).
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="CrawlerService"/> and <see cref="PresenceMaintenanceService"/>, and
/// for the same reason: N web replicas must run exactly one of these. Two would ask every mud on the
/// network twice as often as we said we would, and would race to seed the same addresses.
/// </para>
/// <para>
/// <b>Its own key, not the crawl lease's.</b> A long crawl cycle must not delay an I3 pass and an I3
/// pass must not delay a crawl — they touch different tables and send to different places — and a
/// deployment that has turned the crawler off may still want its I3 bindings kept current.
/// </para>
/// <para>
/// It never lets an exception escape <c>ExecuteAsync</c>, because a hosted service that faults takes
/// the web tier with it and no player count is worth the site. Every wait goes through the injected
/// <see cref="TimeProvider"/>, so a test drives it without sleeping.
/// </para>
/// </remarks>
public sealed class I3Service(
    NpgsqlDataSource source,
    I3GatewayFactory gateways,
    ICrawlTargetRepository targets,
    II3BindingRepository bindings,
    IPresenceStore presence,
    IGameFieldStore fields,
    I3ServiceOptions options,
    TimeProvider time,
    ILogger<I3Service> logger,
    ILoggerFactory? loggers = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("The Intermud-3 pass is disabled in configuration");
            return;
        }

        options.Validate();

        AdvisoryLease? lease = null;
        var announced = false;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (lease is not null && !await lease.IsHeldAsync(stoppingToken))
                    {
                        logger.LogWarning("The Intermud-3 lease was lost; asking again");
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
                                "Another replica holds the Intermud-3 lease; this one will keep asking");
                            announced = true;
                        }

                        await Task.Delay(options.LeaseRetryInterval, time, stoppingToken);
                        continue;
                    }

                    announced = false;

                    await PassAsync(stoppingToken);

                    await Task.Delay(options.Interval, time, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    // Never fault out of here. The commonest failure by far is the sidecar not being
                    // up, which is a container to start rather than a site to take down.
                    logger.LogError(error, "The Intermud-3 pass failed; retrying after the lease interval");

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

    private async Task PassAsync(CancellationToken cancellationToken)
    {
        await using var gateway = await gateways.ConnectAsync(cancellationToken);

        var result = await new I3Cycle(
                gateway, targets, bindings, presence, fields, options.Pass, time,
                loggers?.CreateLogger<I3Cycle>())
            .RunAsync(cancellationToken);

        if (result.Listed > 0)
        {
            logger.LogInformation("Intermud-3 pass complete: {Result}", result);
        }
    }
}
