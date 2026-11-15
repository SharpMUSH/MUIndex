using MarkupString;
using MarkupString.Ansi;
using MarkupString.Ansi.Decoding;
using MarkupString.Decoding;
using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// One picture a connect screen draws, and the cells it draws it over.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds, and only one of them carries pixels. A picture sent <em>inline</em> — sixel, a Kitty
/// graphics transmission, an iTerm2 inline file — arrives as its bytes, decoded here, and
/// <see cref="Bytes"/> holds them. A picture sent over MXP is an <c>IMAGE</c> tag naming an address,
/// which is all <see cref="Address"/> holds: fetching it is the web tier's job, through the same gate
/// as a game's <c>ICON</c>, never the probe's.
/// </para>
/// <para>
/// The position is in the rewritten screen (<see cref="ScreenPictureReading.Banner"/>), whose cells
/// under the picture are blank: <see cref="Line"/> counts its <c>\n</c>-separated lines from 0 and
/// <see cref="Column"/> its cells from 0. Rows and columns, never pixels: the frame on the site draws
/// the picture over the cells the game laid out for it, at whatever size those cells are there.
/// </para>
/// </remarks>
/// <param name="Line">The screen line the picture's top row is on, from 0.</param>
/// <param name="Column">The cell the picture starts at, from 0.</param>
/// <param name="Columns">How many cells wide it is drawn.</param>
/// <param name="Rows">How many lines tall it is drawn.</param>
/// <param name="Address">The absolute address an MXP <c>IMAGE</c> named, or null for an inline picture.</param>
/// <param name="MediaType">What the decoder made of an inline picture's bytes, or null for an address.</param>
/// <param name="Bytes">An inline picture's bytes, or null for an address.</param>
public sealed record ScreenPicture(
    int Line,
    int Column,
    int Columns,
    int Rows,
    string? Address,
    string? MediaType,
    byte[]? Bytes);

/// <summary>A connect screen with its pictures taken out, and the pictures.</summary>
/// <param name="Banner">
/// The screen as text. Unchanged when it drew no picture; otherwise re-written by the decoder, with
/// each picture's cells left blank and its escape sequences gone.
/// </param>
public sealed record ScreenPictureReading(string Banner, IReadOnlyList<ScreenPicture> Pictures)
{
    /// <summary>A screen that drew nothing but text.</summary>
    public static ScreenPictureReading None(string banner) => new(banner, []);
}

/// <summary>
/// Reads the pictures out of a connect screen: sixel, Kitty and iTerm2 sent inline, and MXP's
/// <c>IMAGE</c> over a figure's cells.
/// </summary>
/// <remarks>
/// <para>
/// The three terminal protocols are read by MarkupString's decoder (<c>WithPictures()</c>), which
/// lays each picture over the cells it covers — a sixel's size in cells is a fact about its pixels
/// and the cell size, and the decoder answers it the way a terminal would. A screen with no such
/// sequence is never decoded: decoding writes the screen back out with its escape sequences
/// normalised, which is fine for a screen that carried a megabyte of base64 and not something to
/// inflict on the thousand that carry none, whose <c>banner_hash</c> would all move at once.
/// </para>
/// <para>
/// MXP's <c>IMAGE</c> is read here, by hand, and not by MarkupString's MXP decoder. The probe asks
/// TelnetNegotiationCore to decode MXP entities (<c>Watched.Mxp</c>), so by the time a line reaches
/// this the <c>&amp;lt;name&amp;gt;</c> of a login legend is <c>&lt;name&gt;</c> — and an MXP
/// decoder handed that text reads it as a tag and drops it. Only the tag is taken out; the blank
/// cells the server wrote after it for the picture stay, which is where the picture is drawn.
/// </para>
/// <para>
/// Only a picture laid out over cells is kept: a decoded one whose <see cref="PictureRow"/> starts
/// at its own left edge, or an <c>IMAGE</c> sized in cells (<c>W=6c H=3c</c>). A picture inline in a
/// line of text keeps the text a terminal shows in its place, since there is nowhere on the screen
/// to draw it.
/// </para>
/// <para>
/// Nothing here fetches or dials. An MXP address is passed on as the string the tag named.
/// </para>
/// </remarks>
public static partial class ScreenPictures
{
    /// <summary>The most pictures one screen is read for.</summary>
    /// <remarks>
    /// A ceiling, not a guess at what is common: the commonest number is one. Past eight a screen is
    /// a slideshow, and each picture is a row and a fetch.
    /// </remarks>
    public const int MaxPictures = 8;

    /// <summary>The most bytes one inline picture is kept at. Bigger is refused, never shrunk.</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>
    /// The width the decoder lays a screen out at, which is the widest grid the site's frame draws
    /// (<c>Ansi.MaximumColumns</c> in <c>MUI.Web</c>), so decoding never folds a line the frame would
    /// have kept whole.
    /// </summary>
    public const int Columns = 256;

    /// <summary>The tallest picture kept, in lines. A screen is not a scroll.</summary>
    public const int MaxRows = 100;

    /// <summary>
    /// The cell size a picture's pixels are measured against when the sequence gives no size in cells:
    /// the decoder's default, and a common terminal's.
    /// </summary>
    public const int CellWidth = 8;

    /// <inheritdoc cref="CellWidth"/>
    public const int CellHeight = 16;

    /// <summary>The media types kept. What the site serves; SVG is not among them and never will be.</summary>
    private static readonly HashSet<string> Served = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/gif", "image/jpeg", "image/webp",
    };

    private static readonly MarkupRegistry Ansi = MarkupRegistry.Empty.WithAnsi();

    /// <summary>
    /// The screen's pictures, and the screen with them taken out.
    /// </summary>
    /// <param name="banner">The connect screen as the probe read it.</param>
    /// <param name="mxp">
    /// Whether MXP was on for the session. Off, an <c>IMAGE</c> tag is text — a game that never
    /// negotiated MXP and prints <c>&lt;IMAGE&gt;</c> has printed a word.
    /// </param>
    public static ScreenPictureReading Read(string? banner, bool mxp)
    {
        if (string.IsNullOrEmpty(banner))
        {
            return ScreenPictureReading.None(banner ?? string.Empty);
        }

        var pictures = new List<ScreenPicture>();
        var text = CarriesInline(banner) ? Decode(banner, pictures) : banner;

        if (mxp && text.Contains("<IMAGE", StringComparison.OrdinalIgnoreCase))
        {
            text = Tags(text, pictures);
        }

        return pictures.Count == 0
            ? ScreenPictureReading.None(banner)
            : new ScreenPictureReading(text, pictures);
    }

    /// <summary>Whether the screen carries a terminal picture, before paying for a decode.</summary>
    /// <remarks>
    /// Any DCS rather than sixel's alone, because it is cheaper to over-match than to parse: a false
    /// positive costs one decode that finds nothing, and then the screen is returned untouched.
    /// </remarks>
    internal static bool CarriesInline(string banner) =>
        banner.Contains("\u001bP", StringComparison.Ordinal)
        || banner.Contains("\u001b_G", StringComparison.Ordinal)
        || banner.Contains("\u001b]1337;", StringComparison.Ordinal);

    /// <summary>
    /// Decodes the terminal pictures, adding each to <paramref name="pictures"/>, and returns the
    /// screen written back out with blank cells where they were — or the screen as it came, when it
    /// held none after all.
    /// </summary>
    private static string Decode(string banner, List<ScreenPicture> pictures)
    {
        // A cache per screen: the pictures are this screen's, and a shared one would hand a game the
        // bytes another game sent, which are the same picture read off somebody else's screen.
        var registry = DecoderRegistry.Empty.WithVt().WithPictures(new MemoryPictureCache());
        var decoder = registry.CreateDecoder(new DecoderOptions
        {
            Identity = new ClientIdentity { Columns = Columns, CellWidth = CellWidth, CellHeight = CellHeight },
        });

        // The decoder ends a line at \n and reads \r as a carriage return. The probe joined its lines
        // with \n alone, and a stray \r would send the rest of a line back over its own start.
        var lines = decoder.Feed(banner.Replace("\r", string.Empty, StringComparison.Ordinal) + "\n").ToList();
        var store = decoder.Pictures();
        var found = pictures.Count;
        var text = new List<string>(lines.Count);

        for (var line = 0; line < lines.Count; line++)
        {
            var content = lines[line].Content;

            foreach (var run in content.Runs)
            {
                foreach (var image in run.Markups.OfType<ImageMarkup>())
                {
                    if (pictures.Count < MaxPictures
                        && image.Row is { Row: 0, FirstColumn: 0, IsDescription: false } row
                        && Fits(row.Columns, row.Rows)
                        && store.TryGet(image, out var stored)
                        && Kept(stored) is { } bytes)
                    {
                        var column = content.Substring(0, run.Start).DisplayWidth;
                        pictures.Add(new ScreenPicture(line, column, row.Columns, row.Rows, null, stored.MediaType, bytes));
                    }
                }
            }

            text.Add(content.Render(MarkupFormat.Ansi, Ansi));
        }

        return pictures.Count == found ? banner : string.Join('\n', text);
    }

    /// <summary>
    /// An inline picture's bytes, or null when it is not one the site would serve.
    /// </summary>
    /// <remarks>
    /// The file as sent where there is one (iTerm2, a Kitty PNG), the decoder's PNG of the pixels
    /// where there is not (sixel). Refused rather than shrunk or re-encoded when too big: a picture
    /// rewritten on the way through is a different picture from the one the game sent.
    /// </remarks>
    private static byte[]? Kept(StoredPicture stored)
    {
        var bytes = stored.File.IsEmpty ? stored.Encoded : stored.File;

        return Served.Contains(stored.MediaType) && bytes.Length is > 0 and <= MaxBytes ? bytes.ToArray() : null;
    }

    private static bool Fits(int columns, int rows) => columns is > 0 and <= Columns && rows is > 0 and <= MaxRows;

    /// <summary>
    /// Takes each <c>IMAGE</c> tag sized in cells out of the screen, adding its address to
    /// <paramref name="pictures"/>, and leaves every other tag where it was.
    /// </summary>
    private static string Tags(string banner, List<ScreenPicture> pictures)
    {
        var lines = banner.Split('\n');

        for (var line = 0; line < lines.Length; line++)
        {
            lines[line] = ImageTag().Replace(lines[line], match =>
            {
                if (pictures.Count >= MaxPictures || Image(match.Groups["attributes"].Value) is not { } image)
                {
                    return match.Value;
                }

                var before = lines[line][..match.Index];
                var column = MarkupText.Plain(AnsiEscapeParser.Parse(MxpSignal.Strip(before)).ToPlainText()).DisplayWidth;
                pictures.Add(new ScreenPicture(line, column, image.Columns, image.Rows, image.Address, null, null));

                return string.Empty;
            });
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// The address and cells an <c>IMAGE</c> tag names, or null when it names no address we could
    /// fetch or no size in cells.
    /// </summary>
    /// <remarks>
    /// <c>&lt;IMAGE fname URL=dir W=6c H=3c&gt;</c>: the file name first and unnamed (or as
    /// <c>FName=</c>), the directory as <c>URL=</c>, sizes ending in <c>c</c> for cells. A file name
    /// with no directory has nothing to resolve against — the directory a client would use is the
    /// game's own, and the game did not say it here — so it is not an address.
    /// </remarks>
    internal static (string Address, int Columns, int Rows)? Image(string attributes)
    {
        string? file = null;
        string? directory = null;
        int? columns = null;
        int? rows = null;

        foreach (Match attribute in Attribute().Matches(attributes))
        {
            var name = attribute.Groups["name"].Success ? attribute.Groups["name"].Value : null;
            var value = attribute.Groups["quoted"].Success
                ? attribute.Groups["quoted"].Value
                : attribute.Groups["bare"].Value;

            switch (name?.ToUpperInvariant())
            {
                case null:
                    file ??= value;
                    break;
                case "FNAME":
                    file = value;
                    break;
                case "URL":
                    directory = value;
                    break;
                case "W":
                    columns = Cells(value);
                    break;
                case "H":
                    rows = Cells(value);
                    break;
            }
        }

        if (file is null || columns is not { } width || rows is not { } height || !Fits(width, height))
        {
            return null;
        }

        var address = Uri.TryCreate(file, UriKind.Absolute, out var absolute)
            ? absolute
            : directory is not null && Uri.TryCreate(directory.EndsWith('/') ? directory : directory + "/", UriKind.Absolute, out var root)
              && Uri.TryCreate(root, file, out var resolved)
                ? resolved
                : null;

        return address is { Scheme: "http" or "https" } ? (address.AbsoluteUri, width, height) : null;
    }

    /// <summary>A size in cells (<c>6c</c>), or null for one in pixels or anything else.</summary>
    private static int? Cells(string value) =>
        value.Length > 1
        && value[^1] is 'c' or 'C'
        && int.TryParse(value.AsSpan(0, value.Length - 1), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var cells)
            ? cells
            : null;

    [GeneratedRegex(@"<\s*IMAGE(?<attributes>(?:\s[^>]*)?)>", RegexOptions.IgnoreCase)]
    private static partial Regex ImageTag();

    // name=value, name="value", name='value', or a bare positional value.
    [GeneratedRegex(@"(?:(?<name>[A-Za-z]+)\s*=\s*)?(?:""(?<quoted>[^""]*)""|'(?<quoted>[^']*)'|(?<bare>[^\s""'=]+))")]
    private static partial Regex Attribute();
}
