using MUI.Catalog;
using MUI.Catalog.Persistence;
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
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));

        services.AddSingleton<IFieldRegistry, FieldRegistry>();
        services.AddSingleton<IAvailabilityStore>(s =>
            new NpgsqlAvailabilityStore(s.GetRequiredService<NpgsqlDataSource>()));
        services.AddSingleton<IAvailabilityHistory, StoredAvailabilityHistory>();
        services.AddSingleton<IGameQueries>(s => new NpgsqlGameQueries(
            s.GetRequiredService<NpgsqlDataSource>(),
            s.GetRequiredService<IFieldRegistry>()));
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
