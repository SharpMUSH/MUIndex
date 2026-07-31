using Microsoft.Extensions.DependencyInjection;

using MUI.Import.Sources;

namespace MUI.Import;

/// <summary>Composition for the backfill (spec §7.6).</summary>
/// <remarks>
/// <b>Registering the importers does not schedule them.</b> Nothing here is a hosted service, and
/// there is no timer: an import is one command a human runs once against one deployment
/// (<c>tools/live-import</c>). What this method composes is the reader, so that the about page can
/// derive its attribution list from the same registry the run reads from.
/// </remarks>
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
    /// <paramref name="contactedMudVerseMaintainer"/> is a parameter rather than a constant because
    /// it is a statement of fact about the world: it says a human has written to whoever runs
    /// MudVerse. Until it is true the source is registered, credited on the about page, and refused
    /// at the moment of fetching — which is §7.6's etiquette expressed as a default rather than as a
    /// reminder. MudStats sat behind exactly this until its maintainer was approached; the mechanism
    /// did not go away when it passed, it moved on to the next source.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMuiImporters(
        this IServiceCollection services,
        bool contactedMudVerseMaintainer = false)
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

        // Order is the order a run reads them in, and it is deliberate: the two published listings
        // first, because they cost one request each and carry the addresses somebody demonstrably
        // dialled; then the hand-maintained list, which is the widest and the weakest; then the
        // scrapes, which are the expensive ones.
        services.AddSingleton<IDirectorySource>(provider => TinTinMsspCrawlerSource.Create(
            provider.GetRequiredService<HttpClient>(),
            provider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<IDirectorySource>(provider => TinTinMsdpCrawlerSource.Create(
            provider.GetRequiredService<HttpClient>(),
            provider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<IDirectorySource>(provider => MudConnectorSource.Create(
            provider.GetRequiredService<HttpClient>(),
            provider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<IDirectorySource>(provider => MudStatsSource.Create(
            provider.GetRequiredService<HttpClient>(),
            provider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<IDirectorySource>(provider => MudVerseSource.Create(
            provider.GetRequiredService<HttpClient>(),
            provider.GetRequiredService<TimeProvider>(),
            contactedMudVerseMaintainer));

        services.AddSingleton<SourceRegistry>();
        services.AddSingleton<ImportPipeline>();
        services.AddSingleton<ImportRunner>();

        return services;
    }
}
