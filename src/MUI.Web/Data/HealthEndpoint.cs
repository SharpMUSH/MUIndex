using Microsoft.Extensions.Diagnostics.HealthChecks;

using Npgsql;

namespace MUI.Web.Data;

/// <summary>
/// <c>GET /health</c>: whether this replica can serve traffic right now.
/// </summary>
/// <remarks>
/// <para>
/// A readiness probe for the reverse proxy's routing decision, not a report on the crawler. The
/// crawler is a separate <c>BackgroundService</c> gated by its own Postgres advisory lock (spec
/// §4.11) — a replica that has not yet won the lease, or is running with no crawler at all, still
/// answers every read correctly. "Ready" means "can serve HTTP", never "has finished a warm-up
/// crawl", so this checks nothing about crawl state.
/// </para>
/// <para>
/// Cheap on purpose, because a proxy asks this on every routing decision: with a database configured
/// it is one connection round trip through the pool <see cref="PostgresData.AddPostgresCatalogue"/>
/// already opened, and against the demo fixture — no connection string, nothing to reach — it is
/// unconditionally healthy the instant the process starts.
/// </para>
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
            // Reuses the NpgsqlDataSource AddPostgresCatalogue registers rather than opening a second
            // pool to ask the same question. A registered check, not a bare ping, so a connection
            // failure comes back as a 503 the middleware writes rather than an exception the request
            // has to survive.
            builder.AddCheck<PostgresHealthCheck>("postgres");
        }

        // With no connection string there is nothing to check, and AddHealthChecks() with no checks
        // registered reports Healthy — exactly right for a deployment that was never going to reach
        // a database at all (§8's demo fixture).

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
/// A round trip and nothing else — no query against the catalogue. This is polled on every routing
/// decision a proxy makes, so a health check that touched application data would be a health check
/// that can go slow for the same reasons a page can, on the one endpoint that most needs not to.
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
