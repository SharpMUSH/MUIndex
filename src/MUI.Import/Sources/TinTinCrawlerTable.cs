using System.Globalization;

namespace MUI.Import.Sources;

/// <summary>
/// One MUD's block from a TinTin++ crawler page: its field/value pairs, the links inside the block,
/// and which of its values the crawler itself marked malformed.
/// </summary>
public sealed record TinTinCrawlerRecord(
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyDictionary<string, string> FieldLinks,
    IReadOnlyList<string> Links,
    IReadOnlySet<string> FlaggedInvalid)
{
    /// <summary>The value of a field, or null when it is absent, blank, or flagged invalid.</summary>
    public string? Value(string field)
    {
        if (FlaggedInvalid.Contains(field))
        {
            return null;
        }

        return Fields.TryGetValue(field, out var value) && value.Length > 0 ? value : null;
    }

    /// <summary>
    /// The URL a field's own cell links to, or null.
    /// </summary>
    /// <remarks>
    /// <c>WEBSITE</c> renders as an anchor whose text is the game's name, so the cell's text is
    /// "4Dimensions" and the URL exists only in the <c>href</c>. Asked per field rather than per
    /// record because a line carries two cells and one of them may be an <c>ICON</c> whose link is a
    /// PNG.
    /// </remarks>
    public string? Link(string field) =>
        !FlaggedInvalid.Contains(field) && FieldLinks.TryGetValue(field, out var link) ? link : null;
}

/// <summary>
/// The reader for the TinTin++ crawler mudlists — a full-width box-drawing table rendered to HTML
/// from a terminal capture. The same site publishes one for MSSP and one for MSDP, and they differ
/// only in how wide the label column is.
/// </summary>
/// <remarks>
/// <para>
/// The page is one box per MUD. Inside a box every line is <c>│</c>, a hundred and twenty columns of
/// content, <c>│</c>, and each half of those columns is a right-aligned label in
/// <see cref="MsspLabelWidth"/> or <see cref="MsdpLabelWidth"/> cells followed by a right-aligned
/// value in the rest — two field/value pairs per line. Reading it as fixed-width rather than by
/// splitting on runs of spaces is not fussiness: <c>United States of America</c>, <c>Merc 2.2</c> and
/// <c>XTERM 256 COLORS</c> all contain spaces, and a split-based reader loses a different part of
/// each.
/// </para>
/// <para>
/// <b>The label width is a required argument and has no default.</b> The MSSP page gives a label 17
/// cells and the MSDP page gives it 25, because <c>CONFIGURABLE_VARIABLES</c> does not fit in 17. A
/// reader that guesses wrong does not fail — it reads every label and every value off by eight
/// columns and yields a page of truncated nonsense, which is exactly the class of quiet mis-parse the
/// id-keyed reading in <see cref="MudStatsSource"/> exists to avoid.
/// </para>
/// <para>
/// <b>The crawler's own verdict on a value is carried in colour, and is honoured.</b> The page's
/// legend defines bright red as invalid data, and 57 of the values on the MSSP page on the day this
/// was written are painted in it — <c>LOCATION "US"</c> against a country-name taxonomy,
/// <c>AREAS "50+"</c> against a number, an out-of-range <c>UPTIME</c>. Importing a value the source
/// itself marked malformed, at the bottom of the precedence ladder with no marker on it, would be
/// relaying a source's data minus the source's own caveat, so those values are dropped.
/// </para>
/// <para>
/// <b>A frame must be the full width of the page, and that is a guard rather than tidiness.</b> Both
/// pages interleave the crawler's own boxes with the <em>connect screens</em> of the games it dialled
/// — text a game controls completely. A reader that opened a record on any line merely beginning with
/// <c>┌</c> would let a game draw a box in its own login banner and have the fields inside it read as
/// a record; pointed at another game's <c>HOSTNAME</c> and <c>PORT</c>, that is a fabricated player
/// count attached to somebody else's listing. Every genuine frame this crawler emits is exactly
/// <c>Inner</c> + 2 cells wide and closes with the matching corner.
/// </para>
/// </remarks>
public static class TinTinCrawlerTable
{
    /// <summary>Bright red in the page's legend: <c>## Invalid data</c>.</summary>
    public const string InvalidColour = "#F55";

    /// <summary>The label column on <c>protocols/mssp/mudlist.html</c>.</summary>
    public const int MsspLabelWidth = 17;

    /// <summary>
    /// The label column on <c>protocols/msdp/mudlist.html</c>, which is wider because
    /// <c>CONFIGURABLE_VARIABLES</c> is twenty-two characters long.
    /// </summary>
    public const int MsdpLabelWidth = 25;

    private const char Left = '│';
    private const char TopLeft = '┌';
    private const char TopRight = '┐';
    private const char BottomLeft = '└';
    private const char BottomRight = '┘';

    private const int Inner = 120;
    private const int HalfWidth = 60;

    /// <summary>Every box on the page, in order.</summary>
    public static IReadOnlyList<TinTinCrawlerRecord> Read(string document, int labelWidth)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfLessThan(labelWidth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(labelWidth, HalfWidth);

        var records = new List<TinTinCrawlerRecord>();

        Dictionary<string, string>? fields = null;
        Dictionary<string, string>? fieldLinks = null;
        List<string>? links = null;
        HashSet<string>? invalid = null;

        foreach (var line in HtmlText.RenderLines(document))
        {
            var text = line.Text;

            if (IsFrame(text, TopLeft, TopRight))
            {
                fields = new Dictionary<string, string>(StringComparer.Ordinal);
                fieldLinks = new Dictionary<string, string>(StringComparer.Ordinal);
                links = [];
                invalid = new HashSet<string>(StringComparer.Ordinal);

                continue;
            }

            if (IsFrame(text, BottomLeft, BottomRight))
            {
                if (fields is not null && fields.Count > 0)
                {
                    records.Add(new TinTinCrawlerRecord(fields, fieldLinks!, links!, invalid!));
                }

                fields = null;

                continue;
            }

            // A `├` inside a box is a rule between two parts of ONE record — the MSSP fields above and
            // the crawler's own averages below — and is deliberately not a boundary. Treating it as
            // one splits every MUD in half and loses the link that names it.
            if (fields is null || text.Length != Inner + 2 || !text.StartsWith(Left) || !text.EndsWith(Left))
            {
                continue;
            }

            links!.AddRange(line.Links);

            for (var half = 0; half < Inner; half += HalfWidth)
            {
                var labelStart = 1 + half;
                var valueStart = labelStart + labelWidth;
                var valueEnd = labelStart + HalfWidth;

                var label = text[labelStart..valueStart].Trim();
                if (label.Length == 0)
                {
                    continue;
                }

                var value = text[valueStart..valueEnd].Trim();
                fields[label] = value;

                if (line.LinksIn(valueStart, valueEnd).FirstOrDefault() is { } link)
                {
                    fieldLinks![label] = link;
                }

                if (value.Length > 0
                    && string.Equals(line.ColourOf(valueStart, valueEnd), InvalidColour, StringComparison.Ordinal))
                {
                    invalid!.Add(label);
                }
            }
        }

        return records;
    }

    /// <summary>
    /// The instant the page says it was generated, or null.
    /// </summary>
    /// <remarks>
    /// This is what makes the crawler's player counts importable at all. A snapshot with no stated
    /// moment yields no presence: dating somebody else's measurement to the moment we happened to
    /// read the page would put a fabricated timestamp into the day × hour heatmap.
    /// </remarks>
    public static DateTimeOffset? GeneratedAt(string document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var line in HtmlText.RenderLines(document))
        {
            var text = line.Text;
            var marker = text.IndexOf(" generated on ", StringComparison.Ordinal);
            if (marker < 0)
            {
                continue;
            }

            var tail = text[(marker + " generated on ".Length)..].TrimEnd('│', ' ', '.');

            // "07 Jul 2025 19:25 EST" — the zone is an abbreviation rather than an offset, and .NET
            // will not parse it. Reading the date and treating the abbreviation as its own fixed
            // offset is exact for the ones these pages use and honest about being a fixed offset.
            var space = tail.LastIndexOf(' ');
            if (space < 0)
            {
                continue;
            }

            var stamp = tail[..space].Trim();
            var zone = tail[(space + 1)..].Trim();

            if (DateTime.TryParseExact(stamp, "dd MMM yyyy HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed)
                && OffsetOf(zone) is { } offset)
            {
                return new DateTimeOffset(parsed, offset);
            }
        }

        return null;
    }

    /// <summary>
    /// MSSP's array notation as these pages flatten it: <c>PORT "23" "7777"</c> arrives as
    /// <c>23, 7777</c>. Every value that is a port is kept; anything else is dropped rather than
    /// coerced.
    /// </summary>
    public static IReadOnlyList<int> Ports(string? value)
    {
        if (value is null)
        {
            return [];
        }

        var ports = new List<int>();

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, CultureInfo.InvariantCulture, out var port)
                && port is >= 1 and <= 65535
                && !ports.Contains(port))
            {
                ports.Add(port);
            }
        }

        return ports;
    }

    /// <summary>
    /// The zone abbreviations these pages have actually been observed to emit, and nothing else.
    /// </summary>
    /// <remarks>
    /// An unrecognised abbreviation yields null, which costs the whole page its presence rows — and
    /// that is the right trade. Guessing an offset from a three-letter abbreviation puts a reading in
    /// the wrong hour of the day-of-week × hour heatmap, and several of them are ambiguous across
    /// hemispheres (<c>CST</c> is North American Central, Chinese Standard and Cuban Standard time).
    /// Add one when a page is seen using it.
    /// </remarks>
    private static TimeSpan? OffsetOf(string zone) => zone switch
    {
        "EST" => TimeSpan.FromHours(-5),
        "EDT" => TimeSpan.FromHours(-4),

        // The MSDP mudlist stamps itself in central European time.
        "CET" => TimeSpan.FromHours(1),
        "CEST" => TimeSpan.FromHours(2),

        "UTC" or "GMT" => TimeSpan.Zero,
        _ => null,
    };

    /// <summary>A full-width frame line, corner to matching corner.</summary>
    private static bool IsFrame(string text, char left, char right) =>
        text.Length == Inner + 2 && text[0] == left && text[^1] == right;
}
