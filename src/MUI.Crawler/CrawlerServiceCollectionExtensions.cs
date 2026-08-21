using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Crawler.Persistence;
using MUI.Discovery;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace MUI.Crawler;

/// <summary>
/// Wires the crawler into a host — the whole composition, in one call.
/// </summary>
/// <remarks>
/// The crawler is an in-process <c>BackgroundService</c> in the web deployable (spec §4.11), so
/// <c>Program.cs</c> should be one line and this is where the graph is assembled. No configuration
/// binding here — a library that reads a host's configuration schema has decided what the host's
/// configuration looks like. <see cref="IGameQueries"/> against Postgres is registered too, with
/// <c>TryAdd</c> so a host that already chose an implementation (the fixture, in development) keeps it.
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
    /// Separate because a host with its own <see cref="NpgsqlDataSource"/> must not have a second one
    /// created behind its back — two pools against one database is a connection budget nobody planned.
    /// </remarks>
    public static IServiceCollection AddMuiCrawlerCore(this IServiceCollection services, CrawlerOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton(options);
        services.AddSingleton(options.Discovery);
        services.AddSingleton(options.Probe);
        services.AddSingleton(options.Maintenance);
        services.AddSingleton(options.Maintenance.Retention);

        // §11's unique-player estimate and its salting machinery are gone (never revive without also
        // solving cross-name linkage). The rule that outlived it: a player name is never persisted.

        // The catalogue's own stores. NpgsqlAvailabilityStore is both the store and the reachable
        // history the archive sweep reads, registered once and exposed twice — two instances would be
        // two connection paths answering one question.
        services.TryAddSingleton<IGameStore>(s => new NpgsqlGameStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IEndpointStore>(s => new NpgsqlEndpointStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IGameFieldStore>(s => new NpgsqlGameFieldStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<ISlugHistoryStore>(s => new NpgsqlSlugHistoryStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton(s => new NpgsqlPresenceStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IPresenceStore>(s => s.GetRequiredService<NpgsqlPresenceStore>());
        services.TryAddSingleton(s => new NpgsqlPresenceRollupStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IProbePayloads>(s => new NpgsqlProbePayloads(
            s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<ICrawlCycles>(s => new NpgsqlCrawlCycles(
            s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton(s => new PresenceMaintenance(
            s.GetRequiredService<NpgsqlPresenceStore>(),
            s.GetRequiredService<NpgsqlPresenceRollupStore>(),
            s.GetRequiredService<PresenceRetentionOptions>(),
            s.GetService<ILogger<PresenceMaintenance>>(),

            // §11's shapes are swept by this pass, so the pass has to be handed them. Registered
            // without it, the optional argument stays null and the TTL silently never runs.
            s.GetRequiredService<IProbePayloads>(),

            // And migration 0017's cycle log, on the same terms.
            s.GetRequiredService<ICrawlCycles>()));
        services.TryAddSingleton(s => new NpgsqlAvailabilityStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IAvailabilityStore>(s => s.GetRequiredService<NpgsqlAvailabilityStore>());
        services.TryAddSingleton<IReachableHistory>(s => s.GetRequiredService<NpgsqlAvailabilityStore>());

        // §8's claim settling. A probe of a game whose owner has published a token completes the
        // claim; a probe of a claimed game refreshes beacon_last_seen_at — both on the ordinary
        // schedule, so this belongs to the crawl graph, not the web tier that mints the tokens.
        // CrawlCycle takes ClaimService as an OPTIONAL parameter, so omitting this registration means
        // beacons silently never settle rather than failing loudly — register it unconditionally.
        services.TryAddSingleton<IClaimStore>(s => new NpgsqlClaimStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IOnDemandProbes>(
            s => new NpgsqlOnDemandProbes(s.GetRequiredService<NpgsqlDataSource>()));
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

        // §7.3's operator surface for draining duplicate_review by hand — mui-crawl --merge builds
        // this graph itself per invocation; registered here too so the same MergeAsync path is
        // reachable from a host's own DI (the MCP game_merge tool), rather than only from the CLI.
        services.TryAddSingleton<IMergeLog>(s => new NpgsqlMergeLog(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IUnitOfWorkFactory>(
            s => new NpgsqlUnitOfWorkFactory(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<MergeApplier>();
        services.TryAddSingleton<ReviewMergeService>();

        // The public submission form (spec §7.6, §9), registered here rather than in the web project
        // since everything it needs — registry, endpoint directory, §7.2's gate — is already assembled.
        services.TryAddSingleton(options.Submissions);
        services.TryAddSingleton<ISubmissionSalt>(s => new NpgsqlSubmissionSalt(
            s.GetRequiredService<NpgsqlDataSource>(),
            s.GetRequiredService<SubmissionOptions>(),
            s.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<SubmissionSource>();
        services.TryAddSingleton<ISubmissionLog>(
            s => new NpgsqlSubmissionLog(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<SubmissionService>();

        services.TryAddSingleton<IdentityMatcher>();
        services.TryAddSingleton<ReferralGraphWriter>();
        services.TryAddSingleton<HostConcurrencyGate>();
        services.TryAddSingleton<CrawlRateLimiter>();

        // The gate is on the resolved address, not the name (spec §7.2). SystemHostResolver is the
        // only place live DNS is reached from, and it is injected so no test performs a lookup.
        services.TryAddSingleton<IHostResolver, SystemHostResolver>();
        services.TryAddSingleton<HostScopeGuard>();

        // The same instance behind the interface. Two registrations would be two guards, and a
        // caller reaching the one with no resolver behind it is not a failure anybody would notice.
        services.TryAddSingleton<IHostScopeGuard>(s => s.GetRequiredService<HostScopeGuard>());

        // §11's opt-out: the register, the TXT lookup that lets an operator withdraw one without
        // asking us, and the gate the crawl loop consults before every dial.
        services.TryAddSingleton<ICrawlOptOutRepository>(
            s => new NpgsqlCrawlOptOutRepository(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IDnsTxtResolver, DnsTxtResolver>();
        services.TryAddSingleton<OptOutGate>();

        // §8.3's DNS claim channel, which reuses the resolver above. Registered here rather than
        // with the web tier for the same reason claim settling is: the web tier mints tokens and
        // this reads what an operator published. The verifier is the piece the claim page's check
        // button also resolves, so both paths reach one set of rules.
        services.TryAddSingleton<DnsClaimVerifier>();

        services.TryAddSingleton<IProbe>(s => new TelnetProbe(s.GetRequiredService<ProbeOptions>()));

        // §5.7's re-mint. The default grace, because how long a name must hold before it earns a new
        // URL is a property of the promise rather than something a deployment tunes.
        services.TryAddSingleton(s => new SlugMinter(
            s.GetRequiredService<IGameStore>(),
            s.GetRequiredService<IGameFieldStore>(),
            s.GetRequiredService<ISlugHistoryStore>(),
            grace: null,
            s.GetService<ILogger<SlugMinter>>()));

        services.TryAddSingleton<ProbeIngestor>();
        services.TryAddSingleton<CatalogueBinder>();
        // §9's adoption curves. Registered here rather than with the web tier because the pass that
        // writes them runs beside the crawler, and the read side resolves the same one.
        services.TryAddSingleton<IEcosystemSnapshots>(s => new NpgsqlEcosystemSnapshots(
            s.GetRequiredService<NpgsqlDataSource>()));

        services.TryAddSingleton<CrawlCycle>();

        // §5.3's third reason a transition is written: elapsed time. See CrawlGapGuard for why the
        // crawler is the only thing that can notice a gap, and CrawlGap for what counts as one.
        services.TryAddSingleton(s => new CrawlGapGuard(
            s.GetRequiredService<IAvailabilityStore>(),
            s.GetService<ICrawlCycles>(),
            s.GetRequiredService<TimeProvider>(),
            logger: s.GetService<ILogger<CrawlGapGuard>>()));

        // Registered even when the crawl is off, so CrawlerService says so once in the log rather than
        // an operator having to infer "not crawling" from silence.
        services.AddHostedService<CrawlerService>();

        // Deliberately not gated on options.Enabled: a pure web replica still wants next month's
        // partitions to exist and this month's samples rolled up, and the maintenance pass takes its
        // own advisory lock, so however many replicas register it, one runs it.
        if (options.Maintenance.Enabled)
        {
            services.AddHostedService<PresenceMaintenanceService>();
        }

        // Not gated on options.Enabled either, and for a stronger reason than the pass above: this
        // opens no socket to any game, so a deployment running with the crawler off has every reason
        // to let an operator finish a claim. Its own advisory lease keeps N replicas to one sweep.
        services.TryAddSingleton(options.DnsClaims);

        if (options.DnsClaims.Enabled)
        {
            services.AddHostedService<DnsClaimSweeper>();
        }

        // Gated on Enabled, unlike the two above, because this needs a sidecar that isn't part of the
        // default deployment — registering it regardless would log a failed connection on a loop for
        // a feature nobody turned on.
        if (options.I3.Enabled)
        {
            services.AddSingleton(options.I3);

            // A factory delegate rather than a constructed instance: building a provider here to
            // resolve a logger would stand up a second container, and every singleton in it would be
            // a second copy of something the host thinks it owns one of.
            services.AddSingleton(s => new I3GatewayFactory(
                options.I3.Gateway, s.GetService<ILoggerFactory>()));
            services.TryAddSingleton<II3BindingRepository>(s => new NpgsqlI3BindingRepository(
                s.GetRequiredService<NpgsqlDataSource>()));
            services.AddHostedService<I3Service>();
        }

        return services;
    }
}

/// <summary>
/// A small builder so a host configures the crawler without constructing a record graph by hand.
/// </summary>
/// <remarks>
/// Deliberately not <c>IOptions&lt;T&gt;</c> or bound to a configuration section: <see cref="CrawlerOptions"/>
/// validates itself, and that check has to happen before anything is registered, not the first time
/// something resolves the options.
/// </remarks>
public sealed class CrawlerOptionsBuilder
{
    private readonly List<CrawlSeed> _seeds = [];

    /// <summary>Off makes the deployable a pure web tier.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether the crawler applies pending migrations under the lease before crawling.</summary>
    public bool ApplyMigrations { get; set; } = true;

    /// <summary>Which advisory lock this deployment competes for (spec §12).</summary>
    public long AdvisoryLockKey { get; set; } = AdvisoryLease.CrawlKey;

    /// <summary>Per-cycle bounds.</summary>
    public DiscoveryOptions Discovery { get; set; } = new();

    /// <summary>Per-probe bounds.</summary>
    public ProbeOptions Probe { get; set; } = new();

    /// <summary>What one source may put through the public submission form.</summary>
    public SubmissionOptions Submissions { get; set; } = new();

    /// <summary>
    /// Rollups, partitions ahead of need, and how long each grain of presence is kept (§5.2, §15.4).
    /// Keeps everything until a deployment says otherwise.
    /// </summary>
    public PresenceMaintenanceOptions Maintenance { get; set; } = new();

    /// <summary>
    /// The Intermud-3 pass. Off unless a deployment turns it on, because it needs a sidecar that is
    /// not part of the default arrangement.
    /// </summary>
    public I3ServiceOptions I3 { get; set; } = new();

    /// <summary>
    /// The §8.3 sweep that reads TXT records for games somebody is mid-claim on. On by default; it
    /// costs one lookup per host with a live claim and dials nothing.
    /// </summary>
    public DnsClaimSweepOptions DnsClaims { get; set; } = new();

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
        Maintenance = Maintenance,
        Submissions = Submissions,
        I3 = I3,
        DnsClaims = DnsClaims,
        Seeds = _seeds,
    };
}
