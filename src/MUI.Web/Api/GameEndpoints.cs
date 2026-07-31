using MUI.Catalog;
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

        var matched = await queries.ListAsync(query.Filter, http.RequestAborted);
        var page = matched.Skip(query.Offset).Take(query.Limit).Select(ApiMapper.Summary).ToList();

        await ApiResponse.WriteJsonAsync(http, new GameListView(
            ApiVersion.Current,
            ApiClock.Now(clock),
            query.Echo,
            Total: matched.Count,
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
        var isId = Guid.TryParse(key, out var id);
        var page = isId
            ? await ByIdAsync(queries, id, http.RequestAborted)
            : await queries.FindAsync(key, http.RequestAborted);

        if (page is null)
        {
            // Not found *here* is not the end of the question: a slug this game used to wear is a
            // URL somebody is still holding, and it redirects rather than 404s. Forever.
            if (!isId
                && await slugs.CurrentSlugAsync(key, http.RequestAborted) is { } current
                && await queries.FindAsync(current, http.RequestAborted) is not null)
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

    /// <summary>
    /// A game by its immutable id.
    /// </summary>
    /// <remarks>
    /// It goes the long way round — list everything, including archived, and match — because
    /// <see cref="IGameQueries"/> exposes no lookup by id. A <c>FindAsync(Guid)</c> beside the
    /// existing <c>FindAsync(string)</c> would make this one query; see this task's report.
    /// The GUID is served directly rather than redirected to the slug: the slug is the mutable one,
    /// and sending a caller from the permanent identifier to the temporary one is backwards.
    /// </remarks>
    private static async Task<GamePage?> ByIdAsync(
        IGameQueries queries, Guid id, CancellationToken cancellationToken)
    {
        var all = await queries.ListAsync(new GameFilter { IncludeArchived = true }, cancellationToken);
        var match = all.FirstOrDefault(g => g.Id == id);
        return match is null ? null : await queries.FindAsync(match.Slug, cancellationToken);
    }
}
