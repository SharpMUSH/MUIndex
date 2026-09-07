using MUI.Catalog;
using MUI.Web.Api;

namespace MUI.Streaming.Prototype;

/// <summary>One query result, locale and timestamp shared by every part of a response.</summary>
public sealed record ListingSnapshot(
    GameListing Listing,
    GameFilter Filter,
    string Query,
    string? Error,
    DateTimeOffset Now,
    HttpContext? Context = null)
{
    public static ListingSnapshot FromFixture(
        IReadOnlyList<GameFacetRow> rows, string query = "", HttpContext? context = null, DateTimeOffset? now = null)
        => FromCatalogue(FacetedSearch.Prepare(rows), query, context, now);

    public static ListingSnapshot FromCatalogue(
        FacetedSearch.Catalogue catalogue, string query = "", HttpContext? context = null, DateTimeOffset? now = null)
    {
        var valid = GameFilterBinding.TryRead(query, out var bound, out var error);
        var filter = valid ? bound.Filter : new GameFilter();
        return new(valid ? FacetedSearch.Search(catalogue, filter) : GameListing.Empty,
            filter, query, error, now ?? DateTimeOffset.UtcNow, context);
    }
}
