using MUI.Catalog;

using Microsoft.Net.Http.Headers;

namespace MUI.Web.Api;

/// <summary>
/// Spec §8.5's owner-published outputs: a live player-count badge, and JSON for the game's own site.
/// </summary>
/// <remarks>
/// <para>
/// <b>Public, not owner-gated.</b> These are meant to be embedded on a page we do not control, by an
/// operator who does not want to proxy them, so they answer anybody — and they publish nothing the
/// game's own page does not already show. What a claim grants is the <em>reason</em> to want them,
/// not permission to fetch them.
/// </para>
/// <para>
/// Both go through <see cref="ApiResponse"/>, so both get a strong ETag over the exact bytes,
/// <c>If-None-Match</c> handling, <c>nosniff</c> and the open CORS header the rest of §10 has. The
/// one thing they override is the cache window: a badge is refetched by every reader of somebody's
/// front page, and a minute is too little.
/// </para>
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
            // A badge rather than an empty 404, because whoever sees this is the operator who just
            // pasted the wrong URL onto their own site, and a broken-image icon says nothing.
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            http.Response.ContentType = "image/svg+xml; charset=utf-8";
            http.Response.Headers[HeaderNames.CacheControl] = "no-store";
            await http.Response.WriteAsync(PlayerBadge.UnknownSvg(), http.RequestAborted);
            return;
        }

        var reading = PlayerBadge.Read(game, clock.GetUtcNow());
        var body = System.Text.Encoding.UTF8.GetBytes(PlayerBadge.Svg(reading, game.Name));

        ApiResponse.Prepare(http, "image/svg+xml; charset=utf-8", ETag.Of(body));
        http.Response.Headers[HeaderNames.CacheControl] = PlayerBadge.CacheControl;

        if (ApiResponse.NotModified(http, ETag.Of(body)))
        {
            return;
        }

        http.Response.ContentLength = body.Length;
        await http.Response.Body.WriteAsync(body, http.RequestAborted);
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

            // Null unless we measured it, and gated on the same chip the reading is: a
            // measuredAt beside a count of null would be an instant attached to nothing, and one
            // beside a game's own MSSP assertion would name a measurement nobody took.
            game.PlayersNowProvenance is { IsMeasured: true } measured
                ? measured.LastConfirmedAt
                : null,
            game.LastReachableAt,
            ApiRoutes.Page(game.Slug),
            $"{ApiRoutes.Page(game.Slug)}/badge.svg"));

        http.Response.Headers[HeaderNames.CacheControl] = PlayerBadge.CacheControl;
    }

    /// <summary>
    /// The game a slug names, redirecting from one it used to have (spec §5.7).
    /// </summary>
    /// <remarks>
    /// A badge is the single most likely thing on this site to outlive the URL it was copied from:
    /// it is pasted into somebody's template once and left for years, and §5.7's forever-redirect is
    /// the promise that makes that safe.
    ///
    /// The second half of the answer is not a nicety: this returned a bare null for both "no such
    /// game" and "redirected", so the caller wrote a 404 over the 301 it had just set and the
    /// forever-redirect worked for no route on this pair. Two outcomes, two values.
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

        if (await slugs.CurrentSlugAsync(slug, http.RequestAborted) is { } current
            && await queries.FindAsync(current, http.RequestAborted) is not null)
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
/// <remarks>
/// <see cref="Count"/> is null whenever nothing was measured, and <see cref="State"/> says which of
/// the three cases that is — so a consumer coercing null to zero has to do it on purpose, and one
/// reading the state cannot do it at all. Both are published because §5.4's middle case is the one
/// every reimplementation loses.
/// </remarks>
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
