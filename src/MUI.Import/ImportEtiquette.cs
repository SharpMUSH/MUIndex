namespace MUI.Import;

/// <summary>
/// How we agreed to treat one directory.
/// </summary>
/// <remarks>
/// Spec §7.6 states the etiquette in prose — ask for a bulk export or use a documented API in
/// preference to scraping, honour <c>robots.txt</c>, rate-limit hard, attribute every source — and
/// this record is that paragraph in a form the code can obey. These sites are run by people in the
/// same small hobby, and several of them are the reason any of this data exists at all.
/// </remarks>
public sealed record ImportEtiquette
{
    /// <summary>The name that appears on the about page and in the API's attribution list.</summary>
    public required string SourceName { get; init; }

    /// <summary>Where a reader is sent to credit this source. Never optional.</summary>
    public required Uri AttributionUri { get; init; }

    /// <summary>A published dump or listing page. The best route, and preferred over everything below.</summary>
    public Uri? BulkExportUri { get; init; }

    /// <summary>A documented API. Preferred over scraping.</summary>
    public Uri? ApiUri { get; init; }

    /// <summary>
    /// The last resort. Reachable only when neither of the two above is configured <em>and</em>
    /// <see cref="ContactedMaintainer"/> is true.
    /// </summary>
    public Uri? ScrapeUri { get; init; }

    /// <summary>Must self-identify with an info URL — spec §11's crawler-identification rule.</summary>
    public required string UserAgent { get; init; }

    /// <summary>The floor between two fetches. A longer <c>Crawl-delay</c> in robots.txt wins.</summary>
    public TimeSpan MinimumInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether a human has actually written to whoever runs this site.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Flipping this to <c>true</c> is a statement of fact about the world, not a configuration
    /// convenience. Spec §7.6: "a short email first is both the decent move and the one most likely
    /// to get better data than scraping would".
    /// </para>
    /// <para>
    /// <b>It only ever gates a scrape</b>, and that is the whole of its remaining purpose now that
    /// one source has passed it. A bulk export or a documented API is a route its owner published for
    /// the purpose of being read, and needs no permission asked; walking a site page by page is a
    /// stranger's traffic on a hobbyist's server and does. So the field stays false — and the source
    /// stays registered, credited, and refused at the moment of fetching — for every directory we
    /// have not yet approached. <c>docs/import-sources.md</c> records which ones those are and what
    /// each is waiting on.
    /// </para>
    /// </remarks>
    public bool ContactedMaintainer { get; init; }

    /// <summary>Read before the first content fetch, always.</summary>
    public required Uri RobotsUri { get; init; }

    /// <summary>
    /// A sentence for the about page saying what this source contributed and on what terms.
    /// </summary>
    public string? AttributionNote { get; init; }
}

/// <summary>
/// Who we say we are when we read somebody's site.
/// </summary>
/// <remarks>
/// Spec §11 requires the crawler to self-identify with an info URL so an admin reading their logs can
/// discover who we are and how to opt out. An importer reading a directory's access log is the same
/// obligation over HTTP, and this is the one place the string is built.
/// </remarks>
public static class ImporterIdentity
{
    /// <summary>One crawler, one page about it. The telnet probe's TTYPE points at the same URL.</summary>
    public const string InfoUrl = "https://muindex.org/crawler";

    public const string Product = "MUIndex";

    public static string UserAgent => $"{Product}-Importer/1.0 (+{InfoUrl})";

    /// <summary>Whether a user agent names us and says where to read about us.</summary>
    public static bool SelfIdentifies(string userAgent)
    {
        ArgumentNullException.ThrowIfNull(userAgent);

        return userAgent.Contains(InfoUrl, StringComparison.Ordinal)
            && userAgent.Contains(Product, StringComparison.Ordinal);
    }
}

/// <summary>Thrown when a fetch would break the etiquette in <see cref="ImportEtiquette"/>.</summary>
public sealed class EtiquetteViolationException(string message) : Exception(message);
