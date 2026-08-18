using MUI.Catalog;
using MUI.Crawl;
using MUI.Web.Components.Pages;

namespace MUI.Web.Tests;

/// <summary>
/// The scorecard page (spec §8.5), and the placeholder knowledge it borrows from the probe.
/// </summary>
/// <remarks>
/// Rendered over the demo fixture, which has no accounts — so what these assert about the page is
/// that it is <em>closed</em>, which is the half that matters for a surface gated on ownership. The
/// linting itself is asserted in <c>MUI.Catalog.Tests.MsspLintTests</c>, over inputs a fixture
/// cannot express.
/// </remarks>
public class MsspScorecardSurfaceTests
{
    /// <summary>
    /// Nobody who has not proved they run the game sees it, and it does not say who has.
    /// </summary>
    /// <remarks>
    /// Not-found rather than forbidden: whether a particular account holds a claim is not a fact
    /// this page owes a stranger, and an explicit refusal would confirm it.
    /// </remarks>
    [Test]
    public async Task AStrangerIsNotToldWhetherAnybodyOwnsIt()
    {
        var words = Render.Words(await Render.PageAsync<Mssp>(new() { ["Slug"] = "m-u-s-h" }));

        await Assert.That(words).Contains("Not found");
        await Assert.That(words).DoesNotContain("MSSP requires these");
        await Assert.That(words).DoesNotContain("carries");
    }

    /// <summary>A game that does not exist is not found either, by the same words.</summary>
    [Test]
    public async Task AGameThatDoesNotExistIsNotFound()
    {
        var words = Render.Words(await Render.PageAsync<Mssp>(new() { ["Slug"] = "no-such-game" }));

        await Assert.That(words).Contains("No such game");
    }

    /// <summary>
    /// The linter's judgement about defaults is the probe's, not a second copy of it.
    /// </summary>
    /// <remarks>
    /// <c>MsspDefaults</c> lives in <c>MUI.Crawl</c>, which <c>MUI.Catalog</c> may never reference,
    /// so <see cref="MsspLint.Inspect"/> takes the test as a parameter. This pins the two together: a
    /// placeholder the probe already knows about must produce an <c>Unanswered</c> finding.
    /// </remarks>
    [Test]
    public async Task TheLinterAndTheProbeAgreeAboutWhatCountsAsUnanswered()
    {
        var game = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        var card = MsspLint.Inspect(
            [
                new GameField(game, "NAME", FieldSource.Mssp, "PennMUSH", now, now),
                new GameField(game, "PLAYERS", FieldSource.Mssp, "4", now, now),
                new GameField(game, "UPTIME", FieldSource.Mssp, "900", now, now),
            ],
            MsspDefaults.IsPlaceholder);

        var finding = card.Findings.Single(f => f.Field == "NAME");

        await Assert.That(finding.Kind).IsEqualTo(MsspFindingKind.Unanswered);
        await Assert.That(card.MeetsTheStandard).IsFalse();
    }

    /// <summary>
    /// A blank scorecard for a game we hold no report from says so about us.
    /// </summary>
    /// <remarks>
    /// The wording is the assertion. "We have not read an MSSP report" is a statement about our
    /// crawl; "this game has no MSSP" would be a claim about their server that we did not measure,
    /// and the page must not make it — least of all to the one reader who knows better.
    /// </remarks>
    [Test]
    public async Task NoReportIsWordedAsOurGapRatherThanTheirNeglect()
    {
        var card = MsspLint.Inspect([], MsspDefaults.IsPlaceholder);

        await Assert.That(card.HasReport).IsFalse();
        await Assert.That(card.Findings).IsEmpty();
    }
}
