using MUI.Catalog;
using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// Measured and declared as two columns, never merged into one badge.
/// </summary>
public class CapabilityMatrixTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 20, 0, 0, TimeSpan.Zero);

    private static readonly CapabilityRow[] Rows =
    [
        new("MXP", CapabilityState.Unknown, CapabilityState.Unknown, null),
        new("MSSP", CapabilityState.Present, CapabilityState.Unknown, Now.AddMinutes(-4)),
        new("GMCP", CapabilityState.Absent, CapabilityState.Present, Now.AddYears(-6)),
        new("MSDP", CapabilityState.Absent, CapabilityState.Unknown, Now.AddMinutes(-4)),
    ];

    private static Task<string> MatrixAsync() =>
        Render.ComponentAsync<CapabilityMatrix>(new() { ["Rows"] = Rows, ["Now"] = Now });

    [Test]
    public async Task DisagreementsSortToTheTop()
    {
        var html = await MatrixAsync();
        var body = html[html.IndexOf("<tbody>", StringComparison.Ordinal)..];

        await Assert.That(body.IndexOf("GMCP", StringComparison.Ordinal))
            .IsLessThan(body.IndexOf("MSSP", StringComparison.Ordinal));
    }

    [Test]
    public async Task ADisagreementIsMarkedInTheRowAndNotOnlyByATint()
    {
        // A background tint is invisible in greyscale, to a screen reader, and in print.
        var html = await MatrixAsync();

        await Assert.That(html).Contains("class=\"mark warn\">disagrees</span>");
    }

    [Test]
    public async Task TheTallyIsTheTablesCaptionAndIsSaidExactlyOnce()
    {
        // The tally is stated once, as the table's on-screen caption, not duplicated in the heading —
        // a screen reader must not hear the same sentence twice.
        var html = await MatrixAsync();
        var head = html[..html.IndexOf("<table", StringComparison.Ordinal)];

        await Assert.That(html).Contains("<caption class=\"count");

        // "disagrees", not "disagree": a real ICU plural clause, so one disagreement agrees with its
        // verb.
        await Assert.That(Render.Words(html)).Contains("1 of 4 disagrees with what the game declares.");
        await Assert.That(head).DoesNotContain("1 of 4");

        var occurrences = Render.Words(html).Split("1 of 4").Length - 1;
        await Assert.That(occurrences).IsEqualTo(1);

        await Assert.That(Render.Words(head)).Contains("Capabilities");
    }

    [Test]
    public async Task MeasuredAndDeclaredStayInSeparateColumns()
    {
        var html = await MatrixAsync();

        await Assert.That(html).Contains(">measured</th>");
        await Assert.That(html).Contains(">declared</th>");
        await Assert.That(html).Contains("● offered");
        await Assert.That(html).Contains("◇ claimed");
    }

    [Test]
    public async Task EveryStateCarriesAWordAsWellAsAGlyph()
    {
        // Colour is never the sole carrier, and neither is a glyph alone (greyscale printing, no-symbol terminals).
        var html = await MatrixAsync();

        await Assert.That(html).Contains("offered");
        await Assert.That(html).Contains("absent");
        await Assert.That(html).Contains("silent");
        await Assert.That(html).Contains("claimed");
    }

    [Test]
    public async Task UnknownIsNeverRenderedAsAbsent()
    {
        // "Not observed" and "not there" are different facts.
        var html = await MatrixAsync();
        var mxpRow = html[html.IndexOf("MXP", StringComparison.Ordinal)..];

        await Assert.That(mxpRow[..300]).Contains("– silent");
        await Assert.That(mxpRow[..300]).DoesNotContain("✕ absent");
    }
}
