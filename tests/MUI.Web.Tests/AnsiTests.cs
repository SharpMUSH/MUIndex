using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// The ANSI quotation frame's parser: foreign colour resolved to a locked table, laid out at a
/// fixed 80 columns, cropped honestly, and with the cases the design names kept apart.
/// </summary>
public class AnsiTests
{
    private const string Esc = "\u001b[";

    private static string Screen(int rows) =>
        string.Join('\n', Enumerable.Range(1, rows).Select(i => $"line {i}"));

    [Test]
    public async Task IndexedColourResolvesToTheLockedTableAndNotToASiteColour()
    {
        // The frame's palette is theme-independent by design: games assume a dark terminal, and a
        // light page repainting their green would be fidelity in name only.
        var screen = Ansi.Parse($"{Esc}32mgreen{Esc}0m plain\nb\nc", suppressedByOwner: false);
        var runs = screen.Rows[0].Runs;

        await Assert.That(runs[0].Text).IsEqualTo("green");
        await Assert.That(runs[0].Style.Foreground).IsEqualTo("#00aa00");
        await Assert.That(runs[1].Style.Foreground).IsNull();
    }

    [Test]
    public async Task BoldBrightensAnIndexedForeground()
    {
        // Every game that draws with ESC[1;32m is relying on this, and a renderer that ignores it
        // makes half the connect screens on the internet look muddy.
        var screen = Ansi.Parse($"{Esc}1;32mbright{Esc}0m\nb\nc", suppressedByOwner: false);

        await Assert.That(screen.Rows[0].Runs[0].Style.Foreground).IsEqualTo("#55ff55");
    }

    [Test]
    public async Task TruecolorPassesThroughUntouched()
    {
        var screen = Ansi.Parse($"{Esc}38;2;18;52;86mrgb{Esc}0m\nb\nc", suppressedByOwner: false);

        await Assert.That(screen.Rows[0].Runs[0].Style.Foreground).IsEqualTo("#123456");
    }

    [Test]
    public async Task CursorMovesAndErasesAreDroppedRatherThanRendered()
    {
        // We are quoting a picture, not emulating a terminal. Honouring a cursor move badly is
        // worse than ignoring it.
        var screen = Ansi.Parse($"{Esc}2J{Esc}Hclean\nb\nc", suppressedByOwner: false);

        await Assert.That(screen.Rows[0].Text).IsEqualTo("clean");
    }

    [Test]
    public async Task LinesWrapAtEightyColumnsAndTheWrappedRowIsCounted()
    {
        // The art is a fixed character grid and the row count in the caption has to be the number
        // of rows a reader will actually scroll past.
        var screen = Ansi.Parse(new string('x', 200) + "\na\nb", suppressedByOwner: false);

        await Assert.That(screen.Rows[0].Text.Length).IsEqualTo(Ansi.Columns);
        await Assert.That(screen.RowCount).IsEqualTo(5);
    }

    [Test]
    public async Task AnOwnerSuppressionIsItsOwnStateAndNotAnEmptyScreen()
    {
        var screen = Ansi.Parse(Screen(40), suppressedByOwner: true);

        await Assert.That(screen.State).IsEqualTo(AnsiScreenState.Suppressed);
        await Assert.That(screen.Rows).IsEmpty();
    }

    [Test]
    public async Task UnderThreeRowsTheHeroCollapsesRatherThanFramingNothing()
    {
        var screen = Ansi.Parse(Screen(2), suppressedByOwner: false);

        await Assert.That(screen.State).IsEqualTo(AnsiScreenState.TooSmall);
    }

    [Test]
    public async Task NothingCapturedIsDistinctFromTooSmall()
    {
        // "We have never captured one" and "what came back was two lines" are different facts about
        // a game and the page says which.
        await Assert.That(Ansi.Parse(null, false).State).IsEqualTo(AnsiScreenState.Absent);
        await Assert.That(Ansi.Parse("   ", false).State).IsEqualTo(AnsiScreenState.Absent);
    }

    [Test]
    public async Task ALongScreenIsParsedWholeRatherThanCropped()
    {
        // There is no crop any more. The frame used to hold the first twenty-four rows, offer the
        // whole screen again under "show all N rows" and its text a third time under "read as text"
        // — three copies of one piece of box-drawing, and three passes through it for anybody
        // listening. One region, every row, and it scrolls.
        var screen = Ansi.Parse(Screen(214), suppressedByOwner: false);

        await Assert.That(screen.RowCount).IsEqualTo(214);
        await Assert.That(screen.Rows.Count).IsEqualTo(214);
    }

    [Test]
    public async Task AWidthIsCountedInTerminalCellsAndPerRowRatherThanPerScreen()
    {
        // The row that made a screen-wide flag wrong: seventy-eight ASCII characters and one Han
        // glyph. Seventy-nine characters, eighty cells — and the caption used to halve the whole
        // screen the moment any wide rune appeared anywhere, and report forty for this.
        var mixed = new string('x', 78) + "漢";
        var screen = Ansi.Parse($"{mixed}\nplain\nplain", suppressedByOwner: false);

        await Assert.That(screen.Rows[0].Text.Length).IsEqualTo(79);
        await Assert.That(screen.Rows[0].Cells).IsEqualTo(80);
        await Assert.That(screen.CellColumns).IsEqualTo(80);
        await Assert.That(screen.HasWideRunes).IsTrue();

        // And eighty-one cells cannot be one row of an eighty-column terminal. A double-width glyph
        // that would straddle the right margin is moved whole to the next line rather than split
        // across it, which is what a terminal does — so seventy-nine ASCII plus one Han glyph is two
        // rows, not one over-wide one. The layout counts cells and the caption reads them; before
        // this the layout counted UTF-16 units, so a screen of wide glyphs wrapped after eighty
        // runes, which is a hundred and sixty cells.
        var straddling = Ansi.Parse(new string('x', 79) + "漢\nplain", suppressedByOwner: false);

        await Assert.That(straddling.Rows[0].Cells).IsEqualTo(79);
        await Assert.That(straddling.Rows[1].Text).IsEqualTo("漢");

        // And a screen drawn entirely out of wide glyphs is twice its character count, which is the
        // case the halving was written for and the only one it got right.
        var wide = Ansi.Parse($"{new string('漢', 40)}\n{new string('漢', 40)}\n{new string('漢', 40)}",
            suppressedByOwner: false);

        await Assert.That(wide.CellColumns).IsEqualTo(80);
    }

    [Test]
    public async Task AWideRuneOnOneRowDoesNotWidenTheRowsAroundIt()
    {
        // The width reported is the widest row, not the widest row's arithmetic applied to all of
        // them. A banner with one Japanese line in it is as wide as that line and no wider.
        var screen = Ansi.Parse("ascii\n漢字\nascii", suppressedByOwner: false);

        await Assert.That(screen.Rows[0].Cells).IsEqualTo(5);
        await Assert.That(screen.Rows[1].Cells).IsEqualTo(4);
        await Assert.That(screen.CellColumns).IsEqualTo(5);
    }

    [Test]
    public async Task ACombiningMarkCostsNoCellOfItsOwn()
    {
        // It is drawn onto the cell before it. Counted as a character, an accented Greek or
        // Devanagari banner would be reported wider than it is drawn.
        //
        // Composed here rather than written as a literal, so the decomposition is this test's and
        // not whatever normalisation the file was saved under: three letters, each carrying a
        // combining acute of its own.
        var acute = (char)0x0301;
        var decomposed = string.Concat(Enumerable.Repeat($"e{acute}", 3));
        var screen = Ansi.Parse($"{decomposed}\nplain\nplain", suppressedByOwner: false);

        await Assert.That(screen.Rows[0].Text.Length).IsEqualTo(6);
        await Assert.That(screen.Rows[0].Cells).IsEqualTo(3);
        await Assert.That(screen.HasWideRunes).IsFalse();
    }

    [Test]
    public async Task AnEmptyScreenHasNoWidthRatherThanTheGridsWidth()
    {
        await Assert.That(Ansi.Parse(null, false).CellColumns).IsEqualTo(0);
    }

    /// <summary>
    /// The whole accessible alternative to the picture is outside the picture.
    /// </summary>
    /// <remarks>
    /// <c>role="img"</c> makes every descendant presentational, so a disclosure inside
    /// <c>div.quote</c> is announced as nothing at all and the one-line <c>aria-label</c> becomes
    /// the only alternative there is — a label saying the screen exists, in place of the screen.
    /// The disclosure therefore sits beside the quoted region and inside the figure.
    /// </remarks>
    [Test]
    public async Task TheReadAsTextDisclosureIsOutsideTheRoleImgSubtree()
    {
        var html = await Render.ComponentAsync<AnsiQuote>(new()
        {
            ["Screen"] = Ansi.Parse(Screen(6), suppressedByOwner: false),
        });

        await Assert.That(html).Contains("role=\"img\"");
        await Assert.That(html).Contains("aria-label=");
        await Assert.That(html).Contains("class=\"screen-text\"");

        // The quoted region holds one <pre> and no other element that closes with </div>, so the
        // first </div> after it is its own — and the disclosure has to come after that.
        var quote = html.IndexOf("class=\"quote\"", StringComparison.Ordinal);
        var closes = html.IndexOf("</div>", quote, StringComparison.Ordinal);
        var disclosure = html.IndexOf("class=\"screen-text\"", StringComparison.Ordinal);

        await Assert.That(quote).IsGreaterThanOrEqualTo(0);
        await Assert.That(disclosure).IsGreaterThan(closes);

        // And it is still inside the figure, which is what makes the two one block.
        var figure = html.IndexOf("</figure>", StringComparison.Ordinal);
        await Assert.That(disclosure).IsLessThan(figure);
    }

    [Test]
    public async Task TheCaptionStatesTheWidestRowInCellsRatherThanAHalvedGrid()
    {
        // Seventy-eight ASCII characters and one Han glyph: eighty cells, the widest a row of an
        // eighty-column terminal can be. The caption said forty, because one wide rune anywhere
        // halved the whole screen.
        var html = Render.Words(await Render.ComponentAsync<AnsiQuote>(new()
        {
            ["Screen"] = Ansi.Parse(new string('x', 78) + "漢\nplain\nplain", suppressedByOwner: false),
        }));

        await Assert.That(html).Contains("80×");
        await Assert.That(html).DoesNotContain("40×");
    }

    [Test]
    public async Task TheTextAlternativeCarriesNoColourCodes()
    {
        // A screen reader is told what the screen says, not how it was painted.
        var screen = Ansi.Parse($"{Esc}31mred{Esc}0m\nb\nc", suppressedByOwner: false);

        await Assert.That(screen.PlainText).DoesNotContain("\u001b");
        await Assert.That(screen.PlainText).DoesNotContain("31m");
        await Assert.That(screen.PlainText.Split('\n')[0]).IsEqualTo("red");
    }
}
