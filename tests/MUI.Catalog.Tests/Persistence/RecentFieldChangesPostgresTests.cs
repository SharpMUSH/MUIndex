using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The crawler status page's "recently updated" against a real database: the same exclusions
/// <see cref="IGameQueries.FeedsAsync"/> applies, and the same per-field internal-field filter
/// <c>NpgsqlGameFieldStore.ChangesAsync</c> applies to one game's own page, applied here across every
/// game at once.
/// </summary>
public class RecentFieldChangesPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    private static NpgsqlGameQueries QueriesOn(TestDatabase db) =>
        new(db.DataSource) { Clock = () => Now };

    private static async Task TransitionAsync(
        TestDatabase db, Guid game, string field, string from, string to, DateTimeOffset at)
    {
        var reconciler = new FieldReconciler(new NpgsqlGameFieldStore(db.DataSource));

        await reconciler.ApplyAsync(game, [new FieldObservation(field, FieldSource.Mssp, from)], at);
        await reconciler.ApplyAsync(game, [new FieldObservation(field, FieldSource.Mssp, to)], at.AddMinutes(1));
    }

    [Test]
    public async Task ATransitionOnAListedGameAppearsNewestFirst()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var older = await Seed.GameAsync(db, "older", "Older");
        var newer = await Seed.GameAsync(db, "newer", "Newer");

        await TransitionAsync(db, older, "CODEBASE", "PennMUSH 1.8.7", "PennMUSH 1.8.8p0", Now.AddHours(-2));
        await TransitionAsync(db, newer, "CODEBASE", "Evennia 0.9", "Evennia 1.0", Now.AddHours(-1));

        var changes = await QueriesOn(db).RecentFieldChangesAsync(10);

        await Assert.That(changes[0].Slug).IsEqualTo("newer");
        await Assert.That(changes[1].Slug).IsEqualTo("older");
        await Assert.That(changes[0].OldValue).IsEqualTo("Evennia 0.9");
        await Assert.That(changes[0].NewValue).IsEqualTo("Evennia 1.0");
    }

    [Test]
    public async Task AnInternalFieldNeverReachesTheFeed()
    {
        // banner_hash changes on every crawl a connect screen so much as reflows — never a fact for
        // a reader, and never a way to infer the screen's bytes from a diff of two hashes.
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db, "hashy", "Hashy");

        await TransitionAsync(db, game, InternalFields.BannerHash, "abc123", "def456", Now.AddMinutes(-5));

        await Assert.That((await QueriesOn(db).RecentFieldChangesAsync(10)).Any(c => c.Slug == "hashy")).IsFalse();
    }

    [Test]
    public async Task AnExcludedGamesChangesDoNotReachTheFeed()
    {
        // Same rule the liveness feeds apply: "recently updated" is ops diagnostics, not a public
        // listing, but it still names games and must not leak one staff excluded (a stock/dev
        // instance, migration 0024) or an owner asked out (unlisting).
        await using var db = await PostgresFixture.MigratedAsync();
        var excluded = await Seed.GameAsync(db, "hidden", "Hidden");
        var listed = await Seed.GameAsync(db, "visible", "Visible");

        await new NpgsqlGameStore(db.DataSource)
            .ExcludeAsync(excluded, "Stock configuration.", Now.AddDays(-1));

        await TransitionAsync(db, excluded, "CODEBASE", "A", "B", Now.AddMinutes(-5));
        await TransitionAsync(db, listed, "CODEBASE", "A", "B", Now.AddMinutes(-4));

        var changes = await QueriesOn(db).RecentFieldChangesAsync(10);

        await Assert.That(changes.Any(c => c.Slug == "hidden")).IsFalse();
        await Assert.That(changes.Any(c => c.Slug == "visible")).IsTrue();
    }

    [Test]
    public async Task APerGameCapKeepsOneFlappyGameFromCrowdingOutTheRest()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var flappy = await Seed.GameAsync(db, "flappy", "Flappy");
        var quiet = await Seed.GameAsync(db, "quiet-one", "Quiet One");

        for (var i = 0; i < 5; i++)
        {
            await TransitionAsync(db, flappy, "GENRE", $"G{i}", $"G{i + 1}", Now.AddMinutes(-30 + i));
        }

        await TransitionAsync(db, quiet, "GENRE", "Old", "New", Now.AddMinutes(-1));

        var changes = await QueriesOn(db).RecentFieldChangesAsync(limit: 10, perGameLimit: 2);

        await Assert.That(changes.Count(c => c.Slug == "flappy")).IsEqualTo(2);
        await Assert.That(changes.Any(c => c.Slug == "quiet-one")).IsTrue();
    }
}
