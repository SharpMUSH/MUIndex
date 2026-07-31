namespace MUI.Import.Sources;

/// <summary>
/// The TinTin++ MSSP crawler's mudlist — the best day-one seed available, and a measured one.
/// </summary>
/// <remarks>
/// <para>
/// Spec §10 names the TinTin mudlist outright as a seed source. This is the page the same site
/// publishes from its own MSSP crawler; see <see cref="TinTinCrawlerSource"/> for why it is
/// <see cref="ImportTier.Measured"/> and what it may and may not contribute.
/// </para>
/// <para>
/// Read live on the day this was written: 115 entries, 144 endpoints, 114 dated player counts, 88
/// codebases and 69 websites. <c>tools/live-import</c> is how that was measured and is how to measure
/// it again.
/// </para>
/// </remarks>
public sealed class TinTinMsspCrawlerSource(IDirectoryFetcher fetcher)
    : TinTinCrawlerSource(fetcher, Page, TinTinCrawlerTable.MsspLabelWidth)
{
    public const string Name = "TinTin++ MSSP Mud Crawler";

    private static readonly Uri Page = new("https://tintin.mudhalla.net/protocols/mssp/mudlist.html");

    public override string SourceName => Name;

    /// <summary>
    /// A single published listing page, read as a bulk export.
    /// </summary>
    /// <remarks>
    /// It is <see cref="ImportEtiquette.BulkExportUri"/> rather than a scrape because it is exactly
    /// that: one static page the maintainer generates for the purpose of being read, and reading it
    /// costs the site one request per import rather than one per game. That is also why no
    /// contacted-maintainer gate applies — the gate exists to stop us walking a site that never
    /// offered us a dump.
    /// </remarks>
    public static ImportEtiquette DefaultEtiquette() => new()
    {
        SourceName = Name,
        AttributionUri = new Uri("https://tintin.mudhalla.net/protocols/mssp/"),
        BulkExportUri = Page,
        RobotsUri = new Uri("https://tintin.mudhalla.net/robots.txt"),
        UserAgent = ImporterIdentity.UserAgent,

        // One request per run, so the interval barely matters — but it is the floor a second page
        // would be fetched behind, and it is generous on purpose.
        MinimumInterval = TimeSpan.FromSeconds(15),
        AttributionNote =
            "Seed addresses and MSSP readings from the TinTin++ MSSP Mud Crawler, which publishes the "
            + "MSSP specification this site's probe implements.",
    };

    public static TinTinMsspCrawlerSource Create(HttpClient http, TimeProvider time) =>
        new(new DirectoryFetcher(http, DefaultEtiquette(), time));

    /// <summary>This page's parsing, with no I/O in it, so a fixture exercises all of it.</summary>
    public static IReadOnlyList<ImportedGame> Parse(string document) =>
        Parse(document, Name, Page, TinTinCrawlerTable.MsspLabelWidth);
}
