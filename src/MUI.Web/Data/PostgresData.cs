using MUI.Catalog;
using MUI.Catalog.Persistence;

using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

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

        // TryAdd throughout, and the availability store registered once and exposed through its
        // interfaces rather than newed per interface. Both matter because AddMuiCrawler registers
        // the same objects for the crawl loop and one deployable calls both (§4.11): with AddSingleton
        // this method won the IAvailabilityStore registration while the crawler's TryAdd was skipped,
        // leaving the concrete type and IReachableHistory pointing at a SECOND instance. Harmless on
        // one pool, and a direct contradiction of the comment in the crawler that says the store is
        // registered once because two would be two connection paths answering one question.
        services.TryAddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        // The registry is a lookup table with no state, so the shared instance rather than a second
        // one — which is also what the crawler registers.
        services.TryAddSingleton<IFieldRegistry>(FieldRegistry.Instance);

        services.TryAddSingleton(s => new NpgsqlAvailabilityStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IAvailabilityStore>(s => s.GetRequiredService<NpgsqlAvailabilityStore>());
        services.TryAddSingleton<IReachableHistory>(s => s.GetRequiredService<NpgsqlAvailabilityStore>());
        services.TryAddSingleton<IAvailabilityHistory, StoredAvailabilityHistory>();

        // §10's presence series, read off the rollup rather than the raw table: §5.2 lets retention
        // drop the raw partitions once they have been aggregated, so a series read from raw would
        // quietly shorten as a deployment aged. The crawler registers the same store — TryAdd, so
        // one deployable running both has one of it.
        services.TryAddSingleton(s => new NpgsqlPresenceRollupStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.TryAddSingleton<IPresenceSeries>(s => s.GetRequiredService<NpgsqlPresenceRollupStore>());

        // §5.7's former-slug table. Registered here rather than only with the crawler because a
        // read-only replica serves the redirects too — the promise is about URLs, not about which
        // process happens to be writing. TryAdd for the same reason as everything above it: the
        // crawler registers this one too, and two of them would be two pools answering one question.
        services.TryAddSingleton<ISlugHistoryStore>(s =>
            new NpgsqlSlugHistoryStore(s.GetRequiredService<NpgsqlDataSource>()));

        // §7.3's merge redirect. Beside the former-slug table and for the same reason: the promise is
        // about a URL, and a replica that serves pages has to keep it whether or not it merges anything.
        services.TryAddSingleton<IMergeRedirects>(s =>
            new NpgsqlMergeRedirects(s.GetRequiredService<NpgsqlDataSource>()));

        services.TryAddSingleton<IGameQueries>(s => new NpgsqlGameQueries(
            s.GetRequiredService<NpgsqlDataSource>(),
            s.GetRequiredService<IFieldRegistry>()));

        // Migration 0017's cycle log. Registered on the web side as well as the crawl side, and
        // TryAdd for the same reason as everything above: a replica that does no crawling still
        // renders the strip, because the answer comes out of the database rather than out of this
        // process. That is the whole point — an in-process flag would make the front page's answer
        // depend on which replica served the request.
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
