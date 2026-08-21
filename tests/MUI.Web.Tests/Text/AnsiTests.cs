using MUI.Web.Components;

namespace MUI.Web.Tests.Text;

/// <summary>
/// The ANSI quotation frame's parser: foreign colour resolved to a locked table, laid out at a
/// fixed column grid, cropped honestly, and with the cases the design names kept apart.
/// </summary>
public class AnsiTests
{
    private const string Esc = "\u001b[";

    private static string Screen(int rows) =>
        string.Join('\n', Enumerable.Range(1, rows).Select(i => $"line {i}"));

    [Test]
    public async Task IndexedColourResolvesToTheLockedTableAndNotToASiteColour()
    {
        // The frame's palette is theme-independent by design: games assume a dark terminal.
        var screen = Ansi.Parse($"{Esc}32mgreen{Esc}0m plain\nb\nc", suppressedByOwner: false);
        var runs = screen.Rows[0].Runs;

        await Assert.That(runs[0].Text).IsEqualTo("green");
        await Assert.That(runs[0].Style.Foreground).IsEqualTo("#00aa00");
        await Assert.That(runs[1].Style.Foreground).IsNull();
    }

    [Test]
    public async Task BoldBrightensAnIndexedForeground()
    {
        // Every game drawing with ESC[1;32m relies on this.
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
        // We're quoting a picture, not emulating a terminal.
        var screen = Ansi.Parse($"{Esc}2J{Esc}Hclean\nb\nc", suppressedByOwner: false);

        await Assert.That(screen.Rows[0].Text).IsEqualTo("clean");
    }

    [Test]
    public async Task LinesWrapAtTheColumnGridAndTheWrappedRowIsCounted()
    {
        var screen = Ansi.Parse(new string('x', Ansi.DefaultColumns * 2 + 40) + "\na\nb", suppressedByOwner: false);

        await Assert.That(screen.Rows[0].Text.Length).IsEqualTo(Ansi.DefaultColumns);
        await Assert.That(screen.RowCount).IsEqualTo(5);
    }

    [Test]
    public async Task ArtDrawnWiderThanTheDefaultGridIsLaidOutAtItsOwnWidth()
    {
        // The complaint the default was written against: a screen drawn at 150 columns folded at
        // 100 is not the picture the game sent, it is that picture cut in half and stacked.
        var art = string.Join('\n', Enumerable.Repeat(new string('x', 150), 12));
        var screen = Ansi.Parse(art, suppressedByOwner: false);

        await Assert.That(screen.RowCount).IsEqualTo(12);
        await Assert.That(screen.Rows[0].Text.Length).IsEqualTo(150);
        await Assert.That(screen.CellColumns).IsEqualTo(150);
    }

    [Test]
    public async Task OneRunawayLineIsFoldedRatherThanStretchingTheGridToFitIt()
    {
        // A game that sends a paragraph unwrapped and leaves the folding to the client states no
        // width: nothing else on its screen comes near, so the grid stays where the rest of the
        // screen agrees and the paragraph folds.
        var screen = Ansi.Parse(
            string.Join('\n', Enumerable.Repeat(new string('x', 70), 20)) + "\n" + new string('y', 900),
            suppressedByOwner: false);

        await Assert.That(screen.Rows.Count(r => r.Text.StartsWith('y'))).IsEqualTo(9);
        await Assert.That(screen.CellColumns).IsEqualTo(Ansi.DefaultColumns);
    }

    [Test]
    public async Task OneLineAloneOnAScreenSetsNoWidthOfItsOwn()
    {
        // Corroboration is the whole rule, and a screen of one line has none to offer.
        var screen = Ansi.Parse(new string('x', 380), suppressedByOwner: false);

        await Assert.That(screen.Rows[0].Text.Length).IsEqualTo(Ansi.DefaultColumns);
    }

    [Test]
    public async Task TheGridIsCappedSoAPathologicalScreenCannotStretchTheFrame()
    {
        var screen = Ansi.Parse(
            string.Join('\n', Enumerable.Repeat(new string('x', 4000), 6)),
            suppressedByOwner: false);

        await Assert.That(screen.Rows[0].Text.Length).IsEqualTo(Ansi.MaximumColumns);
    }

    [Test]
    public async Task TheGridIsMeasuredInCellsSoAWideRuneScreenIsNotDoubleCounted()
    {
        // 90 Han glyphs are 180 cells: the grid the screen asks for, not 90 columns of anything.
        var art = string.Join('\n', Enumerable.Repeat(new string('\u6f22', 90), 12));
        var screen = Ansi.Parse(art, suppressedByOwner: false);

        await Assert.That(screen.RowCount).IsEqualTo(12);
        await Assert.That(screen.Rows[0].Cells).IsEqualTo(180);
    }

    [Test]
    public async Task ColourAndTabsAreNotCountedAsWidthWhenTheGridIsMeasured()
    {
        // The escape bytes are not glyphs; a tab is what a terminal advances it to.
        var line = $"{Esc}32m\t{new string('x', 150)}{Esc}0m";
        var screen = Ansi.Parse(string.Join('\n', Enumerable.Repeat(line, 12)), suppressedByOwner: false);

        await Assert.That(screen.RowCount).IsEqualTo(12);
        await Assert.That(screen.Rows[0].Cells).IsEqualTo(158);
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
        // "Never captured one" and "captured two lines" are different facts about a game.
        await Assert.That(Ansi.Parse(null, false).State).IsEqualTo(AnsiScreenState.Absent);
        await Assert.That(Ansi.Parse("   ", false).State).IsEqualTo(AnsiScreenState.Absent);
    }

    [Test]
    public async Task ALongScreenIsParsedWholeRatherThanCropped()
    {
        var screen = Ansi.Parse(Screen(214), suppressedByOwner: false);

        await Assert.That(screen.RowCount).IsEqualTo(214);
        await Assert.That(screen.Rows.Count).IsEqualTo(214);
    }

    [Test]
    public async Task AWidthIsCountedInTerminalCellsAndPerRowRatherThanPerScreen()
    {
        var mixed = new string('x', Ansi.DefaultColumns - 2) + "漢";
        var screen = Ansi.Parse($"{mixed}\nplain\nplain", suppressedByOwner: false);

        await Assert.That(screen.Rows[0].Text.Length).IsEqualTo(Ansi.DefaultColumns - 1);
        await Assert.That(screen.Rows[0].Cells).IsEqualTo(Ansi.DefaultColumns);
        await Assert.That(screen.CellColumns).IsEqualTo(Ansi.DefaultColumns);
        await Assert.That(screen.HasWideRunes).IsTrue();

        // A double-width glyph straddling the right margin moves whole to the next line, matching a
        // real terminal. Layout counts cells now, not UTF-16 units.
        var straddling = Ansi.Parse(new string('x', Ansi.DefaultColumns - 1) + "漢\nplain", suppressedByOwner: false);

        await Assert.That(straddling.Rows[0].Cells).IsEqualTo(Ansi.DefaultColumns - 1);
        await Assert.That(straddling.Rows[1].Text).IsEqualTo("漢");

        var halfGrid = Ansi.DefaultColumns / 2;
        var wide = Ansi.Parse(
            $"{new string('漢', halfGrid)}\n{new string('漢', halfGrid)}\n{new string('漢', halfGrid)}",
            suppressedByOwner: false);

        await Assert.That(wide.CellColumns).IsEqualTo(halfGrid * 2);
    }

    [Test]
    public async Task AWideRuneOnOneRowDoesNotWidenTheRowsAroundIt()
    {
        // The width reported is the widest row, not that row's arithmetic applied to all of them.
        var screen = Ansi.Parse("ascii\n漢字\nascii", suppressedByOwner: false);

        await Assert.That(screen.Rows[0].Cells).IsEqualTo(5);
        await Assert.That(screen.Rows[1].Cells).IsEqualTo(4);
        await Assert.That(screen.CellColumns).IsEqualTo(5);
    }

    [Test]
    public async Task ACombiningMarkCostsNoCellOfItsOwn()
    {
        // Drawn onto the cell before it; counted as a character, an accented banner would be
        // reported wider than it's drawn.
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
    /// <remarks><c>role="img"</c> makes every descendant presentational, so a disclosure inside <c>div.quote</c> would be announced as nothing.</remarks>
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

        var quote = html.IndexOf("class=\"quote\"", StringComparison.Ordinal);
        var closes = html.IndexOf("</div>", quote, StringComparison.Ordinal);
        var disclosure = html.IndexOf("class=\"screen-text\"", StringComparison.Ordinal);

        await Assert.That(quote).IsGreaterThanOrEqualTo(0);
        await Assert.That(disclosure).IsGreaterThan(closes);

        var figure = html.IndexOf("</figure>", StringComparison.Ordinal);
        await Assert.That(disclosure).IsLessThan(figure);
    }

    [Test]
    public async Task TheCaptionStatesTheWidestRowInCellsRatherThanAHalvedGrid()
    {
        var html = Render.Words(await Render.ComponentAsync<AnsiQuote>(new()
        {
            ["Screen"] = Ansi.Parse(new string('x', Ansi.DefaultColumns - 2) + "漢\nplain\nplain", suppressedByOwner: false),
        }));

        await Assert.That(html).Contains($"{Ansi.DefaultColumns}×");
        await Assert.That(html).DoesNotContain($"{Ansi.DefaultColumns / 2}×");
    }

    [Test]
    public async Task TheTextAlternativeCarriesNoColourCodes()
    {
        var screen = Ansi.Parse($"{Esc}31mred{Esc}0m\nb\nc", suppressedByOwner: false);

        await Assert.That(screen.PlainText).DoesNotContain("\u001b");
        await Assert.That(screen.PlainText).DoesNotContain("31m");
        await Assert.That(screen.PlainText.Split('\n')[0]).IsEqualTo("red");
    }
}
