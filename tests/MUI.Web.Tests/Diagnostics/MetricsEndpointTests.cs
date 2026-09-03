using System.Net;

using Microsoft.Extensions.DependencyInjection;

using MUI.Web.Diagnostics;

namespace MUI.Web.Tests.Diagnostics;

/// <summary>
/// <c>GET /metrics</c> over real HTTP.
/// </summary>
/// <remarks>
/// The property that matters most here is a negative one. This body says how much memory the process
/// is holding, how many games are in the crawl queue and how often this site is asked for things —
/// operational detail that has no business being served to the internet — and the deployment reaches
/// it over a loopback port and an SSH tunnel, exactly as node-exporter and cadvisor are reached. So
/// the endpoint must not answer on the port the public router can reach, and that is what most of
/// these assert.
/// </remarks>
public class MetricsEndpointTests
{
    /// <summary>
    /// With no port configured there is no endpoint at all — the deployed default, since a site that
    /// was never asked for metrics should not have grown a diagnostics surface.
    /// </summary>
    [Test]
    public async Task WithNoPortConfiguredThereIsNoEndpoint()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync(MetricsEndpoint.Path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The one that would be a disclosure bug. A metrics port is configured, so the endpoint exists —
    /// and a request arriving on the site's own port, which is the port Traefik forwards to, is
    /// refused rather than answered.
    /// </summary>
    [Test]
    public async Task ItRefusesTheRequestThatArrivedOnThePublicPort()
    {
        // A port this host is definitely not listening on, so any answer would prove the host guard
        // is not being applied rather than that the request reached the right listener.
        await using var site = await SiteHost.StartAsync(
            settings: new Dictionary<string, string?>
            {
                [MetricsEndpoint.PortConfigurationKey] = "59999",
            });

        var response = await site.Client.GetAsync(MetricsEndpoint.Path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// And the positive case: on its own port it answers, in the format Prometheus parses. Driven by
    /// pointing the metrics port at the site's own listener, which is the only way one test host can
    /// be on both sides of the guard above.
    /// </summary>
    [Test]
    public async Task OnItsOwnPortItServesAScrape()
    {
        await using var site = await SiteHost.StartAsync(configureMetricsPortToOwn: true);

        var response = await site.Client.GetAsync(MetricsEndpoint.Path);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // The content type Prometheus expects; anything else and it declines to parse the body.
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("text/plain");

        await Assert.That(body).Contains("mui_gc_heap_size_bytes ");
        await Assert.That(body).Contains("mui_crawl_cycles_total ");
        await Assert.That(body).Contains("mui_http_requests_total{");
    }

    /// <summary>
    /// Requests are counted by status class and by nothing else.
    /// </summary>
    /// <remarks>
    /// Deliberately not by path. A label whose values come from the request URL is unbounded, and
    /// this site's listing alone generates a stream of unique URLs — so a per-path counter would grow
    /// a series per URL for ever, inside the very process whose memory growth this endpoint was built
    /// to explain. The cardinality bound is the point, not an omission.
    /// </remarks>
    [Test]
    public async Task RequestsAreCountedByStatusClassAndNotByPath()
    {
        await using var site = await SiteHost.StartAsync(configureMetricsPortToOwn: true);

        await site.Client.GetAsync("/");
        await site.Client.GetAsync("/no-such-page-" + Guid.NewGuid().ToString("N"));
        await site.Client.GetAsync("/another-missing-" + Guid.NewGuid().ToString("N"));

        var body = await site.Client.GetStringAsync(MetricsEndpoint.Path);

        await Assert.That(RuntimeMetricsTests.Read(body, "mui_http_requests_total", ("status", "2xx")))
            .IsGreaterThanOrEqualTo(1);
        await Assert.That(RuntimeMetricsTests.Read(body, "mui_http_requests_total", ("status", "4xx")))
            .IsGreaterThanOrEqualTo(2);

        // No series carries a path, however many distinct ones were just asked for.
        await Assert.That(body).DoesNotContain("no-such-page-");
        await Assert.That(body).DoesNotContain("path=");
    }

    /// <summary>
    /// The scrape itself is not counted as traffic. It runs every fifteen seconds for ever, and a
    /// request rate that is mostly the monitoring asking about the request rate is a graph that
    /// answers a question nobody asked.
    /// </summary>
    [Test]
    public async Task TheScrapeDoesNotCountItself()
    {
        await using var site = await SiteHost.StartAsync(configureMetricsPortToOwn: true);

        await site.Client.GetStringAsync(MetricsEndpoint.Path);
        var body = await site.Client.GetStringAsync(MetricsEndpoint.Path);

        await Assert.That(RuntimeMetricsTests.Read(body, "mui_http_requests_total", ("status", "2xx")))
            .IsEqualTo(0);
    }
}
