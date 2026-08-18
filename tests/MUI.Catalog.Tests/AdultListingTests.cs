namespace MUI.Catalog.Tests;

/// <summary>
/// Adult games leave the default listing and nothing else — the rule, and the counts it moves.
/// </summary>
/// <remarks>
/// <para>
/// Two declarations put a game here and they are checked separately, because they catch different
/// games: MSSP <c>GENRE Adult</c>, and MSSP's own <c>ADULT MATERIAL</c> flag, which a game may set
/// while declaring any genre at all. One of the four games this catches in production does exactly
/// that, so a rule reading only <c>GENRE</c> would list it.
/// </para>
/// <para>
/// Both are <em>declared</em>. Nothing here is a measurement of what a game contains, and the
/// exclusion is a default of ours over what a game typed about itself — which is why the checkbox
/// that undoes it sits in the bar on every request rather than being hidden behind a preference.
/// </para>
/// </remarks>
public class AdultListingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static GameFacetRow Row(string slug, string? genre = null, bool adult = false) =>
        new(
            new GameSummary(
                Guid.NewGuid(), slug, slug, Tagline: null, LifecycleState.Active, IsClaimed: false,
                PlayersNow: 1, Codebase: "Evennia", MeasuredProtocols: [], LastReachableAt: Now),
            ActivityBand.PlayersNow,
            LastSeenBand.Day,
            TlsMeasured: false,
            Charset: null,
            Language: null,
            Codebase: "Evennia",
            Family: null,
            Genre: genre,
            IsAdult: adult,

            // Every game here answered and was counted. The adult switch is what this suite is
            // about, and a row that was also uncounted would put a second reason in front of the
            // one being asserted.
            Uncounted: false,
            Unreachable: false);

    private static readonly GameFacetRow[] Catalogue =
    [
        Row("plain", genre: "Fantasy"),
        Row("also-plain", genre: "Historical"),
        Row("by-genre", genre: "Adult", adult: true),
        Row("by-flag", genre: "Fantasy", adult: true),
    ];

    /// <summary>The listing surface's default, which is the whole feature.</summary>
    private static readonly GameFilter Listing = new() { IncludeAdult = false };

    private static IReadOnlyList<string> Slugs(GameListing listing) =>
        [.. listing.Games.Select(g => g.Slug)];

    [Test]
    public async Task TheDefaultListingLeavesThemOut()
    {
        var listing = FacetedSearch.Search(Catalogue, Listing);

        await Assert.That(Slugs(listing)).IsEquivalentTo(new[] { "also-plain", "plain" });
    }

    [Test]
    public async Task TheFlagCatchesAGameWhoseGenreSaysSomethingElse()
    {
        // The one that matters: a game declaring ADULT MATERIAL under GENRE Fantasy is exactly the
        // case a rule reading only the genre would list, and it is a real row in production.
        var listing = FacetedSearch.Search(Catalogue, Listing);

        await Assert.That(Slugs(listing)).DoesNotContain("by-flag");
    }

    [Test]
    public async Task TheCheckboxBringsThemBack()
    {
        var listing = FacetedSearch.Search(Catalogue, new GameFilter { IncludeAdult = true });

        await Assert.That(listing.Games.Count).IsEqualTo(4);
    }

    [Test]
    public async Task NoCountOffersAGameTheListingWillNotShow()
    {
        // The panel's standing promise, applied to the games this default removes. Both hidden rows
        // would land in a facet value that is still on offer — "Adult" under genre, "Evennia" under
        // codebase — so a count taken over anything wider than the visible set advertises them.
        var listing = FacetedSearch.Search(Catalogue, Listing);

        foreach (var group in listing.Facets)
        {
            await Assert.That(group.Total).IsEqualTo(listing.Games.Count);

            foreach (var value in group.Values)
            {
                await Assert.That(value.Count).IsLessThanOrEqualTo(listing.Games.Count);
            }
        }
    }

    [Test]
    public async Task TheGenreFacetDoesNotOfferAValueTheDefaultHides()
    {
        // Not a dead affordance left drawn at zero — the value is simply not there, because an
        // open-ended facet offers only what its results contain. That is what makes the checkbox the
        // one way in, and what keeps every other count in this dropdown a promise the listing keeps.
        var genre = FacetedSearch.Search(Catalogue, Listing).Facets.Single(f => f.Key == FacetKeys.Genre);

        await Assert.That(genre.Values.Select(v => v.Token)).DoesNotContain("Adult");
    }

    [Test]
    public async Task TheGenreFacetOffersItOnceTheBoxIsTicked()
    {
        var genre = FacetedSearch
            .Search(Catalogue, new GameFilter { IncludeAdult = true })
            .Facets.Single(f => f.Key == FacetKeys.Genre);

        await Assert.That(genre.Values.Single(v => v.Token == "Adult").Count).IsEqualTo(1);
    }

    [Test]
    public async Task ArchivedAndAdultAreSeparateQuestions()
    {
        // Neither switch may stand in for the other. An archived adult game needs both, and an
        // adult game that is perfectly alive is not reached by the archive toggle.
        GameFacetRow[] rows =
        [
            Row("live-adult", genre: "Adult", adult: true) with { Band = ActivityBand.PlayersNow },
            Row("archived-adult", genre: "Adult", adult: true) with { Band = ActivityBand.Archived },
        ];

        await Assert.That(FacetedSearch.Search(rows, Listing).Games).IsEmpty();
        await Assert.That(Slugs(FacetedSearch.Search(rows, Listing with { IncludeArchived = true })))
            .IsEmpty();
        await Assert.That(Slugs(FacetedSearch.Search(rows, Listing with { IncludeAdult = true })))
            .IsEquivalentTo(new[] { "live-adult" });
        await Assert.That(Slugs(FacetedSearch
                .Search(rows, Listing with { IncludeAdult = true, IncludeArchived = true })))
            .IsEquivalentTo(new[] { "archived-adult", "live-adult" });
    }

    [Test]
    public async Task ProgrammaticCallersAreNotTouchedByTheListingDefault()
    {
        // GameFilter defaults to including them, and the listing surface is where the default lives
        // (GameFilterBinding). The data dump, the home page's counts and the reference figures build
        // filters by hand and never see it — the same way they already include archived games while
        // the listing hides them.
        await Assert.That(new GameFilter().IncludeAdult).IsTrue();
        await Assert.That(FacetedSearch.Search(Catalogue, new GameFilter()).Games.Count).IsEqualTo(4);
    }

    [Test]
    [Arguments("Adult", null, true)]
    [Arguments("adult", null, true)]
    [Arguments("  Adult  ", null, true)]
    [Arguments("Adventure", "1", true)]
    [Arguments("Fantasy", "true", true)]
    [Arguments("Fantasy", "yes", true)]
    [Arguments("Fantasy", "0", false)]
    [Arguments("Fantasy", null, false)]
    [Arguments(null, null, false)]
    [Arguments("Adult Swim Parody", null, false)]
    public async Task TheRuleReadsTwoDeclarationsAndNothingElse(
        string? genre, string? adultMaterial, bool expected)
    {
        // "Adult Swim Parody" is the reason this is an equality and not a substring: MSSP GENRE
        // holds one value, and a game whose genre merely contains the word has declared nothing.
        await Assert.That(AdultContent.Declared(genre, adultMaterial)).IsEqualTo(expected);
    }
}
