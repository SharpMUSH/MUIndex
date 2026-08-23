using Dapper;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Migration 0017's cycle log, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// The pulse query cross-joins two unrelated aggregates with a lateral for the newest cycle — a shape
/// that's easy to get subtly wrong in ways that only show on an empty table, the state every fresh
/// deployment starts in. Empty cases are tested first, by name.
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

    /// <summary>The newest cycles first, and no more than asked for.</summary>
    /// <remarks>The crawler status page's history table — a different question from the pulse's
    /// single newest row, so its own query rather than a limit bolted onto <c>PulseAsync</c>.</remarks>
    [Test]
    public async Task RecentReturnsTheNewestCyclesFirstAndStopsAtTheLimit()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var cycles = new NpgsqlCrawlCycles(database.DataSource);

        await cycles.RecordAsync(Cycle(Now.AddMinutes(-30), considered: 5, answered: 5));
        await cycles.RecordAsync(Cycle(Now.AddMinutes(-20), considered: 6, answered: 6));
        await cycles.RecordAsync(Cycle(Now.AddMinutes(-10), considered: 7, answered: 7));

        var recent = await cycles.RecentAsync(2);

        await Assert.That(recent.Select(c => c.Considered)).IsEquivalentTo([7, 6])
            .Because("newest first, and the limit keeps only two of the three recorded");
    }

    /// <summary>An empty table answers an empty list, not an exception.</summary>
    [Test]
    public async Task RecentOnAFreshDeploymentIsAnEmptyListRatherThanAFault()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var cycles = new NpgsqlCrawlCycles(database.DataSource);

        await Assert.That(await cycles.RecentAsync(10)).IsEmpty();
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

    /// <summary>
    /// The window sums the cycles inside it and none outside it.
    /// </summary>
    /// <remarks>
    /// The status page's history is ten one-minute rows, which on a healthy crawl are nearly
    /// identical and say almost nothing; the figure a reader can actually use is what the loop got
    /// through over a span. Counted from <c>finished_at</c>, the same column the history orders on,
    /// so a cycle appears in exactly one of "the last ten" and "the last day" per its own clock.
    /// </remarks>
    [Test]
    public async Task TheWindowSumsTheCyclesInsideItAndIgnoresTheRest()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var cycles = new NpgsqlCrawlCycles(database.DataSource);

        await cycles.RecordAsync(Cycle(Now.AddHours(-30), considered: 100, answered: 90));
        await cycles.RecordAsync(Cycle(Now.AddHours(-20), considered: 40, answered: 39));
        await cycles.RecordAsync(Cycle(Now.AddMinutes(-5), considered: 8, answered: 6));

        var window = await cycles.WindowAsync(Now, TimeSpan.FromHours(24));

        await Assert.That(window.Cycles).IsEqualTo(2).Because("the thirty-hour-old cycle is outside");
        await Assert.That(window.Considered).IsEqualTo(48);
        await Assert.That(window.Answered).IsEqualTo(45);
        await Assert.That(window.Failed).IsEqualTo(3);
        await Assert.That(window.Span).IsEqualTo(TimeSpan.FromHours(24));
    }

    /// <summary>
    /// A window with no cycles in it is zeroes, not null and not an exception.
    /// </summary>
    /// <remarks>
    /// <c>SUM</c> over no rows is <c>NULL</c> per row, which is the shape that breaks: the page asks
    /// this on every render, including a deployment's first, and a null-mapped int throws.
    /// </remarks>
    [Test]
    public async Task AnEmptyWindowIsZeroesRatherThanNothing()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        var cycles = new NpgsqlCrawlCycles(database.DataSource);

        await cycles.RecordAsync(Cycle(Now.AddDays(-3), considered: 100, answered: 90));

        var window = await cycles.WindowAsync(Now, TimeSpan.FromHours(24));

        await Assert.That(window.Cycles).IsEqualTo(0);
        await Assert.That(window.Probed).IsEqualTo(0);
        await Assert.That(window.IsEmpty).IsTrue();
    }

}
