using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using MUI.Catalog.Persistence;

namespace MUI.Import.Sources;

/// <summary>
/// MudStats — an index of world pages, each carrying an address, a status, a codebase version and a
/// player count with the age of the reading beside it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ImportTier.Measured"/>: the site pings. Spec §7.6 names it in the measured tier
/// explicitly, and its world pages say when each reading was taken, which is what makes the counts
/// importable rather than undatable.
/// </para>
/// <para>
/// <b>It is a scrape, and the gate on it has been satisfied rather than removed.</b> One index fetch
/// plus one fetch per world is a real crawl of somebody else's site, and there is no dump or API. So
/// the route is <see cref="ImportEtiquette.ScrapeUri"/>, and until this was written
/// <see cref="ImportEtiquette.ContactedMaintainer"/> defaulted to false and
/// <see cref="EtiquettePlanner"/> refused to run it at all. The site's maintainer has since been
/// approached and this project's owner has authorised the run, so the default is now true —
/// <em>for this source</em>. The mechanism stays exactly where it was for every directory nobody has
/// written to yet, and <c>docs/import-sources.md</c> records which is which.
/// </para>
/// <para>
/// <b>What the flip does not license is speed.</b> This is a small site run by one person, and every
/// other obligation in §7.6 is still enforced in code rather than remembered: the fifteen-second
/// floor below, the bound on how many world pages one run will fetch, the <c>User-Agent</c> that
/// names us with an info URL, the <c>robots.txt</c> read before the first content fetch, and the
/// attribution the registry derives for the about page. We have walked this catalogue once already;
/// nothing here is greedier for having been given permission.
/// </para>
/// <para>
/// <b>No availability spans, for the same reason as the TinTin crawler page.</b> A world page carries
/// a <c>Status:</c> at one moment, which says the site reached it then and says nothing about for how
/// long. A zero-width interval credits no grace and clutters the series; a span invented around it
/// credits grace nobody measured. The dated player count is imported; the status is not.
/// </para>
/// </remarks>
public sealed partial class MudStatsSource(
    IDirectoryFetcher fetcher,
    TimeProvider time,
    int maxWorlds = MudStatsSource.DefaultMaxWorlds) : DirectorySource(fetcher)
{
    public const string Name = "MudStats";

    /// <summary>
    /// A bound on how many world pages one run will fetch, because a scrape with no bound is a
    /// promise about somebody else's bandwidth that this code cannot keep.
    /// </summary>
    public const int DefaultMaxWorlds = 250;

    private static readonly Uri Site = new("https://mudstats.com/");

    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));

    private readonly int _maxWorlds = maxWorlds > 0
        ? maxWorlds
        : throw new ArgumentOutOfRangeException(nameof(maxWorlds));

    /// <summary>How many world pages this run will fetch at most.</summary>
    public int MaxWorlds => _maxWorlds;

    [GeneratedRegex(@"/World/([A-Za-z0-9%_.\-]+)", RegexOptions.None, 2000)]
    private static partial Regex WorldLink { get; }

    /// <summary><c>host port</c>, as the address cell spells it.</summary>
    [GeneratedRegex(@"^\s*([A-Za-z0-9._\-]+)\s+(\d{1,5})\s*$", RegexOptions.None, 2000)]
    private static partial Regex Address { get; }

    /// <summary>A leading count, then the age of the reading in a parenthesised phrase.</summary>
    [GeneratedRegex(@"^\s*(\d+)\s*\(([^)]*)\)", RegexOptions.None, 2000)]
    private static partial Regex CountAndAge { get; }

    [GeneratedRegex(@"^(\d+)\s+(second|minute|hour|day|week|month)s?\s+ago$", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex RelativeAge { get; }

    public override string SourceName => Name;

    public override ImportTier Tier => ImportTier.Measured;

    /// <param name="contactedMaintainer">
    /// True by default, because for this one source it is true in the world: the maintainer has been
    /// approached and the run authorised. It stays a parameter so a test can assert what the gate
    /// does rather than trusting that it still exists.
    /// </param>
    public static ImportEtiquette DefaultEtiquette(bool contactedMaintainer = true) => new()
    {
        SourceName = Name,
        AttributionUri = Site,
        ScrapeUri = Site,
        RobotsUri = new Uri(Site, "robots.txt"),
        UserAgent = ImporterIdentity.UserAgent,

        // Fifteen seconds between page fetches. At 250 worlds that is an hour of very light traffic
        // rather than a burst, which is what "rate-limit hard where scraping is the only option"
        // means when the alternative is 250 requests in ten seconds.
        MinimumInterval = TimeSpan.FromSeconds(15),
        ContactedMaintainer = contactedMaintainer,
        AttributionNote = "World addresses, status and player counts from MudStats.",
    };

    public static MudStatsSource Create(HttpClient http, TimeProvider time, bool contactedMaintainer = true) =>
        new(new DirectoryFetcher(http, DefaultEtiquette(contactedMaintainer), time), time);

    public override async IAsyncEnumerable<ImportedGame> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var index = await Fetcher.GetStringAsync(Site, cancellationToken).ConfigureAwait(false);

        foreach (var slug in Slugs(index).Take(_maxWorlds))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var uri = new Uri(Site, $"World/{slug}");

            // The index links to worlds the site no longer has — /World/TheChattingZone answers
            // "404 No such world." — and one stale link is not a reason to abandon the other 143.
            if (await Fetcher.TryGetStringAsync(uri, cancellationToken).ConfigureAwait(false)
                is not { } page)
            {
                continue;
            }

            if (ReadWorld(slug, page, uri, SourceName) is not { } game)
            {
                continue;
            }

            // The reading's age is relative to the moment the page was fetched, which is now — so the
            // clock is read here, per page, and not once for the whole run.
            yield return ReadPresence(page, _time.GetUtcNow()) is { } sample
                ? game with { Presence = [sample] }
                : game;
        }
    }

    /// <summary>The world slugs the index links to, in order, deduplicated.</summary>
    public static IReadOnlyList<string> Slugs(string index)
    {
        ArgumentNullException.ThrowIfNull(index);

        var slugs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in WorldLink.Matches(index))
        {
            var slug = match.Groups[1].Value;
            if (slug.Length > 0 && seen.Add(slug))
            {
                slugs.Add(slug);
            }
        }

        return slugs;
    }

    /// <summary>
    /// One world page, parsed. No I/O, so a recorded fixture exercises all of it.
    /// </summary>
    /// <remarks>
    /// <b>Every value is read by element id, never by its label.</b> The page renders a field as
    /// <c>&lt;strong&gt;Address:&lt;/strong&gt;</c> inside one <c>div</c> and its value inside a
    /// sibling — so the obvious regex, anchored on the label, captures the whitespace between two
    /// closing tags and yields an empty string for every field on the page. The ids are stable and
    /// are what this reads.
    /// </remarks>
    public static ImportedGame? ReadWorld(string slug, string page, Uri uri, string sourceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrEmpty(sourceName);

        var address = HtmlText.ElementTextById(page, "value-address");
        if (address is null || Address.Match(address) is not { Success: true } parsed)
        {
            return null;
        }

        var host = parsed.Groups[1].Value;
        var port = int.Parse(parsed.Groups[2].Value, CultureInfo.InvariantCulture);
        if (port is < 1 or > 65535)
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        // "Version" is this site's word for what MSSP calls CODEBASE, and it is the game's own
        // self-report relayed rather than anything MudStats measured.
        if (Value(page, "display-version") is { } version)
        {
            fields["CODEBASE"] = version;
        }

        if (HtmlText.ElementLinksById(page, "links")
                .FirstOrDefault(link => link.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            is { } website)
        {
            fields["WEBSITE"] = website;
        }

        return new ImportedGame
        {
            SourceName = sourceName,
            SourceKey = slug,

            // The page's own title, not the slug: "4 Dimensions" rather than "4Dimensions". The slug
            // stays as the source key, because that is what a re-import must recognise.
            Name = Value(page, "title") ?? slug,
            SourceUri = uri,
            Endpoints = [new ImportedEndpoint(host, port, EndpointKind.Telnet)],
            Fields = fields,
        };
    }

    /// <summary>
    /// The presence reading on a world page, dated by the "(9 minutes ago)" phrase beside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="ReadWorld"/> and taking <paramref name="readAt"/> explicitly, because
    /// the page dates its reading <em>relatively</em> and a relative date is only a date if you know
    /// when you read the page. That makes the clock an argument rather than a hidden
    /// <c>DateTimeOffset.UtcNow</c>, which is also what makes it testable.
    /// </para>
    /// <para>
    /// No phrase, no sample. A count with no stated age would have to be dated to the import instant,
    /// and a fabricated timestamp in the day × hour heatmap is a fact about the game that nobody
    /// measured.
    /// </para>
    /// </remarks>
    public static ImportedPresence? ReadPresence(string page, DateTimeOffset readAt)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (Value(page, "value-playersconnected") is not { } cell
            || CountAndAge.Match(cell) is not { Success: true } match)
        {
            return null;
        }

        if (!int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var count) || count < 0)
        {
            return null;
        }

        if (Ago(match.Groups[2].Value.Trim()) is not { } age)
        {
            return null;
        }

        return new ImportedPresence(readAt - age, count);
    }

    private static string? Value(string page, string id)
    {
        var text = HtmlText.ElementTextById(page, id);

        // "(Unknown)" is this page's spelling of "we have no value", and it is not a value.
        return text is { Length: > 0 } and not "(Unknown)" ? text : null;
    }

    private static TimeSpan? Ago(string phrase)
    {
        if (RelativeAge.Match(phrase) is not { Success: true } match
            || !int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var quantity))
        {
            return null;
        }

        return match.Groups[2].Value.ToLowerInvariant() switch
        {
            "second" => TimeSpan.FromSeconds(quantity),
            "minute" => TimeSpan.FromMinutes(quantity),
            "hour" => TimeSpan.FromHours(quantity),
            "day" => TimeSpan.FromDays(quantity),
            "week" => TimeSpan.FromDays(7 * quantity),

            // Deliberately absent: "month". A month is not a duration, and rounding one to 30 days
            // puts a reading in the wrong week of the heatmap. A reading that old is worth nothing
            // anyway.
            _ => null,
        };
    }
}
