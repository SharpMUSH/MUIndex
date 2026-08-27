using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The hourly rollup is partitioned by month, so retention on it is a drop rather than a delete.
/// </summary>
/// <remarks>
/// <para>
/// It is the fastest-growing table here — measured at ~6,300 rows a day against a 931-game catalogue,
/// which is ~503 MB a year — and the only reader of it is the game page's heatmap, over a 56-day
/// window. So it is the one grain where retention pays, and deleting a year of it a row at a time
/// would leave as many dead tuples for autovacuum to walk on a two-core box.
/// </para>
/// <para>
/// <c>presence_rollup_day</c> is deliberately not partitioned and there is a test below saying so, so
/// that "make it symmetrical" is a decision somebody has to argue with rather than a tidy-up.
/// </para>
/// </remarks>
public class HourlyRollupPartitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task TheHourlyRollupIsPartitionedAndTheDailyOneIsNot()
    {
        await using var db = await PostgresFixture.MigratedAsync();

        await using var connection = db.DataSource.CreateConnection();

        var kinds = (await connection.QueryAsync<(string Name, char Kind)>(
            """
            SELECT c.relname, c.relkind
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = 'public'
               AND c.relname IN ('presence_rollup_hour', 'presence_rollup_day')
            """)).ToDictionary(r => r.Name, r => r.Kind);

        // 'p' is a partitioned table, 'r' an ordinary one.
        await Assert.That(kinds["presence_rollup_hour"]).IsEqualTo('p');
        await Assert.That(kinds["presence_rollup_day"]).IsEqualTo('r');
    }

    /// <summary>The conversion in 0037 has to carry every constraint the old table had.</summary>
    /// <remarks>
    /// A rebuilt table is where a CHECK quietly goes missing, and the one most worth keeping is the
    /// histogram totalling its own tally — without it a rollup could publish a distribution that does
    /// not add up to the samples it claims to describe.
    /// </remarks>
    [Test]
    public async Task TheRebuiltTableKeptItsConstraintsAndItsGeneratedColumn()
    {
        await using var db = await PostgresFixture.MigratedAsync();

        await using var connection = db.DataSource.CreateConnection();

        var checks = (await connection.QueryAsync<string>(
            """
            SELECT conname FROM pg_constraint
             WHERE conrelid = 'presence_rollup_hour'::regclass AND contype = 'c'
            """)).ToList();

        await Assert.That(checks).Contains("presence_rollup_hour_histogram_totals_the_samples");
        await Assert.That(checks).Contains("presence_rollup_hour_counts_iff_counted");
        await Assert.That(checks).Contains("presence_rollup_hour_is_on_the_hour");
        await Assert.That(checks).Contains("presence_rollup_hour_measured_something");

        var generated = await connection.QuerySingleAsync<bool>(
            """
            SELECT attgenerated = 's' FROM pg_attribute
             WHERE attrelid = 'presence_rollup_hour'::regclass AND attname = 'mean_count'
            """);

        await Assert.That(generated).IsTrue();
    }

    [Test]
    public async Task WritingAnHourLandsInThatMonthsPartition()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var rollups = new NpgsqlPresenceRollupStore(db.DataSource);

        await rollups.EnsurePartitionsThroughAsync(Now, Now);
        await WriteHourAsync(db, game, Now);

        await using var connection = db.DataSource.CreateConnection();

        var landed = await connection.QuerySingleAsync<long>(
            "SELECT count(*) FROM presence_rollup_hour_202607");

        await Assert.That(landed).IsEqualTo(1L);
    }

    /// <summary>
    /// Retention drops the month, and leaves the month the boundary falls inside alone.
    /// </summary>
    /// <remarks>
    /// The half-month case is the one worth pinning: a partition is dropped only when its whole span
    /// is past the boundary, because a month the boundary lands in still holds hours that must be
    /// kept. Those are the remainder <c>DeleteBeforeAsync</c> handles.
    /// </remarks>
    [Test]
    public async Task AWholeMonthPastTheBoundaryIsDroppedAndTheBoundarysOwnMonthIsNot()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var rollups = new NpgsqlPresenceRollupStore(db.DataSource);
        var may = new DateTimeOffset(2026, 5, 10, 3, 0, 0, TimeSpan.Zero);
        var june = new DateTimeOffset(2026, 6, 10, 3, 0, 0, TimeSpan.Zero);

        await rollups.EnsurePartitionsThroughAsync(may, Now);
        await WriteHourAsync(db, game, may);
        await WriteHourAsync(db, game, june);

        // Boundary inside June: May is wholly past it, June is not.
        var dropped = await rollups.DropHourPartitionsEndingAtOrBeforeAsync(
            new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero));

        await Assert.That(dropped).Contains("presence_rollup_hour_202605");
        await Assert.That(dropped).DoesNotContain("presence_rollup_hour_202606");

        await using var connection = db.DataSource.CreateConnection();

        await Assert.That(await connection.QuerySingleAsync<long>(
            "SELECT count(*) FROM presence_rollup_hour")).IsEqualTo(1L);
    }

    /// <summary>A partition an operator attached by hand is never ours to drop.</summary>
    /// <remarks>
    /// Retention reads a partition's month back out of its name rather than parsing its bounds, so
    /// anything not named the way we name them is skipped. Same rule raw presence has followed since
    /// 0003, and the reason a drop can never take the wrong month.
    /// </remarks>
    [Test]
    public async Task APartitionNotNamedTheWayWeNameThemIsNeverDropped()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var rollups = new NpgsqlPresenceRollupStore(db.DataSource);

        await using var connection = db.DataSource.CreateConnection();

        await connection.ExecuteAsync(
            """
            CREATE TABLE presence_rollup_hour_archive_2020
            PARTITION OF presence_rollup_hour
            FOR VALUES FROM ('2020-01-01 00:00:00+00') TO ('2021-01-01 00:00:00+00')
            """);

        var dropped = await rollups.DropHourPartitionsEndingAtOrBeforeAsync(
            Now);

        await Assert.That(dropped).DoesNotContain("presence_rollup_hour_archive_2020");

        await Assert.That(await connection.QuerySingleAsync<bool>(
            "SELECT to_regclass('presence_rollup_hour_archive_2020') IS NOT NULL")).IsTrue();
    }

    private static async Task WriteHourAsync(TestDatabase db, Guid game, DateTimeOffset hour)
    {
        await using var command = db.DataSource.CreateCommand(
            """
            INSERT INTO presence_rollup_hour
                (game_id, hour, counted_samples, unmeasurable_samples,
                 min_count, max_count, sum_count, count_histogram)
            VALUES (@game, date_trunc('hour', @hour AT TIME ZONE 'UTC') AT TIME ZONE 'UTC',
                    2, 0, 3, 5, 8, '{"3": 1, "5": 1}'::jsonb)
            """);

        command.Parameters.AddWithValue("game", game);
        command.Parameters.AddWithValue("hour", hour);

        await command.ExecuteNonQueryAsync();
    }
}
