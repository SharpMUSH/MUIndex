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
    /// Under, by scheme, host, port <em>and</em> path prefix — and the path prefix is compared on
    /// whole segments, so <c>/list</c> does not authorise <c>/listing-of-something-else</c>.
    /// </summary>
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

        return path.Length == rootPath.Length
            || rootPath.EndsWith('/')
            || path[rootPath.Length] is '/' or '?';
    }
}
