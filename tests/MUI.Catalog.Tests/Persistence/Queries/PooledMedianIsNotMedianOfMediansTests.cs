using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// A window's median is taken over the window's whole distribution, never over its daily medians.
/// </summary>
/// <remarks>
/// <para>
/// This is the invariant that makes migration 0036's <c>presence_rollup_day.median_count</c> safe to
/// exist. That column is a <em>per-day</em> median, and <c>DailyMediansAsync</c> may read it because
/// it wants exactly one median per day. <see cref="NpgsqlGameQueries.RankingsAsync"/> and the window
/// sorts want something different: one median over every count in the span, pooled. Those two are not
/// the same number, and reading the stored one would be a quiet, plausible-looking wrong answer.
/// </para>
/// <para>
/// It was a doc comment on the migration and nothing else, which is the wrong strength for a rule
/// whose violation looks like an optimisation. The distribution below is chosen so the two answers
/// are far apart rather than coincidentally equal — median of the daily medians is <b>1</b>, median
/// of the pooled counts is <b>100</b> — so a future change that "reuses the column we already have"
/// fails here instead of shipping.
/// </para>
/// </remarks>
public class PooledMedianIsNotMedianOfMediansTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    /// <summary>
    /// Four quiet days and two busy ones.
    /// </summary>
    /// <remarks>
    /// The daily medians are 1, 1, 1, 1, 100, 100 — whose own median is <b>1</b>. Pooled, the counts
    /// are four 1s and thirty 100s: thirty-four samples, so the half-way point is the seventeenth,
    /// which lands in the hundreds and gives <b>100</b>. A game measured at a hundred players for
    /// most of its samples is busy; ranking it at 1 would be counting calendar days rather than
    /// players, which is precisely what reading a stored per-day median here would do.
    /// </remarks>
    [Test]
    public async Task AWindowSortRanksOnThePooledDistributionAndNotOnTheDailyMedians()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "lopsided", "Lopsided");
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        // Four quiet days, then two busy ones. Four days rather than one is what clears the busiest
        // ranking's coverage floor -- RankingSpans.MinimumDays is (7 + 1) / 2 for a week -- so both
        // surfaces under test actually rank the game rather than excluding it.
        for (var day = 6; day >= 3; day--)
        {
            await writer.WriteAsync(game, PresenceReading.Counted(1, FieldSource.Who), Now.AddDays(-day));
        }

        for (var day = 2; day >= 1; day--)
        {
            for (var i = 0; i < 15; i++)
            {
                await writer.WriteAsync(
                    game, PresenceReading.Counted(100, FieldSource.Who), Now.AddDays(-day).AddMinutes(i * 20));
            }
        }

        // Rolls both grains, so the window read below comes off the day rollup's histograms — the
        // same rows migration 0036 stores a per-day median beside.
        await Maintenance(db).RunAsync(Now);

        var window = (await QueriesOn(db).ListAsync(
                new GameFilter { Sort = GameSort.MedianWeek, IncludeArchived = true }))
            .Single(g => g.Slug == "lopsided")
            .PlayersOverWindow;

        await Assert.That(window).IsNotNull();
        await Assert.That(window!.Samples).IsEqualTo(34);

        // The pooled answer.
        await Assert.That(window.Median).IsEqualTo(100);

        // Stated as its own assertion rather than left implied by the one above: 1 is precisely what
        // reading presence_rollup_day.median_count here would produce, and naming it is what makes
        // this test readable as the guard it is.
        await Assert.That(window.Median).IsNotEqualTo(1);

        // And the wrong answer is shown to be genuinely on the table rather than asserted to be, so
        // this cannot quietly become vacuous: these are the per-day medians migration 0036 stores
        // beside these very rows, and their own median is the 1 rejected above.
        await using var connection = db.DataSource.CreateConnection();

        var perDay = (await connection.QueryAsync<int>(
            """
            SELECT median_count FROM presence_rollup_day
             WHERE game_id = @game AND median_count IS NOT NULL
             ORDER BY day
            """,
            new { game })).ToList();

        await Assert.That(perDay).IsEquivalentTo(new[] { 1, 1, 1, 1, 100, 100 });
        await Assert.That(MedianOf(perDay)).IsEqualTo(1);
    }

    /// <summary>
    /// The same ascending walk the rest of the codebase does, over a list this test already knows.
    /// </summary>
    /// <remarks>
    /// Written out rather than called into the production path on purpose: this stands for what a
    /// future "just read the column" change would compute, so it must not share an implementation
    /// with the thing it is meant to catch.
    /// </remarks>
    private static int MedianOf(IReadOnlyList<int> values)
    {
        var sorted = values.Order().ToList();

        return sorted[(int)Math.Ceiling(sorted.Count / 2.0) - 1];
    }

    /// <summary>The same rule for the busiest ranking, which pools over its own span.</summary>
    /// <remarks>
    /// Separate from the window sort because they are separate queries over the same rollup — sharing
    /// an invariant is not sharing an implementation, and the one that gets "optimised" later will be
    /// whichever this test does not cover.
    /// </remarks>
    [Test]
    public async Task TheBusiestRankingAlsoPoolsRatherThanAveragingDays()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "lopsided", "Lopsided");
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        // Four quiet days, then two busy ones. Four days rather than one is what clears the busiest
        // ranking's coverage floor -- RankingSpans.MinimumDays is (7 + 1) / 2 for a week -- so both
        // surfaces under test actually rank the game rather than excluding it.
        for (var day = 6; day >= 3; day--)
        {
            await writer.WriteAsync(game, PresenceReading.Counted(1, FieldSource.Who), Now.AddDays(-day));
        }

        for (var day = 2; day >= 1; day--)
        {
            for (var i = 0; i < 15; i++)
            {
                await writer.WriteAsync(
                    game, PresenceReading.Counted(100, FieldSource.Who), Now.AddDays(-day).AddMinutes(i * 20));
            }
        }

        await Maintenance(db).RunAsync(Now);

        var rankings = await QueriesOn(db).RankingsAsync(RankingSpan.Week);
        var busiest = rankings.Busiest.SingleOrDefault(b => b.Slug == "lopsided");

        await Assert.That(busiest).IsNotNull();
        await Assert.That(busiest!.Median).IsEqualTo(100);
        await Assert.That(busiest.Median).IsNotEqualTo(1);
        await Assert.That(busiest.Days).IsEqualTo(6);
    }

    private static NpgsqlGameQueries QueriesOn(TestDatabase db) =>
        new(db.DataSource, time: new FixedClock(Now));

    private static PresenceMaintenance Maintenance(TestDatabase db) =>
        new(new NpgsqlPresenceStore(db.DataSource),
            new NpgsqlPresenceRollupStore(db.DataSource),
            new PresenceRetentionOptions());

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}
