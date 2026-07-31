namespace MUI.Import;

/// <summary>Which of a source's routes we are entitled to use.</summary>
public enum FetchRoute
{
    BulkExport,
    Api,
    Scrape,

    /// <summary>No route is permitted. The importer does not run.</summary>
    None,
}

/// <summary>The chosen route, the URI it is rooted at, and — when there is none — why not.</summary>
public sealed record FetchDecision(FetchRoute Route, Uri? Uri, string? RefusedReason);

/// <summary>
/// Spec §7.6's preference order, decided once and consulted everywhere.
/// </summary>
/// <remarks>
/// Nothing else in this assembly may reason about which URI to fetch. That is what makes a configured
/// <see cref="ImportEtiquette.ScrapeUri"/> genuinely unreachable while a bulk export or an API exists,
/// rather than merely unreached by the importer that happens to be written correctly.
/// </remarks>
public static class EtiquettePlanner
{
    public const string AnonymousUserAgent =
        "the user agent does not name us or carry an info URL (spec §11)";

    public const string MaintainerNotContacted =
        "scraping is the only configured route and nobody has written to the maintainer yet (spec §7.6)";

    public const string NothingConfigured =
        "no bulk export, API or scrape URI is configured";

    public static FetchDecision Decide(ImportEtiquette etiquette)
    {
        ArgumentNullException.ThrowIfNull(etiquette);

        // Before anything else: if we would arrive anonymously, we do not arrive. An admin who cannot
        // tell from their log who read their site cannot ask us to stop.
        if (!ImporterIdentity.SelfIdentifies(etiquette.UserAgent))
        {
            return new FetchDecision(FetchRoute.None, null, AnonymousUserAgent);
        }

        if (etiquette.BulkExportUri is { } bulk)
        {
            return new FetchDecision(FetchRoute.BulkExport, bulk, null);
        }

        if (etiquette.ApiUri is { } api)
        {
            return new FetchDecision(FetchRoute.Api, api, null);
        }

        if (etiquette.ScrapeUri is { } scrape)
        {
            return etiquette.ContactedMaintainer
                ? new FetchDecision(FetchRoute.Scrape, scrape, null)
                : new FetchDecision(FetchRoute.None, null, MaintainerNotContacted);
        }

        return new FetchDecision(FetchRoute.None, null, NothingConfigured);
    }

    /// <summary>
    /// Whether one specific URI may be fetched. Only URIs under the chosen route's root qualify.
    /// </summary>
    public static bool MayFetch(ImportEtiquette etiquette, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(etiquette);
        ArgumentNullException.ThrowIfNull(uri);

        return Decide(etiquette).Uri is { } allowed && IsUnder(allowed, uri);
    }

    /// <summary>
    /// Under, by scheme, host, port, path prefix <em>and</em> — when the root has one — query.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The path prefix is compared on whole segments, so <c>/list</c> does not authorise
    /// <c>/listing-of-something-else</c>.
    /// </para>
    /// <para>
    /// <b>A root that carries a query is a route the query defines, and the query is compared too.</b>
    /// The Mud Connector's whole catalogue is one page at
    /// <c>/cgi-bin/search.cgi?mode=mobile_biglist</c>, and every other thing that site does is the
    /// same script under another <c>mode</c> — including <c>mode=mud_listing</c>, the 689 per-game
    /// pages reading them one at a time would mean, and <c>mode=check_connect</c>, which opens a live
    /// socket to a third party's server on our behalf. Compared on path alone the route degenerates
    /// to "that script, with any query at all", and both of those become permitted; the promise that
    /// they are never fetched would then be kept only by the importer being written correctly, which
    /// is the exact thing this class exists so that nothing has to rely on.
    /// </para>
    /// </remarks>
    private static bool IsUnder(Uri root, Uri candidate)
    {
        if (Uri.Compare(root, candidate, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) != 0)
        {
            return false;
        }

        var rootPath = root.AbsolutePath;
        var path = candidate.AbsolutePath;

        if (!path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var underPath = path.Length == rootPath.Length
            || rootPath.EndsWith('/')
            || path[rootPath.Length] is '/';

        if (!underPath)
        {
            return false;
        }

        // A query-free root authorises the subtree regardless of query, which is what a listing page
        // with paging needs. A root WITH a query authorises that query and nothing else.
        return root.Query.Length == 0
            || string.Equals(root.Query, candidate.Query, StringComparison.Ordinal);
    }
}
