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
/// <para>
/// One ASP.NET Core deployable serves the site and runs the crawler, which is the whole shape of §4.11
/// — and the thing that makes it safe is that N replicas still run exactly one crawler.
/// <see cref="CrawlLease"/> is that gate: every replica asks on every retry, one holds it for as long
/// as its session lives, and the others do nothing but ask again.
/// </para>
/// <para>
/// <b>Three properties this loop must have, all of which are about not taking the site down with
/// it:</b> it never lets an exception escape <see cref="ExecuteAsync"/>, because a hosted service that
/// faults takes the host with it and the crawl is not worth the site; it re-checks the lease every
/// cycle rather than trusting a connection it took once; and it does every wait through the injected
/// <see cref="TimeProvider"/>, so a test drives it without sleeping.
/// </para>
/// </remarks>
public sealed class CrawlerService(
    NpgsqlDataSource source,
    CrawlCycle cycle,
    ICrawlTargetRepository targets,
    CrawlerOptions options,
    TimeProvider time,
    ILogger<CrawlerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("The crawler is disabled in configuration; this replica serves only the site");
            return;
        }

        options.Validate();

        CrawlLease? lease = null;
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

                    lease ??= await CrawlLease.TryAcquireAsync(
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

                    var report = await cycle.RunAsync(stoppingToken);

                    if (report.Considered > 0)
                    {
                        logger.LogInformation("Crawl cycle complete: {Report}", report);
                    }

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
