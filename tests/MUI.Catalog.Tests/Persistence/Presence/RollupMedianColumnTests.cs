using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// <c>presence_rollup_day.median_count</c> is a cache the database keeps in step by itself.
/// </summary>
/// <remarks>
/// <para>
/// The reason it is a generated column rather than a value the rollup writer computes is that a
/// written value can be forgotten: a second writer, a hand-run <c>UPDATE</c>, a migration that
/// rebuilds histograms, and the median describes a distribution that is no longer there. A
/// generated column has no such path — PostgreSQL recomputes it inside whatever statement writes
/// the row. These tests pin that, since it is the whole reason for the design.
/// </para>
/// <para>
/// The arithmetic itself is pinned separately by
/// <see cref="AgreesWithTheWalkItReplaced"/> — the same ascending walk to <c>ceil(n / 2.0)</c> that
/// <c>DailyMediansAsync</c> does for the days it still computes by hand, so the two halves of that
/// query cannot drift apart.
/// </para>
/// </remarks>
public class RollupMedianColumnTests
{
    private static readonly DateTimeOffset Day = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task TheMedianAppearsWithoutAnybodyComputingIt()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);

        // {1:1, 2:10, 3:2} over 13 samples — half is ceil(13/2) = 7, and the running tally reaches
        // it at 2. Nothing here writes a median; the column is not even named.
        await Insert(db, game, """{"1": 1, "2": 10, "3": 2}""", counted: 13, min: 1, max: 3, sum: 27);

        await Assert.That(await MedianAsync(db, game)).IsEqualTo(2);
    }

    [Test]
    public async Task TheMedianFollowsTheHistogramWhenTheRollupIsWrittenAgain()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);

        await Insert(db, game, """{"1": 1, "2": 10, "3": 2}""", counted: 13, min: 1, max: 3, sum: 27);
        await Assert.That(await MedianAsync(db, game)).IsEqualTo(2);

        // The rollup pass re-aggregating the same day with more probes in it — the writer's own
        // ON CONFLICT shape, which never mentions median_count.
        await Insert(db, game, """{"1": 1, "9": 3}""", counted: 4, min: 1, max: 9, sum: 28);

        await Assert.That(await MedianAsync(db, game)).IsEqualTo(9);
    }

    /// <summary>A day probed but never counted has no distribution, so it has no median (§5.4).</summary>
    /// <remarks>
    /// Rule 4's shape in the schema: the absence of a reading is not a reading of zero. A reader that
    /// coalesced this to 0 would publish "nobody was playing" for an hour nobody could count.
    /// </remarks>
    [Test]
    public async Task ADayWithNoDistributionHasNoMedianRatherThanAZero()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);

        await using var command = db.DataSource.CreateCommand(
            """
            INSERT INTO presence_rollup_day
                (game_id, day, counted_samples, unmeasurable_samples)
            VALUES (@game, @day, 0, 6)
            """);

        command.Parameters.AddWithValue("game", game);
        command.Parameters.AddWithValue("day", Day);

        await command.ExecuteNonQueryAsync();

        await Assert.That(await MedianAsync(db, game)).IsNull();
    }

    /// <summary>
    /// The column may not be written, which is what stops it drifting from the histogram.
    /// </summary>
    [Test]
    public async Task TheMedianCannotBeWrittenByHand()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);

        await Insert(db, game, """{"4": 3}""", counted: 3, min: 4, max: 4, sum: 12);

        var writing = async () => await db.DataSource.CreateCommand(
            "UPDATE presence_rollup_day SET median_count = 99 WHERE game_id = @game")
            .ExecuteNonQueryAsync();

        await Assert.That(writing).Throws<Npgsql.PostgresException>();
    }

    /// <summary>
    /// The stored median is the same figure the hand-walked half of DailyMediansAsync produces.
    /// </summary>
    /// <remarks>
    /// The query reads this column for days that closed before the rollup watermark and walks the
    /// histogram for the watermark's own day. If the two disagreed, a game's trend line would step
    /// at the watermark for arithmetic reasons rather than because anything happened.
    /// </remarks>
    [Test]
    [Arguments("""{"1": 1, "2": 10, "3": 2}""", 13)]
    [Arguments("""{"0": 5, "1": 5}""", 10)]
    [Arguments("""{"7": 1}""", 1)]
    [Arguments("""{"0": 1, "100": 1}""", 2)]
    [Arguments("""{"2": 3, "4": 3, "6": 3}""", 9)]
    public async Task AgreesWithTheWalkItReplaced(string histogram, int counted)
    {
        await using var db = await PostgresFixture.MigratedAsync();

        var stored = await db.DataSource.CreateConnection().QuerySingleAsync<int?>(
            "SELECT presence_histogram_median(@histogram::jsonb)", new { histogram });

        var walked = await db.DataSource.CreateConnection().QuerySingleAsync<int?>(
            """
            SELECT min(w.value)::int
              FROM (SELECT e.key::int AS value,
                           sum(e.value::bigint) OVER (ORDER BY e.key::int) AS running,
                           ceil(sum(e.value::bigint) OVER () / 2.0)        AS half
                      FROM jsonb_each_text(@histogram::jsonb) AS e(key, value)) w
             WHERE w.running >= w.half
            """, new { histogram });

        // And against the definition both are supposed to implement.
        var percentile = await db.DataSource.CreateConnection().QuerySingleAsync<int?>(
            """
            SELECT percentile_disc(0.5) WITHIN GROUP (ORDER BY v.value)::int
              FROM (SELECT e.key::int AS value
                      FROM jsonb_each_text(@histogram::jsonb) AS e(key, value)
                      CROSS JOIN LATERAL generate_series(1, e.value::int) AS g(n)) v
            """, new { histogram });

        await Assert.That(stored).IsEqualTo(walked);
        await Assert.That(stored).IsEqualTo(percentile);
        await Assert.That(counted).IsPositive();
    }

    private static async Task Insert(
        TestDatabase db, Guid game, string histogram, int counted, int min, int max, long sum)
    {
        await using var command = db.DataSource.CreateCommand(
            """
            INSERT INTO presence_rollup_day
                (game_id, day, counted_samples, unmeasurable_samples, min_count, max_count,
                 sum_count, count_histogram)
            VALUES (@game, @day, @counted, 0, @min, @max, @sum, @histogram::jsonb)
            ON CONFLICT (game_id, day) DO UPDATE
               SET counted_samples = EXCLUDED.counted_samples,
                   min_count       = EXCLUDED.min_count,
                   max_count       = EXCLUDED.max_count,
                   sum_count       = EXCLUDED.sum_count,
                   count_histogram = EXCLUDED.count_histogram
            """);

        command.Parameters.AddWithValue("game", game);
        command.Parameters.AddWithValue("day", Day);
        command.Parameters.AddWithValue("counted", counted);
        command.Parameters.AddWithValue("min", min);
        command.Parameters.AddWithValue("max", max);
        command.Parameters.AddWithValue("sum", sum);
        command.Parameters.AddWithValue("histogram", histogram);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int?> MedianAsync(TestDatabase db, Guid game)
    {
        await using var command = db.DataSource.CreateCommand(
            "SELECT median_count FROM presence_rollup_day WHERE game_id = @game");

        command.Parameters.AddWithValue("game", game);

        return await command.ExecuteScalarAsync() as int?;
    }
}
