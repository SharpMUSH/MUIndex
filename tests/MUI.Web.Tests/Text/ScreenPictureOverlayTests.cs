using MUI.Web.Components;

namespace MUI.Web.Tests.Text;

/// <summary>
/// A connect screen's pictures, drawn over the quoted screen on the cells the game left for them.
/// </summary>
public class ScreenPictureOverlayTests
{
    /// <summary>
    /// Three lines at the frame's narrowest grid, then one no other line comes near, which the frame
    /// folds into three rows.
    /// </summary>
    private static readonly string Wrapping =
        string.Concat(Enumerable.Repeat(new string('=', Grid) + "\n", 3)) + new string('x', Grid * 5 / 2) + "\nend";

    private const int Grid = Ansi.DefaultColumns;

    /// <summary>
    /// A row knows which of the game's lines it came from, and how far along it: a line the frame
    /// wraps is still one line of the game's.
    /// </summary>
    [Test]
    public async Task AWrappedLineKeepsItsLineAndCountsItsCells()
    {
        var screen = Ansi.Parse(Wrapping, suppressedByOwner: false);

        var rows = screen.Rows.Select(r => (r.Line, r.Offset)).ToArray();

        await Assert.That(rows).IsEquivalentTo(new[]
        {
            (0, 0), (1, 0), (2, 0), (3, 0), (3, Grid), (3, Grid * 2), (4, 0),
        });
    }

    /// <summary>
    /// A picture is placed on the frame's row that holds its cell, which on a wrapped line is not the
    /// line's first row.
    /// </summary>
    [Test]
    public async Task APictureOnAWrappedLineIsPlacedOnTheRowHoldingItsCell()
    {
        var screen = Ansi.Parse(Wrapping, suppressedByOwner: false);

        var at = AnsiQuote.Place(screen.Rows, new ScreenOverlay("/p", Line: 3, Column: Grid + 5, Columns: 4, Rows: 1));

        await Assert.That(at).IsEqualTo((4, 5));
    }

    /// <summary>A picture whose line the stored screen no longer has is not drawn somewhere else.</summary>
    [Test]
    public async Task APictureOnALineTheScreenDoesNotHaveIsNotDrawn()
    {
        var screen = Ansi.Parse("a\nb\nc", suppressedByOwner: false);

        await Assert.That(AnsiQuote.Place(screen.Rows, new ScreenOverlay("/p", 9, 0, 1, 1))).IsNull();
    }

    [Test]
    public async Task APictureIsDrawnInsideThePreInTheScreensOwnCells()
    {
        var html = await Render.ComponentAsync<AnsiQuote>(new()
        {
            ["Screen"] = Ansi.Parse(
                "Welcome to Ravenmoor\n      \n      \n      \nconnect <name> <password>\ncreate <name> <password>",
                suppressedByOwner: false),
            ["Pictures"] = new[] { new ScreenOverlay("/g/ravenmoor/screen/0?v=abc", 1, 0, 6, 3) },
        });

        var pre = html.IndexOf("<pre", StringComparison.Ordinal);
        var img = html.IndexOf("<img class=\"screen-picture\"", StringComparison.Ordinal);
        var closes = html.IndexOf("</pre>", StringComparison.Ordinal);

        await Assert.That(img).IsGreaterThan(pre);
        await Assert.That(img).IsLessThan(closes);
        await Assert.That(html).Contains("src=\"/g/ravenmoor/screen/0?v=abc\"");
        await Assert.That(html).Contains("alt=\"\"")
            .Because("the frame's label and the text alternative already stand for the whole screen");
        await Assert.That(html).Contains("--row:1;--col:0;--cols:6;--rows:3");
    }

    /// <summary>
    /// A screen that is mostly picture is still a screen: the cells under a picture are blank, and
    /// counting them as empty would collapse the frame and drop the picture with it.
    /// </summary>
    [Test]
    public async Task LinesUnderAPictureCountAsDrawn()
    {
        const string raw = "      \n      \n      \nconnect";

        await Assert.That(Ansi.Parse(raw, suppressedByOwner: false).State).IsEqualTo(AnsiScreenState.TooSmall);
        await Assert.That(Ansi.Parse(raw, suppressedByOwner: false, new HashSet<int> { 0, 1, 2 }).State)
            .IsEqualTo(AnsiScreenState.Rendered);
    }
}
