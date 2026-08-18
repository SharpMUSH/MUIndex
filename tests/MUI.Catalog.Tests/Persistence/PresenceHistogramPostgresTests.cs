using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

using Npgsql;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Migration 0019's distribution, against a real database.
/// </summary>
/// <remarks>
/// The load-bearing test here is not a shape assertion: it's that summing these maps over a window
/// and walking to the half-way position returns the same number <c>percentile_disc(0.5)</c> returns
/// over the same raw samples, across distributions chosen to break a naive implementation — even
/// counts, odd counts, ties, a single sample. The rest guard §5.4 at the new seam: a bucket probed
/// and never counted must not acquire a distribution.
/// </remarks>
public class PresenceHistogramPostgresTests
{
    private static readonly DateTimeOffset Hour = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task EveryCountedBucketCarriesItsDistributionAndItAddsUp()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        // Four probes in one hour: two read 3, one read 0 (a measured zero is a key like any
        // other), and one read 7.
        foreach (var (minute, count) in new[] { (0, 3), (15, 3), (30, 0), (45, 7) })
        {
            await writer.WriteAsync(game, PresenceReading.Counted(count, FieldSource.Who), Hour.AddMinutes(minute));
        }

        await Maintenance(db).RunAsync(Hour.AddHours(1));

        foreach (var grain in new[] { "hour", "day" })
        {
            var histogram = await ScalarAsync(db,
                $"SELECT count_histogram::text FROM presence_rollup_{grain} WHERE game_id = @game",
                new { game });

            await Assert.That(histogram).IsNotNull();

            var total = await ScalarAsync(db,
                $"""
                 SELECT presence_histogram_total(count_histogram)::text = counted_samples::text
                   FROM presence_rollup_{grain} WHERE game_id = @game
                 """,
                new { game });

            await Assert.That(total).IsEqualTo("True").Because($"the {grain} distribution must total the tally");
            await Assert.That(histogram).Contains("\"3\": 2");
            await Assert.That(histogram).Contains("\"0\": 1");
            await Assert.That(histogram).Contains("\"7\": 1");
        }
    }

    [Test]
    public async Task AProbedAndUncountableBucketGetsNoDistribution()
    {
        // §5.4's middle state at the new seam: a distribution on a bucket nothing was counted in
        // would be a statement that something was — an empty `{}` would be the same lie.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        await writer.WriteAsync(
            game, PresenceReading.Unmeasurable(UnmeasurableReason.WhoUnparseable), Hour);
        await writer.WriteAsync(
            game, PresenceReading.Unmeasurable(UnmeasurableReason.WhoNotOffered), Hour.AddMinutes(20));

        await Maintenance(db).RunAsync(Hour.AddHours(1));

        var histogram = await ScalarAsync(db,
            "SELECT count_histogram::text FROM presence_rollup_hour WHERE game_id = @game",
            new { game });

        await Assert.That(histogram).IsNull();
    }

    [Test]
    public async Task TheSchemaRefusesADistributionThatDoesNotAddUp()
    {
        // Half the design is in CHECK constraints: a histogram totalling something other than the
        // tally beside it is a median over the wrong number of probes, and no hand-typed UPDATE may
        // produce one.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO presence_rollup_hour
                    (game_id, hour, counted_samples, unmeasurable_samples,
                     min_count, max_count, sum_count, count_histogram)
                VALUES (@game, @hour, 4, 0, 0, 7, 13, '{"3": 2, "0": 1}'::jsonb)
                """,
                new { game, hour = Hour }))
            .Throws<PostgresException>();

        // And a distribution on a bucket that counted nothing.
        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO presence_rollup_hour
                    (game_id, hour, counted_samples, unmeasurable_samples, count_histogram)
                VALUES (@game, @hour, 0, 3, '{"0": 3}'::jsonb)
                """,
                new { game, hour = Hour.AddHours(1) }))
            .Throws<PostgresException>();
    }

    [Test]
    public async Task ALateSampleIsFoldedInRatherThanAppended()
    {
        // The rollup is a projection, not an accumulation: re-rolling an hour must rewrite the
        // distribution rather than merge into it, or every re-roll would double-count.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        await writer.WriteAsync(game, PresenceReading.Counted(5, FieldSource.Who), Hour);
        await Maintenance(db).RunAsync(Hour.AddHours(1));

        // A probe that finished slowly and landed after its own hour was rolled up.
        await writer.WriteAsync(game, PresenceReading.Counted(5, FieldSource.Who), Hour.AddMinutes(10));
        await Maintenance(db).RunAsync(Hour.AddHours(1));

        var histogram = await ScalarAsync(db,
            "SELECT count_histogram::text FROM presence_rollup_hour WHERE game_id = @game",
            new { game });

        await Assert.That(histogram).Contains("\"5\": 2");

        var counted = await ScalarAsync(db,
            "SELECT counted_samples::text FROM presence_rollup_hour WHERE game_id = @game",
            new { game });

        await Assert.That(counted).IsEqualTo("2");
    }

    /// <summary>
    /// The whole point, asserted directly: the rolled-up median equals the raw one.
    /// </summary>
    /// <remarks>
    /// Distributions chosen for where an off-by-one lives: even/odd sample counts, all-identical, a
    /// single sample, a tie across the middle. <c>percentile_disc</c> takes the element at
    /// <c>ceil(n / 2)</c>; an implementation using <c>n / 2</c> or averaging the two middle values
    /// disagrees on at least one of these.
    /// </remarks>
    [Test]
    public async Task TheRolledUpMedianIsTheMedianOfTheRawSamples()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var writer = new PresenceWriter(new NpgsqlPresenceStore(db.DataSource));

        int[][] distributions =
        [
            [1, 2, 3, 4],
            [1, 2, 3, 4, 5],
            [7, 7, 7],
            [9],
            [0, 0, 4, 4],
            [12, 3, 3, 8, 1, 20, 3],
            [0, 0, 0, 0, 1],
        ];

        var games = new List<Guid>();

        for (var d = 0; d < distributions.Length; d++)
        {
            var counts = distributions[d];
            var game = await Seed.GameAsync(db, $"shape-{d}", $"Shape {d}");
            games.Add(game);

            // Spread across a day so hour buckets differ, exposing a median taken per bucket and
            // averaged rather than summed across the window.
            for (var i = 0; i < counts.Length; i++)
            {
                await writer.WriteAsync(
                    game, PresenceReading.Counted(counts[i], FieldSource.Who), Hour.AddHours(i % 5));
            }
        }

        await Maintenance(db).RunAsync(Hour.AddDays(1));

        await using var connection = await db.DataSource.OpenConnectionAsync();

        foreach (var game in games)
        {
            var raw = await connection.ExecuteScalarAsync<int>(
                """
                SELECT percentile_disc(0.5) WITHIN GROUP (ORDER BY count)
                  FROM presence_sample WHERE game_id = @game AND count IS NOT NULL
                """,
                new { game });

            // The same walk the rankings query does, over the day rollup's distributions
            var rolled = await connection.ExecuteScalarAsync<int>(
                """
                WITH frequency AS (
                    SELECT e.key::int AS value, sum(e.value::bigint) AS times
                      FROM presence_rollup_day r
                      CROSS JOIN LATERAL jsonb_each_text(r.count_histogram) AS e(key, value)
                     WHERE r.game_id = @game AND r.count_histogram IS NOT NULL
                     GROUP BY e.key::int),
                walked AS (
                    SELECT value,
                           sum(times) OVER (ORDER BY value) AS running,
                           ceil(sum(times) OVER () / 2.0) AS half
                      FROM frequency)
                SELECT min(value)::int FROM walked WHERE running >= half
                """,
                new { game });

            await Assert.That(rolled).IsEqualTo(raw)
                .Because("a median read off the rollup must be the median of the samples it replaced");
        }
    }

    private static PresenceMaintenance Maintenance(TestDatabase db) =>
        new(new NpgsqlPresenceStore(db.DataSource),
            new NpgsqlPresenceRollupStore(db.DataSource),
            new PresenceRetentionOptions());

    private static async Task<string?> ScalarAsync(TestDatabase db, string sql, object parameters)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();

        return await connection.ExecuteScalarAsync<string?>(sql, parameters);
    }
}
