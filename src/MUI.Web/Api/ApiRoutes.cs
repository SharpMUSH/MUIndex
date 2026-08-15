namespace MUI.Web.Api;

/// <summary>
/// Every path this API publishes, in one place, so a link in a payload and the route that serves it
/// cannot drift apart.
/// </summary>
/// <remarks>
/// Links are site-relative rather than absolute. This deployable sits behind whatever proxy and
/// hostname an operator gives it, and a payload that hard-coded one would be wrong the first time
/// the dataset was mirrored — which is the case §10 exists to serve.
/// </remarks>
public static class ApiRoutes
{
    public const string Base = "/api";

    public const string Games = Base + "/games";

    public const string Feeds = Base + "/feeds";

    public const string Dump = Base + "/dump/games.json";

    public const string DumpLines = Base + "/dump/games.ndjson";

    public const string DiscoveredRss = Feeds + "/discovered.rss";

    public const string WentDarkRss = Feeds + "/went-dark.rss";

    public const string CameBackRss = Feeds + "/came-back.rss";

    /// <summary>§10's presence time series, for one game.</summary>
    public const string PresenceSuffix = "/presence";

    /// <summary>§10's availability time series, for one game.</summary>
    public const string AvailabilitySuffix = "/availability";

    /// <summary>The page a reader sees. The API never links to a page it does not serve.</summary>
    public static string Page(string slug) => $"/g/{Uri.EscapeDataString(slug)}";

    /// <summary>One game's presence series, keyed the durable way.</summary>
    public static string Presence(Guid id) => $"{Game(id)}{PresenceSuffix}";

    /// <summary>One game's availability series, keyed the durable way.</summary>
    public static string Availability(Guid id) => $"{Game(id)}{AvailabilitySuffix}";

    /// <summary>
    /// The durable link to a game. Keyed on the GUID and not the slug, because the slug is mutable
    /// by design (spec §5.7) and this is the one a mirror should store.
    /// </summary>
    public static string Game(Guid id) => $"{Games}/{id:D}";
}
