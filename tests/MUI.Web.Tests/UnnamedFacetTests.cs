using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Components;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// Games listed under their address because they published no name: one parameter drops them from
/// the API, and the browse panel offers the same switch.
/// </summary>
public class UnnamedFacetTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static GameFacetRow Row(string name) =>
        new(
            new GameSummary(
                Guid.NewGuid(), name.Replace(':', '-'), name, Tagline: null, LifecycleState.Active,
                IsClaimed: false, PlayersNow: 1, Codebase: null, MeasuredProtocols: [],
                LastReachableAt: Now),
            ActivityBand.PlayersNow,
            FacetedSearch.LastSeenOf(Now, Now),
            TlsMeasured: false,
            Charset: null,
            Language: null,
            Codebase: null,
            Family: null,
            Genre: null,
            IsAdult: false,
            Uncounted: false,
            Unreachable: false);

    private static readonly GameFacetRow[] Rows =
        [Row("Convergence MUSH"), Row("mud.example.org:4000"), Row("203.0.113.7:23")];

    [Test]
    public async Task TheApiReadsTheExclusionAndEchoesIt()
    {
        await Assert.That(GameFilterBinding.TryRead("?unnamed=!yes", out var query, out var error))
            .IsTrue().Because(error ?? string.Empty);

        await Assert.That(query.Filter.Unnamed).IsEqualTo(FacetChoice.Not(FacetTokens.Yes));
        await Assert.That(query.Echo.Unnamed).IsEqualTo("!yes");

        var listing = FacetedSearch.Search(Rows, query.Filter);

        await Assert.That(listing.Games.Select(g => g.Name)).IsEquivalentTo(new[] { "Convergence MUSH" });
    }

    [Test]
    public async Task UnsetTheyAreStillListed()
    {
        await Assert.That(GameFilterBinding.TryRead(string.Empty, out var query, out _)).IsTrue();

        await Assert.That(query.Filter.Unnamed).IsNull();
        await Assert.That(query.Echo.Unnamed).IsNull();
        await Assert.That(FacetedSearch.Search(Rows, query.Filter).Games.Count).IsEqualTo(3);
    }

    [Test]
    public async Task AWordOutsideTheVocabularyIsRefused()
    {
        await Assert.That(GameFilterBinding.TryRead("?unnamed=no", out _, out var error)).IsFalse();
        await Assert.That(error).Contains("unnamed");
    }

    [Test]
    public async Task ThePanelOffersTheSwitchWithALinkThatHidesThem()
    {
        var listing = FacetedSearch.Search(Rows, new GameFilter());

        var html = await Render.ComponentAsync<FacetPanel>(new()
        {
            ["Facets"] = listing.Facets,
            ["Filter"] = new GameFilter(),
        });

        await Assert.That(Render.Words(html))
            .Contains(Messages.For(Locales.SourceTag, "facet.unnamed.yes"));
        await Assert.That(html).Contains("href=\"/games?unnamed=%21yes\"");
    }

    [Test]
    public async Task HidingThemIsAChipThatSaysItIsOurListing()
    {
        var filter = new GameFilter { Unnamed = FacetChoice.Not(FacetTokens.Yes) };
        var listing = FacetedSearch.Search(Rows, filter);

        var html = await Render.ComponentAsync<FacetPanel>(new()
        {
            ["Facets"] = listing.Facets,
            ["Filter"] = filter,
            ["Query"] = "?unnamed=!yes",
        });

        await Assert.That(Render.Words(html))
            .Contains(Messages.For(Locales.SourceTag, "facet.excluded.unnamed"));
    }
}
