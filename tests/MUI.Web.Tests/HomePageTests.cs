using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// The front page after the went-dark/came-back split: newly discovered and trending this week are
/// what's left. The other two liveness feeds live on <c>/crawler</c> now, reachable from the nav
/// rather than from a link on this page.
/// </summary>
public class HomePageTests
{
    private static readonly DateTimeOffset Now = FixtureGameQueries.Now;
    private static readonly FixtureGameQueries Queries = new();

    [Test]
    public async Task ThePageDrawsNewlyDiscoveredAndTrendingButNotWentDarkOrCameBack()
    {
        var html = await Render.PageAsync<Home>([]);

        await Assert.That(html).Contains(Messages.For(Locales.SourceTag, "feed.newlyDiscovered"));
        await Assert.That(html).Contains(Messages.For(Locales.SourceTag, "home.trending.title"));

        await Assert.That(html).DoesNotContain("id=\"feed-dark\"");
        await Assert.That(html).DoesNotContain("id=\"feed-back\"");
        await Assert.That(html).DoesNotContain("href=\"/activity\"");
    }

    [Test]
    public async Task TheTrendingRowNamesTheGameTheFixtureMarksUp()
    {
        // M*U*S*H is fixed at Growth: GrowthDirection.Up, so it's the one game on the fixture's
        // trending board (FixtureGameQueries.TrendingRow) — same rule the rankings page checks.
        var html = await Render.PageAsync<Home>([]);
        var section = html[html.IndexOf("id=\"feed-trending\"", StringComparison.Ordinal)..];

        await Assert.That(section).Contains("M*U*S*H");
        await Assert.That(Render.Words(section)).Contains("+1");
    }

    /// <summary>
    /// The tile is "answering but uncounted", and the fixture holds the counter-example that says so.
    /// </summary>
    /// <remarks>
    /// Midnight Sun answers and cannot be counted; Hollow Bell, Gaslight Row and Verdigris carry no
    /// count for the opposite reason — we never got in — and the fixture's own remarks say so beside
    /// each of them. Counting "no count" put all four in the tile, which is rule 2's third state
    /// collapsed into the middle one, and is where the figure's drift came from: a quiet game is
    /// probed every six hours against a two-hour freshness window, so it fell into the tile for four
    /// hours out of every six and back out again.
    /// </remarks>
    [Test]
    public async Task TheUnknownPopulationTileCountsOnlyGamesWeGotIntoAndCouldNotCount()
    {
        var listing = await Queries.ListAsync(new GameFilter { IncludeArchived = true });

        await Assert.That(listing.Where(g => g.AnsweredUncounted).Select(g => g.Slug))
            .IsEquivalentTo(new[] { "midnight-sun" });
        await Assert.That(SiteCounts.From(listing).CountUnknown).IsEqualTo(1);

        // Four games carry no count, and the tile is the one figure that must tell the two reasons
        // apart.
        await Assert.That(listing.Count(g => g.PlayersNow is null)).IsEqualTo(4);
    }

    [Test]
    public async Task ThePlainMirrorCarriesTheSameSplit()
    {
        var listing = await Queries.ListAsync(new GameFilter { IncludeArchived = true });
        var counts = SiteCounts.From(listing);
        var feeds = await Queries.FeedsAsync();
        var trending = (await Queries.RankingsAsync()).TrendingThisWeek;

        var text = PlainText.RenderHome(Locales.SourceTag, counts, feeds, trending, CrawlerPulse.Unknown, Now);

        await Assert.That(text).Contains("NEWLY DISCOVERED");
        await Assert.That(text).Contains(Messages.For(Locales.SourceTag, "home.plain.trending").ToUpperInvariant());
        await Assert.That(text).DoesNotContain("WENT DARK");
        await Assert.That(text).DoesNotContain("CAME BACK");
    }
}
