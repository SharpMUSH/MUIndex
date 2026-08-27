using MUI.Catalog;
using MUI.Catalog.Persistence;

using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

using ZiggyCreatures.Caching.Fusion;

namespace MUI.Web.Data;

/// <summary>
/// Serves <see cref="IAvailabilityHistory"/> from the same intervals the writers store.
/// </summary>
/// <remarks>
/// A one-line adapter, and it exists only because <see cref="IGameQueries"/> does not yet expose
/// availability spans. <see cref="IAvailabilityStore"/> already returns exactly the shape the
/// reachable strip and the archive need, so this forwards rather than queries — when the spans move
/// onto <c>GamePage</c>, both this and the interface it implements are deleted together.
/// </remarks>
public sealed class StoredAvailabilityHistory(IAvailabilityStore store) : IAvailabilityHistory
{
    public Task<IReadOnlyList<AvailabilityInterval>> ForGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default) =>
        store.ForGameAsync(gameId, cancellationToken);
}

/// <summary>Composes the site's reads over a real database, or says why it cannot.</summary>
public static class PostgresData
{
    /// <summary>
    /// Where the connection string is read from, in order. Environment first so a deployment does
    /// not have to ship a config file to point at its own database.
    /// </summary>
    public const string EnvironmentVariable = "MUI_POSTGRES";

    public const string ConfigurationKey = "ConnectionStrings:MUIndex";

    public static string? ResolveConnectionString(IConfiguration configuration) =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : configuration[ConfigurationKey] is { Length: > 0 } fromConfig
                ? fromConfig
                : null;

    /// <summary>
    /// Registers the reads against PostgreSQL and applies any outstanding migrations.
    /// </summary>
    /// <remarks>
    /// Migrations run at startup deliberately. The runner is idempotent and keeps a ledger, so a
    /// second start applies nothing — and a site whose schema silently trails its code is a worse
    /// failure than one that refuses to start.
    /// </remarks>
    public static void AddPostgresCatalogue(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        // TryAdd throughout: AddMuiCrawler registers these same objects, and one deployable calls both
        // (§4.11). AddSingleton here once won the IAvailabilityStore registration while the crawler's
        // TryAdd was skipped, leaving the concrete type and IReachableHistory pointing at a second
        // instance — harmless on one pool, but two connection paths answering one question.
        services.TryAddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        services.TryAddSingleton<IFieldRegistry>(FieldRegistry.Instance);

        services.TryAddSingleton(s => new NpgsqlAvailabilityStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IAvailabilityStore>(s => s.GetRequiredService<NpgsqlAvailabilityStore>());
        services.TryAddSingleton<IReachableHistory>(s => s.GetRequiredService<NpgsqlAvailabilityStore>());
        services.TryAddSingleton<IAvailabilityHistory, StoredAvailabilityHistory>();

        // §10's presence series, read off the rollup rather than the raw table: §5.2 lets retention
        // drop raw partitions once aggregated, so a series read from raw would quietly shorten as a
        // deployment aged.
        services.TryAddSingleton(s => new NpgsqlPresenceRollupStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IPresenceSeries>(s => s.GetRequiredService<NpgsqlPresenceRollupStore>());
        services.TryAddSingleton<IPresenceTrends>(s => new PresenceTrends(s.GetRequiredService<IPresenceSeries>()));

        // §5.7's former-slug table, registered here too: a read-only replica serves the redirects,
        // since the promise is about URLs, not about which process happens to be writing.
        services.TryAddSingleton<ISlugHistoryStore>(s =>
            new NpgsqlSlugHistoryStore(s.GetRequiredService<NpgsqlDataSource>()));

        // §7.3's merge redirect, for the same reason as the former-slug table above.
        services.TryAddSingleton<IMergeRedirects>(s =>
            new NpgsqlMergeRedirects(s.GetRequiredService<NpgsqlDataSource>()));

        // The listing's assembled catalogue lives here. One registered cache, shared by the queries
        // that fill it and the IListingCache that clears it — a private instance on either side
        // would be a cache the other one could not reach.
        services.AddFusionCache();

        services.TryAddSingleton<IListingCache>(s =>
            new ListingCache(s.GetRequiredService<IFusionCache>()));

        services.TryAddSingleton<IGameQueries>(s => new NpgsqlGameQueries(
            s.GetRequiredService<NpgsqlDataSource>(),
            s.GetRequiredService<IFieldRegistry>(),
            s.GetRequiredService<TimeProvider>(),
            s.GetRequiredService<IFusionCache>()));

        // Migration 0017's cycle log, registered on the web side too: a replica that does no crawling
        // still renders the strip, since the answer comes from the database rather than this process.
        services.TryAddSingleton<ICrawlCycles>(s => new NpgsqlCrawlCycles(
            s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<ICrawlerPulse, StoredCrawlerPulse>();
    }

    public static async Task ApplyMigrationsAsync(IServiceProvider services, ILogger logger)
    {
        var applied = await new MigrationRunner(
            services.GetRequiredService<NpgsqlDataSource>(), logger).ApplyAsync();

        if (applied.Count > 0)
        {
            logger.LogInformation("Applied {Count} migration(s): {Names}", applied.Count, string.Join(", ", applied));
        }
    }
}

/// <summary>
/// Whether the catalogue on screen was measured or invented.
/// </summary>
/// <remarks>
/// Rendered, not just logged. A site whose entire claim is that its data is measured cannot show
/// invented data without saying so — a reader who cannot tell the difference is being misled by
/// exactly the mechanism the project exists to replace.
/// </remarks>
public sealed record CatalogueSource(bool IsMeasured);
