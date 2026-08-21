using MUI.Catalog;
using MUI.Discovery;
using MUI.I3;

using Microsoft.Extensions.Logging;

using Npgsql;

namespace MUI.Crawler;

/// <summary>
/// Opens a connection to the Intermud-3 sidecar for the length of one pass.
/// </summary>
/// <remarks>
/// Per pass, not per process: the long-lived thing is the sidecar's own TCP session to the router,
/// which holds the password, mudlist and reconnect machinery. Ours is a cheap socket to localhost, so
/// there's no reconnect logic or backoff here — a pass either connects or doesn't, and the next tries
/// again.
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
    /// The pass needs a sidecar behind a compose profile, since joining I3 registers a name on
    /// somebody else's network permanently and must never happen as a side effect of <c>compose up</c>.
    /// </remarks>
    public bool Enabled { get; init; }

    /// <summary>Which advisory lock the I3 pass competes for.</summary>
    public long AdvisoryLockKey { get; init; } = AdvisoryLease.I3Key;

    /// <summary>
    /// How often a pass runs.
    /// </summary>
    /// <remarks>
    /// Five minutes, about the mudlist rather than the counts: the router pushes deltas continuously
    /// and the gateway caches them, so a pass reads a local cache. <see cref="I3Options.AskEvery"/>
    /// (half an hour) is what actually bounds what we send.
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
            // minutes.
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
/// Shares <see cref="LeasedBackgroundService"/> with <see cref="CrawlerService"/> and
/// <see cref="PresenceMaintenanceService"/>, and for the same reason: N web replicas must run exactly
/// one of these, or two would ask every mud twice as often as promised. Its own key, not the crawl
/// lease's, so neither pass can delay the other and a deployment with the crawler off can still keep
/// I3 bindings current.
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
    ILoggerFactory? loggers = null)
    : LeasedBackgroundService(source, options.AdvisoryLockKey, options.LeaseRetryInterval, time, logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("The Intermud-3 pass is disabled in configuration");
            return;
        }

        options.Validate();

        await RunLeaseLoopAsync(stoppingToken);
    }

    protected override async Task<TimeSpan> RunPassAsync(CancellationToken stoppingToken)
    {
        await using var gateway = await gateways.ConnectAsync(stoppingToken);

        var result = await new I3Cycle(
                gateway, targets, bindings, presence, fields, options.Pass, Time,
                loggers?.CreateLogger<I3Cycle>())
            .RunAsync(stoppingToken);

        if (result.Listed > 0)
        {
            logger.LogInformation("Intermud-3 pass complete: {Result}", result);
        }

        return options.Interval;
    }

    protected override string LeaseLostMessage => "The Intermud-3 lease was lost; asking again";

    protected override string LeaseWaitingMessage =>
        "Another replica holds the Intermud-3 lease; this one will keep asking";

    /// <remarks>
    /// Never fault out of here. The commonest failure by far is the sidecar not being up, which is a
    /// container to start rather than a site to take down.
    /// </remarks>
    protected override string FailureMessage => "The Intermud-3 pass failed; retrying after the lease interval";
}
