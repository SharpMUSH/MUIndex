using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Components;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

/// <summary>
/// The listing's order, and the one thing it may not do with the games it cannot rank.
/// </summary>
/// <remarks>
/// A large share of this catalogue answers with nothing we can count — we got in and the
/// <c>WHO</c> was past our parser, or the game published no <c>PLAYERS</c>. Sorted as zeroes those
/// games pile up at the bottom of "players on now" indistinguishable from the games we measured and
/// found empty, which is this project's central claim made backwards on the page most likely to be
/// read off. Every test here is about that one sentence.
/// </remarks>
public class SortingTests
{
    private static readonly FixtureGameQueries Queries = new();

    [Test]
    public async Task AGameWeCouldNotCountSortsAfterEveryGameWeCould()
    {
        var listing = await Queries.SearchAsync(new GameFilter { Sort = GameSort.Players });
        var counted = listing.Games.Select(g => g.PlayersNow is not null).ToList();

        // Every counted game before every uncounted one: no true may follow a false.
        await Assert.That(counted.SkipWhile(c => c).Any(c => c)).IsFalse();
        await Assert.That(counted).Contains(true);
        await Assert.That(counted).Contains(false);
    }

    [Test]
    public async Task AMeasuredZeroSortsAmongTheCountsAndNotAmongTheUnknowns()
    {
        // The whole distinction, in one row. We got in and nobody was there, which is a measurement;
        // the games below the break are ones we could not count at all. Ranking the zero with them
        // would throw away the difference the sort exists to preserve.
        var listing = await Queries.SearchAsync(new GameFilter { Sort = GameSort.Players });
        var zero = listing.Games.Single(g => g.PlayersNow is 0);
        var firstUnknown = listing.Games.First(g => g.PlayersNow is null);

        await Assert.That(listing.Games.ToList().IndexOf(zero))
            .IsLessThan(listing.Games.ToList().IndexOf(firstUnknown));

        await Assert.That(GameSorting.IsUnranked(zero, GameSort.Players)).IsFalse();
    }

    [Test]
    public async Task AGameWeHaveNeverReachedIsNotAGameWeReachedLongAgo()
    {
        // The same rule on the other sort. Null has no date, so it cannot be the oldest date — and
        // ordering it as one would date our own ignorance as somebody's outage.
        var never = Summary("never", players: null, reached: null);
        var ancient = Summary("ancient", players: null, reached: FixtureGameQueries.Now.AddYears(-4));

        var order = GameSorting.Apply([never, ancient], GameSort.Reached);

        await Assert.That(order[0].Name).IsEqualTo("ancient");
        await Assert.That(GameSorting.IsUnranked(never, GameSort.Reached)).IsTrue();
        await Assert.That(GameSorting.IsUnranked(ancient, GameSort.Reached)).IsFalse();
    }

    [Test]
    public async Task TheDefaultOrderRanksNobody()
    {
        // A listing that arrives pre-ranked makes an editorial claim the reader never asked for.
        // Sorting is a thing a reader chooses, so the default is the one order that ranks nothing.
        await Assert.That(new GameFilter().Sort).IsEqualTo(GameSort.Name);

        GameFilterBinding.TryRead(string.Empty, out var unasked, out _);
        await Assert.That(unasked.Filter.Sort).IsEqualTo(GameSort.Name);

        var listing = await Queries.SearchAsync(new GameFilter());
        await Assert.That(listing.Games.Select(g => g.Name))
            .IsEquivalentTo([.. listing.Games.Select(g => g.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)]);
    }

    [Test]
    public async Task TheListingSaysWhereTheSortRanOutOfThingsToRank()
    {
        // Without this the page reads 219, 71, 15, 9, 0, and then a long tail of rows showing no
        // number — a list that looks exactly like the lie. The break is a real list item, so it is in
        // the accessibility tree rather than being a border a sighted reader might notice.
        // Unsorted, there is nothing the order failed to rank and so nothing to announce.
        await Assert.That(await Render.PageAsync<Games>([])).DoesNotContain("unranked-break");

        var sorted = await Render.PageAsync<Games>([], "?sort=players");

        await Assert.That(sorted).Contains("unranked-break");
        await Assert.That(Render.Words(sorted)).Contains("from here");
        await Assert.That(Render.Words(sorted)).Contains(FacetWords.Unranked(GameSort.Players));
        await Assert.That(FacetWords.Unranked(GameSort.Players)).Contains("not zero");
    }

    [Test]
    public async Task ARowNeverPrintsAnAgeAsNowAgo()
    {
        // Relative.Format's freshest bucket is the word "now", and every caller appending " ago" to
        // it wrote "last reached now ago" for the ninety seconds after each probe — which, on a
        // listing rendered while a crawl is running, was most of the rows on the page.
        var html = Render.Words(await Render.PageAsync<Games>([]));

        await Assert.That(html).DoesNotContain("now ago");
        await Assert.That(Relative.Ago(TimeSpan.FromSeconds(10))).IsEqualTo("just now");
        await Assert.That(Relative.Ago(TimeSpan.FromMinutes(20))).IsEqualTo("20m ago");
    }

    [Test]
    public async Task ThePlainSurfaceSaysWhatOrderItIsInAndWhereTheBreakFell()
    {
        // A sorted list that does not say what it is sorted by is one a reader has to
        // reverse-engineer from the first few rows, which is how the tail gets misread. If a fact
        // only survives graphically, its graphic was decoration.
        await Assert.That(GameFilterBinding.TryRead("?sort=players", out var query, out _)).IsTrue();

        var text = PlainText.RenderListing(
            await Queries.SearchAsync(query.Filter), query.Filter, FixtureGameQueries.Now);

        await Assert.That(Render.Words(text)).Contains($"Sorted by {FacetWords.Sort(GameSort.Players)}");
        await Assert.That(Render.Words(text)).Contains(FacetWords.Unranked(GameSort.Players));

        // And the parameter that changes it, because a text browser cannot operate a <select>.
        await Assert.That(text).Contains($"?{FacetKeys.Sort}=");
    }

    [Test]
    public async Task SortingMovesNoFacetCount()
    {
        // Counts are taken over a set and a set has no order, so this is true by construction — and
        // asserted anyway, because the day it stops being true the panel starts promising one number
        // and delivering another depending on how the reader happened to be reading.
        var unsorted = await Queries.SearchAsync(new GameFilter());
        var sorted = await Queries.SearchAsync(new GameFilter { Sort = GameSort.Players });

        await Assert.That(Counts(sorted)).IsEquivalentTo(Counts(unsorted));
        await Assert.That(sorted.Games.Count).IsEqualTo(unsorted.Games.Count);

        static List<string> Counts(GameListing listing) =>
        [
            .. listing.Facets.SelectMany(g => g.Values.Select(v => $"{g.Key}/{v.Token}={v.Count}")).Order(),
        ];
    }

    private static GameSummary Summary(string name, int? players, DateTimeOffset? reached) => new(
        Guid.NewGuid(), name, name, null, LifecycleState.Active, false, players, null, [], reached);
}
