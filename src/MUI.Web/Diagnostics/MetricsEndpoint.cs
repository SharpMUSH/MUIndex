using System.Globalization;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
/// on the host and an SSH tunnel to the monitoring box, so the bytes never cross a network. The
/// socket guard below is what keeps it off the listener Traefik forwards to; there is no token,
/// because there is no route from outside for a token to defend.
/// </para>
/// <para>
/// <b>"Which listener" must be read from the connection, never from the request.</b> The obvious
/// spelling is <c>RequireHost($"*:{port}")</c> and it is a disclosure bug — see
/// <see cref="MapMuiMetrics"/>. That version shipped in a pull request and was caught in review.
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

        // TryAdd, the way AddMuiCrawler does it: CrawlMetrics needs a clock, and an extension method
        // that only works when something else happened to have registered one first is a trap for
        // whoever calls it next. AddMuiSite registers the same instance earlier, so this is a no-op
        // in the deployed graph and the difference is only visible to a caller composing this alone.
        services.TryAddSingleton(TimeProvider.System);

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

        if (ResolvePort(configuration) is not { } port)
        {
            return app;
        }

        app.Use(async (context, next) =>
        {
            // The scrape is not traffic. It runs every fifteen seconds for ever, and a request rate
            // that is mostly the monitoring asking about the request rate answers nobody's question.
            //
            // On the metrics listener only, for the same reason the endpoint itself checks the
            // socket: `/metrics` asked for on the public port is not a scrape, it is somebody
            // probing, and that is exactly the request worth having on a graph. Gating on the path
            // alone would have made the one request nobody should be making the one request nothing
            // counted.
            if (context.Connection.LocalPort == port
                && context.Request.Path.StartsWithSegments(Path, StringComparison.Ordinal))
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

        // The whole security model, and it reads the accepting socket rather than anything the
        // caller sent.
        //
        // **This was `RequireHost($"*:{port}")` and that was a disclosure bug.** `RequireHost`
        // matches `HttpRequest.Host` — the `Host` *header*, which the client writes. Traefik's own
        // `Host()` matcher ignores the port, so `Host: mu-index.com:9102` satisfied the public
        // router's rule, reached the site's listener, and then satisfied a guard reading the same
        // attacker-supplied string. The whole body went out over the internet for one curl flag.
        // Found by review; `ItRefusesAPublicRequestThatWritesTheMetricsPortIntoTheHostHeader` is the
        // case that now fails without this.
        //
        // `Connection.LocalPort` is which socket accepted the connection. A caller cannot write it,
        // a proxy cannot forward it, and it is the only thing here that actually means "arrived on
        // the metrics listener". The answer is a plain 404, the same as any unknown path, so there
        // is still nothing to probe for.
        .AddEndpointFilter(async (invocation, next) =>
            invocation.HttpContext.Connection.LocalPort == port
                ? await next(invocation)
                : Results.NotFound())
        .AllowAnonymous();

        return app;
    }
}
