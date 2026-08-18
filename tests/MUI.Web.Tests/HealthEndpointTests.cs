using System.Net;

using MUI.Web.Data;
using MUI.Web.Tests.Support;

namespace MUI.Web.Tests;

/// <summary>
/// <c>GET /health</c> over real HTTP — the reverse proxy's routing decision, and the one endpoint on
/// this site that has to answer correctly before there is anything else worth asking about.
/// </summary>
/// <remarks>
/// The crawler is deliberately turned off in every database-backed case here
/// (<see cref="CrawlerSettings.EnabledConfigurationKey"/>): these assertions are about whether this
/// replica can serve traffic, not about whether it happens to hold the crawl lease, and a hosted
/// crawler with nothing to seed would only add an unrelated advisory-lock round trip to every test.
/// </remarks>
public class HealthEndpointTests
{
    /// <summary>
    /// No connection string, nothing to reach — the demo fixture is ready the instant it starts.
    /// </summary>
    [Test]
    public async Task TheDemoFixtureIsAlwaysReady()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync(HealthEndpoint.Path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    /// <summary>A database-backed replica is ready once Postgres answers a connection.</summary>
    [Test]
    public async Task TheDatabaseBackedSiteIsReadyWhenPostgresAnswers()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var site = await SiteHost.StartAsync(
            settings: new Dictionary<string, string?>
            {
                [CrawlerSettings.EnabledConfigurationKey] = "false",
            },
            connectionString: database.ConnectionString);

        var response = await site.Client.GetAsync(HealthEndpoint.Path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    /// <summary>
    /// A database-backed replica that cannot reach Postgres reports 503, not 200 — a proxy must never
    /// route a reader to a replica that would fail every read.
    /// </summary>
    [Test]
    public async Task TheDatabaseBackedSiteIsNotReadyWhenPostgresDoesNotAnswer()
    {
        // Nothing listens on port 1, so the connection is refused immediately rather than timing
        // out — the same shape of failure as a database that is down or a network policy that
        // refuses the socket. Timeout=2 bounds Npgsql's own retry loop, which the local refusal never
        // reaches, but which a slower CI network might.
        const string unreachable = "Host=127.0.0.1;Port=1;Database=mui;Username=mui;Timeout=2";

        await using var site = await SiteHost.StartAsync(
            settings: new Dictionary<string, string?>
            {
                [CrawlerSettings.EnabledConfigurationKey] = "false",
            },
            connectionString: unreachable);

        var response = await site.Client.GetAsync(HealthEndpoint.Path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
    }
}
