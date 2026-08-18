using MUI.Catalog;
using MUI.Catalog.Persistence;

using Microsoft.Net.Http.Headers;

namespace MUI.Web.Api;

/// <summary>
/// Spec §8.5's owner-published outputs: a live player-count badge, and JSON for the game's own site.
/// </summary>
/// <remarks>
/// <b>Public, not owner-gated</b> — meant to be embedded on a page we do not control, publishing
/// nothing the game's own page doesn't already show. Both go through <see cref="ApiResponse"/> for a
/// strong ETag, <c>If-None-Match</c>, <c>nosniff</c> and open CORS, overriding only the cache window:
/// a badge is refetched by every reader of somebody's front page, and a minute is too little.
/// </remarks>
public static class BadgeEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/g/{slug}/badge.svg", SvgAsync);
        app.MapGet("/g/{slug}/badge.json", JsonAsync);
    }

    /// <summary>The badge, as an image.</summary>
    public static async Task SvgAsync(
        HttpContext http,
        string slug,
        IGameQueries queries,
        ISlugHistory slugs,
        TimeProvider clock)
    {
        var (game, redirected) = await ResolveAsync(http, slug, queries, slugs, ".svg");

        if (redirected)
        {
            return;
        }

        if (game is null)
        {
            // A badge rather than an empty 404 — a broken-image icon would tell the operator nothing.
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            http.Response.ContentType = "image/svg+xml; charset=utf-8";
            http.Response.Headers[HeaderNames.CacheControl] = "no-store";
            await http.Response.WriteAsync(PlayerBadge.UnknownSvg(), http.RequestAborted);
            return;
        }

        var reading = PlayerBadge.Read(game, clock.GetUtcNow());
        var body = System.Text.Encoding.UTF8.GetBytes(PlayerBadge.Svg(reading, game.Name));

        await ApiResponse.WriteAsync(
            http, body, "image/svg+xml; charset=utf-8", ETag.Of(body), PlayerBadge.CacheControl);
    }

    /// <summary>The same reading, for a page that would rather draw its own.</summary>
    public static async Task JsonAsync(
        HttpContext http,
        string slug,
        IGameQueries queries,
        ISlugHistory slugs,
        TimeProvider clock)
    {
        var (game, redirected) = await ResolveAsync(http, slug, queries, slugs, ".json");

        if (redirected)
        {
            return;
        }

        if (game is null)
        {
            await ApiResponse.ProblemAsync(
                http,
                StatusCodes.Status404NotFound,
                "No such game",
                $"Nothing in the catalogue answers to '{slug}'.");
            return;
        }

        var reading = PlayerBadge.Read(game, clock.GetUtcNow());

        await ApiResponse.WriteJsonAsync(http, new BadgeView(
            game.Slug,
            game.Name,
            game.State,
            reading.Count,
            reading.Word,
            reading.Description,
            reading.Age?.TotalSeconds,

            // Null unless we measured it — gated on the same chip the reading is, so measuredAt never
            // names a measurement nobody took.
            game.PlayersNowProvenance is { IsMeasured: true } measured
                ? measured.LastConfirmedAt
                : null,
            game.LastReachableAt,
            ApiRoutes.Page(game.Slug),
            $"{ApiRoutes.Page(game.Slug)}/badge.svg"),
            PlayerBadge.CacheControl);
    }

    /// <summary>
    /// The game a slug names, redirecting from one it used to have (spec §5.7).
    /// </summary>
    /// <remarks>
    /// A badge is the single most likely thing on this site to outlive the URL it was copied from —
    /// pasted into somebody's template once and left for years — so §5.7's forever-redirect matters
    /// here. Two outcomes, two values: a bare null couldn't tell "no such game" from "redirected",
    /// and the caller wrote a 404 over the 301 it had just set.
    /// </remarks>
    private static async Task<(GameSummary? Game, bool Redirected)> ResolveAsync(
        HttpContext http,
        string slug,
        IGameQueries queries,
        ISlugHistory slugs,
        string suffix)
    {
        if (await queries.FindAsync(slug, http.RequestAborted) is { } page)
        {
            return (page.Summary, false);
        }

        // The same resolver as the page and the API — a badge is the surface a stale URL survives
        // longest on.
        if (await SlugDestination.ForAsync(
                slug,
                http.RequestServices.GetService<IMergeRedirects>(),
                slugs,
                queries,
                http.RequestAborted) is { } current)
        {
            http.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            http.Response.Headers[HeaderNames.Location] = $"{ApiRoutes.Page(current)}/badge{suffix}";
            http.Response.Headers[HeaderNames.CacheControl] = PlayerBadge.CacheControl;
            return (null, true);
        }

        return (null, false);
    }
}

/// <summary>
/// The badge as data (spec §8.5), for a site that would rather render its own.
/// </summary>
/// <remarks><see cref="Count"/> is null whenever nothing was measured; <see cref="State"/> says which of the three cases that is, so a consumer can't coerce null to zero by accident.</remarks>
public sealed record BadgeView(
    string Slug,
    string Name,
    LifecycleState Lifecycle,
    int? Count,
    string State,
    string Description,
    double? AgeSeconds,
    DateTimeOffset? MeasuredAt,
    DateTimeOffset? LastReachableAt,
    string PageUrl,
    string BadgeUrl);
