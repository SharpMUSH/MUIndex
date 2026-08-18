using MUI.Catalog.Persistence;
using MUI.Crawl;

namespace MUI.Web.Icons;

/// <summary>
/// Serving a game's icon from this origin, and registering the one HTTP client that fetches it.
/// </summary>
public static class IconEndpoint
{
    /// <summary>The path a game page's <c>img</c> points at.</summary>
    public const string Path = "/g/{slug}/icon";

    /// <summary>The same path for a known slug, so the page and the route have one spelling.</summary>
    public static string For(string slug) => $"/g/{slug}/icon";

    /// <summary>
    /// The icon fetcher, as a typed client through <see cref="IHttpClientFactory"/>.
    /// </summary>
    /// <remarks>
    /// A typed client, never <c>new HttpClient()</c> or a static one — the factory rotates the
    /// handler so a moved DNS record is picked up rather than pinned for the process's life, which
    /// would compound §7.2's TOCTOU gap. Everything that makes this client safe lives on the
    /// registration, not the call site, so no second caller can acquire a laxer one.
    /// </remarks>
    public static IServiceCollection AddMuiIcons(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IIconStore>(s => new NpgsqlIconStore(
            s.GetRequiredService<Npgsql.NpgsqlDataSource>()));

        // IHostScopeGuard and ProbeOptions are AddMuiCrawler's; §7.2's gate is reused rather than
        // re-derived, since a second copy of those range checks is two sets of rules to keep in sync.
        services
            .AddHttpClient<IconFetcher>((s, client) =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);

                // Same contact address the telnet probe announces over TTYPE and MNES.
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    $"mu-index-crawler/1.0 (+{s.GetRequiredService<ProbeOptions>().InfoUrl})");

                client.DefaultRequestHeaders.Accept.ParseAdd("image/png, image/jpeg, image/gif, image/webp");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // A redirect is a second address §7.2's gate never ruled on.
                AllowAutoRedirect = false,
                UseCookies = false,

                // Bounded, so a moved DNS record is picked up rather than pinned for the process's
                // life — the reason the factory is used at all.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddHostedService<IconRefresher>();

        return services;
    }

    /// <summary>
    /// <c>GET /g/{slug}/icon</c> — the bytes, or nothing at all.
    /// </summary>
    /// <remarks>
    /// 404 rather than a placeholder: a game with no icon renders no element, so a request reaching
    /// here is a stale page or a stranger guessing. The content type served is the one determined from
    /// the bytes, never the far end's claim, with <c>nosniff</c> alongside it.
    /// </remarks>
    public static IEndpointRouteBuilder MapMuiIcons(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(Path, async (
            string slug,
            IGameStore games,
            IIconStore icons,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await games.BySlugAsync(slug, cancellationToken) is not { } game
                || await icons.ForGameAsync(game.Id, cancellationToken) is not { } icon)
            {
                return Results.NotFound();
            }

            context.Response.Headers.XContentTypeOptions = "nosniff";

            // Long, and safe to be: the URL is per game, so a stale cached copy is a decoration one
            // day out of date, not a wrong game.
            context.Response.Headers.CacheControl = "public, max-age=86400";

            return Results.Bytes(icon.Bytes, icon.ContentType, lastModified: icon.FetchedAt);
        });

        return endpoints;
    }
}
