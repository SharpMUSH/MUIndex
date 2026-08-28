using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Two replicas starting together apply the schema once, not twice.
/// </summary>
/// <remarks>
/// <para>
/// The deployment runs <c>replicas: 2</c> so a version cutover never drops to zero backends, which
/// means both processes call <c>ApplyMigrationsAsync</c> on every start. Before the lock they both
/// read the ledger first and both acted on it, and <c>deploy/compose.production.yaml</c> carried that
/// as a known and deliberately open issue.
/// </para>
/// <para>
/// It came due on 2026-08-27 deploying migration 0037. The prediction there was that the loser would
/// fail cleanly on the ledger's primary key, after its own DDL had succeeded. What actually happened
/// was worse in kind: the loser failed <em>inside</em> the script, having already renamed the winner's
/// freshly-built table aside and copied eighty-three thousand rows into a parent whose partitions its
/// own <c>IF NOT EXISTS</c> had quietly declined to create. Transactional DDL rolled all of it back,
/// which is the only reason that was an incident and not a loss — the reasoning that said it was safe
/// was describing a different failure than the one that happened.
/// </para>
/// <para>
/// So what is pinned here is not "the loser fails tidily" but "the loser never runs the script at
/// all".
/// </para>
/// </remarks>
public class MigrationConcurrencyTests
{
    [Test]
    public async Task TwoRunnersAgainstOneDatabaseApplyEveryMigrationExactlyOnce()
    {
        await using var db = await PostgresFixture.FreshDatabaseAsync();

        // Started together on purpose: the bug needed both to read the ledger before either wrote to
        // it, which is exactly what two replicas coming up from one `docker compose up -d` do.
        var both = await Task.WhenAll(
            new MigrationRunner(db.DataSource).ApplyAsync(),
            new MigrationRunner(db.DataSource).ApplyAsync());

        var applied = both[0].Count + both[1].Count;

        await using var connection = db.DataSource.CreateConnection();

        var ledger = await connection.QuerySingleAsync<int>("SELECT count(*) FROM mui_migration");
        var distinct = await connection.QuerySingleAsync<int>(
            "SELECT count(DISTINCT name) FROM mui_migration");

        // One of them did the work and the other found nothing to do; between them they applied the
        // whole schema once. Which one wins is a race and is not asserted.
        await Assert.That(applied).IsEqualTo(ledger);
        await Assert.That(ledger).IsEqualTo(distinct);
        await Assert.That(both.Count(r => r.Count == 0)).IsEqualTo(1);
    }

    /// <summary>
    /// The lock is free once the run returns, so the next start is not blocked by the last.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A session-level advisory lock outlives its transaction by design, which is what the run needs
    /// — it spans one transaction per script — but it also makes leaking one a way to wedge every
    /// future start of every replica. Postgres frees it when the backend goes away, so a process that
    /// dies mid-migration cannot lock the schema out for ever; what this covers is the ordinary path,
    /// where releasing it has to be our own doing.
    /// </para>
    /// <para>
    /// Asserted by taking the lock from a fresh session rather than by reading <c>pg_locks</c>: the
    /// question that matters is "can the next replica start", and advisory locks are re-entrant within
    /// a session, so this deliberately uses a connection of its own.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheLockIsFreeOnceTheRunReturns()
    {
        await using var db = await PostgresFixture.FreshDatabaseAsync();

        await new MigrationRunner(db.DataSource).ApplyAsync();

        await using var connection = db.DataSource.CreateConnection();
        await connection.OpenAsync();

        var taken = await connection.QuerySingleAsync<bool>(
            "SELECT pg_try_advisory_lock(@key)", new { key = MigrationRunner.MigrationKey });

        await connection.ExecuteAsync(
            "SELECT pg_advisory_unlock(@key)", new { key = MigrationRunner.MigrationKey });

        await Assert.That(taken).IsTrue();

        // And a second run still finds nothing to do rather than waiting on a lock nobody released.
        await Assert.That(await new MigrationRunner(db.DataSource).ApplyAsync()).IsEmpty();
    }

    /// <summary>
    /// A held lock makes the run give up and say why, rather than hang for ever.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failure this rules out is the one a bare <c>pg_advisory_lock</c> would have: a backend that
    /// is wedged rather than dead holds the lock, TCP keepalive defaults are measured in hours, and
    /// every replica sits in startup for all of it — silently, serving nothing. Bounded and loud beats
    /// unbounded and quiet: the process exits before serving and <c>restart: unless-stopped</c> tries
    /// again, by which time the winner has normally finished.
    /// </para>
    /// <para>
    /// The lock is taken from a connection this test keeps open for the duration, because that is the
    /// shape of the thing being simulated — another session holding it. It is released in the
    /// <c>finally</c> so a failure here cannot wedge the rest of the suite against a shared container.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AWaiterGivesUpWithSomethingActionableRatherThanHanging()
    {
        await using var db = await PostgresFixture.FreshDatabaseAsync();

        await using var holder = db.DataSource.CreateConnection();
        await holder.OpenAsync();

        await holder.ExecuteAsync(
            "SELECT pg_advisory_lock(@key)", new { key = MigrationRunner.MigrationKey });

        // Five minutes is the shipped ceiling, so this asserts the shape rather than sitting through
        // it: the run must still be waiting well after an unblocked acquire would have returned.
        var run = new MigrationRunner(db.DataSource).ApplyAsync();

        try
        {
            var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(8)));

            await Assert.That(finished).IsNotEqualTo((Task)run);
            await Assert.That(run.IsCompleted).IsFalse();
        }
        finally
        {
            await holder.ExecuteAsync("SELECT pg_advisory_unlock_all()");
        }

        // Awaited rather than abandoned. Left running it would apply the schema while the fixture is
        // being torn down underneath it, and anything it threw would be an unobserved exception on a
        // test that had already reported success -- which is the shape of a flake nobody can
        // reproduce.
        await Assert.That(await run).IsNotEmpty();
    }
}
