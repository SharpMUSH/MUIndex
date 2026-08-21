using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The icon cache against a real database (migration 0013).
/// </summary>
/// <remarks>
/// The interesting half is <see cref="IIconStore.DueAsync"/>, which is the only query here with a
/// decision in it: which icon is most worth fetching next, read through the same field precedence the
/// page uses so an owner's override is the URL we go to.
/// </remarks>
public class IconStorePostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task WhatIsStoredComesBack()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var icons = new NpgsqlIconStore(db.DataSource);

        await icons.UpsertAsync(Icon(game, "https://corvid.example/logo.png"), None);

        var read = await icons.ForGameAsync(game, None);

        await Assert.That(read!.ContentType).IsEqualTo("image/png");
        await Assert.That(read.Width).IsEqualTo(48);
        await Assert.That(read.Bytes).IsEquivalentTo(new byte[] { 1, 2, 3 });
        await Assert.That(read.ETag).IsEqualTo("\"abc\"");
    }

    [Test]
    public async Task ARefetchReplacesRatherThanAccumulates()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var icons = new NpgsqlIconStore(db.DataSource);

        await icons.UpsertAsync(Icon(game, "https://corvid.example/old.png"), None);
        await icons.UpsertAsync(Icon(game, "https://corvid.example/new.png"), None);

        await Assert.That((await icons.ForGameAsync(game, None))!.SourceUrl)
            .IsEqualTo("https://corvid.example/new.png");

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM game_icon")).IsEqualTo(1);
    }

    /// <summary>A game whose ICON field names a URL we hold nothing for is due.</summary>
    [Test]
    public async Task AGameWithADeclaredIconAndNoCachedOneIsDue()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var icons = new NpgsqlIconStore(db.DataSource);

        await FieldAsync(db, game, FieldSource.Mssp, "https://corvid.example/logo.png");

        var due = await icons.DueAsync(10, Now, None);

        await Assert.That(due.Count).IsEqualTo(1);
        await Assert.That(due[0].DeclaredUrl).IsEqualTo("https://corvid.example/logo.png");
        await Assert.That(due[0].CachedUrl).IsNull();
    }

    /// <summary>
    /// An owner's ICON outranks their game's, because that is what §8.5 put it in the overridable
    /// set for.
    /// </summary>
    /// <remarks>
    /// Read through the same precedence the page uses. A refresher that fetched the MSSP URL while
    /// the page said the owner's would serve one picture and credit another.
    /// </remarks>
    [Test]
    public async Task TheUrlWeFetchIsTheOneThePageWouldShow()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var icons = new NpgsqlIconStore(db.DataSource);

        await FieldAsync(db, game, FieldSource.Mssp, "https://corvid.example/config.png");
        await FieldAsync(db, game, FieldSource.Owner, "https://corvid.example/chosen.png");

        var due = await icons.DueAsync(10, Now, None);

        await Assert.That(due.Single().DeclaredUrl).IsEqualTo("https://corvid.example/chosen.png");
    }

    /// <summary>
    /// A URL that has moved since we cached it sorts ahead of one that is merely old.
    /// </summary>
    /// <remarks>
    /// The first is an icon we are currently serving from the wrong address, which is a small
    /// untruth on a page; the second is a picture that might have been edited in place, which is
    /// not. One pass is bounded, so the order is what decides which gets fixed today.
    /// </remarks>
    [Test]
    public async Task AMovedUrlIsFetchedBeforeAMerelyStaleOne()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var stale = await Seed.GameAsync(db, slug: "stale", name: "Stale");
        var moved = await Seed.GameAsync(db, slug: "moved", name: "Moved");
        var icons = new NpgsqlIconStore(db.DataSource);

        await FieldAsync(db, stale, FieldSource.Mssp, "https://stale.example/logo.png");
        await icons.UpsertAsync(
            Icon(stale, "https://stale.example/logo.png") with { FetchedAt = Now.AddYears(-1) }, None);

        await FieldAsync(db, moved, FieldSource.Mssp, "https://moved.example/new.png");
        await icons.UpsertAsync(
            Icon(moved, "https://moved.example/old.png") with { FetchedAt = Now }, None);

        var due = await icons.DueAsync(10, Now.AddDays(-7), None);

        await Assert.That(due.Select(d => d.GameId)).IsEquivalentTo(new[] { moved, stale });
        await Assert.That(due[0].GameId).IsEqualTo(moved);
    }

    /// <summary>A freshly fetched icon at an unchanged URL is not due again.</summary>
    [Test]
    public async Task AFreshIconAtAnUnchangedUrlIsNotDue()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var icons = new NpgsqlIconStore(db.DataSource);

        await FieldAsync(db, game, FieldSource.Mssp, "https://corvid.example/logo.png");
        await icons.UpsertAsync(Icon(game, "https://corvid.example/logo.png"), None);

        await Assert.That(await icons.DueAsync(10, Now.AddDays(-7), None)).IsEmpty();
    }

    /// <summary>
    /// A withdrawn override is an empty row, and an empty row names no URL to fetch.
    /// </summary>
    /// <remarks>
    /// Nothing is deleted, so the row outlives the override. Read without the empty-value filter this
    /// would win the precedence group and leave the game with no icon at all rather than handing the
    /// field back to its own report — the same trap <c>NpgsqlGameQueries</c> already documents.
    /// </remarks>
    [Test]
    public async Task AWithdrawnOverrideHandsTheIconBackToTheReport()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var icons = new NpgsqlIconStore(db.DataSource);

        await FieldAsync(db, game, FieldSource.Mssp, "https://corvid.example/config.png");
        await FieldAsync(db, game, FieldSource.Owner, string.Empty);

        var due = await icons.DueAsync(10, Now, None);

        await Assert.That(due.Single().DeclaredUrl).IsEqualTo("https://corvid.example/config.png");
    }

    private static GameIcon Icon(Guid game, string url) =>
        new(game, url, "image/png", 48, 48, [1, 2, 3], "\"abc\"", Now);

    private static async Task FieldAsync(TestDatabase db, Guid game, FieldSource source, string url)
    {
        await new NpgsqlGameFieldStore(db.DataSource).UpsertAsync(
            new GameField(game, "ICON", source, url, Now.AddYears(-1), Now), None);
    }
}
