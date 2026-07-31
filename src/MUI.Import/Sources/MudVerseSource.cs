using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using MUI.Catalog.Persistence;

namespace MUI.Import.Sources;

/// <summary>
/// MudVerse — an hourly MSSP crawl with the reading time printed beside the reading.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ImportTier.Measured"/>, and spec §7.6 names it in that tier explicitly. The site says
/// so itself: "Our crawler runs roughly once an hour and will check the connectivity of each game in
/// our database. It will also send an MSSP request to each game and log the results." What makes it
/// worth more than that claim is that each game page carries <em>Connection Tested</em> and
/// <em>Last Successful Connection</em> to the minute, in GMT — so a count read off it is dated by
/// the source rather than by us.
/// </para>
/// <para>
/// <b>Measured and asserted are separated on the page itself, and the two disagree.</b> The "Game
/// Details" panel is what the owner typed into a submission form — its player count is a dropdown
/// bucket like <c>0-5</c> — and the MSSP panel is what the crawler read. Only the MSSP panel is
/// imported. Nothing from the owner-submitted panel is, which is why a bucketed count cannot leak in.
/// </para>
/// <para>
/// <b>It is a scrape, and it is gated.</b> Enumerating the catalogue costs one request to the
/// sitemap the site advertises in its own <c>robots.txt</c>; reading it costs one request per game,
/// which is a real crawl of a hobbyist's server. There is no dump and no documented API, so the route
/// is <see cref="ImportEtiquette.ScrapeUri"/> and <see cref="ImportEtiquette.ContactedMaintainer"/>
/// is <b>false</b>: nobody has written to whoever runs the site, so <see cref="EtiquettePlanner"/>
/// refuses to run it and <see cref="ImportRunner"/> says why. That refusal is the point. It is also
/// the working example of what the contacted-maintainer gate is still for now that MudStats has
/// passed it — one short email is the whole of what stands between this file and 273 dated readings.
/// </para>
/// <para>
/// <b>What is deliberately not imported.</b> The site publishes a per-game daily series at
/// <c>/json.php?listing_id=…</c> going back to November 2022, titled "Average Players Online Per
/// Day". It is not imported and must not be: a day's <em>average</em> is a derived statistic, and a
/// <c>PresenceSample</c> is a count somebody read at an instant (§5.2). Placing an average at the
/// midnight the bucket is keyed to would put a number nobody measured into that hour of the
/// day-of-week × hour heatmap and launder a derivation into a measurement. Availability spans are
/// not imported either, for the reason both TinTin pages give: two instants — tested, and last
/// successful — say the host answered then and say nothing about for how long.
/// </para>
/// </remarks>
public sealed partial class MudVerseSource(
    IDirectoryFetcher fetcher,
    int maxGames = MudVerseSource.DefaultMaxGames) : DirectorySource(fetcher)
{
    public const string Name = "MudVerse";

    /// <summary>
    /// A bound on how many game pages one run will fetch, because a scrape with no bound is a promise
    /// about somebody else's bandwidth that this code cannot keep. The catalogue held 273 games when
    /// this was written.
    /// </summary>
    public const int DefaultMaxGames = 400;

    private static readonly Uri Site = new("https://www.mudverse.com/");

    /// <summary>The enumeration route the site advertises in its own <c>robots.txt</c>.</summary>
    private static readonly Uri Sitemap = new("https://www.mudverse.com/sitemap.xml");

    /// <summary>
    /// MSSP variables worth carrying, in this site's spelling, mapped to <see cref="FieldRegistry"/>
    /// names. <c>PLAYERS</c> is absent because a count is not a field (§5.2) and is read as presence;
    /// <c>UPTIME</c> because this page renders it as "598 hours" rather than as MSSP's epoch second;
    /// <c>PORT</c> because the connection panel already carries it as an address. <c>CONTACT</c> is
    /// absent because this page renders it as an anchor labelled "E-mail", so the only value
    /// recoverable from it is the <c>mailto:</c> URL — a different shape from the address the other
    /// sources carry, and not one worth writing a second parse for to store a game's inbox.
    /// <c>HOSTNAME</c> <em>is</em> carried, as the other sources carry it: it is the game's own claim
    /// about where it lives, it sits at the bottom of the §5.1 ladder like any other imported field,
    /// and where it disagrees with the address this directory publishes, that disagreement is the
    /// interesting fact and must not be dropped.
    /// </summary>
    private static readonly string[] DescriptiveFields =
    [
        "CRAWL DELAY", "CODEBASE", "CREATED", "FAMILY", "GAMEPLAY", "GAMESYSTEM", "GENRE", "HOSTNAME",
        "INTERMUD", "LANGUAGE", "LOCATION", "MINIMUM AGE", "NAME", "STATUS", "SUBGENRE",
    ];

    /// <summary>
    /// Cells whose text is anchor text and whose value lives only in the <c>href</c>. Of these only
    /// the URLs are kept as fields — <c>CONTACT</c>'s <c>mailto:</c> is read so that it is not stored
    /// as the word "E-mail", and then dropped.
    /// </summary>
    private static readonly string[] Linked = ["WEBSITE", "ICON", "DISCORD", "CONTACT"];

    /// <summary>The subset of <see cref="Linked"/> stored, and stored as their URL.</summary>
    private static readonly string[] LinkedFields = ["WEBSITE", "ICON", "DISCORD"];

    /// <summary>Rendered <c>yes</c>/<c>no</c> by this site, and each the game's own claim.</summary>
    private static readonly string[] DeclaredCapabilities =
    [
        "ANSI", "ATCP", "GMCP", "MCCP", "MCP", "MSDP", "MSP", "MXP", "PUEBLO", "UTF-8", "VT100",
        "XTERM 256 COLORS",
    ];

    private readonly int _maxGames = maxGames > 0
        ? maxGames
        : throw new ArgumentOutOfRangeException(nameof(maxGames));

    [GeneratedRegex(@"<loc>\s*([^<\s]+)\s*</loc>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex SitemapLocation { get; }

    [GeneratedRegex(@"^https?://(?:www\.)?mudverse\.com/game/(\d+)/?$", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex GamePath { get; }

    /// <summary>
    /// One flat <c>&lt;tr&gt;&lt;td&gt;NAME&lt;/td&gt;&lt;td&gt;value&lt;/td&gt;&lt;/tr&gt;</c>, with
    /// attributes tolerated on the tags. A regex demanding bare tags does not fail visibly when the
    /// site adds a <c>class</c>: it reads the panel as empty, which this importer then reports as
    /// "this game has no MSSP data" — indistinguishable from a game that genuinely has none.
    /// </summary>
    [GeneratedRegex(@"<tr\b[^>]*>\s*<td\b[^>]*>(.*?)</td>\s*<td\b[^>]*>(.*?)</td>\s*</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex MsspRow { get; }

    /// <summary>A <c>fact-label</c> and the <c>fact-value</c> that immediately follows it.</summary>
    [GeneratedRegex(@"fact-label[""'][^>]*>(?<label>[^<]*)</span>\s*<span[^>]*fact-value[^>]*>(?<value>.*?)</span>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex LabelledFact { get; }

    /// <summary>"07/31/26 03:35 am GMT".</summary>
    [GeneratedRegex(@"^(\d{2})/(\d{2})/(\d{2})\s+(\d{1,2}):(\d{2})\s*(am|pm)\s*GMT$",
        RegexOptions.IgnoreCase, 2000)]
    private static partial Regex GmtStamp { get; }

    /// <summary>"MSSP Data - Crawled on 07/31/26 - What is MSSP?"</summary>
    [GeneratedRegex(@"Crawled on\s+(\d{2})/(\d{2})/(\d{2})", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex CrawledOn { get; }

    public override string SourceName => Name;

    public override ImportTier Tier => ImportTier.Measured;

    /// <param name="contactedMaintainer">
    /// False by default, and that is a statement of fact rather than a setting: nobody has written to
    /// MudVerse. Passing true is what a human does after they have.
    /// </param>
    public static ImportEtiquette DefaultEtiquette(bool contactedMaintainer = false) => new()
    {
        SourceName = Name,
        AttributionUri = Site,
        ScrapeUri = Site,
        RobotsUri = new Uri(Site, "robots.txt"),
        UserAgent = ImporterIdentity.UserAgent,

        // Fifteen seconds between pages, the same floor MudStats gets and for the same reason: at
        // this catalogue's size that is an hour of very light traffic rather than a burst.
        MinimumInterval = TimeSpan.FromSeconds(15),
        ContactedMaintainer = contactedMaintainer,
        AttributionNote =
            "Addresses, MSSP readings and dated player counts from MudVerse, which has crawled MUDs "
            + "and made MSSP requests since November 2022.",
    };

    /// <remarks>
    /// A <see cref="TimeProvider"/> only because the fetcher's rate limit needs a clock. Nothing in
    /// the parsing does: every reading this source yields is dated by the page it came from, which is
    /// the property that makes the site importable at all.
    /// </remarks>
    public static MudVerseSource Create(HttpClient http, TimeProvider time, bool contactedMaintainer = false) =>
        new(new DirectoryFetcher(http, DefaultEtiquette(contactedMaintainer), time));

    public override async IAsyncEnumerable<ImportedGame> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The sitemap rather than the paginated listing: it is one request for the whole catalogue,
        // it is the enumeration the site itself points a crawler at in robots.txt, and the listing
        // page defaults to showing only the games that are online — which would quietly make this
        // source's coverage a function of who happened to be up.
        var sitemap = await Fetcher.GetStringAsync(Sitemap, cancellationToken).ConfigureAwait(false);

        foreach (var listing in Listings(sitemap).Take(_maxGames))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var uri = new Uri(Site, $"game/{listing}");

            // A sitemap is generated ahead of the pages it names and outlives the ones that go. One
            // missing listing is not a reason to abandon the rest of the catalogue.
            if (await Fetcher.TryGetStringAsync(uri, cancellationToken).ConfigureAwait(false)
                is not { } page)
            {
                continue;
            }

            if (ReadGame(listing, page, uri, SourceName) is { } game)
            {
                yield return game;
            }
        }
    }

    /// <summary>The listing ids the sitemap names, in order, deduplicated.</summary>
    public static IReadOnlyList<string> Listings(string sitemap)
    {
        ArgumentNullException.ThrowIfNull(sitemap);

        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match location in SitemapLocation.Matches(sitemap))
        {
            if (GamePath.Match(location.Groups[1].Value.Trim()) is { Success: true } game
                && seen.Add(game.Groups[1].Value))
            {
                ids.Add(game.Groups[1].Value);
            }
        }

        return ids;
    }

    /// <summary>
    /// One game page, parsed. No I/O, so a recorded fixture exercises all of it.
    /// </summary>
    public static ImportedGame? ReadGame(string listingId, string page, Uri uri, string sourceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(listingId);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrEmpty(sourceName);

        // The address this directory publishes for the game, out of its connection panel. The
        // Connectivity block directly below the panel reports testing THIS host and port, which is
        // what makes it the address to lead with — and what stops the record being anchored on the
        // game's own declared HOSTNAME, which is kept as a field instead.
        if (HtmlText.ElementTextById(page, "detailsHost") is not { Length: > 0 } host
            || Port(page, "detailsPort") is not { } port)
        {
            return null;
        }

        var endpoints = new List<ImportedEndpoint> { new(host, port, EndpointKind.Telnet) };

        // A TLS port in the same panel. Be precise about what is and is not known here: the page
        // states one Connection Tested and one Last Successful Connection, both singular, so it does
        // NOT say this port was dialled — it says the directory publishes it as a way in. That is a
        // candidate address, and §7.2 is exactly what makes a candidate safe: it becomes a game by
        // answering for itself, so seeding it costs one probe and asserts nothing.
        //
        // It is a different thing from the TinTin sources' MSSP `SSL`, which is refused: that is a
        // number inside a GAME's own protocol reply, and minting an endpoint from a game's claim
        // about itself is the measured-versus-declared confusion this project exists to avoid.
        if (Port(page, "detailsTlsPort") is { } tls && tls != port)
        {
            endpoints.Add(new ImportedEndpoint(host, tls, EndpointKind.Tls));
        }

        var mssp = Mssp(page);
        var fields = ReadFields(mssp);

        return new ImportedGame
        {
            SourceName = sourceName,
            SourceKey = listingId,
            Name = HtmlText.ElementTextById(page, "detailsName") is { Length: > 0 } title
                ? title
                : mssp.GetValueOrDefault("NAME") ?? host,
            SourceUri = uri,
            Endpoints = endpoints,
            Fields = fields,
            Presence = ReadPresence(page, mssp),
        };
    }

    /// <summary>
    /// The player count on a game page, dated by the connection the reading came from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The page states three things about time: the moment connectivity was last <em>tested</em>, the
    /// moment a connection last <em>succeeded</em>, and — to the day only — when the MSSP block below
    /// was crawled. The reading belongs to the last successful connection, because a failed test
    /// reads nothing; so that is what dates it.
    /// </para>
    /// <para>
    /// <b>And it is imported only when the MSSP block agrees it was crawled that day.</b> When the
    /// last test failed, or the game has stopped answering MSSP, the block on the page is older than
    /// the connection above it and there is nothing on the page saying how much older. Dating a stale
    /// count to a fresh connection would be an invention, and the day-level heading is exactly enough
    /// to notice it and refuse.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ImportedPresence> ReadPresence(
        string page,
        IReadOnlyDictionary<string, string> mssp)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(mssp);

        if (!mssp.TryGetValue("PLAYERS", out var players)
            || !int.TryParse(players, CultureInfo.InvariantCulture, out var count)
            || count < 0)
        {
            // Absent or unreadable. §5.4: a count we could not read is not a zero.
            return [];
        }

        if (Fact(page, "Last Successful Connection") is not { } stamp || Instant(stamp) is not { } at)
        {
            return [];
        }

        if (CrawledOn.Match(HtmlText.ElementTextById(page, "black-link") ?? string.Empty)
            is not { Success: true } crawled)
        {
            return [];
        }

        var day = new DateOnly(
            2000 + int.Parse(crawled.Groups[3].Value, CultureInfo.InvariantCulture),
            int.Parse(crawled.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(crawled.Groups[2].Value, CultureInfo.InvariantCulture));

        return DateOnly.FromDateTime(at.UtcDateTime) == day ? [new ImportedPresence(at, count)] : [];
    }

    /// <summary>
    /// The MSSP panel's variables, as the crawler read them. Empty when the page says it has none.
    /// </summary>
    /// <remarks>
    /// Bounded to the panel that carries them, because this page uses one table class for two
    /// unrelated tables: a game's MSSP readings and its player reviews. Read by class alone, a game
    /// with reviews contributes its review titles as MSSP variables.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Mssp(string page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        if (HtmlText.ElementBodyByClass(page, "mssp-data") is not { } panel)
        {
            return values;
        }

        foreach (Match row in MsspRow.Matches(panel))
        {
            var name = HtmlText.Collapse(HtmlText.StripTags(row.Groups[1].Value));
            if (name.Length == 0 || string.Equals(name, "MSSP Variable", StringComparison.Ordinal))
            {
                continue;
            }

            var cell = row.Groups[2].Value;

            // CONTACT, WEBSITE, ICON and DISCORD render as an anchor whose text is a label —
            // "E-mail", "Website", "Icon Link" — so the value exists only in the href. Reading the
            // cell stores the word "Website" as a game's website.
            //
            // Named, rather than "prefer an href wherever there is one": the day the site links a
            // CODEBASE or a LOCATION to a search page, a preference would silently turn that field
            // into a URL, and a field quietly becoming the wrong kind of thing is worse than one
            // that stops being read.
            var value = Linked.Contains(name, StringComparer.Ordinal) && HrefIn(cell) is { Length: > 0 } href
                ? href
                : HtmlText.Collapse(HtmlText.StripTags(cell));

            if (value.Length > 0)
            {
                values[name] = value;
            }
        }

        return values;
    }

    private static Dictionary<string, string> ReadFields(IReadOnlyDictionary<string, string> mssp)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in DescriptiveFields)
        {
            if (mssp.TryGetValue(name, out var value) && value.Length > 0)
            {
                fields[name] = value;
            }
        }

        foreach (var name in LinkedFields)
        {
            if (mssp.TryGetValue(name, out var url)
                && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                fields[name] = url;
            }
        }

        // The game's claim, relayed — never `.measured`, which belongs to a handshake of our own
        // (§5.1). This site renders the claim as the words the MSSP spec renders as 1 and 0.
        foreach (var capability in DeclaredCapabilities)
        {
            if (mssp.TryGetValue(capability, out var claim) && Flag(claim) is { } declared)
            {
                fields[CapabilityFields.Declared(capability)] = declared;
            }
        }

        return fields;
    }

    private static string? Flag(string value) => value.Trim().ToLowerInvariant() switch
    {
        "yes" or "1" or "true" => "1",
        "no" or "0" or "false" => "0",

        // Anything else — "Sometimes", "Restricted", a blank — is not a boolean and is not coerced
        // into one. A capability claim we could not read is absent, not denied.
        _ => null,
    };

    private static int? Port(string page, string id) =>
        HtmlText.ElementTextById(page, id) is { } text
        && int.TryParse(text.Trim(), CultureInfo.InvariantCulture, out var port)
        && port is >= 1 and <= 65535
            ? port
            : null;

    /// <summary>The value beside a <c>fact-label</c> with this text.</summary>
    private static string? Fact(string page, string label)
    {
        foreach (Match fact in LabelledFact.Matches(page))
        {
            if (string.Equals(HtmlText.Collapse(fact.Groups["label"].Value), label, StringComparison.Ordinal))
            {
                return HtmlText.Collapse(HtmlText.StripTags(fact.Groups["value"].Value));
            }
        }

        return null;
    }

    /// <summary>"07/31/26 03:35 am GMT" — month, day, two-digit year, in GMT as the page says.</summary>
    private static DateTimeOffset? Instant(string stamp)
    {
        if (GmtStamp.Match(stamp) is not { Success: true } match)
        {
            return null;
        }

        var hour = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) % 12;
        if (string.Equals(match.Groups[6].Value, "pm", StringComparison.OrdinalIgnoreCase))
        {
            hour += 12;
        }

        try
        {
            return new DateTimeOffset(
                2000 + int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                hour,
                int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
                0,
                TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            // A stamp that is not a date is not a date. No reading rather than a guessed one.
            return null;
        }
    }

    private static string? HrefIn(string markup) =>
        Regex.Match(markup, @"href\s*=\s*['""]([^'""]*)['""]", RegexOptions.None, TimeSpan.FromSeconds(2))
            is { Success: true } match
            ? match.Groups[1].Value
            : null;
}
