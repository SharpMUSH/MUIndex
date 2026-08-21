using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Discovery;

using Microsoft.Extensions.Logging;

using Npgsql;

namespace MUI.Crawler;

/// <summary>What a deployment owns about spec §8.3's DNS claim sweep.</summary>
public sealed record DnsClaimSweepOptions
{
    /// <summary>
    /// Whether this deployable runs the sweep at all.
    /// </summary>
    /// <remarks>
    /// Independent of <see cref="CrawlerOptions.Enabled"/>: the sweep dials no games, so a deployment
    /// running with the crawler off still has every reason to let an operator finish a claim.
    /// </remarks>
    public bool Enabled { get; init; } = true;

    public long AdvisoryLockKey { get; init; } = AdvisoryLease.DnsClaimKey;

    /// <summary>
    /// How often a pass runs.
    /// </summary>
    /// <remarks>
    /// Minutes rather than hours, because what this bounds is how long somebody sits looking at a
    /// page that says "not yet" after doing everything correctly — and the pass costs one lookup per
    /// host that has a live claim, which is a handful across the whole catalogue.
    /// </remarks>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a replica that could not take the lock waits before asking again.</summary>
    public TimeSpan LeaseRetryInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long to wait when the schema is not there yet.</summary>
    public TimeSpan SchemaWait { get; init; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        if (Interval <= TimeSpan.Zero || LeaseRetryInterval <= TimeSpan.Zero || SchemaWait <= TimeSpan.Zero)
        {
            throw new ArgumentException("Sweep intervals have to be positive.");
        }
    }
}

/// <summary>
/// Reads the zone files of games somebody is claiming, so §8.1's promise holds for §8.3's channel.
/// </summary>
/// <remarks>
/// <para>
/// The claim page's <em>check now</em> button is the fast path and not the only one: an operator who
/// added the TXT record and closed the tab has done everything asked of them, and a channel that
/// verified only on a button press would be a channel with a footnote. This is the "publish it and
/// we notice" half.
/// </para>
/// <para>
/// <b>Its own hosted service, not a step in the crawl loop.</b> A crawl cycle is a thing that dials
/// games under <c>CRAWL DELAY</c> and §7.2's address gate; this opens no socket to any game and
/// should not queue behind one — nor should it stop happening on a deployment running with the
/// crawler switched off. It costs one lookup per host that has a live claim, so its load is set by
/// how many people are mid-claim rather than by how large the catalogue is. Its lease loop is
/// <see cref="LeasedBackgroundService"/>, shared with <see cref="CrawlerService"/>,
/// <see cref="PresenceMaintenanceService"/> and <see cref="I3Service"/>.
/// </para>
/// </remarks>
public sealed class DnsClaimSweeper(
    NpgsqlDataSource source,
    DnsClaimVerifier verifier,
    IClaimStore claims,
    DnsClaimSweepOptions options,
    TimeProvider time,
    ILogger<DnsClaimSweeper> logger)
    : LeasedBackgroundService(source, options.AdvisoryLockKey, options.LeaseRetryInterval, time, logger)
{
    /// <summary>
    /// One pass: every game with a claim a lookup could change, each checked once.
    /// </summary>
    /// <remarks>
    /// Distinct games, not distinct claims — two accounts mid-claim on one game is one zone to read,
    /// and <see cref="DnsClaimVerifier"/> offers every token it finds there. One game's failure is
    /// caught here rather than at the pass boundary, so a hostname whose resolver is broken cannot
    /// stop everybody else's claim from settling.
    /// </remarks>
    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        var live = await claims.PendingOrDnsVerifiedAsync(Time.GetUtcNow(), cancellationToken);
        var settled = 0;

        foreach (var gameId in live.Select(claim => claim.GameId).Distinct())
        {
            try
            {
                if (await verifier.CheckAsync(gameId, cancellationToken) is ClaimVerdict.Verified)
                {
                    settled++;
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "The DNS claim check for {Game} failed; the rest of the sweep goes on", gameId);
            }
        }

        return settled;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("The DNS claim sweep is disabled in configuration");
            return;
        }

        options.Validate();

        await RunLeaseLoopAsync(stoppingToken);
    }

    protected override async Task<TimeSpan> RunPassAsync(CancellationToken stoppingToken)
    {
        var settled = await SweepAsync(stoppingToken);

        if (settled > 0)
        {
            logger.LogInformation("{Count} claim(s) were proved by a TXT record this pass", settled);
        }

        return options.Interval;
    }

    protected override string LeaseLostMessage => "The DNS claim sweep lease was lost; asking again";

    protected override string LeaseWaitingMessage =>
        "Another replica holds the DNS claim sweep lease; this one will keep asking";

    /// <remarks>
    /// A pass that threw is a pass to retry. Nothing here is written that a later pass could not
    /// decide again: presence establishes and absence never revokes, so a missed sweep costs a delay
    /// and never a claim.
    /// </remarks>
    protected override string FailureMessage => "The DNS claim sweep failed; retrying";

    protected override TimeSpan SchemaWaitInterval => options.SchemaWait;

    protected override string SchemaWaitingMessage =>
        "The claim schema is not applied yet; waiting for the migration run";

    // Migrations may be being applied right now by whichever replica holds the crawl lease — this
    // service starts beside that, not after it.
    /// <summary>Whether <c>game_claim</c> exists yet, asked without throwing if it does not.</summary>
    protected override async Task<bool> SchemaReadyAsync(CancellationToken cancellationToken)
    {
        await using var connection = await Source.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT to_regclass('public.game_claim') IS NOT NULL";

        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}
