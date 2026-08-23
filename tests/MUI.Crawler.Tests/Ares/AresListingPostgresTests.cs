using Dapper;

using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// <c>ares_listing</c> against a real PostgreSQL (migration 0034).
/// </summary>
/// <remarks>
/// This table is the pass's own memory of what the hub last said, and the constraint that matters is
/// that a disappearance is a date rather than a delete: nothing here is ever removed, and a game the
/// hub drops keeps being probed on our side regardless (§7.4, §7.5).
/// </remarks>
public class AresListingPostgresTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private static AresListing Listing(string host, int port, DateTimeOffset at) => new()
    {
        Hostname = host,
        Port = port,
        Name = "A Game",
        Description = "Blurb.",
        Genre = "Sci-Fi",
        Website = $"https://{host}",
        Status = "Open",
        LastPing = "08/21/2026",
        FirstSeenAt = at,
        LastListedAt = at,
    };

    [Test]
    public async Task AListingIsRecordedAndRefreshedWithoutLosingWhenWeFirstSawIt()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var listings = new NpgsqlAresListingRepository(db.DataSource);

        await listings.UpsertAsync(Listing("one.example.org", 4201, At), default);
        await listings.UpsertAsync(
            Listing("one.example.org", 4201, At.AddDays(1)) with { Status = "Beta" }, default);

        var row = (await listings.AllAsync(default)).Single();

        await Assert.That(row.Status).IsEqualTo("Beta");
        await Assert.That(row.FirstSeenAt).IsEqualTo(At);
        await Assert.That(row.LastListedAt).IsEqualTo(At.AddDays(1));
    }

    /// <summary>
    /// A listing that stops appearing is dated, never removed — the same rule the catalogue itself
    /// follows. The date is the fact; the row stays.
    /// </summary>
    [Test]
    public async Task AListingThatStopsAppearingIsDatedRatherThanDeleted()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var listings = new NpgsqlAresListingRepository(db.DataSource);
        var second = At.AddDays(1);

        await listings.UpsertAsync(Listing("gone.example.org", 4201, At), default);
        await listings.UpsertAsync(Listing("stays.example.org", 4202, At), default);

        // Second pass: only one of them comes back.
        await listings.UpsertAsync(Listing("stays.example.org", 4202, second), default);
        var delisted = await listings.DelistMissingAsync(second, default);

        await Assert.That(delisted).IsEqualTo(1);

        var rows = await listings.AllAsync(default);
        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows.Single(r => r.Hostname == "gone.example.org").DelistedAt)
            .IsEqualTo(second);
        await Assert.That(rows.Single(r => r.Hostname == "stays.example.org").DelistedAt).IsNull();
    }

    /// <summary>
    /// A game that comes back is listed again, not left dated: the column is the hub's current
    /// opinion, not a tombstone.
    /// </summary>
    [Test]
    public async Task ARelistedGameStopsBeingDelisted()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var listings = new NpgsqlAresListingRepository(db.DataSource);

        await listings.UpsertAsync(Listing("back.example.org", 4201, At), default);
        await listings.DelistMissingAsync(At.AddDays(1), default);
        await listings.UpsertAsync(Listing("back.example.org", 4201, At.AddDays(2)), default);

        var row = (await listings.AllAsync(default)).Single();

        await Assert.That(row.DelistedAt).IsNull();
        await Assert.That(row.FirstSeenAt).IsEqualTo(At);
        await Assert.That(row.LastListedAt).IsEqualTo(At.AddDays(2));
    }

    /// <summary>
    /// A sweep run twice must not re-date a listing that was already dated — the first date is when
    /// the hub stopped mentioning it, and moving it forward would erase that.
    /// </summary>
    [Test]
    public async Task ASecondSweepDoesNotMoveAnAlreadyRecordedDelisting()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var listings = new NpgsqlAresListingRepository(db.DataSource);

        await listings.UpsertAsync(Listing("gone.example.org", 4201, At), default);
        await listings.DelistMissingAsync(At.AddDays(1), default);

        var again = await listings.DelistMissingAsync(At.AddDays(2), default);

        await Assert.That(again).IsEqualTo(0);
        await Assert.That((await listings.AllAsync(default)).Single().DelistedAt)
            .IsEqualTo(At.AddDays(1));
    }

    /// <summary>
    /// The listing attaches to the game its address turned out to be, once the ordinary crawl has
    /// promoted it. This table never mints one (§7.1).
    /// </summary>
    [Test]
    public async Task AListingBindsToTheGameItsAddressTurnedOutToBe()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var listings = new NpgsqlAresListingRepository(db.DataSource);
        var game = await GameAsync(db);

        await listings.UpsertAsync(Listing("bound.example.org", 4201, At), default);
        await listings.BindAsync("bound.example.org", 4201, game, default);

        await Assert.That((await listings.AllAsync(default)).Single().GameId).IsEqualTo(game);
    }

    /// <summary>A minimal listed game, so a binding has something real to point at.</summary>
    private static async Task<Guid> GameAsync(TestDatabase db)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await db.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO game (id, slug, name, state, is_claimed, first_seen_at)
            VALUES (@id, @slug, @name, 'active', false, @at)
            """,
            new { id, slug = $"g-{id:N}", name = "A Game", at = At });

        return id;
    }
}
