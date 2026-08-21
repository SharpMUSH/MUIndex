using Microsoft.Extensions.Diagnostics.HealthChecks;

using Npgsql;

namespace MUI.Web.Data;

/// <summary>
/// <c>GET /health</c>: whether this replica can serve traffic right now.
/// </summary>
/// <remarks>
/// A readiness probe for the reverse proxy, not a report on the crawler — a replica with no crawl
/// lease still answers every read correctly, so "ready" means "can serve HTTP", never "finished a
/// warm-up crawl". Cheap on purpose: one connection round trip through the pool
/// <see cref="PostgresData.AddPostgresCatalogue"/> already opened, or unconditionally healthy against
/// the demo fixture.
/// </remarks>
public static class HealthEndpoint
{
    public const string Path = "/health";

    public static IServiceCollection AddMuiHealth(
        this IServiceCollection services,
        string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = services.AddHealthChecks();

        if (connectionString is not null)
        {
            // Reuses the NpgsqlDataSource AddPostgresCatalogue registers, rather than opening a second
            // pool to ask the same question.
            builder.AddCheck<PostgresHealthCheck>("postgres");
        }

        // With no connection string, AddHealthChecks() with no checks registered reports Healthy —
        // correct for a deployment on the demo fixture (§8), which was never going to reach a database.

        return services;
    }

    /// <summary>Maps the endpoint, unauthenticated: a proxy dials this with no session of its own.</summary>
    public static WebApplication MapMuiHealth(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks(Path).AllowAnonymous();

        return app;
    }
}

/// <summary>
/// Whether the database <see cref="PostgresData.AddPostgresCatalogue"/> pointed at answers a
/// connection, asked as cheaply as a connection allows.
/// </summary>
/// <remarks>
/// A round trip and nothing else — no query against the catalogue, since this is polled on every
/// routing decision and is the one endpoint that most needs not to go slow.
/// </remarks>
public sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception error)
            when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL was not reachable.", error);
        }
    }
}
