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
    /// <para>
    /// <b>A typed client, never <c>new HttpClient()</c> and never a static one.</b> The factory is
    /// what rotates the underlying handler, so a DNS record that moves is eventually picked up
    /// instead of being pinned for the life of the process. On the one component here whose job is to
    /// fetch URLs strangers control, a handler that never re-resolves is the same class of bug as
    /// §7.2's time-of-check-to-time-of-use gap and would compound it.
    /// </para>
    /// <para>
    /// Everything that makes this client safe is on the registration rather than at the call site, so
    /// there is one place to read it and no way for a second caller to acquire a laxer one.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMuiIcons(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IIconStore>(s => new NpgsqlIconStore(
            s.GetRequiredService<Npgsql.NpgsqlDataSource>()));

        // IHostScopeGuard and ProbeOptions are AddMuiCrawler's, which runs immediately before this
        // under the same connection-string guard. §7.2's gate is reused rather than re-derived: a
        // second copy of those range checks is two sets of rules that must agree for ever, and the
        // day they disagree is the day one of them is wrong about 169.254.169.254.
        services
            .AddHttpClient<IconFetcher>((s, client) =>
            {
                // Short, because nothing here is worth waiting on: an icon that arrives on the next
                // pass instead of this one has cost a reader nothing.
                client.Timeout = TimeSpan.FromSeconds(10);

                // The address the telnet probe already announces over TTYPE and MNES, so an operator
                // reading a web server log and an operator reading a telnet log look up one page —
                // and so this deployment's contact address is a thing it says rather than a default
                // it inherits, which is the ContactedMaintainer lesson.
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    $"mu-index-crawler/1.0 (+{s.GetRequiredService<ProbeOptions>().InfoUrl})");

                // We ask for images and nothing else. A far end that would rather send us HTML is
                // told plainly that we will not read it.
                client.DefaultRequestHeaders.Accept.ParseAdd("image/png, image/jpeg, image/gif, image/webp");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // A redirect is a second address §7.2's gate never ruled on. Following one would let
                // a URL that passes the gate hand us straight to an address that would not.
                AllowAutoRedirect = false,

                // Nothing here has a session, and a cookie jar shared across every game's web host
                // is a thing to have on purpose or not at all.
                UseCookies = false,

                // Bounded, so a moved DNS record is picked up rather than pinned for the process's
                // life. This is the reason the factory is used at all.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddHostedService<IconRefresher>();

        return services;
    }

    /// <summary>
    /// <c>GET /g/{slug}/icon</c> — the bytes, or nothing at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>404 rather than a placeholder.</b> A game with no icon renders no element (the page asks
    /// first), so a request reaching here for a game we hold nothing for is a stale page or a
    /// stranger guessing — neither of which wants a picture invented for it.
    /// </para>
    /// <para>
    /// <b>The content type is the one we determined from the bytes</b>, never the one the far end
    /// claimed, and <c>nosniff</c> goes with it: a type we checked, served as a type nobody may
    /// reinterpret.
    /// </para>
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

            // A type we checked, and a header saying nobody may decide it is something else.
            context.Response.Headers.XContentTypeOptions = "nosniff";

            // Long, and safe to be: the URL is per game and the bytes behind it change only when an
            // owner points the field somewhere new, at which point a stale copy in somebody's cache
            // is a decoration one day out of date.
            context.Response.Headers.CacheControl = "public, max-age=86400";

            return Results.Bytes(icon.Bytes, icon.ContentType, lastModified: icon.FetchedAt);
        });

        return endpoints;
    }
}
