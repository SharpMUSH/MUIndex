using System.Buffers;
using System.Globalization;
using System.Text;

namespace MUI.Web.Components;

/// <summary>
/// Turns a captured connect screen into rows of styled runs, server-side.
/// </summary>
/// <remarks>
/// Foreign colour is <em>quoted, not hosted</em>: indexed colours resolve against one locked
/// 16-colour table identical in both themes, and the frame keeps its own fixed black ground, so
/// nothing bleeds either way. The art is laid out at the width the screen itself was drawn for
/// (<see cref="Ansi.Grid"/>) and never scaled; wrapped rows are counted, since the caption's row
/// count has to match what a reader actually scrolls past.
/// </remarks>
public static class Ansi
{
    /// <summary>
    /// The grid a screen is laid out at when it asks for nothing wider — and the width anything
    /// wider than the screen's own grid is folded at. 80 clipped a real cluster of AresMUSH-family
    /// banners mid-glyph; 100 clears the widest line on 809 of the 865 screens we hold.
    /// </summary>
    public const int DefaultColumns = 100;

    /// <summary>
    /// The widest grid a screen can ask for. The widest art anyone has actually drawn us is 243
    /// columns; past 256 a screen is not wide, it is a horizontal scroll bar with a picture in it.
    /// </summary>
    public const int MaximumColumns = 256;

    /// <summary>Below this there is not enough screen to be worth a frame, so the hero collapses.</summary>
    public const int MinimumRows = 3;

    /// <summary>
    /// How many terminal cells one rune occupies: two, one, or none.
    /// </summary>
    /// <remarks>
    /// Per rune, not per screen (i18n S4): a row of seventy-nine ASCII characters ending in one Han
    /// glyph is eighty-one cells wide, and no single screen-wide flag can say so.
    /// </remarks>
    public static int CellWidth(Rune rune) => Rune.GetUnicodeCategory(rune) switch
    {
        // A combining mark is drawn onto the cell before it and claims none of its own.
        UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark => 0,
        _ => IsWide(rune) ? 2 : 1,
    };

    /// <summary>
    /// The East Asian Wide and Fullwidth ranges, which is what "two cells" means in practice.
    /// </summary>
    public static bool IsWide(Rune rune) => rune.Value switch
    {
        >= 0x1100 and <= 0x115f => true,        // Hangul Jamo initial consonants
        >= 0x2e80 and <= 0x303e => true,        // CJK radicals, Kangxi, CJK symbols
        >= 0x3041 and <= 0x33ff => true,        // kana, Hangul compatibility, CJK compatibility
        >= 0x3400 and <= 0x4dbf => true,        // CJK extension A
        >= 0x4e00 and <= 0x9fff => true,        // CJK unified ideographs
        >= 0xa000 and <= 0xa4cf => true,        // Yi
        >= 0xac00 and <= 0xd7a3 => true,        // Hangul syllables
        >= 0xf900 and <= 0xfaff => true,        // CJK compatibility ideographs
        >= 0xfe30 and <= 0xfe6f => true,        // CJK compatibility forms
        >= 0xff00 and <= 0xff60 => true,        // fullwidth forms
        >= 0xffe0 and <= 0xffe6 => true,        // fullwidth signs
        >= 0x20000 and <= 0x3fffd => true,      // CJK extensions B and beyond
        _ => false,
    };

    private const char Escape = '\u001b';

    /// <summary>
    /// The locked table, close to xterm's. Theme-independent by design: games assume a dark
    /// terminal, so a light page must not repaint their palette and call it fidelity. Only the
    /// sixteen indexed slots are ours to choose; 256-colour and truecolor pass through.
    /// </summary>
    private static readonly string[] Indexed16 =
    [
        "#000000", "#aa0000", "#00aa00", "#aa5500", "#0000aa", "#aa00aa", "#00aaaa", "#aaaaaa",
        "#555555", "#ff5555", "#55ff55", "#ffff55", "#5555ff", "#ff55ff", "#55ffff", "#ffffff",
    ];

    public static AnsiScreen Parse(string? raw, bool suppressedByOwner)
    {
        // The owner's choice is a fact about us, not the game — answered before parsing, never
        // dressed up as an empty or broken screen.
        if (suppressedByOwner)
        {
            return new AnsiScreen(AnsiScreenState.Suppressed, [], 0);
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new AnsiScreen(AnsiScreenState.Absent, [], 0);
        }

        var rows = Layout(raw, Grid(raw));
        var substantial = rows.Count(r => r.Text.Trim().Length > 0);

        // Below three rows there is nothing to show, and a frame around it would be a layout hole
        // pretending to be evidence.
        return substantial < MinimumRows
            ? new AnsiScreen(AnsiScreenState.TooSmall, rows, rows.Count)
            : new AnsiScreen(AnsiScreenState.Rendered, rows, rows.Count);
    }

    /// <summary>
    /// The width this screen was drawn for: the widest line another line comes near, folded at
    /// <see cref="DefaultColumns"/> below and <see cref="MaximumColumns"/> above.
    /// </summary>
    /// <remarks>
    /// The widest line alone is not it. A game that sends a paragraph unwrapped — one line of 1,444
    /// characters on a screen where nothing else passes 77 — is leaving the folding to the client,
    /// not declaring a width, and taking it at its word would drag every short line on that screen
    /// out behind a scroll bar. So the measurement walks down from the widest and keeps stepping
    /// over any line more than half again the next one below it: a width no other line comes near
    /// is one line's accident, and the first corroborated width is the grid.
    /// </remarks>
    internal static int Grid(string raw)
    {
        var widths = LineWidths(raw);

        if (widths.Count < 2)
        {
            // One line corroborates nothing — not even itself.
            return DefaultColumns;
        }

        widths.Sort(static (a, b) => b.CompareTo(a));

        var i = 0;
        while (i + 1 < widths.Count && widths[i] > widths[i + 1] * 3 / 2)
        {
            i++;
        }

        return Math.Clamp(widths[i], DefaultColumns, MaximumColumns);
    }

    /// <summary>
    /// Every drawn line's width in cells, blank lines left out: they draw nothing and so vouch for
    /// no width. Measured over the same runes <see cref="Layout"/> lays out, so the grid a screen
    /// asks for is one its own longest line fits inside.
    /// </summary>
    private static List<int> LineWidths(string raw)
    {
        var widths = new List<int>();
        var width = 0;
        var drawn = false;
        var ignored = SgrState.Default;

        void FlushLine()
        {
            if (drawn)
            {
                widths.Add(width);
            }

            width = 0;
            drawn = false;
        }

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];

            if (c == Escape)
            {
                i = ReadEscape(raw, i, ref ignored);
                continue;
            }

            switch (c)
            {
                case '\r':
                    continue;
                case '\n':
                    FlushLine();
                    continue;
                case '\t':
                    width = ((width / 8) + 1) * 8;
                    continue;
            }

            if (char.IsControl(c))
            {
                continue;
            }

            if (Rune.DecodeFromUtf16(raw.AsSpan(i), out var rune, out var consumed) is OperationStatus.Done)
            {
                width += CellWidth(rune);
                drawn |= !Rune.IsWhiteSpace(rune);
                i += consumed - 1;
            }
            else
            {
                width++;
                drawn = true;
            }
        }

        FlushLine();

        return widths;
    }

    private static List<AnsiRow> Layout(string raw, int columns)
    {
        var rows = new List<AnsiRow>();
        var runs = new List<AnsiRun>();
        var text = new StringBuilder();
        var style = SgrState.Default;
        var emitted = style;
        var column = 0;

        void FlushRun()
        {
            if (text.Length == 0)
            {
                return;
            }

            runs.Add(new AnsiRun(text.ToString(), emitted.Resolve()));
            text.Clear();
        }

        void FlushRow()
        {
            FlushRun();
            rows.Add(new AnsiRow(runs.ToArray()));
            runs = [];
            column = 0;
        }

        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];

            if (c == Escape)
            {
                i = ReadEscape(raw, i, ref style);
                continue;
            }

            switch (c)
            {
                case '\r':
                    continue;
                case '\n':
                    FlushRow();
                    continue;
                case '\t':
                {
                    var stop = ((column / 8) + 1) * 8;
                    while (column < stop && column < columns)
                    {
                        Append(" ", 1);
                    }

                    continue;
                }
            }

            // Control bytes a game emits for its own reasons — bell, backspace, form feed — are
            // dropped rather than rendered as replacement glyphs.
            if (!char.IsControl(c))
            {
                // A rune at a time, not a UTF-16 unit at a time: a loop advancing once per char
                // wrapped a CJK screen after eighty runes (a hundred and sixty cells), double-counted
                // an astral rune, and gave a combining mark a cell it never draws in.
                if (Rune.DecodeFromUtf16(raw.AsSpan(i), out var rune, out var consumed)
                    is OperationStatus.Done)
                {
                    Append(raw.AsSpan(i, consumed), CellWidth(rune));
                    i += consumed - 1;
                }
                else
                {
                    // Lone surrogate: no width the terminal agrees on. Kept as the byte the game
                    // sent rather than dropped or replaced (rule 5).
                    Append(raw.AsSpan(i, 1), 1);
                }
            }
        }

        FlushRow();

        // A screen that ends with a newline should not gain a phantom final row for it.
        while (rows.Count > 0 && rows[^1].Text.Length == 0)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return rows;

        void Append(ReadOnlySpan<char> glyph, int cells)
        {
            if (style != emitted)
            {
                FlushRun();
                emitted = style;
            }

            // The wrap happens when the next glyph arrives, not when the last cell is filled —
            // a terminal defers it the same way, and flushing eagerly gave every line that filled
            // the grid exactly a blank row after it. A wide glyph that won't fit in the last cell
            // moves whole for the same reason: a terminal does not split a character across a wrap.
            if (column + cells > columns)
            {
                FlushRow();
            }

            text.Append(glyph);
            column += cells;
        }
    }

    /// <summary>Consumes one escape sequence and returns the index of its last character.</summary>
    private static int ReadEscape(string raw, int start, ref SgrState style)
    {
        if (start + 1 >= raw.Length)
        {
            return start;
        }

        if (raw[start + 1] != '[')
        {
            // Not CSI — a charset selection, a save-cursor — consumed and dropped. We are quoting a
            // picture, not emulating a terminal.
            return start + 1;
        }

        var i = start + 2;
        var parameters = i;
        while (i < raw.Length && raw[i] is >= ' ' and <= '?')
        {
            i++;
        }

        if (i >= raw.Length)
        {
            return raw.Length - 1;
        }

        // Only SGR changes how the picture looks; honouring cursor moves and erases badly is worse
        // than ignoring them.
        if (raw[i] == 'm')
        {
            ApplySgr(raw.AsSpan(parameters, i - parameters), ref style);
        }

        return i;
    }

    private static void ApplySgr(ReadOnlySpan<char> parameters, ref SgrState style)
    {
        Span<int> codes = stackalloc int[32];
        var count = 0;

        foreach (var range in parameters.Split(';'))
        {
            if (count == codes.Length)
            {
                break;
            }

            var part = parameters[range];
            codes[count++] = part.Length == 0 || !int.TryParse(part, out var value) ? 0 : value;
        }

        if (count == 0)
        {
            style = SgrState.Default;
            return;
        }

        for (var i = 0; i < count; i++)
        {
            switch (codes[i])
            {
                case 0: style = SgrState.Default; break;
                case 1: style = style with { Bold = true }; break;
                case 2: style = style with { Faint = true }; break;
                case 4: style = style with { Underline = true }; break;
                case 7: style = style with { Inverse = true }; break;
                case 22: style = style with { Bold = false, Faint = false }; break;
                case 24: style = style with { Underline = false }; break;
                case 27: style = style with { Inverse = false }; break;
                case >= 30 and <= 37: style = style with { Foreground = Indexed16[codes[i] - 30] }; break;
                case 39: style = style with { Foreground = null }; break;
                case >= 40 and <= 47: style = style with { Background = Indexed16[codes[i] - 40] }; break;
                case 49: style = style with { Background = null }; break;
                case >= 90 and <= 97: style = style with { Foreground = Indexed16[codes[i] - 90 + 8], Bright = true }; break;
                case >= 100 and <= 107: style = style with { Background = Indexed16[codes[i] - 100 + 8] }; break;
                case 38 or 48:
                {
                    var target = codes[i];
                    var colour = ReadExtended(codes, count, ref i);
                    if (colour is not null)
                    {
                        style = target == 38
                            ? style with { Foreground = colour, Bright = true }
                            : style with { Background = colour };
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    /// Reads <c>5;n</c> or <c>2;r;g;b</c> after a 38 or 48, advancing past what it consumed.
    /// Truecolor and 256-colour pass through untouched — only the sixteen indexed slots are ours.
    /// </summary>
    private static string? ReadExtended(ReadOnlySpan<int> codes, int count, ref int i)
    {
        if (i + 1 >= count)
        {
            return null;
        }

        switch (codes[i + 1])
        {
            case 5 when i + 2 < count:
            {
                var n = codes[i + 2];
                i += 2;
                return Xterm256(n);
            }

            case 2 when i + 4 < count:
            {
                var (r, g, b) = (codes[i + 2], codes[i + 3], codes[i + 4]);
                i += 4;
                return $"#{Clamp(r):x2}{Clamp(g):x2}{Clamp(b):x2}";
            }

            default:
                return null;
        }

        static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
    }

    private static string Xterm256(int n)
    {
        if (n is >= 0 and < 16)
        {
            return Indexed16[n];
        }

        if (n is >= 232 and <= 255)
        {
            var grey = 8 + ((n - 232) * 10);
            return $"#{grey:x2}{grey:x2}{grey:x2}";
        }

        if (n is < 16 or > 255)
        {
            return Indexed16[7];
        }

        var index = n - 16;
        var levels = new[] { 0, 95, 135, 175, 215, 255 };
        var r = levels[index / 36];
        var g = levels[index / 6 % 6];
        var b = levels[index % 6];
        return $"#{r:x2}{g:x2}{b:x2}";
    }

    /// <summary>The parser's running state. Colours are resolved to CSS as they are set.</summary>
    private readonly record struct SgrState(
        string? Foreground,
        string? Background,
        bool Bold,
        bool Faint,
        bool Underline,
        bool Inverse,
        bool Bright)
    {
        public static readonly SgrState Default = new(null, null, false, false, false, false, false);

        /// <summary>
        /// Bold brightens an indexed foreground, which is what every game that draws with
        /// <c>ESC[1;32m</c> is relying on. It does not brighten a 256-colour or truecolor value.
        /// </summary>
        public AnsiStyle Resolve()
        {
            var fg = Foreground;
            if (Bold && !Bright && fg is not null)
            {
                var index = Array.IndexOf(Indexed16, fg);
                if (index is >= 0 and < 8)
                {
                    fg = Indexed16[index + 8];
                }
            }

            var bg = Background;
            if (Inverse)
            {
                (fg, bg) = (bg ?? Indexed16[0], fg ?? Indexed16[7]);
            }

            return new AnsiStyle(fg, bg, Bold, Underline, Faint);
        }
    }
}

/// <summary>What the page found when it went to quote a connect screen.</summary>
public enum AnsiScreenState
{
    /// <summary>There is a screen and it is worth showing.</summary>
    Rendered,

    /// <summary>The owner asked us not to republish it. Stated plainly, without editorial.</summary>
    Suppressed,

    /// <summary>Fewer than three rows carry anything. The hero collapses rather than framing nothing.</summary>
    TooSmall,

    /// <summary>Nothing was captured at all.</summary>
    Absent,
}

public sealed record AnsiStyle(string? Foreground, string? Background, bool Bold, bool Underline, bool Faint)
{
    public bool IsPlain => Foreground is null && Background is null && !Bold && !Underline && !Faint;

    public string Css
    {
        get
        {
            var parts = new List<string>(4);
            if (Foreground is not null)
            {
                parts.Add($"color:{Foreground}");
            }

            if (Background is not null)
            {
                parts.Add($"background:{Background}");
            }

            if (Bold)
            {
                parts.Add("font-weight:600");
            }

            if (Underline)
            {
                parts.Add("text-decoration:underline");
            }

            if (Faint)
            {
                parts.Add("opacity:.7");
            }

            return string.Join(';', parts);
        }
    }
}

public sealed record AnsiRun(string Text, AnsiStyle Style);

public sealed record AnsiRow(IReadOnlyList<AnsiRun> Runs)
{
    public string Text => string.Concat(Runs.Select(r => r.Text));

    /// <summary>
    /// The terminal cells this row occupies, which is not the number of characters in it.
    /// </summary>
    /// <remarks>
    /// Summed rune by rune, not a screen-wide flag — a row may mix a wide script with a narrow one.
    /// </remarks>
    public int Cells
    {
        get
        {
            var cells = 0;

            foreach (var run in Runs)
            {
                // EnumerateRunes yields a ref struct enumerator, so this is a loop rather than a
                // LINQ Sum — there is no IEnumerable here to hang one off.
                foreach (var rune in run.Text.EnumerateRunes())
                {
                    cells += Ansi.CellWidth(rune);
                }
            }

            return cells;
        }
    }

    /// <summary>Whether any rune in this row is drawn two cells wide.</summary>
    public bool HasWideRunes
    {
        get
        {
            foreach (var run in Runs)
            {
                foreach (var rune in run.Text.EnumerateRunes())
                {
                    if (Ansi.IsWide(rune))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}

/// <summary>
/// A parsed connect screen and the honest facts about its size.
/// </summary>
/// <remarks>
/// No crop: the frame renders every row once and scrolls, so a screen reader meets the art once and
/// the text alternative once — not three copies of the same box-drawing in one document.
/// </remarks>
public sealed record AnsiScreen(AnsiScreenState State, IReadOnlyList<AnsiRow> Rows, int RowCount)
{
    /// <summary>
    /// The text alternative. Colour codes are never announced — a screen reader is told what the
    /// screen says, not how it was painted.
    /// </summary>
    public string PlainText => string.Join('\n', Rows.Select(r => r.Text.TrimEnd()));

    /// <summary>
    /// The width this screen occupies in terminal cells: the widest row, measured cell by cell.
    /// </summary>
    /// <remarks>
    /// The widest row, not a screen-wide halving. <see cref="Ansi.Grid"/> is the layout grid in
    /// characters; this is what those characters cost a terminal, and the two differ row by row on
    /// a screen that mixes scripts.
    /// </remarks>
    public int CellColumns => Rows.Count == 0 ? 0 : Rows.Max(r => r.Cells);

    /// <summary>Whether any rune anywhere on the screen is drawn two cells wide.</summary>
    public bool HasWideRunes => Rows.Any(r => r.HasWideRunes);
}
