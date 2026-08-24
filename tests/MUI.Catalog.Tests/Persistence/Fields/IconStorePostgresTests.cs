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

        var due = await icons.DueAsync(10, Now, Now, None);

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

        var due = await icons.DueAsync(10, Now, Now, None);

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

        var due = await icons.DueAsync(10, Now.AddDays(-7), Now, None);

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

        await Assert.That(await icons.DueAsync(10, Now.AddDays(-7), Now, None)).IsEmpty();
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

        var due = await icons.DueAsync(10, Now, Now, None);

        await Assert.That(due.Single().DeclaredUrl).IsEqualTo("https://corvid.example/config.png");
    }

    /// <summary>
    /// The bug this table exists for: a bounded pass over candidates that all tie must not return the
    /// same ones for ever.
    /// </summary>
    /// <remarks>
    /// Every game with a declared <c>ICON</c> and nothing cached held nothing to rank by, so all of
    /// them tied on both sort keys and <c>LIMIT</c> chose the same rows every time. A failed fetch
    /// wrote nothing, so nothing ever broke the tie. Production ran this way for six days: fifteen
    /// URLs re-fetched every thirty minutes, all failing, while forty-seven games were never
    /// attempted once. Two games and a limit of one is the same shape at the smallest size that shows
    /// it.
    /// </remarks>
    [Test]
    public async Task ACandidateThatFailedIsNotOfferedAgainAheadOfOneNeverTried()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var first = await Seed.GameAsync(db, slug: "first", name: "First");
        var second = await Seed.GameAsync(db, slug: "second", name: "Second");
        var icons = new NpgsqlIconStore(db.DataSource);

        await FieldAsync(db, first, FieldSource.Mssp, "https://first.example/logo.png");
        await FieldAsync(db, second, FieldSource.Mssp, "https://second.example/logo.png");

        var head = (await icons.DueAsync(1, Now, Now, None)).Single();

        await icons.RecordFailureAsync(
            new IconAttempt(head.GameId, head.DeclaredUrl, Now, 1, Now.AddMinutes(30)), None);

        // Half an hour on: the one that failed is back in the queue, and behind the one that has
        // still never been asked.
        var next = (await icons.DueAsync(1, Now, Now.AddHours(1), None)).Single();

        await Assert.That(next.GameId).IsNotEqualTo(head.GameId);
    }

    /// <summary>A URL that just failed is not asked again until its back-off has elapsed.</summary>
    /// <remarks>
    /// The other half of the same fix, and the half the operators of fifteen web servers would have
    /// cared about: forty-eight requests a day each, indefinitely, for an image that was not there.
    /// </remarks>
    [Test]
    public async Task AFailedUrlWaitsOutItsBackOff()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var icons = new NpgsqlIconStore(db.DataSource);

        await FieldAsync(db, game, FieldSource.Mssp, "https://corvid.example/logo.png");
        await icons.RecordFailureAsync(
            new IconAttempt(game, "https://corvid.example/logo.png", Now, 1, Now.AddHours(2)), None);

        await Assert.That(await icons.DueAsync(10, Now, Now.AddHours(1), None)).IsEmpty();
        await Assert.That(await icons.DueAsync(10, Now, Now.AddHours(3), None)).IsNotEmpty();
    }

    /// <summary>
    /// A new address is a new question: the old one's failures neither delay it nor count against it.
    /// </summary>
    /// <remarks>
    /// An owner who fixes a broken <c>ICON</c> would otherwise serve out the back-off earned by the
    /// URL they had just replaced — up to a week of it, for having corrected the thing we were
    /// complaining about.
    /// </remarks>
    [Test]
    public async Task AnAttemptAgainstAnAddressTheGameHasLeftDelaysNothing()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var icons = new NpgsqlIconStore(db.DataSource);

        await FieldAsync(db, game, FieldSource.Mssp, "https://corvid.example/new.png");
        await icons.RecordFailureAsync(
            new IconAttempt(game, "https://corvid.example/old.png", Now, 6, Now.AddDays(7)), None);

        var due = await icons.DueAsync(10, Now, Now.AddMinutes(1), None);

        await Assert.That(due.Single().DeclaredUrl).IsEqualTo("https://corvid.example/new.png");
        await Assert.That(due[0].Failures).IsEqualTo(0);
    }

    /// <summary>The failure count is what the caller sizes the next back-off from, so it comes back.</summary>
    [Test]
    public async Task TheFailureCountComesBackWithTheCandidate()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var icons = new NpgsqlIconStore(db.DataSource);

        await FieldAsync(db, game, FieldSource.Mssp, "https://corvid.example/logo.png");
        await icons.RecordFailureAsync(
            new IconAttempt(game, "https://corvid.example/logo.png", Now, 3, Now.AddHours(2)), None);

        await Assert.That((await icons.DueAsync(10, Now, Now.AddHours(3), None)).Single().Failures)
            .IsEqualTo(3);
    }

    /// <summary>
    /// An icon that arrives clears the failures behind it, so the next one starts from the beginning.
    /// </summary>
    /// <remarks>
    /// Left standing, a game that failed six times and then succeeded would serve a week's back-off
    /// the first time its web server so much as hiccuped.
    /// </remarks>
    [Test]
    public async Task AFetchedIconClearsWhatFailedBeforeIt()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var icons = new NpgsqlIconStore(db.DataSource);

        await FieldAsync(db, game, FieldSource.Mssp, "https://corvid.example/logo.png");
        await icons.RecordFailureAsync(
            new IconAttempt(game, "https://corvid.example/logo.png", Now, 4, Now.AddDays(1)), None);

        await icons.UpsertAsync(Icon(game, "https://corvid.example/logo.png"), None);

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM icon_attempt")).IsEqualTo(0);
    }

    private static GameIcon Icon(Guid game, string url) =>
        new(game, url, "image/png", 48, 48, [1, 2, 3], "\"abc\"", Now);

    private static async Task FieldAsync(TestDatabase db, Guid game, FieldSource source, string url)
    {
        await new NpgsqlGameFieldStore(db.DataSource).UpsertAsync(
            new GameField(game, "ICON", source, url, Now.AddYears(-1), Now), None);
    }
}
