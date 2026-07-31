using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using MUI.Catalog.Persistence;

namespace MUI.Import.Sources;

/// <summary>
/// The Mud Connector — the oldest and largest hand-maintained list in the hobby, and an asserted one.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ImportTier.Asserted"/>, which spec §7.6 names it as by name. That is not a slight on
/// the site; it is what the tier means. TMC does connect — its Big List carries a per-game
/// <c>Connect Status</c> column reading <c>Connected</c>, <c>Connect Refused</c> or <c>N/A</c>, which
/// no submission form produces — but <b>nothing on the page says when that connection was
/// attempted</b>. A measurement whose age is unknown is not importable as a measurement: dating it to
/// the moment we read the page is the exact fabrication §5.2 forbids, and treating it as timeless is
/// worse. So the status is read and discarded, and what is taken is addresses, names and websites.
/// </para>
/// <para>
/// The same applies to its player counts, twice over. The only number on a listing is
/// <c># Players Online</c>, and it is a submission-form bucket — <c>100+</c>, linking to "other muds
/// with a similar # players" — on a listing whose own header may say it was last updated in 2008.
/// Neither a count nor dated.
/// </para>
/// <para>
/// <b>One request, for the whole list.</b> The site publishes its entire catalogue as a single page,
/// so this is <see cref="ImportEtiquette.BulkExportUri"/> rather than a scrape and the
/// contacted-maintainer gate does not apply — that gate exists to stop us walking a site page by
/// page, and this walks nothing. It is worth being precise about the claim: this is not a data dump
/// the maintainer prepared for machines, it is a listing page they publish for people, and the reason
/// it is treated as an export is arithmetic — 689 games for one GET. <b>The per-game listing pages
/// are deliberately never fetched.</b> They would turn one request into seven hundred and add, for
/// each, an undated status and a bucketed player count that this importer would refuse anyway.
/// </para>
/// </remarks>
public sealed partial class MudConnectorSource(IDirectoryFetcher fetcher) : DirectorySource(fetcher)
{
    public const string Name = "The Mud Connector";

    /// <summary>The whole catalogue, on one page, in one request.</summary>
    private static readonly Uri BigList =
        new("https://www.mudconnect.com/cgi-bin/search.cgi?mode=mobile_biglist");

    /// <summary>
    /// One row, with attributes tolerated on the tag. A regex demanding a bare <c>&lt;tr&gt;</c> does
    /// not fail visibly when the site adds a <c>class</c> — it reads the list as empty, which is
    /// indistinguishable from a site that is down.
    /// </summary>
    [GeneratedRegex(@"<tr\b[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 4000)]
    private static partial Regex Row { get; }

    /// <summary>The address TMC records for a game, inside the link it offers to connect with.</summary>
    [GeneratedRegex(@"telnet://([A-Za-z0-9._\-]+):(\d{1,5})", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex TelnetAddress { get; }

    /// <summary>The anchor whose href is the game's own TMC listing; its text is the game's name.</summary>
    [GeneratedRegex(@"<a[^>]*mode=mud_listing[^>]*>(?<name>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex ListingLink { get; }

    /// <summary>The site's outbound redirector, whose <c>url</c> is the game's website.</summary>
    [GeneratedRegex(@"redirect\.cgi\?[^'""]*?url=(?<url>https?://[^'""&]+)", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex WebsiteLink { get; }

    public override string SourceName => Name;

    public override ImportTier Tier => ImportTier.Asserted;

    public static ImportEtiquette DefaultEtiquette() => new()
    {
        SourceName = Name,
        AttributionUri = new Uri("https://www.mudconnect.com/"),
        BulkExportUri = BigList,
        RobotsUri = new Uri("https://www.mudconnect.com/robots.txt"),
        UserAgent = ImporterIdentity.UserAgent,

        // One request per run, so the interval is a floor nothing reaches. It is stated anyway,
        // because the day somebody adds a second page to this source is the day it matters.
        MinimumInterval = TimeSpan.FromSeconds(15),
        AttributionNote =
            "Seed addresses and websites from The Mud Connector's Big List, which is hand-maintained "
            + "and contributes addresses only — no history, no player counts.",
    };

    public static MudConnectorSource Create(HttpClient http, TimeProvider time) =>
        new(new DirectoryFetcher(http, DefaultEtiquette(), time));

    public override async IAsyncEnumerable<ImportedGame> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var document = await Fetcher.GetStringAsync(BigList, cancellationToken).ConfigureAwait(false);

        foreach (var game in Parse(document))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return game;
        }
    }

    /// <summary>The whole of the parsing, with no I/O in it, so a fixture exercises all of it.</summary>
    /// <remarks>
    /// Bounded to the list's own table, because the page around it is a site: a sidebar of links, a
    /// news panel, a login modal. Read unbounded, a row is whatever happens to look like one.
    /// </remarks>
    public static IReadOnlyList<ImportedGame> Parse(string document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (HtmlText.ElementBodyById(document, "mobile-biglist") is not { } table)
        {
            return [];
        }

        var games = new List<ImportedGame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match row in Row.Matches(table))
        {
            if (Read(row.Groups[1].Value) is { } game && seen.Add(game.SourceKey))
            {
                games.Add(game);
            }
        }

        return games;
    }

    private static ImportedGame? Read(string row)
    {
        if (TelnetAddress.Match(row) is not { Success: true } address)
        {
            return null;
        }

        var host = address.Groups[1].Value;
        if (!int.TryParse(address.Groups[2].Value, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            return null;
        }

        var name = ListingLink.Match(row) is { Success: true } link
            ? HtmlText.Collapse(HtmlText.StripTags(link.Groups["name"].Value))
            : string.Empty;

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        if (name.Length > 0)
        {
            // TMC's own spelling of the game's name, which is what its maintainer typed. It is a
            // field like any other and sits at the bottom of the §5.1 ladder, under anything the
            // game says about itself.
            fields["NAME"] = name;
        }

        if (WebsiteLink.Match(row) is { Success: true } website)
        {
            fields["WEBSITE"] = website.Groups["url"].Value;
        }

        // The Connect Status cell is read by nothing. It is a real measurement with no reading time
        // attached, and there is no honest place to put one of those.
        return new ImportedGame
        {
            SourceName = Name,
            SourceKey = $"{host}:{port}",
            Name = name.Length > 0 ? name : host,
            SourceUri = BigList,
            Endpoints = [new ImportedEndpoint(host, port, EndpointKind.Telnet)],
            Fields = fields,
        };
    }
}
