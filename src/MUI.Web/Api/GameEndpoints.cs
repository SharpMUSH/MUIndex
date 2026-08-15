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

        // The facets come back with the listing rather than from a second call, so a consumer
        // building a filter UI over this endpoint gets counts that describe the page it was handed
        // — the same guarantee the site's own panel has, for the same reason.
        var matched = await queries.SearchAsync(query.Filter, http.RequestAborted);

        // One instant for the whole response: every age on a row is measured from the same moment
        // the body is stamped with, so a listing cannot disagree with its own generatedAt.
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
    /// A game by its immutable id: look it up, then read its page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two indexed lookups. It used to list the whole catalogue, archived games included, and pick
    /// one row out of the result — so the identifier this API tells consumers to store (§5.7) was
    /// the most expensive way to ask for a game, and got slower with every game added.
    /// <see cref="IGameQueries.FindByIdAsync"/> was already there for the owner surfaces and answers
    /// this exactly, which is why closing it needs no new method on the interface.
    /// </para>
    /// <para>
    /// The GUID is served directly rather than redirected to the slug: the slug is the mutable one,
    /// and sending a caller from the permanent identifier to the temporary one is backwards. And a
    /// miss is a miss — an id is minted once and never reused, so unlike a slug there is no former
    /// one to redirect to.
    /// </para>
    /// </remarks>
    private static async Task<GamePage?> ByIdAsync(
        IGameQueries queries, Guid id, CancellationToken cancellationToken)
    {
        var match = await queries.FindByIdAsync(id, cancellationToken);
        return match is null ? null : await queries.FindAsync(match.Slug, cancellationToken);
    }
}
