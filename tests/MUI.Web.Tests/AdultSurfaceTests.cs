using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Components;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

/// <summary>
/// The adult default where a reader meets it: the listing, the checkbox, the chip and the text.
/// </summary>
/// <remarks>
/// The rule itself is asserted in <c>MUI.Catalog.Tests.AdultListingTests</c>, with no markup in the
/// way. What is here is the part that can go wrong between the rule and a person: a default nobody
/// asked for, whose only sign is a control in the bar — so the control has to be there, it has to be
/// there in plain text as well, and the URL it produces has to mean the same thing to the read API.
/// </remarks>
public class AdultSurfaceTests
{
    private static readonly FixtureGameQueries Queries = new();

    /// <summary>The demo's one game declaring adult content.</summary>
    private const string Adult = "cinder";

    /// <summary>What an empty querystring means — which is where the default lives.</summary>
    private static GameFilter Listing(string query = "")
    {
        GameFilterBinding.TryRead(query, out var parsed, out var error);

        return error is null ? parsed.Filter : throw new InvalidOperationException(error);
    }

    private static async Task<string> PanelAsync(GameFilter filter)
    {
        var listing = await Queries.SearchAsync(filter);

        return await Render.ComponentAsync<FacetPanel>(new()
        {
            ["Facets"] = listing.Facets,
            ["Filter"] = filter,
        });
    }

    [Test]
    public async Task AskingForNothingLeavesThemOut()
    {
        var slugs = (await Queries.ListAsync(Listing())).Select(g => g.Slug);

        await Assert.That(slugs).DoesNotContain(Adult);
    }

    [Test]
    public async Task OutOfTheListingAndOfNothingElse()
    {
        // The same shape as archiving (spec §7.5), and the reason this is a listing default rather
        // than a deletion: the game's own page still answers, and every fact on it is untouched.
        await Assert.That(await Queries.FindAsync(Adult)).IsNotNull();
    }

    [Test]
    public async Task TheCheckboxBringsThemBack()
    {
        var slugs = (await Queries.ListAsync(Listing($"?{FacetKeys.Adult}=true"))).Select(g => g.Slug);

        await Assert.That(slugs).Contains(Adult);
    }

    [Test]
    public async Task TheBarCarriesTheControlWhetherOrNotItIsTicked()
    {
        // Drawn on every request, ticked or not. A default whose only undo appears once you have
        // already undone it is a default a reader cannot find, and this one hides games silently.
        foreach (var filter in new[] { Listing(), Listing($"?{FacetKeys.Adult}=true") })
        {
            var html = await PanelAsync(filter);

            await Assert.That(html).Contains($"name=\"{FacetKeys.Adult}\"");
            await Assert.That(Render.Words(html)).Contains("include adult");
        }
    }

    [Test]
    public async Task TheTickedBoxIsCheckedWhenTheUrlAsksForThem()
    {
        // The URL is the whole of this page's state, so a shared link has to come back with the box
        // in the position that produced it.
        await Assert.That(await PanelAsync(Listing())).DoesNotContain($"name=\"{FacetKeys.Adult}\" value=\"true\" checked");
        await Assert.That(await PanelAsync(Listing($"?{FacetKeys.Adult}=1")))
            .Contains($"name=\"{FacetKeys.Adult}\" value=\"true\" checked");
    }

    [Test]
    public async Task IncludingThemIsAChipThatCanBeTakenBackOff()
    {
        const string Query = $"?{FacetKeys.Adult}=true";

        var filter = Listing(Query);
        var listing = await Queries.SearchAsync(filter);
        var chip = ActiveFilters.For(listing.Facets, filter, Query).Single(c => c.Facet is "adult");

        await Assert.That(chip.Value).IsEqualTo("included");
        await Assert.That(chip.RemoveHref).DoesNotContain(FacetKeys.Adult);
    }

    [Test]
    public async Task ThePlainListingSaysWhichOfTheTwoItIs()
    {
        // A text browser cannot see a checkbox. Both exclusions are named on the line that says what
        // this listing is, because a reader who cannot see the control cannot infer the default.
        var hidden = PlainText.RenderListing(
            await Queries.SearchAsync(Listing()), Listing(), FixtureGameQueries.Now);

        var shown = PlainText.RenderListing(
            await Queries.SearchAsync(Listing($"?{FacetKeys.Adult}=1")),
            Listing($"?{FacetKeys.Adult}=1"),
            FixtureGameQueries.Now);

        await Assert.That(hidden).Contains("adult excluded");
        await Assert.That(hidden).DoesNotContain(Adult);
        await Assert.That(shown).Contains("adult included");
        await Assert.That(shown).Contains(Adult);
    }

    [Test]
    public async Task TheWizardCannotOfferAChoiceTheListingAnswersWithNothing()
    {
        // /find is a GET straight at /games, so its options are the listing's own vocabulary. Built
        // from an unfiltered catalogue it would offer "Adult (1)" and then submit ?genre=Adult,
        // which this listing returns nothing for — a count promising a result it cannot deliver,
        // which is the one thing the facet counts exist to make impossible.
        var html = await Render.PageAsync<FindAGame>([]);

        await Assert.That(html).DoesNotContain($"value=\"{AdultContent.Genre}\"");
    }

    [Test]
    public async Task TheFiguresBuiltInCodeStillCountTheWholeCatalogue()
    {
        // The scope of the default, pinned. It belongs to the listing surface — the home page's
        // counts, the ecosystem dashboard and the data dump build their filters in code and never
        // read a querystring, so they go on counting adult games exactly as they count archived
        // ones. A default that leaked into those would quietly move published aggregates.
        var everything = await Queries.ListAsync(new GameFilter { IncludeArchived = true });
        var dashboard = await Queries.EcosystemAsync();
        var archived = await Queries.ListAsync(new GameFilter { Band = ActivityBand.Archived });

        await Assert.That(everything.Select(g => g.Slug)).Contains(Adult);
        await Assert.That(dashboard.ListedGames).IsEqualTo(everything.Count - archived.Count);
    }
}
