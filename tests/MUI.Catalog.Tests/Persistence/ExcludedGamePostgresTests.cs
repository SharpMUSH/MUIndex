using Dapper;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

using Npgsql;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// An address that answers like a game and is not one (migration 0024).
/// </summary>
/// <remarks>
/// <b>The property under test is that the crawl cannot undo a person's judgement.</b> Archiving was
/// the obvious tool and is the wrong one: it means "this stopped answering", and
/// <c>ProbeIngestor</c> restores an archived game on the next successful probe, immediately and
/// without a human. The nine instances this was built for — <c>Your MUD Name</c>, <c>test</c>,
/// <c>lpcdb</c>, four <c>*-Dev</c> servers and a stock mudlib demo — all answer perfectly well, so
/// archiving them would have flipped each back within one cycle and written a change every time.
/// </remarks>
public class ExcludedGamePostgresTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> GameAsync(TestDatabase db, string slug)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await db.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO game (id, slug, name, state, is_claimed, first_seen_at)
            VALUES (@id, @slug, @slug, 'active', false, @at)
            """,
            new { id, slug, at = At });

        return id;
    }

    [Test]
    public async Task AnExclusionCarriesItsReason()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var id = await GameAsync(db, "your-mud-name");

        await games.ExcludeAsync(id, "Stock configuration; the mud is still called Your MUD Name.", At);

        var game = await games.ByIdAsync(id);

        await Assert.That(game!.State).IsEqualTo(LifecycleState.Excluded);
    }

    /// <summary>
    /// <b>The whole point.</b> The ingestor calls <c>SetStateAsync</c> through
    /// <c>ArchiveSweeper.RestoreAsync</c> on every probe that answers, and the sweeper calls it when
    /// a game goes dark. Neither may move an excluded game.
    /// </summary>
    [Test]
    [Arguments(LifecycleState.Active)]
    [Arguments(LifecycleState.Archived)]
    [Arguments(LifecycleState.Dark)]
    public async Task TheCrawlCannotMoveAnExcludedGame(LifecycleState attempt)
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var id = await GameAsync(db, "a-dev-instance");

        await games.ExcludeAsync(id, "Dev instance of a game listed separately.", At);
        await games.SetStateAsync(id, attempt, At.AddHours(1));

        await Assert.That((await games.ByIdAsync(id))!.State).IsEqualTo(LifecycleState.Excluded);
    }

    /// <summary>Reversed by a person, and then the crawl owns it again.</summary>
    [Test]
    public async Task IncludingItPutsItBackUnderTheCrawlsControl()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var id = await GameAsync(db, "reconsidered");

        await games.ExcludeAsync(id, "Looked like a test instance.", At);
        await games.IncludeAsync(id, At.AddDays(1));

        await Assert.That((await games.ByIdAsync(id))!.State).IsEqualTo(LifecycleState.Active);

        await games.SetStateAsync(id, LifecycleState.Archived, At.AddDays(2));

        await Assert.That((await games.ByIdAsync(id))!.State).IsEqualTo(LifecycleState.Archived);
    }

    /// <summary>
    /// An exclusion nobody explained is the thing the constraint exists to prevent, and it is the
    /// database that refuses rather than a writer that happens to check.
    /// </summary>
    [Test]
    public async Task AnExclusionWithoutAReasonIsRefusedByTheSchema()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var id = await GameAsync(db, "unexplained");

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
                "UPDATE game SET state = 'excluded', excluded_at = @at WHERE id = @id",
                new { id, at = At }))
            .Throws<PostgresException>();
    }

    /// <summary>§7.5 is unchanged: nothing is deleted and the page still answers.</summary>
    [Test]
    public async Task TheGameItselfSurvivesIntact()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var id = await GameAsync(db, "still-here");

        await games.ExcludeAsync(id, "A tool on the network rather than a game.", At);

        var game = await games.ByIdAsync(id);

        await Assert.That(game).IsNotNull();
        await Assert.That(game!.Slug).IsEqualTo("still-here");
        await Assert.That(game.FirstSeenAt).IsEqualTo(At);
    }
}
