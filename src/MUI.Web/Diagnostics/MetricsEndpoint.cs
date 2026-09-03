using System.Globalization;

namespace MUI.Web.Diagnostics;

/// <summary>
/// <c>GET /metrics</c> — what this process will say about its own memory, on a port the public
/// router cannot reach.
/// </summary>
/// <remarks>
/// <para>
/// Built on 2026-09-03, after three days of a memory climb that no measurement outside the process
/// could explain. <c>container_memory_rss</c>, <c>working_set</c> and <c>smaps_rollup</c> all agreed
/// the replicas were growing and none of them could say whether that was a live set, a collector
/// that had not been pressed into returning what it held, or fragmentation — those are one number
/// from outside and three from inside. The endpoint exists to make that distinction observable
/// permanently rather than by attaching a debugger to a production process during an incident.
/// </para>
/// <para>
/// <b>Off unless a port is named, and never on the port the site is served from.</b> The body
/// carries how much memory this process holds, how many games are queued and how often the site is
/// asked for things — operational detail with no business being served to the internet. The
/// deployment reaches it the same way it reaches node-exporter and cadvisor: a loopback publication
/// on the host and an SSH tunnel to the monitoring box, so the bytes never cross a network. The host
/// guard below is what keeps it off the listener Traefik forwards to; there is no token, because
/// there is no route from outside for a token to defend.
/// </para>
/// </remarks>
public static class MetricsEndpoint
{
    public const string Path = "/metrics";

    /// <summary>
    /// The port to serve metrics on. Unset means no endpoint at all.
    /// </summary>
    /// <remarks>
    /// A port rather than an on/off switch, because "which listener" <em>is</em> the security
    /// property here. A boolean would have to be paired with a separate port setting, and the
    /// failure mode of forgetting the second one is serving this to the internet.
    /// </remarks>
    public const string PortConfigurationKey = "MUI_METRICS_PORT";

    /// <summary>The configured port, or null when nobody named one.</summary>
    public static int? ResolvePort(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration[PortConfigurationKey];

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Throws rather than falling back to "off". A deployment that meant to expose metrics and
        // typed the port wrongly should find out at startup, not by wondering why the graph is
        // empty a week later.
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            && port is > 0 and <= 65535
                ? port
                : throw new InvalidOperationException(
                    $"{PortConfigurationKey} is '{value}', which is not a port number.");
    }

    public static IServiceCollection AddMuiMetrics(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<RequestMetrics>();
        services.AddSingleton<CrawlMetrics>();

        // The same object under the crawl loop's own name, not a second one. A separate registration
        // would leave the loop recording into an instance nothing serves, and the failure would look
        // like a flat graph during the incident it was built for.
        services.AddSingleton<MUI.Crawler.ICycleObserver>(s => s.GetRequiredService<CrawlMetrics>());

        return services;
    }

    /// <summary>Counts every request, when metrics are on at all.</summary>
    public static IApplicationBuilder UseMuiMetrics(this IApplicationBuilder app, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (ResolvePort(configuration) is null)
        {
            return app;
        }

        app.Use(async (context, next) =>
        {
            // The scrape is not traffic. It runs every fifteen seconds for ever, and a request rate
            // that is mostly the monitoring asking about the request rate answers nobody's question.
            if (context.Request.Path.StartsWithSegments(Path, StringComparison.Ordinal))
            {
                await next(context);
                return;
            }

            var metrics = context.RequestServices.GetRequiredService<RequestMetrics>();

            metrics.Entered();

            try
            {
                await next(context);
            }
            finally
            {
                // In the finally, so a request that threw is still counted — an unhandled exception
                // is exactly the traffic worth seeing on a graph.
                metrics.Observe(context.Response.StatusCode);
                metrics.Left();
            }
        });

        return app;
    }

    public static WebApplication MapMuiMetrics(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (ResolvePort(app.Configuration) is not { } port)
        {
            return app;
        }

        app.MapGet(Path, (RequestMetrics requests, CrawlMetrics crawl) =>
        {
            var text = new PrometheusText();

            RuntimeMetrics.WriteTo(text);
            crawl.WriteTo(text);
            requests.WriteTo(text);

            // The version Prometheus's own exposition uses. A different content type and it declines
            // to parse the body rather than guessing.
            return Results.Text(
                text.ToString(),
                "text/plain",
                System.Text.Encoding.UTF8);
        })

        // The whole security model in one line: a request that arrived on any other listener does
        // not match this route and falls through to the 404 every unknown path gets. Traefik
        // forwards to the site's port, so this is never reachable from the internet — and it is a
        // route that does not exist there, rather than one that exists and refuses, so there is
        // nothing to probe for.
        .RequireHost($"*:{port.ToString(CultureInfo.InvariantCulture)}")
        .AllowAnonymous();

        return app;
    }
}
