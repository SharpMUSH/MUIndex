using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Crawler.Persistence;
using MUI.Discovery;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Npgsql;

namespace MUI.Crawler;

/// <summary>
/// Wires the crawler into a host — the whole composition, in one call.
/// </summary>
/// <remarks>
/// <para>
/// The crawler is an in-process <c>BackgroundService</c> in the web deployable (spec §4.11), so
/// <c>Program.cs</c> should be one line and this should be where the graph is assembled. Everything
/// registered here is either a type from the three projects below or an adapter that joins two of
/// them; there is no configuration binding, because a library that reads a host's configuration
/// schema has decided what the host's configuration looks like.
/// </para>
/// <para>
/// <b>The read side is registered too.</b> <see cref="IGameQueries"/> against Postgres is what turns
/// the site from a fixture into measured data, and it is registered with <c>TryAdd</c> so a host that
/// has already chosen an implementation — the fixture, in development — keeps it.
/// </para>
/// </remarks>
public static class CrawlerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ingestion pipeline, the crawl loop and the hosted service.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="connectionString">A PostgreSQL connection string.</param>
    /// <param name="configure">Anything a deployment owns: seeds, bounds, whether the loop runs.</param>
    public static IServiceCollection AddMuiCrawler(
        this IServiceCollection services,
        string connectionString,
        Action<CrawlerOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new CrawlerOptionsBuilder();
        configure?.Invoke(builder);

        var options = builder.Build();
        options.Validate();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        return services.AddMuiCrawlerCore(options);
    }

    /// <summary>
    /// The same graph, against a data source the host already owns.
    /// </summary>
    /// <remarks>
    /// Separate because a host that has its own <see cref="NpgsqlDataSource"/> — configured with a
    /// type mapper, a logger, or a connection multiplexer — must not have a second one created behind
    /// its back. Two pools against one database is a connection budget nobody planned.
    /// </remarks>
    public static IServiceCollection AddMuiCrawlerCore(this IServiceCollection services, CrawlerOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton(options);
        services.AddSingleton(options.Discovery);
        services.AddSingleton(options.Probe);

        // The catalogue's own stores. NpgsqlAvailabilityStore is both the store and the reachable
        // history the archive sweep reads, so it is registered once and exposed twice — two instances
        // would be two connection paths answering one question.
        services.TryAddSingleton<IGameStore>(s => new NpgsqlGameStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IEndpointStore>(s => new NpgsqlEndpointStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IGameFieldStore>(s => new NpgsqlGameFieldStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IPresenceStore>(s => new NpgsqlPresenceStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton(s => new NpgsqlAvailabilityStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IAvailabilityStore>(s => s.GetRequiredService<NpgsqlAvailabilityStore>());
        services.TryAddSingleton<IReachableHistory>(s => s.GetRequiredService<NpgsqlAvailabilityStore>());

        // §8's claim settling. Every probe of a game whose owner has published a token completes the
        // claim, and every probe of a claimed game refreshes beacon_last_seen_at — both on the
        // ordinary schedule, which is why this belongs to the crawl graph rather than to the web
        // tier that mints the tokens.
        //
        // It was missing, and CrawlCycle takes its ClaimService as an optional parameter, so a
        // crawler-only deployment settled nothing at all and said nothing about it. mui-crawl passes
        // one by hand, which is why its tests never noticed.
        services.TryAddSingleton<IClaimStore>(s => new NpgsqlClaimStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<ClaimService>();

        // The three writers of §6.5, plus the field registry they judge staleness against.
        services.TryAddSingleton<IFieldRegistry>(FieldRegistry.Instance);
        services.TryAddSingleton<IPresenceWriter, PresenceWriter>();
        services.TryAddSingleton<IAvailabilityWriter, AvailabilityWriter>();
        services.TryAddSingleton<IFieldReconciler, FieldReconciler>();
        services.TryAddSingleton<ArchiveSweeper>();

        // The read side. TryAdd, so a host that already chose the fixture keeps it.
        services.TryAddSingleton<IGameQueries>(s => new NpgsqlGameQueries(
            s.GetRequiredService<NpgsqlDataSource>(), s.GetRequiredService<IFieldRegistry>()));

        // Discovery: the registry, the graph, the review queue, and the identity matcher's three
        // narrow reads.
        services.TryAddSingleton<ICrawlTargetRepository>(
            s => new NpgsqlCrawlTargetRepository(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IReferralRepository>(
            s => new NpgsqlReferralRepository(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IDuplicateReviewRepository>(
            s => new NpgsqlDuplicateReviewRepository(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IGameDirectory, CatalogueGameDirectory>();
        services.TryAddSingleton<IEndpointDirectory, CatalogueEndpointDirectory>();
        services.TryAddSingleton<IGameFieldIndex>(
            s => new NpgsqlGameFieldIndex(s.GetRequiredService<NpgsqlDataSource>()));

        services.TryAddSingleton<IdentityMatcher>();
        services.TryAddSingleton<ReferralGraphWriter>();
        services.TryAddSingleton<HostGate>();
        services.TryAddSingleton<CrawlRateLimiter>();

        // The gate is on the resolved address, not the name (spec §7.2). SystemHostResolver is the
        // only place live DNS is reached from, and it is injected so no test performs a lookup.
        services.TryAddSingleton<IHostResolver, SystemHostResolver>();
        services.TryAddSingleton<HostScopeGuard>();

        services.TryAddSingleton<IProbe>(s => new TelnetProbe(s.GetRequiredService<ProbeOptions>()));

        services.TryAddSingleton<ProbeIngestor>();
        services.TryAddSingleton<CatalogueBinder>();
        services.TryAddSingleton<CrawlCycle>();

        if (options.Enabled)
        {
            services.AddHostedService<CrawlerService>();
        }

        return services;
    }
}

/// <summary>
/// A small builder so a host configures the crawler without constructing a record graph by hand.
/// </summary>
/// <remarks>
/// Deliberately not <c>IOptions&lt;T&gt;</c> and deliberately not bound to a configuration section:
/// <see cref="CrawlerOptions"/> validates itself and refuses a setting that would be wrong in a way
/// nobody notices until it is on the network, and that check has to happen before anything is
/// registered rather than the first time something resolves the options.
/// </remarks>
public sealed class CrawlerOptionsBuilder
{
    private readonly List<CrawlSeed> _seeds = [];

    /// <summary>Off makes the deployable a pure web tier.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether the crawler applies pending migrations under the lease before crawling.</summary>
    public bool ApplyMigrations { get; set; } = true;

    /// <summary>Which advisory lock this deployment competes for (spec §12).</summary>
    public long AdvisoryLockKey { get; set; } = CrawlLease.DefaultKey;

    /// <summary>Per-cycle bounds.</summary>
    public DiscoveryOptions Discovery { get; set; } = new();

    /// <summary>Per-probe bounds.</summary>
    public ProbeOptions Probe { get; set; } = new();

    /// <summary>
    /// Adds an address the crawler knows on day one.
    /// </summary>
    /// <param name="host">The host name or literal address.</param>
    /// <param name="port">The port.</param>
    /// <param name="isOperatorSeed">
    /// Whether to exempt this address from the resolved-address gate. <b>False unless a human means
    /// it</b> (§7.2) — pointing the crawler at your own <c>127.0.0.1</c> is a thing to say out loud,
    /// once, per address.
    /// </param>
    public CrawlerOptionsBuilder Seed(string host, int port, bool isOperatorSeed = false)
    {
        _seeds.Add(new CrawlSeed(host, port, isOperatorSeed));
        return this;
    }

    public CrawlerOptions Build() => new()
    {
        Enabled = Enabled,
        ApplyMigrations = ApplyMigrations,
        AdvisoryLockKey = AdvisoryLockKey,
        Discovery = Discovery,
        Probe = Probe,
        Seeds = _seeds,
    };
}
