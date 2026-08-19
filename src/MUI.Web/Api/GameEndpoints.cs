using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Data;

using Microsoft.Net.Http.Headers;

namespace MUI.Web.Api;

/// <summary>The listing and the game — the two routes everything else is built out of.</summary>
public static class GameEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiRoutes.Games, ListAsync);
        api.MapGet(ApiRoutes.Games + "/{key}", GameAsync);
    }

    /// <summary>
    /// The listing, taking the facet panel's own querystring so the two surfaces answer one question.
    /// </summary>
    /// <remarks>
    /// Archived games are out of the default listing and of nothing else (spec §7.5): they are one
    /// <c>archived=true</c> away, they are in the dump, and their own route keeps working forever.
    /// </remarks>
    public static async Task ListAsync(HttpContext http, IGameQueries queries, TimeProvider clock)
    {
        if (!GameFilterBinding.TryRead(http.Request.Query, out var query, out var error))
        {
            await ApiResponse.ProblemAsync(http, StatusCodes.Status400BadRequest, "Bad filter", error!);
            return;
        }

        // The facets come back with the listing rather than from a second call, so counts describe
        // the page a consumer was actually handed.
        var matched = await queries.SearchAsync(query.Filter, http.RequestAborted);

        // One instant for the whole response, so a listing can't disagree with its own generatedAt.
        var now = ApiClock.Now(clock);
        var page = matched.Games
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(game => ApiMapper.Summary(game, now))
            .ToList();

        await ApiResponse.WriteJsonAsync(http, new GameListView(
            ApiVersion.Current,
            now,
            query.Echo,
            [.. matched.Facets.Select(ApiMapper.Facet)],
            Total: matched.Games.Count,
            query.Limit,
            query.Offset,
            Count: page.Count,
            page));
    }

    /// <summary>
    /// One game, by its immutable GUID or by its current slug — and a permanent redirect from any
    /// slug it used to have (spec §5.7).
    /// </summary>
    public static async Task GameAsync(
        HttpContext http,
        string key,
        IGameQueries queries,
        IAvailabilityHistory availability,
        ISlugHistory slugs,
        TimeProvider clock)
    {
        // Two keys, one read each (spec §5.7).
        var isId = Guid.TryParse(key, out var id);
        var page = isId
            ? await queries.FindAsync(id, http.RequestAborted)
            : await queries.FindAsync(key, http.RequestAborted);

        if (page is null)
        {
            // Not found here isn't the end of the question: a former slug redirects rather than
            // 404s, forever, through the same SlugDestination the page uses.
            if (!isId
                && await SlugDestination.ForAsync(
                    key,
                    http.RequestServices.GetService<IMergeRedirects>(),
                    slugs,
                    queries,
                    http.RequestAborted) is { } current)
            {
                http.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                http.Response.Headers[HeaderNames.Location] =
                    $"{ApiRoutes.Games}/{Uri.EscapeDataString(current)}";
                http.Response.Headers[HeaderNames.CacheControl] = ApiResponse.CacheControl;
                return;
            }

            await ApiResponse.ProblemAsync(
                http,
                StatusCodes.Status404NotFound,
                "No such game",
                $"Nothing in the catalogue answers to '{key}'. Nothing here is ever deleted, so a "
                + "game that has gone dark still has a page — try the listing with archived=true.");
            return;
        }

        var intervals = await availability.ForGameAsync(page.Summary.Id, http.RequestAborted);

        await ApiResponse.WriteJsonAsync(
            http, ApiMapper.Game(page, intervals, ApiClock.Now(clock)));
    }

}
