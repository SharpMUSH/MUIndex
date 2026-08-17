using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

using Npgsql;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// A game whose owners asked to come out of the listing (migration 0025).
/// </summary>
/// <remarks>
/// <b>The property under test is that it is neither of its neighbours.</b> Archiving says the game
/// stopped answering, which would be a false statement about a game that is running perfectly well
/// and simply not being dialled. Exclusion says we decided it is not a game somebody can play, which
/// would be a false statement about a game. This state says what actually happened, and the two
/// halves of that — an account that asked, and a probe that can undo it — are what these tests pin.
/// </remarks>
public class UnlistedGamePostgresTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> GameAsync(TestDatabase db, string slug)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await db.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO game (id, slug, name, state, is_claimed, first_seen_at)
            VALUES (@id, @slug, @slug, 'active', true, @at)
            """,
            new { id, slug, at = At });

        return id;
    }

    /// <summary>An account, because the unlisting has to be attributable to one.</summary>
    private static async Task<Guid> OwnerAsync(TestDatabase db, string name)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await db.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO app_user (
                id, display_name, normalised_name, security_stamp, concurrency_stamp, created_at)
            VALUES (@id, @name, upper(@name), 'stamp', 'stamp', @at)
            """,
            new { id, name, at = At });

        return id;
    }

    [Test]
    public async Task AnUnlistingRecordsTheAccountThatAskedForIt()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var id = await GameAsync(db, "a-game-that-asked");
        var owner = await OwnerAsync(db, "their-admin");

        await games.UnlistAsync(id, owner, At);

        await Assert.That((await games.ByIdAsync(id))!.State).IsEqualTo(LifecycleState.Unlisted);

        await using var connection = await db.DataSource.OpenConnectionAsync();
        var by = await connection.QuerySingleAsync<Guid>(
            "SELECT unlisted_by FROM game WHERE id = @id", new { id });

        await Assert.That(by).IsEqualTo(owner);
    }

    /// <summary>
    /// An unlisting nobody can attribute is the thing the constraint exists to prevent, and it is the
    /// database that refuses rather than a writer that happens to check.
    /// </summary>
    /// <remarks>
    /// The lesson is <c>ContactedMaintainer</c>'s: a claim about what a third party wants, made by
    /// whoever typed it and traceable to nobody, shipped once already. A NOT NULL account is what
    /// that defect did not have.
    /// </remarks>
    [Test]
    public async Task AnUnlistingWithNobodyBehindItIsRefusedByTheSchema()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var id = await GameAsync(db, "unattributed");

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
                "UPDATE game SET state = 'unlisted', unlisted_at = @at WHERE id = @id",
                new { id, at = At }))
            .Throws<PostgresException>();
    }

    /// <summary>
    /// The sweeper and the ingestor both go through <c>SetStateAsync</c>, and neither may move it.
    /// </summary>
    /// <remarks>
    /// Rarely reached and load-bearing anyway. A game that opted out writes no availability
    /// transition at all, so it has no open unreachable interval for the sweeper to archive it on —
    /// but a game that had already gone dark before its owners asked does, and archiving that one
    /// would replace "they asked" with "it stopped answering" in the column the listing reads.
    /// </remarks>
    [Test]
    [Arguments(LifecycleState.Active)]
    [Arguments(LifecycleState.Archived)]
    [Arguments(LifecycleState.Dark)]
    public async Task TheCrawlCannotMoveAnUnlistedGameThroughSetState(LifecycleState attempt)
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var id = await GameAsync(db, "left-alone");

        await games.UnlistAsync(id, await OwnerAsync(db, "asker"), At);
        await games.SetStateAsync(id, attempt, At.AddHours(1));

        await Assert.That((await games.ByIdAsync(id))!.State).IsEqualTo(LifecycleState.Unlisted);
    }

    /// <summary>
    /// <b>The difference from an exclusion.</b> A probe that answers relists it, which is what makes
    /// the exit an operator can work alone into a real exit.
    /// </summary>
    /// <remarks>
    /// Safe by construction rather than by a check: an opted-out address is refused before the dial
    /// (§11), so a probe that answered is proof no opt-out stands on it any more. Nobody has to ask
    /// us twice, and nobody has to hold an account here to be listed again.
    /// </remarks>
    [Test]
    public async Task AProbeThatAnswersPutsItBackInTheListing()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var availability = new NpgsqlAvailabilityStore(db.DataSource);
        var sweeper = new ArchiveSweeper(games, availability, availability);

        var id = await GameAsync(db, "asked-us-back");

        await games.UnlistAsync(id, await OwnerAsync(db, "asker"), At);

        await Assert.That(await sweeper.RestoreAsync(id, At.AddDays(30))).IsTrue();
        await Assert.That((await games.ByIdAsync(id))!.State).IsEqualTo(LifecycleState.Active);
    }

    /// <summary>An exclusion is not undone by a socket answering, and never was.</summary>
    [Test]
    public async Task AProbeThatAnswersDoesNotUndoAnExclusion()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var availability = new NpgsqlAvailabilityStore(db.DataSource);
        var sweeper = new ArchiveSweeper(games, availability, availability);

        var id = await GameAsync(db, "your-mud-name");

        await games.ExcludeAsync(id, "Stock configuration.", At);

        await Assert.That(await sweeper.RestoreAsync(id, At.AddDays(30))).IsFalse();
        await Assert.That((await games.ByIdAsync(id))!.State).IsEqualTo(LifecycleState.Excluded);
    }

    /// <summary>Relisting clears the attribution, because the row no longer says anything.</summary>
    [Test]
    public async Task RelistingClearsTheAskAndHandsTheGameBackToTheCrawl()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var id = await GameAsync(db, "reconsidered");

        await games.UnlistAsync(id, await OwnerAsync(db, "asker"), At);
        await games.RelistAsync(id, At.AddDays(1));

        await Assert.That((await games.ByIdAsync(id))!.State).IsEqualTo(LifecycleState.Active);

        await using var connection = await db.DataSource.OpenConnectionAsync();
        var at = await connection.QuerySingleAsync<DateTimeOffset?>(
            "SELECT unlisted_at FROM game WHERE id = @id", new { id });

        await Assert.That(at).IsNull();

        // And the crawl owns it again, which is the half that would be missed by asserting the state.
        await games.SetStateAsync(id, LifecycleState.Archived, At.AddDays(2));

        await Assert.That((await games.ByIdAsync(id))!.State).IsEqualTo(LifecycleState.Archived);
    }

    /// <summary>§7.5 is unchanged: nothing is deleted and the page still answers.</summary>
    [Test]
    public async Task TheGameItselfSurvivesIntact()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var id = await GameAsync(db, "still-here");

        await games.UnlistAsync(id, await OwnerAsync(db, "asker"), At);

        var game = await games.ByIdAsync(id);

        await Assert.That(game).IsNotNull();
        await Assert.That(game!.Slug).IsEqualTo("still-here");
        await Assert.That(game.FirstSeenAt).IsEqualTo(At);
    }
}
