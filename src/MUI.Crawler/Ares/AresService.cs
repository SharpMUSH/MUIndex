using Microsoft.Extensions.Logging;

using MUI.Ares;
using MUI.Catalog;
using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler;

/// <summary>What a deployment owns about the AresCentral pass.</summary>
public sealed record AresServiceOptions
{
    /// <summary>
    /// Whether this deployable runs the AresCentral pass. <b>On by default</b>, unlike I3's.
    /// </summary>
    /// <remarks>
    /// I3 is off by default because joining the network registers a name on somebody else's router
    /// permanently, and that must never happen as a side effect of <c>compose up</c>. This is a GET
    /// against a documented API with credentials a deployment either holds or does not; it registers
    /// nothing. A deployment with no credentials never runs it, because the host turns it off when it
    /// finds none rather than because the default said so.
    /// </remarks>
    public bool Enabled { get; init; } = true;

    /// <summary>Which advisory lock the pass competes for (spec §12).</summary>
    public long AdvisoryLockKey { get; init; } = AdvisoryLease.AresKey;

    /// <summary>
    /// How often a pass runs.
    /// </summary>
    /// <remarks>
    /// Hourly. The list moves on the order of days — a game appears when somebody launches one — so
    /// this is already far more often than the data changes, and one request is the whole cost.
    /// </remarks>
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>How long a replica that could not take the lease waits before asking again.</summary>
    public TimeSpan LeaseRetryInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Where the hub is, and what it expects us to present.</summary>
    public AresOptions Hub { get; init; } = new();

    public void Validate()
    {
        if (Interval <= TimeSpan.Zero || LeaseRetryInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The AresCentral pass needs a positive interval and lease retry interval.");
        }

        if (Enabled)
        {
            // Refused at startup rather than discovered as a 401 once an hour for ever.
            Hub.Validate();
        }
    }
}

/// <summary>
/// The AresCentral pass, as an in-process <c>BackgroundService</c> gated on a Postgres advisory lock
/// (spec §12).
/// </summary>
/// <remarks>
/// The same shape as <see cref="I3Service"/> and for the same reason: N web replicas must run exactly
/// one of these, or a site that promised to be polite asks the hub N times an hour.
/// </remarks>
public sealed class AresService(
    NpgsqlDataSource source,
    IAresGames hub,
    ICrawlTargetRepository targets,
    IAresListingRepository listings,
    IGameFieldStore fields,
    AresServiceOptions options,
    TimeProvider time,
    ILogger<AresService> logger,
    ILoggerFactory? loggers = null)
    : LeasedBackgroundService(source, options.AdvisoryLockKey, options.LeaseRetryInterval, time, logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("The AresCentral pass is disabled in configuration");
            return;
        }

        options.Validate();

        await RunLeaseLoopAsync(stoppingToken);
    }

    protected override async Task<TimeSpan> RunPassAsync(CancellationToken stoppingToken)
    {
        var result = await new AresCycle(
                hub, targets, listings, fields, Time, loggers?.CreateLogger<AresCycle>())
            .RunAsync(stoppingToken);

        logger.LogInformation("AresCentral pass complete: {Result}", result);

        return options.Interval;
    }

    protected override string LeaseLostMessage => "The AresCentral lease was lost; asking again";

    protected override string LeaseWaitingMessage =>
        "Another replica holds the AresCentral lease; this one will keep asking";

    /// <remarks>
    /// The commonest failures are a credential problem and the hub being down, and neither is a
    /// reason to take the web tier with it.
    /// </remarks>
    protected override string FailureMessage =>
        "The AresCentral pass failed; retrying after the lease interval";
}
