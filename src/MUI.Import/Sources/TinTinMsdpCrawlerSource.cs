namespace MUI.Import.Sources;

/// <summary>
/// The TinTin++ MSDP crawler's mudlist — the MSSP crawler's sibling, reaching the games that answer
/// MSDP.
/// </summary>
/// <remarks>
/// <para>
/// Same site, same crawler, same box-drawing page, same tier and the same entitlements — see
/// <see cref="TinTinCrawlerSource"/>. What it adds is a different population: MSDP is negotiated by a
/// partly different set of games from MSSP, so the two pages are not one list read twice. Read on the
/// day this was written it carried 44 readable entries, of which a handful appear on no other source
/// this project reads.
/// </para>
/// <para>
/// <b>Two differences from the MSSP page are worth knowing before changing anything here.</b> Its
/// label column is eight cells wider, because <c>CONFIGURABLE_VARIABLES</c> does not fit in
/// seventeen — read with the MSSP width it yields a page of truncated nonsense rather than an error.
/// And it carries no per-mud link, so the address TinTin <em>dialled</em> is not recoverable from it
/// and the game's own declared <c>HOSTNAME</c> is the only address there is. That is still worth
/// seeding, because a crawl target is a candidate that becomes a game by answering for itself (§7.2)
/// — but it is a weaker address than the MSSP page's, and it is why this source is registered after
/// that one.
/// </para>
/// <para>
/// <b>It is a snapshot and an old one</b> — the copy read while this was written was generated in
/// January 2024. That is not a reason to date its readings to today; it is the reason not to. Each
/// player count is imported at the instant the page states, which is where it belongs in the
/// day-of-week × hour heatmap, and a page whose stamp cannot be read yields no presence at all.
/// </para>
/// </remarks>
public sealed class TinTinMsdpCrawlerSource(IDirectoryFetcher fetcher)
    : TinTinCrawlerSource(fetcher, Page, TinTinCrawlerTable.MsdpLabelWidth)
{
    public const string Name = "TinTin++ MSDP Mud Crawler";

    private static readonly Uri Page = new("https://tintin.mudhalla.net/protocols/msdp/mudlist.html");

    public override string SourceName => Name;

    /// <summary>
    /// One published listing page, read as a bulk export, exactly as its MSSP sibling is.
    /// </summary>
    public static ImportEtiquette DefaultEtiquette() => new()
    {
        SourceName = Name,
        AttributionUri = new Uri("https://tintin.mudhalla.net/protocols/msdp/"),
        BulkExportUri = Page,
        RobotsUri = new Uri("https://tintin.mudhalla.net/robots.txt"),
        UserAgent = ImporterIdentity.UserAgent,
        MinimumInterval = TimeSpan.FromSeconds(15),
        AttributionNote =
            "Seed addresses and MSDP readings from the TinTin++ MSDP Mud Crawler, the sibling of the "
            + "MSSP crawler on the same site.",
    };

    public static TinTinMsdpCrawlerSource Create(HttpClient http, TimeProvider time) =>
        new(new DirectoryFetcher(http, DefaultEtiquette(), time));

    /// <summary>This page's parsing, with no I/O in it, so a fixture exercises all of it.</summary>
    public static IReadOnlyList<ImportedGame> Parse(string document) =>
        Parse(document, Name, Page, TinTinCrawlerTable.MsdpLabelWidth);
}
