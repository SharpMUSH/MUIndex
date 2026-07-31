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
    public async Task AScreenPastTwentyFourRowsIsCroppedAndSaysHowManyThereAre()
    {
        var screen = Ansi.Parse(Screen(47), suppressedByOwner: false);

        await Assert.That(screen.RowCount).IsEqualTo(47);
        await Assert.That(screen.IsCropped).IsTrue();
        await Assert.That(screen.IsOversized).IsFalse();
        await Assert.That(screen.Visible.Count()).IsEqualTo(Ansi.CropRows);
    }

    [Test]
    public async Task AScreenPastTwoHundredRowsIsFlaggedAsOversizedAndStillCropsAtTwentyFour()
    {
        var screen = Ansi.Parse(Screen(214), suppressedByOwner: false);

        await Assert.That(screen.IsOversized).IsTrue();
        await Assert.That(screen.Visible.Count()).IsEqualTo(Ansi.CropRows);
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
