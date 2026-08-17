using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

/// <summary>
/// The plain surface, which is the test of whether a fact is being communicated at all.
/// </summary>
/// <remarks>
/// These assertions are deliberately about <em>words</em>. If a state can only be told apart by a
/// colour, a glyph or a cell shape, it fails here — and failing here means the graphic version was
/// decoration rather than information.
/// </remarks>
public class PlainSurfaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 20, 0, 0, TimeSpan.Zero);
    private static readonly FixtureGameQueries Queries = new();

    private static async Task<string> RenderAsync(string slug)
    {
        var page = await Queries.FindAsync(slug);
        return PlainText.Render(page!, Now);
    }

    /// <summary>
    /// The three states that withhold a listing are three markers, not one.
    /// </summary>
    /// <remarks>
    /// This is the surface a reader reaches with a script, so the marker is the whole answer. Folding
    /// `unlisted` into `[archived]` would put the wrong fact — "it stopped answering" — in the one
    /// field a parser reads, about a game that is running and simply not being dialled.
    /// </remarks>
    [Test]
    [Arguments(LifecycleState.Archived, "[archived]")]
    [Arguments(LifecycleState.Excluded, "[excluded]")]
    [Arguments(LifecycleState.Unlisted, "[unlisted]")]
    public async Task EachWayOutOfTheListingIsMarkedAsItself(LifecycleState state, string marker)
    {
        var page = await Queries.FindAsync("m-u-s-h");
        var text = PlainText.Render(
            page! with { Summary = page.Summary with { State = state } },
            Now);

        await Assert.That(text.Split('\n')[0]).EndsWith(marker);
    }

    [Test]
    public async Task AnUnknownCountSaysSoInWordsRatherThanBeingBlank()
    {
        // A blank reads as zero to a human exactly as it does to a parser. Midnight Sun answers,
        // offers no MSSP and no pre-login WHO, so there is genuinely nothing to count.
        var text = await RenderAsync("midnight-sun");

        await Assert.That(text).Contains("Players now: unknown");
        await Assert.That(text).DoesNotContain("Players now: 0");
    }

    [Test]
    public async Task AMeasuredZeroIsPrintedAsZeroAndNotAsUnknown()
    {
        // Eldertale really has nobody on. That is a measurement and must not be softened into a
        // shrug — the two are different facts and the plain surface has to keep them apart.
        var text = await RenderAsync("eldertale");

        await Assert.That(text).Contains("Players now: 0");
        await Assert.That(text).DoesNotContain("unknown");
    }

    [Test]
    public async Task TheGamePagesOwnCountSaysHowItWasObtainedJustAsTheListingDoes()
    {
        // The game page is the surface a reader trusts most, and it was the one left saying
        // "Players now: 9" flat while the listing beside it said the game had asserted that number.
        // A page cannot be less labelled than the index that points at it.
        var declared = await RenderAsync("ashen-court");
        var measured = await RenderAsync("m-u-s-h");

        await Assert.That(declared).Contains("Players now: 9  (declared, 9m)");
        await Assert.That(measured).Contains("Players now: 15  (measured, 4m)");

        // A count nobody could take keeps its sentence and gains no label.
        await Assert.That(await RenderAsync("midnight-sun"))
            .Contains("Players now: unknown (no count could be measured)");
    }

    [Test]
    public async Task ADisagreementIsFlaggedInWordsNotByColour()
    {
        var text = await RenderAsync("m-u-s-h");

        await Assert.That(text).Contains("** disagree");
        await Assert.That(text).Contains("disagree)");
    }

    [Test]
    public async Task CapabilityStatesAreSpelledOut()
    {
        var text = await RenderAsync("m-u-s-h");

        await Assert.That(text).Contains("measured: yes");
        await Assert.That(text).Contains("declared: -");
    }

    [Test]
    public async Task AStaleFieldSaysItIsStale()
    {
        // "created 2009, declared, 6y, stale" — the age and the judgement both survive without a
        // colour to carry them.
        var text = await RenderAsync("m-u-s-h");

        await Assert.That(text).Contains("stale");
    }

    [Test]
    public async Task AnArchivedGameSaysArchivedInItsFirstLine()
    {
        var text = await RenderAsync("gaslight-row");

        await Assert.That(text.Split('\n')[0]).Contains("[archived]");
    }

    [Test]
    public async Task AnUnclaimedGameSaysSoWithoutSoundingLikeAnError()
    {
        var text = await RenderAsync("eldertale");

        await Assert.That(text.Split('\n')[0]).Contains("[unclaimed]");
    }
}

/// <summary>The listing's own rules, asserted against the query interface rather than the markup.</summary>
public class ListingTests
{
    private static readonly FixtureGameQueries Queries = new();

    [Test]
    public async Task ArchivedGamesAreExcludedByDefault()
    {
        var games = await Queries.ListAsync(new GameFilter());

        await Assert.That(games.Any(g => g.State is LifecycleState.Archived)).IsFalse();
    }

    [Test]
    public async Task ArchivedGamesComeBackWhenAskedFor()
    {
        // Excluded from the listing and from nothing else — the page, the URL and the history all
        // survive, so the toggle is the only thing standing between a reader and the record.
        var games = await Queries.ListAsync(new GameFilter { IncludeArchived = true });

        await Assert.That(games.Any(g => g.State is LifecycleState.Archived)).IsTrue();
    }

    [Test]
    public async Task AnArchivedGameStillHasAPage()
    {
        var page = await Queries.FindAsync("gaslight-row");

        await Assert.That(page).IsNotNull();
        await Assert.That(page!.Summary.State).IsEqualTo(LifecycleState.Archived);
    }

    [Test]
    public async Task TheActivityGridCarriesAllThreeStates()
    {
        // A fixture that only ever produced counted cells would let a renderer conflate the other
        // two and still look right. All three have to be present for the grid to be testable at all.
        var page = await Queries.FindAsync("m-u-s-h");
        var cells = page!.Activity;

        await Assert.That(cells.Any(c => c.IsCounted)).IsTrue();
        await Assert.That(cells.Any(c => c.IsUnmeasurable)).IsTrue();
        await Assert.That(cells.Any(c => c.IsGap)).IsTrue();
    }

    [Test]
    public async Task AMeasuredZeroCellIsCountedRatherThanAGap()
    {
        var page = await Queries.FindAsync("m-u-s-h");
        var zero = page!.Activity.First(c => c.Count == 0);

        await Assert.That(zero.IsCounted).IsTrue();
        await Assert.That(zero.IsGap).IsFalse();
        await Assert.That(zero.IsUnmeasurable).IsFalse();
    }
}
