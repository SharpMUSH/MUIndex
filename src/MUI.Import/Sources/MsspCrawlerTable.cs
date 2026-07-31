using System.Globalization;

namespace MUI.Import.Sources;

/// <summary>
/// One MUD's block from the TinTin++ MSSP crawler page: its field/value pairs, the links inside the
/// block, and which of its values the crawler itself marked malformed.
/// </summary>
public sealed record MsspCrawlerRecord(
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
/// The reader for the TinTin++ MSSP crawler's mudlist page: a full-width box-drawing table rendered
/// to HTML from a terminal capture.
/// </summary>
/// <remarks>
/// <para>
/// The page is one box per MUD. Inside a box every line is <c>│</c>, a hundred and twenty columns of
/// content, <c>│</c>, and each half of those columns is a right-aligned label in seventeen cells
/// followed by a right-aligned value in the rest — two field/value pairs per line. Reading it as
/// fixed-width rather than by splitting on runs of spaces is not fussiness: <c>United States of
/// America</c>, <c>Merc 2.2</c> and <c>XTERM 256 COLORS</c> all contain spaces, and a split-based
/// reader loses a different part of each.
/// </para>
/// <para>
/// <b>The crawler's own verdict on a value is carried in colour, and is honoured.</b> The page's
/// legend defines bright red as invalid data, and 57 of the values on the day this was written are
/// painted in it — <c>LOCATION "US"</c> against a country-name taxonomy, <c>AREAS "50+"</c> against a
/// number, an out-of-range <c>UPTIME</c>. Importing a value the source itself marked malformed, at
/// the bottom of the precedence ladder with no marker on it, would be relaying a source's data minus
/// the source's own caveat, so those values are dropped.
/// </para>
/// </remarks>
public static class MsspCrawlerTable
{
    /// <summary>Bright red in the page's legend: <c>## Invalid data</c>.</summary>
    public const string InvalidColour = "#F55";

    private const char Left = '│';
    private const char TopLeft = '┌';
    private const char BottomLeft = '└';

    private const int Inner = 120;
    private const int HalfWidth = 60;
    private const int LabelWidth = 17;

    /// <summary>Every box on the page, in order.</summary>
    public static IReadOnlyList<MsspCrawlerRecord> Read(string document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var records = new List<MsspCrawlerRecord>();

        Dictionary<string, string>? fields = null;
        Dictionary<string, string>? fieldLinks = null;
        List<string>? links = null;
        HashSet<string>? invalid = null;

        foreach (var line in HtmlText.RenderLines(document))
        {
            var text = line.Text;

            if (text.StartsWith(TopLeft))
            {
                fields = new Dictionary<string, string>(StringComparer.Ordinal);
                fieldLinks = new Dictionary<string, string>(StringComparer.Ordinal);
                links = [];
                invalid = new HashSet<string>(StringComparer.Ordinal);

                continue;
            }

            if (text.StartsWith(BottomLeft))
            {
                if (fields is not null && fields.Count > 0)
                {
                    records.Add(new MsspCrawlerRecord(fields, fieldLinks!, links!, invalid!));
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
                var valueStart = labelStart + LabelWidth;
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

            // "07 Jul 2025 19:25 EST" — the zone is a US abbreviation rather than an offset, and
            // .NET will not parse it. Reading the date and treating the abbreviation as its own fixed
            // offset is exact for the two the page uses and honest about being a fixed offset.
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

    private static TimeSpan? OffsetOf(string zone) => zone switch
    {
        "EST" => TimeSpan.FromHours(-5),
        "EDT" => TimeSpan.FromHours(-4),
        "UTC" or "GMT" => TimeSpan.Zero,
        _ => null,
    };

    /// <summary>
    /// MSSP's array notation as this page flattens it: <c>PORT "23" "7777"</c> arrives as
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
}
