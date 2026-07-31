using MUI.Catalog;

namespace MUI.Catalog.Tests;

/// <summary>
/// The third state a facet can be in: filtered out (spec §9).
/// </summary>
/// <remarks>
/// Absent means the facet is not being asked about, a value means <em>only these</em>, and an
/// excluded value means <em>anything but these</em>. Without the third, "show me the games that are
/// not Evennia" is a question the catalogue can answer and the interface cannot ask.
/// </remarks>
public class FacetPolarityTests
{
    private static readonly GameSummary Penn = Game("penn", "PennMUSH 1.8.8p0");
    private static readonly GameSummary Evennia = Game("evennia", "Evennia");
    private static readonly GameSummary Rom = Game("rom", "ROM");
    private static readonly GameSummary Nameless = Game("nameless", null);

    [Test]
    public async Task ExcludingAValueReturnsEverythingElse()
    {
        var listing = Search(new GameFilter { Codebase = FacetChoice.Not("Evennia") });

        await Assert.That(listing.Games.Select(g => g.Slug))
            .IsEquivalentTo(new[] { "penn", "rom", "nameless" });
    }

    [Test]
    public async Task IncludingAValueStillReturnsOnlyIt()
    {
        var listing = Search(new GameFilter { Codebase = FacetChoice.Of("Evennia") });

        await Assert.That(listing.Games.Select(g => g.Slug)).IsEquivalentTo(new[] { "evennia" });
    }

    [Test]
    public async Task NoSelectionFiltersOnNothing()
    {
        await Assert.That(Search(new GameFilter()).Games.Count).IsEqualTo(4);
    }

    /// <summary>
    /// A game with no value for the facet survives an exclusion, because it is not the thing excluded.
    /// </summary>
    /// <remarks>
    /// The tempting bug is to treat "not Evennia" as "has a codebase, and it is not Evennia", which
    /// would quietly drop every game whose codebase we never identified — turning our own gap in
    /// measurement into a property of those games. Not identifying a codebase is a measurement, and
    /// it is not a measurement of being Evennia.
    /// </remarks>
    [Test]
    public async Task AGameWithNoValueSurvivesAnExclusion()
    {
        var listing = Search(new GameFilter { Codebase = FacetChoice.Not("Evennia") });

        await Assert.That(listing.Games.Select(g => g.Slug)).Contains("nameless");
    }

    /// <summary>Excluding the unknown is its own question, and answers it.</summary>
    [Test]
    public async Task TheUnknownCanItselfBeExcluded()
    {
        var listing = Search(new GameFilter
        {
            Codebase = FacetChoice.Unknown with { Exclude = true },
        });

        await Assert.That(listing.Games.Select(g => g.Slug))
            .IsEquivalentTo(new[] { "penn", "evennia", "rom" });
    }

    /// <summary>The family filter is a bounded prefix, and it inverts the same way.</summary>
    [Test]
    public async Task ExcludingACodebaseFamilyTakesEveryPatchlevelWithIt()
    {
        var listing = Search(new GameFilter { CodebaseFamily = FacetChoice.Not("PennMUSH") });

        // PennMUSH 1.8.8p0 goes with the family it belongs to; ROM stays, and is not caught by a
        // prefix that would have gathered ROMulus.
        await Assert.That(listing.Games.Select(g => g.Slug))
            .IsEquivalentTo(new[] { "evennia", "rom", "nameless" });
    }

    /// <summary>A token round-trips through the URL with its polarity intact.</summary>
    [Test]
    public async Task PolaritySurvivesTheQuerystring()
    {
        foreach (var choice in new[]
        {
            FacetChoice.Of("Evennia"),
            FacetChoice.Not("Evennia"),
            FacetChoice.Unknown,
            FacetChoice.Unknown with { Exclude = true },
        })
        {
            await Assert.That(FacetChoice.Parse(choice.Token)).IsEqualTo(choice);
        }
    }

    /// <summary>
    /// A value that begins with the marker stays reachable rather than reading as its own negation.
    /// </summary>
    [Test]
    public async Task AValueBeginningWithTheMarkerIsNotMistakenForAnExclusion()
    {
        var literal = FacetChoice.Of("!important");

        await Assert.That(literal.Token).IsEqualTo("!!important");
        await Assert.That(FacetChoice.Parse(literal.Token)).IsEqualTo(literal);
        await Assert.That(FacetChoice.Parse(literal.Token).Exclude).IsFalse();
    }

    /// <summary>
    /// Polarity is applied to the answer, never per token.
    /// </summary>
    /// <remarks>
    /// A facet can hand one row several tokens — a game reached an hour ago is in the last day, the
    /// last week and the last month. Inverting the comparison per token would make an excluded
    /// selection mean "some token differs", which every multi-token row satisfies, so "not seen in
    /// the last day" would return everything.
    /// </remarks>
    [Test]
    public async Task AMultiTokenFacetInvertsOnceRatherThanPerToken()
    {
        var choice = FacetChoice.Not("day");

        await Assert.That(choice.Admits(covered: true)).IsFalse();
        await Assert.That(choice.Admits(covered: false)).IsTrue();
        await Assert.That(FacetChoice.Of("day").Admits(covered: true)).IsTrue();
    }

    /// <summary>The panel can tell the three states apart, which is what lets it draw them apart.</summary>
    [Test]
    public async Task AFacetValueReportsWhichOfTheThreeStatesItIsIn()
    {
        var listing = Search(new GameFilter { Codebase = FacetChoice.Not("Evennia") });
        var codebase = listing.Facets.Single(f => f.Key == FacetKeys.Codebase);

        await Assert.That(codebase.Values.Single(v => v.Token == "Evennia").State)
            .IsEqualTo(FacetState.Excluded);
        await Assert.That(codebase.Values.Single(v => v.Token == "ROM").State)
            .IsEqualTo(FacetState.Unselected);
    }

    private static GameListing Search(GameFilter filter) =>
        FacetedSearch.Search([.. new[] { Penn, Evennia, Rom, Nameless }.Select(Row)], filter);

    private static GameFacetRow Row(GameSummary game) => new(
        game,
        ActivityBand.Quiet,
        LastSeenBand.Week,
        TlsMeasured: false,
        Charset: null,
        Language: null,
        Codebase: game.Codebase,
        Family: null,
        Genre: null);

    private static GameSummary Game(string slug, string? codebase) => new(
        Guid.NewGuid(), slug, slug, null, LifecycleState.Active, IsClaimed: false,
        PlayersNow: 1, Codebase: codebase, MeasuredProtocols: []);
}
