using System.Globalization;
using System.Runtime.CompilerServices;

using MUI.Catalog.Persistence;

namespace MUI.Import.Sources;

/// <summary>
/// The TinTin++ MSSP crawler's mudlist — the best day-one seed available, and a measured one.
/// </summary>
/// <remarks>
/// <para>
/// Spec §10 names the TinTin mudlist outright as a seed source. This is the page the same site
/// publishes from its own MSSP crawler, and it is <see cref="ImportTier.Measured"/> rather than
/// asserted for the reason §7.6 gives: the site actively probes. Every entry on it is a host somebody
/// else's crawler <em>connected to</em> and negotiated MSSP with, which is worth strictly more than a
/// hand-maintained list of addresses — and it is also the population this project's probe is designed
/// around, since the same site publishes the MSSP specification.
/// </para>
/// <para>
/// <b>What is measured and what is merely relayed are not the same, within one page.</b> That TinTin
/// reached the host, and the player count it read, are its measurements. The contents of the MSSP
/// reply — <c>GENRE</c>, <c>CODEBASE</c>, <c>ANSI 1</c> — are the game's own declarations, relayed. So
/// capabilities land under <c>capability.x.declared</c> and never <c>.measured</c>: the field name
/// says the game claimed it, and the <c>imported_measured</c> source says a third party is who told
/// us.
/// </para>
/// <para>
/// <b>No availability spans.</b> The page is a snapshot with one generation timestamp, so it can say
/// the host answered at an instant and cannot say for how long. A zero-width interval would credit no
/// grace and clutter the series; inventing a span around it would credit grace we did not measure.
/// The presence sample, which is dated by that same timestamp, is imported.
/// </para>
/// <para>
/// Read live on the day this was written: 115 entries, 144 endpoints, 114 dated player counts, 88
/// codebases and 69 websites. <c>tools/live-tintin-import</c> is how that was measured and is how to
/// measure it again.
/// </para>
/// </remarks>
public sealed class TinTinMsspCrawlerSource(IDirectoryFetcher fetcher) : DirectorySource(fetcher)
{
    public const string Name = "TinTin++ MSSP Mud Crawler";

    private static readonly Uri Listing = new("https://tintin.mudhalla.net/protocols/mssp/mudlist.html");

    /// <summary>
    /// The MSSP variables worth carrying, mapped to <see cref="FieldRegistry"/> names.
    /// </summary>
    /// <remarks>
    /// <c>PLAYERS</c> is absent on purpose: a count is not a <c>GameField</c> (§5.2), and it is read
    /// as presence below. <c>UPTIME</c> is absent for the same reason — the registry declares it but
    /// nothing stores it as a field, and this page renders it in days rather than as MSSP's epoch
    /// second anyway. The crawler's own <c>ACTIVE PLAYERS</c> and <c>AVERAGE UPTIME</c> averages are
    /// absent because they are TinTin's derived statistics rather than facts about the game in this
    /// site's vocabulary, and importing a derived average as a field would launder one into the other.
    /// </remarks>
    private static readonly string[] DescriptiveFields =
    [
        "CRAWL DELAY", "HOSTNAME", "CODEBASE", "CONTACT", "CREATED", "ICON", "LANGUAGE", "LOCATION",
        "MINIMUM AGE", "FAMILY", "GENRE", "GAMEPLAY", "GAMESYSTEM", "INTERMUD", "STATUS", "SUBGENRE",
    ];

    /// <summary>
    /// Capabilities the page reports, in this site's own spelling. Each becomes
    /// <c>capability.x.declared</c> — the game said so; nobody here watched it happen.
    /// </summary>
    private static readonly string[] DeclaredCapabilities =
    [
        "ANSI", "XTERM 256 COLORS", "MCCP", "MCP", "MSP", "MXP", "VT100",
    ];

    public override string SourceName => Name;

    public override ImportTier Tier => ImportTier.Measured;

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
        BulkExportUri = Listing,
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

    public override async IAsyncEnumerable<ImportedGame> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var document = await Fetcher.GetStringAsync(Listing, cancellationToken).ConfigureAwait(false);

        foreach (var game in Parse(document))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return game;
        }
    }

    /// <summary>The whole of the parsing, with no I/O in it, so a fixture exercises all of it.</summary>
    public static IReadOnlyList<ImportedGame> Parse(string document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var generatedAt = MsspCrawlerTable.GeneratedAt(document);
        var games = new List<ImportedGame>();

        foreach (var record in MsspCrawlerTable.Read(document))
        {
            if (Read(record, generatedAt) is { } game)
            {
                games.Add(game);
            }
        }

        return games;
    }

    private static ImportedGame? Read(MsspCrawlerRecord record, DateTimeOffset? generatedAt)
    {
        var ports = MsspCrawlerTable.Ports(record.Value("PORT"));
        if (ports.Count == 0)
        {
            return null;
        }

        // Two candidate addresses, and they are not the same kind of thing. The crawl key in the
        // record's own link is the address TinTin dialled — a measurement. HOSTNAME is what the game
        // declared in its MSSP reply — an assertion, absent for 48 of the 115 entries on the day this
        // was written, and disagreeing with the dialled address for another 10. Both are worth
        // probing, because a crawl target is a candidate that becomes a game by answering (§7.2), so
        // both are seeded; the dialled one leads because it is the one somebody demonstrated works.
        var dialled = CrawlKeyHost(record.Links);
        var declared = record.Value("HOSTNAME");
        var hosts = new List<string>();

        foreach (var host in new[] { dialled, declared })
        {
            if (host is { Length: > 0 } && !hosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                hosts.Add(host);
            }
        }

        // The resolved IP is a LAST resort and never an addition. It is the same machine as the name
        // above it, and the crawler serialises per host — so seeding both would put two probes at one
        // server at once, under two spellings, for ever. A name also survives a move; an address does
        // not, which is the whole reason §5.5 keeps endpoints plural and historical.
        if (hosts.Count == 0 && record.Value("IP") is { Length: > 0 } address)
        {
            hosts.Add(address);
        }

        if (hosts.Count == 0)
        {
            return null;
        }

        var endpoints = new List<ImportedEndpoint>();
        foreach (var host in hosts)
        {
            foreach (var port in ports)
            {
                endpoints.Add(new ImportedEndpoint(host, port, EndpointKind.Telnet));
            }
        }

        var name = record.Value("NAME") ?? hosts[0];
        var fields = ReadFields(record);

        return new ImportedGame
        {
            SourceName = Name,
            SourceKey = $"{hosts[0]}:{ports[0]}",
            Name = name,
            SourceUri = Listing,
            Endpoints = endpoints,
            Fields = fields,
            Presence = ReadPresence(record, generatedAt),
        };
    }

    private static Dictionary<string, string> ReadFields(MsspCrawlerRecord record)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in DescriptiveFields)
        {
            if (record.Value(field) is { } value)
            {
                fields[field] = value;
            }
        }

        if (record.Value("NAME") is { } declaredName)
        {
            fields["NAME"] = declaredName;
        }

        // The website's text on this page is the link's anchor text — the game's name again — and the
        // URL is only in the href, so reading the cell stores "4Dimensions" as a WEBSITE. Asked of
        // the WEBSITE cell specifically rather than of the record, because a line carries two cells
        // and one game's ICON sits beside its WEBSITE with a PNG in it.
        foreach (var linked in new[] { "WEBSITE", "ICON", "DISCORD" })
        {
            if (record.Link(linked) is { } url && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                fields[linked] = url;
            }
        }

        foreach (var capability in DeclaredCapabilities)
        {
            if (record.Value(capability) is { } claim && claim is "0" or "1")
            {
                fields[CapabilityFields.Declared(capability)] = claim;
            }
        }

        // MSSP states SSL as the TLS port rather than a flag, so a number here is a declaration that
        // TLS exists. It is recorded as the declared capability and NOT turned into a TLS endpoint:
        // nobody has connected to that port, and minting an endpoint from a claim is exactly the
        // measured-versus-declared confusion this project exists to avoid.
        if (record.Value("SSL") is { } ssl && int.TryParse(ssl, CultureInfo.InvariantCulture, out var tlsPort))
        {
            fields[CapabilityFields.Declared("SSL")] = tlsPort > 0 ? "1" : "0";
        }

        return fields;
    }

    private static IReadOnlyList<ImportedPresence> ReadPresence(
        MsspCrawlerRecord record,
        DateTimeOffset? generatedAt)
    {
        if (generatedAt is not { } at)
        {
            return [];
        }

        if (record.Value("PLAYERS") is not { } players
            || !int.TryParse(players, CultureInfo.InvariantCulture, out var count)
            || count < 0)
        {
            // Unparseable, absent, or flagged invalid by the crawler itself. No row: a count we could
            // not read is not zero (§5.4), and an unmeasurable reason belongs to a probe of ours
            // rather than to somebody else's page.
            return [];
        }

        return [new ImportedPresence(at, count)];
    }

    /// <summary>
    /// The host out of the record's own <c>mud/&lt;host&gt;_&lt;port&gt;.html</c> link — the address
    /// the crawler dialled, which for 48 of the entries is the only address on the page.
    /// </summary>
    private static string? CrawlKeyHost(IReadOnlyList<string> links)
    {
        const string prefix = "mud/";
        const string suffix = ".html";

        foreach (var link in links)
        {
            if (!link.StartsWith(prefix, StringComparison.Ordinal)
                || !link.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var key = link[prefix.Length..^suffix.Length];
            var underscore = key.LastIndexOf('_');

            if (underscore > 0)
            {
                return key[..underscore];
            }
        }

        return null;
    }
}
