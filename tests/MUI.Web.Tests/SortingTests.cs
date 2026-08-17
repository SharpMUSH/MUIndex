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
    public async Task TheDefaultOrderLeadsWithTheMeasurementRatherThanTheSpelling()
    {
        // The default was the alphabet, on the argument that a pre-ranked listing makes an editorial
        // claim. The argument holds against a ranking and not against this: the alphabet is an order
        // too, and the one it produces puts whichever game starts with a digit above the whole
        // hobby for no reason anybody chose. Neither is an opinion of ours — one reads a measurement
        // and the other reads a spelling — so the default is the one that answers what a reader came
        // for. What it may never do is rank an unknown, and the test below that one still holds.
        await Assert.That(new GameFilter().Sort).IsEqualTo(GameSort.Players);

        GameFilterBinding.TryRead(string.Empty, out var unasked, out _);
        await Assert.That(unasked.Filter.Sort).IsEqualTo(GameSort.Players);
    }

    [Test]
    public async Task ThePageAndTheApiCannotDisagreeAboutWhatOrderAUrlAsksFor()
    {
        // The default lives on GameFilter and the binding reads it off a default instance rather
        // than naming it a second time. Two literals is how /games?band=quiet and
        // /api/games?band=quiet come to answer one URL two ways.
        GameFilterBinding.TryRead(string.Empty, out var unasked, out _);

        await Assert.That(unasked.Filter.Sort).IsEqualTo(new GameFilter().Sort);
    }

    [Test]
    public async Task TheAlphabetIsStillOneClickAwayAndStillMeansWhatItMeant()
    {
        // A default changing may not re-point a URL that named an order explicitly.
        await Assert.That(GameFilterBinding.TryRead("?sort=name", out var query, out _)).IsTrue();
        await Assert.That(query.Filter.Sort).IsEqualTo(GameSort.Name);

        var listing = await Queries.SearchAsync(query.Filter);
        await Assert.That(listing.Games.Select(g => g.Name))
            .IsEquivalentTo([.. listing.Games.Select(g => g.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)]);
    }

    [Test]
    public async Task TheListingSaysWhereTheSortRanOutOfThingsToRank()
    {
        // Without this the page reads 219, 71, 15, 9, 0, and then a long tail of rows showing no
        // number — a list that looks exactly like the lie. The break is a real list item, so it is in
        // the accessibility tree rather than being a border a sighted reader might notice.
        // Alphabetically there is nothing the order failed to rank and so nothing to announce.
        await Assert.That(await Render.PageAsync<Games>([], "?sort=name"))
            .DoesNotContain("unranked-break");

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

    [Test]
    public async Task AWindowSortRanksOnTheWindowAndNotOnTheCountOnTheRow()
    {
        // The point of the three window orders: a game with two people on right now and a steady
        // average outranks one that happens to have five on at the moment this page was drawn.
        var steady = Windowed("steady", playersNow: 2, average: 40, peak: 44, samples: 300);
        var spiking = Windowed("spiking", playersNow: 5, average: 3, peak: 90, samples: 300);

        var byAverage = GameSorting.Apply([spiking, steady], GameSort.AverageWeek);
        var byPeak = GameSorting.Apply([steady, spiking], GameSort.PeakWeek);

        await Assert.That(byAverage[0].Name).IsEqualTo("steady");
        await Assert.That(byPeak[0].Name).IsEqualTo("spiking");
    }

    [Test]
    public async Task AnAverageNeedsEnoughCountsToBeAnAverageAndAPeakDoesNot()
    {
        // They fail differently, so they are floored differently. A mean over four probes is not a
        // mean of anything and would put a game found on Friday above one measured three hundred
        // times — that is ranking our crawl schedule. A peak is one observation and is true however
        // few of them there were: we counted that many people on at once, and suppressing it would
        // hide a measurement we actually took.
        var thin = Windowed("thin", playersNow: 1, average: 90, peak: 90, samples: 4);

        await Assert.That(GameSorting.IsUnranked(thin, GameSort.AverageWeek)).IsTrue();
        await Assert.That(GameSorting.IsUnranked(thin, GameSort.PeakWeek)).IsFalse();

        // A game measured enough times clears it, so the floor is a floor and not a wall.
        var thick = thin with { PlayersOverWindow = thin.PlayersOverWindow! with { Samples = 24 } };

        await Assert.That(GameSorting.IsUnranked(thick, GameSort.AverageWeek)).IsFalse();

        // That this floor is the one /rankings puts under its median is not asserted here: it is
        // NpgsqlGameQueries.MinimumRankingSamples = SortWindows.MinimumSamples, so the two cannot
        // drift without a compiler error, which is a better guarantee than a test.
    }

    [Test]
    public async Task AGameWithNoWindowSortsBelowTheBreakAndNeverAsAZero()
    {
        // The same rule as every other sort here, on the newest columns. A game we could not count
        // in the window has no average, and an average of nought is a different claim entirely.
        var counted = Windowed("counted", playersNow: 0, average: 1.5, peak: 6, samples: 200);
        var uncountable = Summary("uncountable", players: null, reached: FixtureGameQueries.Now);

        var order = GameSorting.Apply([uncountable, counted], GameSort.AverageMonth);

        await Assert.That(order[0].Name).IsEqualTo("counted");
        await Assert.That(GameSorting.IsUnranked(uncountable, GameSort.AverageMonth)).IsTrue();
        await Assert.That(FacetWords.Unranked(GameSort.AverageMonth)).Contains("not an average of zero");
        await Assert.That(FacetWords.Unranked(GameSort.PeakMonth)).Contains("not a game nobody was on");
    }

    [Test]
    public async Task TheRowSaysWhatTheWindowSortRankedItOnAndOverHowManyCounts()
    {
        // A listing ordered by a figure that appears nowhere on its rows is one a reader has to take
        // on trust — and the sample tally rides along because a mean is a mean of something (§15.7):
        // thirty counts and three hundred are not the same evidence.
        var html = Render.Words(await Render.PageAsync<Games>([], "?sort=averageMonth"));

        await Assert.That(html).Contains("avg");
        await Assert.That(html).Contains("counts");
        await Assert.That(html).Contains("30d");

        // And in plain text, or the figure on the rendered row is decoration.
        await Assert.That(GameFilterBinding.TryRead("?sort=peakWeek", out var query, out _)).IsTrue();

        var text = PlainText.RenderListing(
            await Queries.SearchAsync(query.Filter), query.Filter, FixtureGameQueries.Now);

        await Assert.That(Render.Words(text)).Contains("Ranked on:");
        await Assert.That(Render.Words(text)).Contains("at once");
    }

    [Test]
    public async Task EverySortHasWordsAndAGroupToBeOfferedUnder()
    {
        // The panel enumerates the enum, so a member added without a label renders an option reading
        // "name" — which is an order the reader did not choose, silently.
        foreach (var sort in Enum.GetValues<GameSort>())
        {
            await Assert.That(FacetWords.Sort(sort)).IsNotEmpty();
            await Assert.That(FacetWords.SortGroup(sort)).IsNotEmpty();

            if (sort is not GameSort.Name)
            {
                await Assert.That(FacetWords.Unranked(sort))
                    .IsNotEmpty()
                    .Because($"{sort} can leave games unranked and has to say what they have in common");
            }
        }

        // Distinct labels, or two options in the control are one choice wearing two rows.
        await Assert.That(Enum.GetValues<GameSort>().Select(FacetWords.Sort).Distinct().Count())
            .IsEqualTo(Enum.GetValues<GameSort>().Length);
    }

    private static GameSummary Summary(string name, int? players, DateTimeOffset? reached) => new(
        Guid.NewGuid(), name, name, null, LifecycleState.Active, false, players, null, [], reached);

    private static GameSummary Windowed(
        string name, int? playersNow, double average, int peak, int samples) =>
        Summary(name, playersNow, FixtureGameQueries.Now) with
        {
            PlayersOverWindow = new PresenceWindow(SortWindows.Week, average, peak, samples),
        };
}
