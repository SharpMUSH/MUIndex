using System.Buffers;
using System.Globalization;
using System.Text;

namespace MUI.Web.Components;

/// <summary>
/// Turns a captured connect screen into rows of styled runs, server-side.
/// </summary>
/// <remarks>
/// <para>
/// Foreign colour is <em>quoted, not hosted</em>. A game's SGR is arbitrary and will clash with any
/// palette, so the site does not try to absorb it: the parser resolves every indexed colour against
/// one locked 16-colour table that is identical in both themes, and the frame it renders into keeps
/// its own fixed black ground. Nothing bleeds either way — no colour from a game is ever sampled for
/// chrome, and no site colour tints the art.
/// </para>
/// <para>
/// The art is a fixed character grid, so it is laid out at exactly <see cref="Columns"/> columns and
/// never scaled. Longer lines wrap the way a terminal wraps them, and the wrapped rows are counted,
/// because the row count the caption states has to be the number of rows a reader would actually
/// scroll past.
/// </para>
/// </remarks>
public static class Ansi
{
    /// <summary>
    /// The grid the art was drawn for. Games assume it; scaling breaks the box-drawing. 80 clipped
    /// a real cluster of AresMUSH-family banners mid-glyph (measured against captured connect
    /// screens in prod, 2026-08-18); 100 clears that cluster's widest observed line (97) with a
    /// little headroom without absorbing the much longer lines that are cursor-addressed screens
    /// misread as one row, not honest width.
    /// </summary>
    public const int Columns = 100;

    /// <summary>Below this there is not enough screen to be worth a frame, so the hero collapses.</summary>
    public const int MinimumRows = 3;

    /// <summary>
    /// How many terminal cells one rune occupies: two, one, or none.
    /// </summary>
    /// <remarks>
    /// This is the arithmetic a terminal does, and it is per rune rather than per screen. A row of
    /// seventy-nine ASCII characters ending in one Han glyph is eighty-one cells wide, and no single
    /// flag over the whole screen can say so — a caption that halves everything because one wide
    /// rune appeared somewhere reports forty for that row, which is the wrong number for anybody
    /// trying to redraw it faithfully (i18n S4).
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
    /// The locked table, close to xterm's. Theme-independent by design: games assume a dark terminal
    /// — nearly all of them do — so a light page must not repaint their palette and call it fidelity.
    /// Only the sixteen indexed slots are ours to choose; 256-colour and truecolor pass through.
    /// </summary>
    private static readonly string[] Indexed16 =
    [
        "#000000", "#aa0000", "#00aa00", "#aa5500", "#0000aa", "#aa00aa", "#00aaaa", "#aaaaaa",
        "#555555", "#ff5555", "#55ff55", "#ffff55", "#5555ff", "#ff55ff", "#55ffff", "#ffffff",
    ];

    public static AnsiScreen Parse(string? raw, bool suppressedByOwner)
    {
        // The owner's choice is a fact about us, not about the game, so it is answered before
        // anything is parsed and never dressed up as an empty or broken screen.
        if (suppressedByOwner)
        {
            return new AnsiScreen(AnsiScreenState.Suppressed, [], 0);
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new AnsiScreen(AnsiScreenState.Absent, [], 0);
        }

        var rows = Layout(raw);
        var substantial = rows.Count(r => r.Text.Trim().Length > 0);

        // Blank, tiny or hostile: below three rows there is nothing to show and a frame around it
        // would be a layout hole pretending to be evidence.
        return substantial < MinimumRows
            ? new AnsiScreen(AnsiScreenState.TooSmall, rows, rows.Count)
            : new AnsiScreen(AnsiScreenState.Rendered, rows, rows.Count);
    }

    private static List<AnsiRow> Layout(string raw)
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
                    while (column < stop && column < Columns)
                    {
                        Append(" ", 1);
                    }

                    continue;
                }
            }

            // Control bytes a game emits for its own reasons — bell, backspace, form feed — are not
            // part of the picture and are dropped rather than rendered as replacement glyphs.
            if (!char.IsControl(c))
            {
                // A rune at a time, not a UTF-16 unit at a time. The width model beside this counts
                // terminal cells — two for a wide glyph, none for a combining mark — and a loop that
                // advanced once per char disagreed with it three ways: a CJK screen wrapped after
                // eighty runes, which is a hundred and sixty cells; an astral rune counted twice for
                // the two units it is stored in; and a combining mark claimed a cell it never draws
                // in. The wrap has to be where the terminal put it or the picture is not the one the
                // game sent.
                if (Rune.DecodeFromUtf16(raw.AsSpan(i), out var rune, out var consumed)
                    is OperationStatus.Done)
                {
                    Append(raw.AsSpan(i, consumed), CellWidth(rune));
                    i += consumed - 1;
                }
                else
                {
                    // Lone surrogate: not a rune, so it has no width the terminal agrees on. Kept
                    // as the byte the game sent rather than dropped or replaced (rule 5).
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

            // A wide glyph that will not fit in the last cell of a row is put on the next one, which
            // is what a terminal does: it does not split a character across the wrap.
            if (cells > 1 && column + cells > Columns)
            {
                FlushRow();
            }

            text.Append(glyph);
            column += cells;

            // Wrap exactly where a terminal would. The wrapped row is a real row and is counted as
            // one, because the reader has to scroll past it either way.
            if (column >= Columns)
            {
                FlushRow();
            }
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
            // Anything that is not CSI — a charset selection, a save-cursor — is consumed and
            // dropped. We are quoting a picture, not emulating a terminal.
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

        // Only SGR changes how the picture looks. Cursor moves and erases are the ones that would
        // need a screen buffer to honour, and honouring them badly is worse than ignoring them.
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
    /// Summed rune by rune rather than derived from a screen-wide flag: a row may mix a wide script
    /// with a narrow one, and that mixed row is precisely the one whose width a reader cannot guess.
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
/// There is no crop here any more, and the absence is the point. The frame used to show the first
/// twenty-four rows, offer the whole screen again under "show all N rows", and offer its text a third
/// time under "read as text" — three copies of the same box-drawing in one document, which a screen
/// reader walks three times to reach four lines of prose. The frame now renders every row once and
/// scrolls, and the text alternative below it is the only other copy.
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
    /// The widest row and not a screen-wide halving. <see cref="Ansi.Columns"/> is the grid the
    /// parser lays out to, in characters; this is what those characters cost a terminal, and on a
    /// screen that mixes scripts the two differ row by row.
    /// </remarks>
    public int CellColumns => Rows.Count == 0 ? 0 : Rows.Max(r => r.Cells);

    /// <summary>Whether any rune anywhere on the screen is drawn two cells wide.</summary>
    public bool HasWideRunes => Rows.Any(r => r.HasWideRunes);
}
