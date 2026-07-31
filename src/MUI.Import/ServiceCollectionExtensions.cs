using Microsoft.Extensions.DependencyInjection;

using MUI.Import.Sources;

namespace MUI.Import;

/// <summary>Composition for the backfill (spec §7.6).</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the importers, the pipeline and the provenance store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything an import <em>writes</em> through — the crawl registry, the endpoint, field and
    /// presence stores, the availability writer — is resolved from the container and registered
    /// elsewhere, so this method adds a reader and never a second opinion about storage.
    /// </para>
    /// <para>
    /// <paramref name="contactedMudStatsMaintainer"/> is a parameter rather than a constant because
    /// it is a statement of fact about the world: it says a human has written to whoever runs
    /// MudStats. Until it is true the source is registered, credited on the about page, and refused
    /// at the moment of fetching — which is §7.6's etiquette expressed as a default rather than as a
    /// reminder.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMuiImporters(
        this IServiceCollection services,
        bool contactedMudStatsMaintainer = false)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IImportProvenanceStore, NpgsqlImportProvenanceStore>();
        services.AddSingleton<IImportedAvailabilityWriter, ImportedAvailabilityWriter>();

        // One HttpClient for every importer. The per-source rate limit lives in PolitenessGate and is
        // per source, so sharing the connection pool costs nothing and sharing a socket exhaustion
        // bug would cost a lot.
        services.AddSingleton(_ => new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        });

        services.AddSingleton<IDirectorySource>(provider => TinTinMsspCrawlerSource.Create(
            provider.GetRequiredService<HttpClient>(),
            provider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<IDirectorySource>(provider => MudStatsSource.Create(
            provider.GetRequiredService<HttpClient>(),
            provider.GetRequiredService<TimeProvider>(),
            contactedMudStatsMaintainer));

        services.AddSingleton<SourceRegistry>();
        services.AddSingleton<ImportPipeline>();
        services.AddSingleton<ImportRunner>();

        return services;
    }
}
