using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Crawler.Persistence;
using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Tests.Support;

/// <summary>
/// The crawl graph the hosted service builds, with the socket replaced and nothing else.
/// </summary>
/// <remarks>
/// Shared rather than private to one suite because the thing under test in several of them is what
/// a whole cycle writes, and a second copy of this wiring would be a second definition of what
/// production looks like — free to drift, and wrong in a way no test would report.
/// </remarks>
public static class CrawlCycles
{
    public static CrawlCycle Build(
        NpgsqlDataSource source,
        IProbe probe,
        TimeProvider time,
        IHostResolver? resolver = null,
        DiscoveryOptions? options = null,
        TimeSpan? grace = null,
        IDnsTxtResolver? dns = null)
    {
        var discovery = options ?? new DiscoveryOptions
        {
            // No rate floor: these tests assert on what was written, and a real 250 ms gap between
            // dials would make the suite slow for nothing. The limiter has its own tests upstream.
            GlobalInterval = TimeSpan.Zero,
            PerHostInterval = TimeSpan.Zero,
        };

        var games = new NpgsqlGameStore(source);
        var endpoints = new NpgsqlEndpointStore(source);
        var fields = new NpgsqlGameFieldStore(source);
        var availability = new NpgsqlAvailabilityStore(source);
        var targets = new NpgsqlCrawlTargetRepository(source);
        var slugs = new NpgsqlSlugHistoryStore(source);

        // One resolver, shared with the identity matcher below, same as production: ResolvedEndpoint
        // reads the answers HostScopeGuard's resolver already gives rather than a second lookup path.
        var effectiveResolver = resolver ?? new FakeHostResolver();

        return new CrawlCycle(
            targets,
            probe,
            // §11's gate, against the real register in the real database — "an opt-out wrote no
            // availability row" is a claim about storage and only Postgres can answer it.
            new OptOutGate(new NpgsqlCrawlOptOutRepository(source), dns ?? new ScriptedDns(), time),
            new HostScopeGuard(effectiveResolver),
            new ProbeIngestor(
                new PresenceWriter(new NpgsqlPresenceStore(source)),
                new AvailabilityWriter(availability),
                new FieldReconciler(fields),
                games,
                new ArchiveSweeper(games, availability, availability),
                new SlugMinter(games, fields, slugs, grace)),
            new CatalogueBinder(
                games,
                endpoints,
                fields,
                slugs,
                new IdentityMatcher(
                    new CatalogueGameDirectory(games),
                    new CatalogueEndpointDirectory(endpoints),
                    fields,
                    new NpgsqlGameFieldIndex(source),
                    discovery,
                    effectiveResolver,
                    new NpgsqlMergeLog(source)),
                new NpgsqlDuplicateReviewRepository(source),
                new NpgsqlMergeLog(source),
                time),
            new ReferralGraphWriter(new NpgsqlReferralRepository(source), targets, discovery, time),
            new CrawlRateLimiter(discovery, time),
            new HostConcurrencyGate(),
            discovery,
            time,
            claims: null,
            payloads: null,
            // Issue #185 — a suite asserting what a cycle wrote has to be given the same register
            // the deployment has, or "a refusal left a record" is untestable here.
            refusals: new NpgsqlCrawlRefusalStore(source));
    }

    /// <summary>Confirmation on, with no wait, so a suite asserts the behaviour and not the delay.</summary>
    public static DiscoveryOptions Confirming() => new()
    {
        GlobalInterval = TimeSpan.Zero,
        PerHostInterval = TimeSpan.Zero,
        ConfirmationDelay = TimeSpan.Zero,
    };

    public static Task SeedAsync(NpgsqlDataSource source, params CrawlSeed[] seeds) =>
        CrawlSeeds.PlantAsync(new NpgsqlCrawlTargetRepository(source), seeds, TimeProvider.System);
}
