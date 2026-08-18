using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

using Npgsql;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The migration runner, and the properties the schema itself is asked to enforce.
/// </summary>
/// <remarks>
/// These need a real PostgreSQL: a <c>CHECK</c> constraint asserted against an in-memory dictionary
/// has not been asserted at all. Where none is reachable they skip by name rather than pass.
/// </remarks>
public class SchemaTests
{
    [Test]
    public async Task TheMigrationsAreNumberedAndAppliedInThatOrder()
    {
        var names = MigrationRunner.Scripts.Select(script => script.Name).ToList();

        await Assert.That(names).IsNotEmpty();
        await Assert.That(names[0]).IsEqualTo("0001_game.sql");
        await Assert.That(names).IsEquivalentTo(names.Order(StringComparer.Ordinal).ToList());
    }

    [Test]
    public async Task ASecondRunAppliesNothing()
    {
        // It runs on every process start, in every replica, for ever. A second run applying anything
        // would mean a deploy could not be repeated.
        await using var db = await PostgresFixture.FreshDatabaseAsync();

        var first = await new MigrationRunner(db.DataSource).ApplyAsync();
        var second = await new MigrationRunner(db.DataSource).ApplyAsync();

        await Assert.That(first).Count().IsEqualTo(MigrationRunner.Scripts.Count);
        await Assert.That(second).IsEmpty();
    }

    [Test]
    public async Task EveryMigrationIsRecordedInTheLedger()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();

        var applied = await connection.QueryAsync<string>("SELECT name FROM mui_migration ORDER BY name");

        await Assert.That(applied.ToList())
            .IsEquivalentTo(MigrationRunner.Scripts.Select(s => s.Name).ToList());
    }

    [Test]
    public async Task NoSchemaIdentifierSaysUptime()
    {
        // Spec §5.8 binds identifiers, not values — MSSP's own UPTIME variable may still appear as a
        // VALUE in game_field.field, which is why this reads the catalogue and not our data.
        await using var db = await PostgresFixture.MigratedAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();

        var offenders = await connection.QueryAsync<string>(
            """
            SELECT table_name || '.' || column_name
              FROM information_schema.columns
             WHERE table_schema = 'public' AND column_name ILIKE '%uptime%'
            UNION ALL
            SELECT table_name FROM information_schema.tables
             WHERE table_schema = 'public' AND table_name ILIKE '%uptime%'
            """);

        await Assert.That(offenders.ToList()).IsEmpty();
    }

    [Test]
    public async Task APresenceRowMayNotBeBothCountedAndUncountable()
    {
        // §5.4's contract, in the schema rather than only in the writer. A row carrying both a count
        // and a reason is a third state the heatmap has no rendering for.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        await new NpgsqlPresenceStore(db.DataSource).EnsurePartitionAsync(Seed.Now);

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO presence_sample (game_id, at, count, source, unmeasurable_reason)
                VALUES (@game, @at, 4, 'who', 'who_unparseable')
                """,
                new { game, at = Seed.Now }))
            .Throws<PostgresException>();
    }

    [Test]
    public async Task APresenceRowWithNoCountMustSayWhy()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        await new NpgsqlPresenceStore(db.DataSource).EnsurePartitionAsync(Seed.Now);

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO presence_sample (game_id, at, count, source)
                VALUES (@game, @at, NULL, 'who')
                """,
                new { game, at = Seed.Now }))
            .Throws<PostgresException>();
    }

    [Test]
    public async Task AGameMayNotHaveTwoOpenIntervals()
    {
        // The invariant the whole design rests on, enforced by a partial unique index so a caller who
        // forgot to close the first cannot leave two running.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var store = new NpgsqlAvailabilityStore(db.DataSource);

        await store.OpenAsync(new AvailabilityInterval
        {
            GameId = game,
            State = AvailabilityState.Reachable,
            FromAt = Seed.Now,
        });

        await Assert.That(async () => await store.OpenAsync(new AvailabilityInterval
            {
                GameId = game,
                State = AvailabilityState.Unreachable,
                FromAt = Seed.Now.AddHours(1),
                Cause = FailureCause.Dns,
            }))
            .Throws<PostgresException>();
    }

    [Test]
    public async Task AnArchivedGameCarriesAnArchiveDateAndAnUnarchivedOneDoesNot()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO game (id, slug, name, state, first_seen_at)
                VALUES (gen_random_uuid(), 'ghost', 'Ghost', 'archived', now())
                """))
            .Throws<PostgresException>();
    }

    [Test]
    public async Task AnEndpointHostMustAlreadyBeCanonical()
    {
        // The unique index on (host, port) is only an identity guarantee if one machine has one
        // spelling. A write path that forgot to normalise now fails loudly rather than quietly
        // minting the duplicate listing §7.3 exists to prevent.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO game_endpoint (game_id, host, port, kind, first_seen_at, last_seen_at, state)
                VALUES (@game, 'MUD.Example.ORG', 4201, 'telnet', now(), now(), 'active')
                """,
                new { game }))
            .Throws<PostgresException>();
    }
}
