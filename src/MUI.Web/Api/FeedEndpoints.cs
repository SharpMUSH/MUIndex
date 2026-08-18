using Microsoft.Extensions.Options;

using MUI.Catalog;

namespace MUI.Web.Api;

/// <summary>
/// The three liveness registers — newly discovered, went dark, came back — as JSON and as RSS.
/// </summary>
/// <remarks>The differentiator (spec §9): no incumbent measures continuously enough to publish them. RSS is the whole of v1's notification story; webhooks are deferred (spec §14).</remarks>
public static class FeedEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiRoutes.Feeds, FeedsAsync);

        foreach (var register in Enum.GetValues<FeedRegister>())
        {
            api.MapGet(
                RssFeed.Path(register),
                (HttpContext http,
                 IGameQueries queries,
                 IOptions<DatasetLicenceOptions> licence,
                 TimeProvider clock) => RssAsync(http, register, queries, licence, clock));
        }
    }

    public static async Task FeedsAsync(HttpContext http, IGameQueries queries, TimeProvider clock)
    {
        var feeds = await queries.FeedsAsync(http.RequestAborted);

        static FeedEntryView[] View(IReadOnlyList<FeedEntry> entries) =>
            [.. entries.Select(ApiMapper.Feed)];

        await ApiResponse.WriteJsonAsync(http, new FeedsView(
            ApiVersion.Current,
            ApiClock.Now(clock),
            View(feeds.NewlyDiscovered),
            View(feeds.WentDark),
            View(feeds.CameBack),
            Enum.GetValues<FeedRegister>().ToDictionary(RssFeed.Key, RssFeed.Path, StringComparer.Ordinal)));
    }

    public static async Task RssAsync(
        HttpContext http,
        FeedRegister register,
        IGameQueries queries,
        IOptions<DatasetLicenceOptions> licence,
        TimeProvider clock)
    {
        var feeds = await queries.FeedsAsync(http.RequestAborted);

        var xml = RssFeed.Render(
            register,
            RssFeed.Entries(feeds, register),
            Origin(http),
            ApiClock.Now(clock),
            licence.Value.View());

        await ApiResponse.WriteTextAsync(http, xml, "application/rss+xml; charset=utf-8");
    }

    /// <summary>
    /// RSS items need absolute links, which is the one place this API cannot use a relative path.
    /// Taken from the request rather than configured, so a mirror serves its own links.
    /// </summary>
    /// <remarks>Shares the answer with the canonical link, preview metadata and sitemap rather than assembling it separately.</remarks>
    private static Uri Origin(HttpContext http) => SiteUrls.OriginOf(http);
}
