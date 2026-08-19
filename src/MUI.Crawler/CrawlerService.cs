using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Discovery;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace MUI.Crawler;

/// <summary>
/// The crawl loop, as an in-process <c>BackgroundService</c> gated on a Postgres advisory lock
/// (spec §4.11, §12).
/// </summary>
/// <remarks>
/// One ASP.NET Core deployable serves the site and runs the crawler; <see cref="AdvisoryLease"/> is
/// what keeps N replicas from becoming N crawlers. This loop must never let an exception escape
/// <see cref="ExecuteAsync"/> — a hosted service that faults takes the whole host down with it — must
/// re-check the lease every cycle rather than trust a connection it took once, and must do every wait
/// through the injected <see cref="TimeProvider"/> so a test can drive it without sleeping.
/// </remarks>
public sealed class CrawlerService(
    NpgsqlDataSource source,
    CrawlCycle cycle,
    ICrawlTargetRepository targets,
    CrawlerOptions options,
    TimeProvider time,
    ILogger<CrawlerService> logger,
    // Migration 0017. Optional so a deployment that predates the table keeps crawling rather than
    // failing on a missing relation — the strip is a window on the work, never a condition of it.
    ICrawlCycles? cycles = null,
    // Optional for the same reason, and because a crawl with no gap guard is a crawl that keeps its
    // old behaviour rather than one that fails to start.
    CrawlGapGuard? gaps = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("The crawler is disabled in configuration; this replica serves only the site");
            return;
        }

        options.Validate();

        AdvisoryLease? lease = null;
        var migrated = false;
        var announced = false;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (lease is not null && !await lease.IsHeldAsync(stoppingToken))
                    {
                        // The session that held the lock has gone. Believing otherwise is how two
                        // replicas end up crawling at once.
                        logger.LogWarning("The crawl lease was lost; standing down and asking again");
                        await lease.DisposeAsync();
                        lease = null;
                        migrated = false;
                    }

                    lease ??= await AdvisoryLease.TryAcquireAsync(
                        source, options.AdvisoryLockKey, stoppingToken);

                    if (lease is null)
                    {
                        if (!announced)
                        {
                            logger.LogInformation(
                                "Another replica holds the crawl lease; this one will keep asking");
                            announced = true;
                        }

                        await Task.Delay(options.Discovery.LeaseRetryInterval, time, stoppingToken);
                        continue;
                    }

                    announced = false;

                    if (!migrated)
                    {
                        await StartCrawlingAsync(stoppingToken);
                        migrated = true;
                    }

                    var startedAt = time.GetUtcNow();
                    var report = await cycle.RunAsync(stoppingToken);

                    if (report.Considered > 0)
                    {
                        logger.LogInformation("Crawl cycle complete: {Report}", report);
                    }

                    await RecordAsync(startedAt, report, stoppingToken);

                    await Task.Delay(options.Discovery.PollInterval, time, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    // Never fault out of here. A crawl cycle that threw is a cycle to retry, and a
                    // hosted service that faults takes the web tier down with it.
                    logger.LogError(error, "The crawl loop failed; retrying after the lease interval");

                    try
                    {
                        await Task.Delay(options.Discovery.LeaseRetryInterval, time, stoppingToken);
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

    /// <summary>
    /// Stores what the cycle just did, so the site can show its own instrument running.
    /// </summary>
    /// <remarks>
    /// Isolated and swallows everything: this is telemetry about a crawl already stored, and a
    /// failure to describe the work must never stop the work. Empty cycles are written too — "nothing
    /// was due" is how a reader tells a quiet registry from a stopped loop.
    /// </remarks>
    private async Task RecordAsync(
        DateTimeOffset startedAt,
        CycleReport report,
        CancellationToken cancellationToken)
    {
        if (cycles is null)
        {
            return;
        }

        try
        {
            await cycles.RecordAsync(
                new CrawlCycleRecord(
                    startedAt,
                    time.GetUtcNow(),
                    report.Considered,
                    report.Probed,
                    report.Answered,
                    report.Failed,
                    report.Refused,
                    report.OptedOut,
                    report.Errored,
                    report.Listed,
                    report.ReviewsOpened,
                    report.Counted,
                    report.Unmeasurable,
                    report.Transitions,
                    report.ReferralsAdded),
                cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(error, "The crawl cycle ran but could not be recorded");
        }
    }

    /// <summary>
    /// What happens once, on the replica that won the lock: the schema, then the seed list.
    /// </summary>
    /// <remarks>
    /// Under the lease deliberately. Migrations are idempotent and safe to race, but running them
    /// behind the lock means one replica applies them and the rest never try — which turns a race
    /// nobody would have noticed into a thing that cannot happen.
    /// </remarks>
    private async Task StartCrawlingAsync(CancellationToken cancellationToken)
    {
        if (options.ApplyMigrations)
        {
            var applied = await new MigrationRunner(source, logger).ApplyAsync(cancellationToken);

            if (applied.Count > 0)
            {
                logger.LogInformation("Applied {Count} migrations", applied.Count);
            }
        }

        var added = await CrawlSeeds.PlantAsync(targets, options.Seeds, time, cancellationToken);

        logger.LogInformation(
            "Holding the crawl lease. {Added} of {Total} configured seeds were new",
            added, options.Seeds.Count);

        // Before the first cycle, so it also covers a lease lost and retaken: whichever replica picks
        // the crawl back up is the one that should record how long nobody was holding it. Isolated
        // like RecordAsync: this edits history rather than adding to it, and a guard that can't run
        // is a reason to crawl anyway, not to fail startup.
        if (gaps is not null)
        {
            try
            {
                await gaps.CloseAnyGapAsync(cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(error, "Could not close the intervals left open by a crawl gap");
            }
        }
    }
}

/// <summary>
/// Puts a deployment's configured addresses into the registry.
/// </summary>
/// <remarks>
/// <b>Idempotent, and it never touches a schedule.</b> <see cref="ICrawlTargetRepository.AddAsync"/>
/// returns the existing id for an address it already holds and changes nothing else, so restarting the
/// process does not drag every seed forward into being due — which would make a crash loop into a
/// burst of traffic at somebody else's server.
/// </remarks>
public static class CrawlSeeds
{
    public static async Task<int> PlantAsync(
        ICrawlTargetRepository targets,
        IReadOnlyList<CrawlSeed> seeds,
        TimeProvider time,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(time);

        var now = time.GetUtcNow();
        var added = 0;

        foreach (var seed in seeds)
        {
            if (await targets.ByAddressAsync(seed.Host, seed.Port, cancellationToken) is not null)
            {
                continue;
            }

            await targets.AddAsync(
                new CrawlTarget
                {
                    Id = Guid.CreateVersion7(),
                    Host = seed.Host,
                    Port = seed.Port,
                    // Due now: a seed nobody has probed is the one thing a fresh registry has to do.
                    NextProbeAt = now,
                    FirstSeenAt = now,
                    IsOperatorSeed = seed.IsOperatorSeed,
                },
                cancellationToken);

            added++;
        }

        return added;
    }
}
