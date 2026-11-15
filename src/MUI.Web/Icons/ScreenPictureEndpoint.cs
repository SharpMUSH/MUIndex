using Microsoft.Extensions.DependencyInjection.Extensions;

using MUI.Catalog;
using MUI.Catalog.Persistence;

namespace MUI.Web.Icons;

/// <summary>
/// Serving the pictures a connect screen draws, from this origin.
/// </summary>
public static class ScreenPictureEndpoint
{
    /// <summary>The path a picture drawn over the quoted screen points at.</summary>
    public const string Path = "/g/{slug}/screen/{ordinal:int}";

    /// <summary>
    /// The same path for one picture, with its version in the query so a picture replaced at the
    /// same place on the screen is not answered from a reader's cache.
    /// </summary>
    public static string For(string slug, int ordinal, string version) =>
        $"/g/{Uri.EscapeDataString(slug)}/screen/{ordinal}?v={Uri.EscapeDataString(version)}";

    /// <summary>
    /// The store, and the loop that fetches the pictures a screen names by address. After
    /// <see cref="IconEndpoint.AddMuiIcons"/>, whose fetcher it reuses.
    /// </summary>
    public static IServiceCollection AddMuiScreenPictures(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // TryAdd: AddMuiCrawler registers the same store for the binder that writes these rows.
        services.TryAddSingleton<IScreenPictureStore>(s => new NpgsqlScreenPictureStore(
            s.GetRequiredService<Npgsql.NpgsqlDataSource>()));

        services.AddHostedService<ScreenPictureRefresher>();

        return services;
    }

    /// <summary>
    /// <c>GET /g/{slug}/screen/{ordinal}</c> — the bytes, or nothing at all.
    /// </summary>
    /// <remarks>
    /// 404 when the owner has hidden the screen: a picture of a screen the page does not show is that
    /// screen, shown. The type served is read again from the bytes, never taken from the far end or
    /// from what the terminal protocol called it, and <c>nosniff</c> goes with it.
    /// </remarks>
    public static IEndpointRouteBuilder MapMuiScreenPictures(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(Path, async (
            string slug,
            int ordinal,
            IGameQueries games,
            IScreenPictureStore pictures,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await games.FindAsync(slug, cancellationToken) is not { ConnectScreenSuppressed: false } page
                || await pictures.FileAsync(page.Summary.Id, ordinal, cancellationToken) is not { } file
                || ImageHeader.Read(file.Bytes) is not { } header)
            {
                return Results.NotFound();
            }

            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.CacheControl = "public, max-age=86400";

            return Results.Bytes(file.Bytes, header.ContentType, lastModified: file.CapturedAt);
        });

        return endpoints;
    }
}
