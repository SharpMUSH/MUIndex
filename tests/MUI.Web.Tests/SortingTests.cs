using MUI.Web.Localization;
using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Components;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

/// <summary>
/// The listing's order, and the one thing it may not do with the games it cannot rank.
/// </summary>
/// <remarks>Games we can't count must never sort as zeroes — that would make them indistinguishable from games measured and found empty.</remarks>
public class SortingTests
{
    private static readonly FixtureGameQueries Queries = new();

    [Test]
    public async Task AGameWeCouldNotCountSortsAfterEveryGameWeCould()
    {
        var listing = await Queries.SearchAsync(new GameFilter { Sort = GameSort.Players });
        var counted = listing.Games.Select(g => g.PlayersNow is not null).ToList();

        // No true may follow a false.
        await Assert.That(counted.SkipWhile(c => c).Any(c => c)).IsFalse();
        await Assert.That(counted).Contains(true);
        await Assert.That(counted).Contains(false);
    }

    [Test]
    public async Task AMeasuredZeroSortsAmongTheCountsAndNotAmongTheUnknowns()
    {
        // A measured zero must rank above the uncounted break, not with it.
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
        // Null has no date and cannot be the oldest date — ordering it as one would date our own ignorance as somebody's outage.
        var never = Summary("never", players: null, reached: null);
        var ancient = Summary("ancient", players: null, reached: FixtureGameQueries.Now.AddYears(-4));

        var order = GameSorting.Apply([never, ancient], GameSort.Reached);

        await Assert.That(order[0].Name).IsEqualTo("ancient");
        await Assert.That(GameSorting.IsUnranked(never, GameSort.Reached)).IsTrue();
        await Assert.That(GameSorting.IsUnranked(ancient, GameSort.Reached)).IsFalse();
    }

    [Test]
    public async Task AGameNeverSeenIsNotAGameFoundLongAgo()
    {
        // Every real game has a first-seen date (the column is not nullable), but the model allows
        // null so a test double that doesn't set it isn't silently dated to now.
        var never = Summary("never", players: null, reached: null);
        var ancient = Summary("ancient", players: null, reached: null) with
        {
            FirstSeenAt = FixtureGameQueries.Now.AddYears(-4),
        };

        var order = GameSorting.Apply([never, ancient], GameSort.Discovered);

        await Assert.That(order[0].Name).IsEqualTo("ancient");
        await Assert.That(GameSorting.IsUnranked(never, GameSort.Discovered)).IsTrue();
        await Assert.That(GameSorting.IsUnranked(ancient, GameSort.Discovered)).IsFalse();
    }

    [Test]
    public async Task NewestDiscoveredSortsFirstAndTheListingCanBeAskedForIt()
    {
        var listing = await Queries.SearchAsync(new GameFilter { Sort = GameSort.Discovered, IncludeArchived = true });

        var dates = listing.Games.Select(g => g.FirstSeenAt).Where(d => d is not null).Cast<DateTimeOffset>().ToList();

        await Assert.That(dates).IsNotEmpty();
        await Assert.That(dates.SequenceEqual(dates.OrderByDescending(d => d))).IsTrue();
    }

    [Test]
    public async Task TheDefaultOrderLeadsWithTheMeasurementRatherThanTheSpelling()
    {
        // Default was previously the alphabet — an order too, but one ranking by spelling instead of
        // measurement for no reason anybody chose.
        await Assert.That(new GameFilter().Sort).IsEqualTo(GameSort.Players);

        GameFilterBinding.TryRead(string.Empty, out var unasked, out _);
        await Assert.That(unasked.Filter.Sort).IsEqualTo(GameSort.Players);
    }

    [Test]
    public async Task ThePageAndTheApiCannotDisagreeAboutWhatOrderAUrlAsksFor()
    {
        GameFilterBinding.TryRead(string.Empty, out var unasked, out _);

        await Assert.That(unasked.Filter.Sort).IsEqualTo(new GameFilter().Sort);
    }

    [Test]
    public async Task TheAlphabetIsStillOneClickAwayAndStillMeansWhatItMeant()
    {
        // A default change may not re-point a URL that named an order explicitly.
        await Assert.That(GameFilterBinding.TryRead("?sort=name", out var query, out _)).IsTrue();
        await Assert.That(query.Filter.Sort).IsEqualTo(GameSort.Name);

        var listing = await Queries.SearchAsync(query.Filter);
        await Assert.That(listing.Games.Select(g => g.Name))
            .IsEquivalentTo([.. listing.Games.Select(g => g.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)]);
    }

    [Test]
    public async Task TheListingSaysWhereTheSortRanOutOfThingsToRank()
    {
        // Alphabetically there is nothing the order failed to rank, so nothing to announce.
        await Assert.That(await Render.PageAsync<Games>([], "?sort=name"))
            .DoesNotContain("unranked-break");

        var sorted = await Render.PageAsync<Games>([], "?sort=players");

        await Assert.That(sorted).Contains("unranked-break");
        await Assert.That(Render.Words(sorted)).Contains("from here");
        await Assert.That(Render.Words(sorted)).Contains(FacetWords.Unranked(Locales.SourceTag, GameSort.Players));
        await Assert.That(FacetWords.Unranked(Locales.SourceTag, GameSort.Players)).IsEqualTo("Unknown count");
    }

    [Test]
    public async Task ARowNeverPrintsAnAgeAsNowAgo()
    {
        // Relative.Format's freshest bucket is "now"; appending " ago" to it used to write "last reached now ago".
        var html = Render.Words(await Render.PageAsync<Games>([]));

        await Assert.That(html).DoesNotContain("now ago");
        await Assert.That(Relative.Ago(Locales.SourceTag, TimeSpan.FromSeconds(10))).IsEqualTo("just now");
        await Assert.That(Relative.Ago(Locales.SourceTag, TimeSpan.FromMinutes(20))).IsEqualTo("20m ago");
    }

    [Test]
    public async Task ThePlainSurfaceSaysWhatOrderItIsInAndWhereTheBreakFell()
    {
        await Assert.That(GameFilterBinding.TryRead("?sort=players", out var query, out _)).IsTrue();

        var text = PlainText.RenderListing(
            await Queries.SearchAsync(query.Filter), query.Filter, FixtureGameQueries.Now);

        await Assert.That(Render.Words(text)).Contains($"Sorted by {FacetWords.Sort(Locales.SourceTag, GameSort.Players)}");
        await Assert.That(Render.Words(text)).Contains(FacetWords.Unranked(Locales.SourceTag, GameSort.Players));

        // The parameter that changes it, since a text browser can't operate a <select>.
        await Assert.That(text).Contains($"?{FacetKeys.Sort}=");
    }

    [Test]
    public async Task SortingMovesNoFacetCount()
    {
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
        // A game steady at forty outranks one that happens to have five on right now; they swap when the question is the biggest night either had.
        var steady = Windowed("steady", playersNow: 2, median: 40, peak: 44, samples: 300);
        var spiking = Windowed("spiking", playersNow: 5, median: 3, peak: 90, samples: 300);

        var byMedian = GameSorting.Apply([spiking, steady], GameSort.MedianWeek);
        var byPeak = GameSorting.Apply([steady, spiking], GameSort.PeakWeek);

        await Assert.That(byMedian[0].Name).IsEqualTo("steady");
        await Assert.That(byPeak[0].Name).IsEqualTo("spiking");
    }

    [Test]
    public async Task AMedianNeedsEnoughCountsToBeAMedianAndAPeakDoesNot()
    {
        // A median over four probes would rank our crawl schedule; a peak is one true observation however few there were.
        var thin = Windowed("thin", playersNow: 1, median: 90, peak: 90, samples: 4);

        await Assert.That(GameSorting.IsUnranked(thin, GameSort.MedianWeek)).IsTrue();
        await Assert.That(GameSorting.IsUnranked(thin, GameSort.PeakWeek)).IsFalse();

        // Enough measurements clears the floor — it's a floor, not a wall.
        var thick = thin with { PlayersOverWindow = thin.PlayersOverWindow! with { Samples = 24 } };

        await Assert.That(GameSorting.IsUnranked(thick, GameSort.MedianWeek)).IsFalse();

        // Not asserted here that this matches /rankings' floor — a compiler error
        // (NpgsqlGameQueries.MinimumRankingSamples = SortWindows.MinimumSamples) guarantees that instead.
    }

    [Test]
    public async Task AGameWithNoWindowSortsBelowTheBreakAndNeverAsAZero()
    {
        // A game we couldn't count in the window has no typical count — not a typical count of zero.
        var counted = Windowed("counted", playersNow: 0, median: 2, peak: 6, samples: 200);
        var uncountable = Summary("uncountable", players: null, reached: FixtureGameQueries.Now);

        var order = GameSorting.Apply([uncountable, counted], GameSort.MedianMonth);

        await Assert.That(order[0].Name).IsEqualTo("counted");
        await Assert.That(GameSorting.IsUnranked(uncountable, GameSort.MedianMonth)).IsTrue();
        await Assert.That(FacetWords.Unranked(Locales.SourceTag, GameSort.MedianMonth)).Contains("not a typical count of zero");
        await Assert.That(FacetWords.Unranked(Locales.SourceTag, GameSort.PeakMonth)).Contains("not a game nobody was on");
    }

    [Test]
    public async Task TheRowSaysWhatTheWindowSortRankedItOnAndOverHowManyCounts()
    {
        // The sample tally rides along because a mean is a mean of something (§15.7).
        var html = Render.Words(await Render.PageAsync<Games>([], "?sort=medianMonth"));

        await Assert.That(html).Contains("median");
        await Assert.That(html).Contains("counts");
        await Assert.That(html).Contains("30d");

        await Assert.That(GameFilterBinding.TryRead("?sort=peakWeek", out var query, out _)).IsTrue();

        var text = PlainText.RenderListing(
            await Queries.SearchAsync(query.Filter), query.Filter, FixtureGameQueries.Now);

        await Assert.That(Render.Words(text)).Contains("Ranked on:");
        await Assert.That(Render.Words(text)).Contains("at once");
    }

    [Test]
    public async Task EverySortHasWordsAndAGroupToBeOfferedUnder()
    {
        // A member added without a label renders as "name" silently.
        foreach (var sort in Enum.GetValues<GameSort>())
        {
            await Assert.That(FacetWords.Sort(Locales.SourceTag, sort)).IsNotEmpty();
            await Assert.That(FacetWords.SortGroup(Locales.SourceTag, sort)).IsNotEmpty();

            if (sort is not GameSort.Name)
            {
                await Assert.That(FacetWords.Unranked(Locales.SourceTag, sort))
                    .IsNotEmpty()
                    .Because($"{sort} can leave games unranked and has to say what they have in common");
            }
        }

        await Assert.That(Enum.GetValues<GameSort>().Select(s => FacetWords.Sort(Locales.SourceTag, s)).Distinct().Count())
            .IsEqualTo(Enum.GetValues<GameSort>().Length);
    }

    private static GameSummary Summary(string name, int? players, DateTimeOffset? reached) => new(
        Guid.NewGuid(), name, name, null, LifecycleState.Active, false, players, null, [], reached);

    private static GameSummary Windowed(
        string name, int? playersNow, int median, int peak, int samples) =>
        Summary(name, playersNow, FixtureGameQueries.Now) with
        {
            PlayersOverWindow = new PresenceWindow(SortWindows.Week, median, peak, samples),
        };
}
