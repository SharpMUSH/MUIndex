using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// What a merge does to the public reads (spec §7.3, migration 0018).
/// </summary>
/// <remarks>
/// A merge is a redirect — nothing moves between the two games; the absorbed one keeps its
/// endpoints, fields and history exactly where they were measured, and every public surface simply
/// stops offering it separately. The merge filter is composed into the same shared predicate the
/// submission rule uses (a per-call-site filter previously missed queries reaching <c>game</c>
/// through a join), so these tests check more than one surface on purpose.
/// </remarks>
public class MergedGameVisibilityPostgresTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static NpgsqlGameQueries QueriesOn(TestDatabase db) => new(db.DataSource);

    private static async Task MergeAsync(TestDatabase db, Guid into, Guid from, DateTimeOffset? reverted = null)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO merge_log (id, into_game_id, from_game_id, score, signals, at, reverted_at)
            VALUES (@id, @into, @from, 0.5, '[]'::jsonb, @at, @reverted)
            """,
            new { id = Guid.CreateVersion7(), into, from, at = Now, reverted });
    }

    [Test]
    public async Task AnAbsorbedGameIsNotListed()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var survivor = await Seed.GameAsync(db, "aardwolf-mud", "Aardwolf MUD");
        var absorbed = await Seed.GameAsync(db, "aardwolf-mud-2", "Aardwolf MUD");

        await MergeAsync(db, survivor, absorbed);

        var listed = await QueriesOn(db).ListAsync(new GameFilter());

        await Assert.That(listed.Select(game => game.Slug)).IsEquivalentTo(["aardwolf-mud"]);
    }

    [Test]
    public async Task AnAbsorbedGameIsNotCountedAsAListing()
    {
        // The count and the list are separate queries and have disagreed before.
        await using var db = await PostgresFixture.MigratedAsync();
        var survivor = await Seed.GameAsync(db, "aardwolf-mud", "Aardwolf MUD");
        var absorbed = await Seed.GameAsync(db, "aardwolf-mud-2", "Aardwolf MUD");

        await MergeAsync(db, survivor, absorbed);

        await Assert.That((await QueriesOn(db).RankingsAsync()).ListedGames).IsEqualTo(1);
    }

    [Test]
    public async Task AnAbsorbedGameKeepsEverythingItHad()
    {
        // §7.5: nothing is ever deleted — the merge is a pointer, and a reader who follows it back
        // must find the game intact.
        await using var db = await PostgresFixture.MigratedAsync();
        var survivor = await Seed.GameAsync(db, "aardwolf-mud", "Aardwolf MUD");
        var absorbed = await Seed.GameAsync(db, "aardwolf-mud-2", "Aardwolf MUD");

        await MergeAsync(db, survivor, absorbed);

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<string>(
                "SELECT state FROM game WHERE id = @absorbed", new { absorbed }))
            .IsEqualTo("active");
        await Assert.That(await connection.ExecuteScalarAsync<string>(
                "SELECT slug FROM game WHERE id = @absorbed", new { absorbed }))
            .IsEqualTo("aardwolf-mud-2");
    }

    [Test]
    public async Task ARevertedMergeGivesTheGameBackToTheListing()
    {
        // Reverting is clearing one pointer; the listing must reflect that.
        await using var db = await PostgresFixture.MigratedAsync();
        var survivor = await Seed.GameAsync(db, "aardwolf-mud", "Aardwolf MUD");
        var absorbed = await Seed.GameAsync(db, "aardwolf-mud-2", "Aardwolf MUD");

        await MergeAsync(db, survivor, absorbed, reverted: Now.AddDays(1));

        var listed = await QueriesOn(db).ListAsync(new GameFilter());

        await Assert.That(listed).Count().IsEqualTo(2);
    }

    [Test]
    public async Task ThereIsNoRedirectOntoAPageThatIsNotPublic()
    {
        // A 301 is cached by the reader's browser, so sending them to a 404 is a mistake they can't
        // undo by reloading. An unclaimed submission is not a public page, so it's not a redirect
        // target either — it becomes one the moment somebody claims the survivor, which is why this
        // is decided on read rather than refused at the merge.
        await using var db = await PostgresFixture.MigratedAsync();
        var survivor = await Seed.GameAsync(db, "aardwolf-mud", "Aardwolf MUD");
        var absorbed = await Seed.GameAsync(db, "aardwolf-mud-2", "Aardwolf MUD");

        await using (var connection = await db.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                "UPDATE game SET submitted_at = @now, is_claimed = false WHERE id = @survivor",
                new { now = Now, survivor });
        }

        await MergeAsync(db, survivor, absorbed);

        await Assert.That(await new NpgsqlMergeRedirects(db.DataSource).AbsorbedIntoAsync("aardwolf-mud-2"))
            .IsNull();
    }

    [Test]
    public async Task AClaimedSurvivorIsARedirectTarget()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var survivor = await Seed.GameAsync(db, "aardwolf-mud", "Aardwolf MUD");
        var absorbed = await Seed.GameAsync(db, "aardwolf-mud-2", "Aardwolf MUD");

        await MergeAsync(db, survivor, absorbed);

        await Assert.That(await new NpgsqlMergeRedirects(db.DataSource).AbsorbedIntoAsync("aardwolf-mud-2"))
            .IsEqualTo("aardwolf-mud");
    }

    [Test]
    public async Task TheSurvivorIsUntouched()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var survivor = await Seed.GameAsync(db, "aardwolf-mud", "Aardwolf MUD");
        var absorbed = await Seed.GameAsync(db, "aardwolf-mud-2", "Aardwolf MUD");

        await MergeAsync(db, survivor, absorbed);

        await Assert.That(await QueriesOn(db).FindAsync("aardwolf-mud")).IsNotNull();
    }

    [Test]
    public async Task AnAbsorbedGameIsNotFoundByItsOwnSlug()
    {
        // The lookup goes through the same predicate as the listing, so an absorbed game stops
        // resolving — what makes the redirect load-bearing rather than a courtesy.
        // MergedGamePageTests depends on this from the other side.
        await using var db = await PostgresFixture.MigratedAsync();
        var survivor = await Seed.GameAsync(db, "aardwolf-mud", "Aardwolf MUD");
        var absorbed = await Seed.GameAsync(db, "aardwolf-mud-2", "Aardwolf MUD");

        await MergeAsync(db, survivor, absorbed);

        await Assert.That(await QueriesOn(db).FindAsync("aardwolf-mud-2")).IsNull();
    }
}
