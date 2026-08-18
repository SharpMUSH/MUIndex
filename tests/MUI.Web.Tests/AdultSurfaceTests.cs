using MUI.Web.Localization;
using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Components;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

/// <summary>
/// The adult default where a reader meets it: the listing, the checkbox, the chip and the text.
/// </summary>
/// <remarks>The rule itself is asserted in <c>MUI.Catalog.Tests.AdultListingTests</c>; this covers what can go wrong between the rule and a person — the control must exist, in plain text too, and its URL must mean the same thing to the read API.</remarks>
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
        // Same shape as archiving (spec §7.5): a listing default, not a deletion — the page still answers.
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
        // Drawn on every request, ticked or not — a default whose only undo appears once already
        // undone is one a reader can't find. A link, not a checkbox on a submit button.
        foreach (var filter in new[] { Listing(), Listing($"?{FacetKeys.Adult}=true") })
        {
            var html = await PanelAsync(filter);

            // Href is the *other* position, so a fixed-URL assertion would only ever find one state.
            await Assert.That(html).Contains("adult content,");
            await Assert.That(Render.Words(html)).Contains("adult");
        }
    }

    [Test]
    public async Task TheSwitchSaysWhichWayItIsSetAndLinksToTheOther()
    {
        // The URL is the whole of this page's state, so a shared link comes back in the position that produced it.
        var off = await PanelAsync(Listing());
        var on = await PanelAsync(Listing($"?{FacetKeys.Adult}=1"));

        // Off: says so, and its link turns them on.
        await Assert.That(off).Contains("adult content, hidden");
        await Assert.That(off).Contains($"href=\"/games?{FacetKeys.Adult}=true\"");

        // On: its link clears the key rather than setting it false, so a copied address says only what was asked for.
        await Assert.That(on).Contains("adult content, shown");
        await Assert.That(on).DoesNotContain($"href=\"/games?{FacetKeys.Adult}=true\"");
    }

    [Test]
    public async Task IncludingThemIsAChipThatCanBeTakenBackOff()
    {
        const string Query = $"?{FacetKeys.Adult}=true";

        var filter = Listing(Query);
        var listing = await Queries.SearchAsync(filter);

        // Asks the bundle what the chip says rather than repeating the English — the fact under test
        // is not wording.
        var chips = ActiveFilters.For(Locales.SourceTag, listing.Facets, filter, Query);
        var chip = chips.Single(
            c => c.Facet == FacetWords.Group(Locales.SourceTag, FacetKeys.Adult));

        await Assert.That(chip.Value)
            .IsEqualTo(Messages.For(Locales.SourceTag, "facet.value.included"));
        await Assert.That(chip.RemoveHref).DoesNotContain(FacetKeys.Adult);
    }

    [Test]
    public async Task ThePlainListingSaysWhichOfTheTwoItIs()
    {
        // A text browser can't see a checkbox, so the line naming what this listing is must say which state it's in.
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
        // Built from an unfiltered catalogue this would offer "Adult (1)" and submit ?genre=Adult,
        // which this listing returns nothing for — a count promising a result it can't deliver.
        var html = await Render.PageAsync<FindAGame>([]);

        await Assert.That(html).DoesNotContain($"value=\"{AdultContent.Genre}\"");
    }

    [Test]
    public async Task TheFiguresBuiltInCodeStillCountTheWholeCatalogue()
    {
        // The default belongs to the listing surface only; aggregates built in code (never reading a
        // querystring) must go on counting adult games exactly as they count archived ones.
        var everything = await Queries.ListAsync(new GameFilter { IncludeArchived = true });
        var dashboard = await Queries.EcosystemAsync();
        var archived = await Queries.ListAsync(new GameFilter { Band = ActivityBand.Archived });

        await Assert.That(everything.Select(g => g.Slug)).Contains(Adult);
        await Assert.That(dashboard.ListedGames).IsEqualTo(everything.Count - archived.Count);
    }
}
