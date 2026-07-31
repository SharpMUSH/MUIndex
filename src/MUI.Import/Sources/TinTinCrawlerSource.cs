using System.Globalization;
using System.Runtime.CompilerServices;

using MUI.Catalog.Persistence;

namespace MUI.Import.Sources;

/// <summary>
/// The shape common to both TinTin++ crawler mudlists — the MSSP one and the MSDP one.
/// </summary>
/// <remarks>
/// <para>
/// Both are <see cref="ImportTier.Measured"/> for the reason spec §7.6 gives: the site actively
/// probes. Every entry on either page is a host somebody else's crawler <em>connected to</em> and
/// negotiated a protocol with, which is worth strictly more than a hand-maintained list of addresses
/// — and it is also the population this project's probe is designed around, since the same site
/// publishes the MSSP specification.
/// </para>
/// <para>
/// <b>What is measured and what is merely relayed are not the same, within one page.</b> That TinTin
/// reached the host, and the player count it read, are its measurements. The contents of the reply —
/// <c>GENRE</c>, <c>CODEBASE</c>, <c>ANSI 1</c> — are the game's own declarations, relayed. So
/// capabilities land under <c>capability.x.declared</c> and never <c>.measured</c>: the field name
/// says the game claimed it, and the <c>imported_measured</c> source says a third party is who told
/// us.
/// </para>
/// <para>
/// <b>No availability spans, from either page.</b> Each is a snapshot with one generation timestamp,
/// so it can say the host answered at an instant and cannot say for how long. A zero-width interval
/// would credit no grace and clutter the series; inventing a span around it would credit grace we did
/// not measure. The presence sample, which is dated by that same timestamp, is imported.
/// </para>
/// </remarks>
public abstract class TinTinCrawlerSource : DirectorySource
{
    /// <summary>
    /// The fields worth carrying, mapped to <see cref="FieldRegistry"/> names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PLAYERS</c> is absent on purpose: a count is not a <c>GameField</c> (§5.2), and it is read
    /// as presence below. <c>UPTIME</c> is absent for the same reason — the registry declares it but
    /// nothing stores it as a field, and these pages render it in days rather than as MSSP's epoch
    /// second anyway.
    /// </para>
    /// <para>
    /// The crawler's own derived statistics are absent from both pages' worth of columns: the MSSP
    /// page's <c>ACTIVE PLAYERS</c> and <c>AVERAGE UPTIME</c> averages, and the MSDP page's
    /// <c>LISTS</c>, <c>COMMANDS</c>, <c>CONFIGURABLE_VARIABLES</c>, <c>REPORTABLE_VARIABLES</c> and
    /// <c>SENDABLE_VARIABLES</c> counts. They are facts about TinTin's reading rather than about the
    /// game in this site's vocabulary, and importing a derived statistic as a field would launder one
    /// into the other. One whitelist covers both pages; a field a page does not carry simply never
    /// appears.
    /// </para>
    /// </remarks>
    private static readonly string[] DescriptiveFields =
    [
        "CRAWL DELAY", "HOSTNAME", "CODEBASE", "CONTACT", "CREATED", "ICON", "LANGUAGE", "LOCATION",
        "MINIMUM AGE", "FAMILY", "GENRE", "GAMEPLAY", "GAMESYSTEM", "INTERMUD", "STATUS", "SUBGENRE",
    ];

    /// <summary>
    /// Capabilities the MSSP page reports, in that site's own spelling. Each becomes
    /// <c>capability.x.declared</c> — the game said so; nobody here watched it happen.
    /// </summary>
    /// <remarks>
    /// The MSDP page carries none of these and so contributes none. It is worth being explicit about
    /// what is <em>not</em> derived from it: a game answering MSDP is a capability TinTin observed
    /// rather than one the game declared, so neither name fits — <c>.declared</c> would be false and
    /// <c>.measured</c> is reserved for this project's own handshake (§5.1). Nothing is written.
    /// </remarks>
    private static readonly string[] DeclaredCapabilities =
    [
        "ANSI", "XTERM 256 COLORS", "MCCP", "MCP", "MSP", "MXP", "VT100",
    ];

    protected TinTinCrawlerSource(IDirectoryFetcher fetcher, Uri listing, int labelWidth)
        : base(fetcher)
    {
        Listing = listing ?? throw new ArgumentNullException(nameof(listing));
        LabelWidth = labelWidth;
    }

    /// <summary>The single published page this source reads.</summary>
    public Uri Listing { get; }

    /// <summary>How wide the label column is on this particular page.</summary>
    public int LabelWidth { get; }

    public sealed override ImportTier Tier => ImportTier.Measured;

    public sealed override async IAsyncEnumerable<ImportedGame> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var document = await Fetcher.GetStringAsync(Listing, cancellationToken).ConfigureAwait(false);

        foreach (var game in Parse(document, SourceName, Listing, LabelWidth))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return game;
        }
    }

    /// <summary>The whole of the parsing, with no I/O in it, so a fixture exercises all of it.</summary>
    public static IReadOnlyList<ImportedGame> Parse(string document, string sourceName, Uri listing, int labelWidth)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(sourceName);
        ArgumentNullException.ThrowIfNull(listing);

        var generatedAt = TinTinCrawlerTable.GeneratedAt(document);
        var games = new List<ImportedGame>();

        foreach (var record in TinTinCrawlerTable.Read(document, labelWidth))
        {
            if (Read(record, generatedAt, sourceName, listing) is { } game)
            {
                games.Add(game);
            }
        }

        return games;
    }

    private static ImportedGame? Read(
        TinTinCrawlerRecord record,
        DateTimeOffset? generatedAt,
        string sourceName,
        Uri listing)
    {
        var ports = TinTinCrawlerTable.Ports(record.Value("PORT"));
        if (ports.Count == 0)
        {
            return null;
        }

        // Two candidate addresses, and they are not the same kind of thing. The crawl key in the
        // record's own link is the address TinTin dialled — a measurement. HOSTNAME is what the game
        // declared in its reply — an assertion, absent for 48 of the 115 MSSP entries on the day this
        // was written, and disagreeing with the dialled address for another 10. Both are worth
        // probing, because a crawl target is a candidate that becomes a game by answering (§7.2), so
        // both are seeded; the dialled one leads because it is the one somebody demonstrated works.
        // The MSDP page carries no per-mud link at all, so there the declared name is all there is.
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

        return new ImportedGame
        {
            SourceName = sourceName,
            SourceKey = $"{hosts[0]}:{ports[0]}",
            Name = record.Value("NAME") ?? hosts[0],
            SourceUri = listing,
            Endpoints = endpoints,
            Fields = ReadFields(record),
            Presence = ReadPresence(record, generatedAt),
        };
    }

    private static Dictionary<string, string> ReadFields(TinTinCrawlerRecord record)
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

        // The website's text on these pages is the link's anchor text — the game's name again — and
        // the URL is only in the href, so reading the cell stores "4Dimensions" as a WEBSITE. Asked of
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
        TinTinCrawlerRecord record,
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
    /// the crawler dialled, which for 48 of the MSSP entries is the only address on the page. The MSDP
    /// page has no such link and yields null here.
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
