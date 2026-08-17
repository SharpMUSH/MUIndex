using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Components.Pages;
using MUI.Web.Data;
using MUI.Web.Fixtures;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace MUI.Web.Tests;

/// <summary>
/// The game page's own in-page links, and the sections they are supposed to land on.
/// </summary>
/// <remarks>
/// A footer link to <c>#changed</c> is not a broken link a browser reports: it scrolls nowhere and
/// says nothing, and to a screen reader following it the page simply does not move. The change list
/// renders only for a game with a recorded change, so the link has to be conditioned on the same
/// thing the heading is — the fixture gives every game two changes, which is why nothing had ever
/// rendered this page without one.
/// </remarks>
public class GameAnchorsTests
{
    [Test]
    public async Task TheFooterLinksToTheChangeListOnlyWhereThereIsOne()
    {
        var withChanges = await RenderAsync(strip: false);

        await Assert.That(withChanges).Contains("id=\"changed\"");
        await Assert.That(withChanges).Contains("href=\"#changed\"");

        var without = await RenderAsync(strip: true);

        await Assert.That(without).DoesNotContain("id=\"changed\"");
        await Assert.That(without).DoesNotContain("href=\"#changed\"");

        // The rest of the footer is untouched, so what went is the link and not the row it sat in.
        await Assert.That(without).Contains("href=\"#declared\"");
        await Assert.That(without).Contains("id=\"declared\"");
        await Assert.That(without).Contains("?plain=1");
    }

    /// <summary>
    /// Every anchor the page links to is an id the page rendered.
    /// </summary>
    /// <remarks>
    /// Stated as the general rule rather than about <c>#changed</c> alone, so a section that becomes
    /// conditional later is caught by the test that already exists.
    /// </remarks>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EveryInPageLinkLandsOnSomething(bool strip)
    {
        var html = await RenderAsync(strip);

        foreach (var target in System.Text.RegularExpressions.Regex
            .Matches(html, "href=\"#([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal))
        {
            await Assert.That(html)
                .Contains($"id=\"{target}\"")
                .Because($"the page links to #{target} and renders no such id");
        }
    }

    private static Task<string> RenderAsync(bool strip) =>
        Render.ComponentAsync<Game>(
            new() { ["Slug"] = "m-u-s-h" },
            services =>
            {
                var fixture = new FixtureGameQueries();

                services.AddSingleton<IGameQueries>(strip ? new WithoutChanges(fixture) : fixture);
                services.AddSingleton<IAvailabilityHistory>(fixture);
                services.AddSingleton(TimeProvider.System);
                services.AddSingleton<NavigationManager>(new Nowhere());
                services.AddSingleton(new CatalogueSource(IsMeasured: false));
            });

    /// <summary>The fixture, with the change history taken off the page it answers with.</summary>
    /// <remarks>
    /// Every fixture game carries two changes, so this is the only way to see the page a game with a
    /// quiet record actually gets. It delegates rather than reimplements: what is under test is one
    /// conditional in the markup, and a second fixture would drift from the first.
    /// </remarks>
    private sealed class WithoutChanges(IGameQueries inner) : IGameQueries
    {
        public Task<GameListing> SearchAsync(GameFilter filter, CancellationToken cancellationToken = default) =>
            inner.SearchAsync(filter, cancellationToken);

        public Task<IReadOnlyList<GameSummary>> ListAsync(GameFilter filter, CancellationToken cancellationToken = default) =>
            inner.ListAsync(filter, cancellationToken);

        public async Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default) =>
            await inner.FindAsync(slug, cancellationToken) is { } page ? page with { Changes = [] } : null;

        public async Task<GamePage?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            await inner.FindAsync(id, cancellationToken) is { } page ? page with { Changes = [] } : null;

        public Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.FindByIdAsync(id, cancellationToken);

        public Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default) =>
            inner.FeedsAsync(cancellationToken);

        public Task<EcosystemDashboard> EcosystemAsync(CancellationToken cancellationToken = default) =>
            inner.EcosystemAsync(cancellationToken);

        public Task<Rankings> RankingsAsync(
            RankingSpan span = RankingSpan.Week,
            CancellationToken cancellationToken = default) =>
            inner.RankingsAsync(span, cancellationToken);
    }

    /// <summary>A navigation manager for a component rendered outside a request.</summary>
    private sealed class Nowhere : NavigationManager
    {
        public Nowhere() => Initialize("http://localhost/", "http://localhost/g/m-u-s-h");

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
        }
    }
}
