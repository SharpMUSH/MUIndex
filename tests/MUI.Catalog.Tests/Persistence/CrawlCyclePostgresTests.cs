using Dapper;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Migration 0017's cycle log, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// The pulse query is a cross join over two unrelated aggregates and a lateral for the newest cycle.
/// That shape is easy to get subtly wrong in a way that only shows on an empty table — which is the
/// state every fresh deployment starts in and the one the front page must survive — so the empty
/// cases are tested first and by name.
/// </remarks>
public class CrawlCyclePostgresTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private static CrawlCycleRecord Cycle(DateTimeOffset finished, int considered, int answered) =>
        new(finished.AddSeconds(-11), finished, considered, considered, answered,
            considered - answered, 0, 0, 0, 1, 0, answered, 0, 2, 3);

    /// <summary>
    /// An empty registry and no cycles still answers, because that is a fresh deployment.
    /// </summary>
    /// <remarks>
    /// A join written the obvious way returns no rows here and <c>QuerySingleAsync</c> throws, which
    /// on the front page's path is a 500 on the first page load of a new install.
    /// </remarks>
    [Test]
    public async Task AnEmptyDatabaseYieldsAPulseRatherThanNothing()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var cycles = new NpgsqlCrawlCycles(database.DataSource);

        var pulse = await cycles.PulseAsync(Now);

        await Assert.That(pulse.LastProbeAt).IsNull();
        await Assert.That(pulse.LastCycle).IsNull();
        await Assert.That(pulse.TargetsKnown).IsEqualTo(0);
        await Assert.That(pulse.State(Now)).IsEqualTo(CrawlState.NotYet);
    }

    /// <summary>The newest cycle wins, and every counter survives the round trip.</summary>
    [Test]
    public async Task ThePulseCarriesTheNewestCycle()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var cycles = new NpgsqlCrawlCycles(database.DataSource);

        await cycles.RecordAsync(Cycle(Now.AddMinutes(-5), considered: 40, answered: 39));
        await cycles.RecordAsync(Cycle(Now.AddMinutes(-1), considered: 8, answered: 6));

        var pulse = await cycles.PulseAsync(Now);

        await Assert.That(pulse.LastCycle!.Considered).IsEqualTo(8);
        await Assert.That(pulse.LastCycle.Answered).IsEqualTo(6);
        await Assert.That(pulse.LastCycle.Failed).IsEqualTo(2);
        await Assert.That(pulse.LastCycle.Listed).IsEqualTo(1);
        await Assert.That(pulse.LastCycle.Transitions).IsEqualTo(2);
        await Assert.That(pulse.LastCycle.Referrals).IsEqualTo(3);
        await Assert.That(pulse.LastCycle.Took).IsEqualTo(TimeSpan.FromSeconds(11));
    }

    /// <summary>The registry half comes from the crawler's own schedule columns.</summary>
    [Test]
    public async Task ThePulseReadsTheRegistrysClockFromCrawlTarget()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var cycles = new NpgsqlCrawlCycles(database.DataSource);

        await using var connection = await database.DataSource.OpenConnectionAsync();

        // Two overdue and one not, so DueNow is a filter rather than a count of everything.
        await connection.ExecuteAsync(
            """
            INSERT INTO crawl_target (
                id, host, port, first_seen_at, next_probe_at, last_probed_at, consecutive_failures)
            VALUES (gen_random_uuid(), 'a.example.org', 4201, @seen, @past, @probed, 0),
                   (gen_random_uuid(), 'b.example.org', 4201, @seen, @past, @older,  0),
                   (gen_random_uuid(), 'c.example.org', 4201, @seen, @soon, @older,  0)
            """,
            new
            {
                seen = Now.AddDays(-9),
                past = Now.AddMinutes(-2),
                soon = Now.AddMinutes(10),
                probed = Now.AddSeconds(-30),
                older = Now.AddHours(-3),
            });

        var pulse = await cycles.PulseAsync(Now);

        await Assert.That(pulse.TargetsKnown).IsEqualTo(3);
        await Assert.That(pulse.DueNow).IsEqualTo(2);
        await Assert.That(pulse.LastProbeAt).IsEqualTo(Now.AddSeconds(-30));
        await Assert.That(pulse.NextDueAt).IsEqualTo(Now.AddMinutes(-2));

        // A probe thirty seconds ago is the loop working, which is the only claim this makes.
        await Assert.That(pulse.State(Now)).IsEqualTo(CrawlState.Working);
    }

    /// <summary>
    /// The TTL drops old cycles and keeps the window.
    /// </summary>
    /// <remarks>
    /// Deletion is allowed here precisely because no row is a measurement of anybody's game — see
    /// the migration. If that ever stops being true this test is the wrong test.
    /// </remarks>
    [Test]
    public async Task TheSweepDropsCyclesPastTheirTtlAndKeepsTheRest()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var cycles = new NpgsqlCrawlCycles(database.DataSource);

        await cycles.RecordAsync(Cycle(Now.AddDays(-40), 1, 1));
        await cycles.RecordAsync(Cycle(Now.AddDays(-31), 1, 1));
        await cycles.RecordAsync(Cycle(Now.AddDays(-2), 5, 5));

        var gone = await cycles.SweepAsync(Now.AddDays(-30));

        await Assert.That(gone).IsEqualTo(2);
        await Assert.That((await cycles.PulseAsync(Now)).LastCycle!.Considered).IsEqualTo(5);
    }

    /// <summary>The schema probe answers true once the migration has run.</summary>
    [Test]
    public async Task TheTableAnnouncesItselfAsInstalled()
    {
        await using var database = await PostgresFixture.MigratedAsync();

        await Assert.That(await new NpgsqlCrawlCycles(database.DataSource).IsInstalledAsync()).IsTrue();
    }
}
