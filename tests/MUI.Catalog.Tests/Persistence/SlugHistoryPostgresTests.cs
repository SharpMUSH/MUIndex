using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

using Npgsql;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Spec §5.7 — every slug a game has ever had redirects to it, for ever.
/// </summary>
/// <remarks>
/// The promise is about URLs strangers are holding, so these assert on what the database answers
/// rather than on what a writer believes it wrote. The properties that matter are: a former slug
/// resolves in one hop however many renames ago it was, nothing is ever deleted, an archived game's
/// URLs work exactly as its page does, and no slug can be made to redirect to itself.
/// </remarks>
public class SlugHistoryPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    [Test]
    public async Task ASlugAGameGaveUpRedirectsToTheOneItHasNow()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var history = new NpgsqlSlugHistoryStore(db.DataSource);
        var id = await Seed.GameAsync(db);

        var retired = await games.RenameAsync(id, "Harbourlight", "harbourlight", Now);

        await Assert.That(retired).IsEqualTo("corvid");
        await Assert.That(await history.CurrentSlugAsync("corvid")).IsEqualTo("harbourlight");
        await Assert.That((await games.ByIdAsync(id))!.Name).IsEqualTo("Harbourlight");
    }

    [Test]
    public async Task AGameRenamedTwiceRedirectsFromItsOldestUrlInOneHop()
    {
        // The row names a game rather than another slug, so a chain is not a chain: both former
        // URLs point at the game and resolve to whatever it is called now.
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var history = new NpgsqlSlugHistoryStore(db.DataSource);
        var id = await Seed.GameAsync(db);

        await games.RenameAsync(id, "Harbourlight", "harbourlight", Now);
        await games.RenameAsync(id, "Tidewater Nights", "tidewater-nights", Now.AddYears(1));

        await Assert.That(await history.CurrentSlugAsync("corvid")).IsEqualTo("tidewater-nights");
        await Assert.That(await history.CurrentSlugAsync("harbourlight")).IsEqualTo("tidewater-nights");

        // Nothing is ever deleted: the middle URL is still a row of its own.
        var worn = await history.ForGameAsync(id);
        await Assert.That(worn.Select(row => row.Slug).Order().ToList())
            .IsEquivalentTo(new[] { "corvid", "harbourlight" });
    }

    [Test]
    public async Task NoSlugRedirectsToItself()
    {
        // A game may take back a name it used to have, which leaves a row pointing at a slug that is
        // current again. Answering with it would be a 301 to the URL that was asked for — a loop no
        // reader can escape — so the row stops answering instead, and is still not deleted.
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var history = new NpgsqlSlugHistoryStore(db.DataSource);
        var id = await Seed.GameAsync(db);

        await games.RenameAsync(id, "Harbourlight", "harbourlight", Now);
        await games.RenameAsync(id, "Corvid", "corvid", Now.AddYears(1));

        await Assert.That(await history.CurrentSlugAsync("corvid")).IsNull();
        await Assert.That(await history.CurrentSlugAsync("harbourlight")).IsEqualTo("corvid");
        await Assert.That(await history.RetiredByAsync("corvid")).IsEqualTo(id);
    }

    [Test]
    public async Task AnArchivedGamesFormerUrlsWorkExactlyAsItsPageDoes()
    {
        // §7.5: archiving takes a game out of the default listing and out of nothing else. Its URLs
        // are part of "and nothing else", so nothing here filters on state.
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var history = new NpgsqlSlugHistoryStore(db.DataSource);
        var id = await Seed.GameAsync(db);

        await games.RenameAsync(id, "Harbourlight", "harbourlight", Now);
        await games.SetStateAsync(id, LifecycleState.Archived, Now.AddYears(1));

        await Assert.That(await history.CurrentSlugAsync("corvid")).IsEqualTo("harbourlight");
    }

    [Test]
    public async Task ANameThatMintsTheSameSlugRetiresNothing()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var history = new NpgsqlSlugHistoryStore(db.DataSource);
        var id = await Seed.GameAsync(db);

        var retired = await games.RenameAsync(id, "Corvid!", "corvid", Now);

        await Assert.That(retired).IsNull();
        await Assert.That(await history.ForGameAsync(id)).IsEmpty();
        await Assert.That((await games.ByIdAsync(id))!.Name).IsEqualTo("Corvid!");
    }

    [Test]
    public async Task AGameThatGivesUpAUrlItHasGivenUpBeforeStillReportsTheMove()
    {
        // A -> B -> A -> B. The fourth rename retires a slug already in the table, so the insert is
        // suppressed — and reading the answer off the insert reported "the slug did not move" while
        // the URL moved underneath it. What the caller asked was what this game used to be at, and
        // the row it already has is the record, not the write.
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var history = new NpgsqlSlugHistoryStore(db.DataSource);
        var id = await Seed.GameAsync(db);

        await games.RenameAsync(id, "Harbourlight", "harbourlight", Now);
        await games.RenameAsync(id, "Corvid", "corvid", Now.AddDays(1));
        var again = await games.RenameAsync(id, "Harbourlight", "harbourlight", Now.AddDays(2));

        await Assert.That(again).IsEqualTo("corvid");
        await Assert.That(await history.CurrentSlugAsync("corvid")).IsEqualTo("harbourlight");

        // And the first retirement is the one kept: a row says when a URL somebody bookmarked
        // started redirecting, and taking a name back for a day does not rewrite that.
        var corvid = (await history.ForGameAsync(id)).Single(row => row.Slug == "corvid");
        await Assert.That(corvid.RetiredAt).IsEqualTo(Now);
    }

    [Test]
    public async Task ASlugSomebodyIsStillHoldingIsNotFreeForAnotherGame()
    {
        // The half of the uniqueness question game.slug cannot answer: no game currently wears
        // 'corvid', and it is still spoken for, because a URL that once worked keeps working.
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var history = new NpgsqlSlugHistoryStore(db.DataSource);
        var first = await Seed.GameAsync(db);
        var second = await Seed.GameAsync(db, "gaslight-row", "Gaslight Row");

        await games.RenameAsync(first, "Harbourlight", "harbourlight", Now);

        await Assert.That(await games.BySlugAsync("corvid")).IsNull();
        await Assert.That(await history.RetiredByAsync("corvid")).IsEqualTo(first);
        await Assert.That(await history.RetiredByAsync("corvid")).IsNotEqualTo(second);
    }

    [Test]
    public async Task OneUrlCanOnlyEverHaveBelongedToOneGame()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var first = await Seed.GameAsync(db);
        var second = await Seed.GameAsync(db, "gaslight-row", "Gaslight Row");

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            "INSERT INTO game_slug_history (slug, game_id, retired_at) VALUES ('corvid', @first, @at)",
            new { first, at = Now });

        await Assert.That(async () => await connection.ExecuteAsync(
                "INSERT INTO game_slug_history (slug, game_id, retired_at) VALUES ('corvid', @second, @at)",
                new { second, at = Now }))
            .Throws<PostgresException>();
    }

    [Test]
    public async Task RenamingOntoAUrlAnotherGameWearsFails()
    {
        // The unique index on game.slug, reached through the rename. Two games answering to one URL
        // is the one outcome worse than a 404.
        await using var db = await PostgresFixture.MigratedAsync();
        var games = new NpgsqlGameStore(db.DataSource);
        var first = await Seed.GameAsync(db);
        await Seed.GameAsync(db, "gaslight-row", "Gaslight Row");

        await Assert.That(async () => await games.RenameAsync(first, "Gaslight Row", "gaslight-row", Now))
            .Throws<PostgresException>();

        await Assert.That((await games.ByIdAsync(first))!.Slug).IsEqualTo("corvid");
    }

    [Test]
    public async Task AFormerSlugOfAGameNobodyEverListedIsNothingRatherThanAThrow()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var history = new NpgsqlSlugHistoryStore(db.DataSource);

        await Assert.That(await history.CurrentSlugAsync("nobody")).IsNull();
        await Assert.That(await history.RetiredByAsync("nobody")).IsNull();
    }
}
