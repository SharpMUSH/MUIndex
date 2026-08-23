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
    /// Whether this deployable runs the AresCentral pass. <b>Off unless a host turns it on.</b>
    /// </summary>
    /// <remarks>
    /// Off by default for a different reason than I3's. I3 is off because joining the network
    /// registers a name on somebody else's router permanently; this is off because <b>a pass with no
    /// credentials cannot do anything except fail</b>, and <see cref="Validate"/> throws when it is
    /// on without them — so a default of <c>true</c> would mean any host that built
    /// <c>CrawlerOptions</c> by hand crashed at startup on a feature it never asked for.
    /// <para>
    /// Safe by construction rather than by a correction applied elsewhere. <c>CrawlerSettings.Apply</c>
    /// turns this on the moment it finds a credential pair, which is where "on as soon as you have
    /// credentials" actually lives — a default that had to be undone by a later call is the same
    /// shape of bug as a gate defaulting to a claim about somebody's consent.
    /// </para>
    /// </remarks>
    public bool Enabled { get; init; }

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

        // Checked whether or not the pass is on. Switching it off does not make a broken credential
        // pair correct, it postpones finding out — and the operator who later switches it back on is
        // the one who pays, by which time the mistake is old enough that nobody connects the two.
        var id = !string.IsNullOrWhiteSpace(Hub.ClientId);
        var key = !string.IsNullOrWhiteSpace(Hub.ApiKey);

        if (id != key)
        {
            throw new InvalidOperationException(
                "AresCentral issues a client id and an API key as a pair; configure both or neither.");
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
    IHttpClientFactory clients,
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
        // Built per pass and never held. This service is a singleton; a client kept across passes
        // keeps the handler it was created with, so the pooled rotation IHttpClientFactory exists to
        // provide never happens and that handler's DNS answer is pinned for the life of the process.
        // One request an hour for weeks is exactly long enough to outlive an address change, and the
        // symptom would be every pass failing until somebody restarted the site.
        var hub = new AresGamesClient(
            clients.CreateClient(AresGamesClient.HttpClientName),
            options.Hub,
            loggers?.CreateLogger<AresGamesClient>());

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
