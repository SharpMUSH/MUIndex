using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// The front page after the went-dark/came-back split: newly discovered and trending this week are
/// what's left, with a link to <c>/activity</c> for the two that moved.
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

        // Not the literal heading words: "came back" also appears in the activity-link sentence
        // below the feeds, so the section ids are what actually distinguishes "drawn" from "linked".
        await Assert.That(html).DoesNotContain("id=\"feed-dark\"");
        await Assert.That(html).DoesNotContain("id=\"feed-back\"");
    }

    [Test]
    public async Task ThePageLinksToActivityForWhatMoved()
    {
        var html = await Render.PageAsync<Home>([]);

        await Assert.That(html).Contains("href=\"/activity\"");
        await Assert.That(html).Contains(Messages.For(Locales.SourceTag, "home.activityLink"));
    }

    [Test]
    public async Task TheTrendingRowNamesTheGameTheFixtureMarksUp()
    {
        // M*U*S*H is fixed at Growth: GrowthDirection.Up, so it's the one game on the fixture's
        // trending board (FixtureGameQueries.TrendingRow) — same rule the rankings page checks.
        var html = await Render.PageAsync<Home>([]);
        var section = html[html.IndexOf("id=\"feed-trending\"", StringComparison.Ordinal)..];

        await Assert.That(section).Contains("M*U*S*H");
        await Assert.That(Render.Words(section)).Contains("+47%");
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
        await Assert.That(text).Contains(Messages.For(Locales.SourceTag, "home.plain.activityLink"));
    }
}
