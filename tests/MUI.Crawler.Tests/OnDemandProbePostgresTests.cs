using Dapper;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// §8.1's on-demand check, against the table that decides what gets probed.
/// </summary>
/// <remarks>
/// Due-ness comes only from <c>crawl_target.next_probe_at</c>, so these assert on the schedule rather
/// than on any audit log — a rate limiter on an action that doesn't move the schedule is a convincing
/// no-op.
/// </remarks>
public class OnDemandProbePostgresTests
{
    /// <summary>An owner who has just edited mush.cnf is not left waiting on the scheduler.</summary>
    [Test]
    public async Task AskingForACheckBringsTheGamesTargetForward()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var now = DateTimeOffset.UtcNow;

        var (game, target) = await SeedAsync(db, next: now.AddHours(6));

        var moved = await new NpgsqlOnDemandProbes(db.DataSource).BringForwardAsync(game, now);

        await Assert.That(moved).IsTrue();
        await Assert.That(await NextProbeAsync(db, target)).IsEqualTo(now).Within(TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// An ask can only make a probe sooner, never later.
    /// </summary>
    /// <remarks>
    /// <c>LEAST</c> rather than an assignment — pushing an overdue target back to "now" would let a
    /// claimant pressing the button walk their own game backwards.
    /// </remarks>
    [Test]
    public async Task AnAskNeverPushesAProbeBack()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var now = DateTimeOffset.UtcNow;
        var overdue = now.AddHours(-3);

        var (game, target) = await SeedAsync(db, next: overdue);

        await new NpgsqlOnDemandProbes(db.DataSource).BringForwardAsync(game, now);

        await Assert.That(await NextProbeAsync(db, target))
            .IsEqualTo(overdue).Within(TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Every address of the game moves, because a game is claimed and a target is an address.
    /// </summary>
    /// <remarks>
    /// A game may have several endpoints (§5.5); moving only one would make the button work or not
    /// depending on which listener the crawler found first.
    /// </remarks>
    [Test]
    public async Task EveryTargetForTheGameMoves()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var now = DateTimeOffset.UtcNow;

        var (game, first) = await SeedAsync(db, next: now.AddHours(6));
        var second = await AttachAsync(db, game, "other.example.org", 4202, now.AddHours(9));

        await new NpgsqlOnDemandProbes(db.DataSource).BringForwardAsync(game, now);

        await Assert.That(await NextProbeAsync(db, first)).IsEqualTo(now).Within(TimeSpan.FromSeconds(1));
        await Assert.That(await NextProbeAsync(db, second)).IsEqualTo(now).Within(TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// A game the registry has no target for is not an error, and says so by answering false.
    /// </summary>
    /// <remarks>
    /// It happens: a game merged away under §7.3, or one added by a path that never minted a target.
    /// </remarks>
    [Test]
    public async Task AGameWithNoTargetMovesNothingAndDoesNotThrow()
    {
        await using var db = await PostgresFixture.MigratedAsync();

        var moved = await new NpgsqlOnDemandProbes(db.DataSource)
            .BringForwardAsync(Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        await Assert.That(moved).IsFalse();
    }

    /// <summary>
    /// One game's ask does not disturb another's schedule.
    /// </summary>
    [Test]
    public async Task AnAskTouchesOnlyTheGameItNames()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var now = DateTimeOffset.UtcNow;
        var later = now.AddHours(6);

        var (mine, _) = await SeedAsync(db, next: later);
        var (_, theirs) = await SeedAsync(db, next: later, slug: "other", host: "third.example.org");

        await new NpgsqlOnDemandProbes(db.DataSource).BringForwardAsync(mine, now);

        await Assert.That(await NextProbeAsync(db, theirs))
            .IsEqualTo(later).Within(TimeSpan.FromSeconds(1));
    }

    private static async Task<(Guid Game, Guid Target)> SeedAsync(
        TestDatabase db,
        DateTimeOffset next,
        string slug = "corvid",
        string host = "mud.example.org")
    {
        var game = Guid.CreateVersion7();

        await new NpgsqlGameStore(db.DataSource).InsertAsync(new GameRecord(
            game,
            slug,
            slug,
            Tagline: null,
            LifecycleState.Active,
            IsClaimed: true,
            FirstSeenAt: DateTimeOffset.UtcNow.AddYears(-1),
            LastReachableAt: null,
            ArchivedAt: null));

        return (game, await AttachAsync(db, game, host, 4201, next));
    }

    private static async Task<Guid> AttachAsync(
        TestDatabase db,
        Guid game,
        string host,
        int port,
        DateTimeOffset next)
    {
        var targets = new NpgsqlCrawlTargetRepository(db.DataSource);

        var id = await targets.AddAsync(
            new CrawlTarget
            {
                Id = Guid.CreateVersion7(),
                Host = host,
                Port = port,
                Depth = 0,
                NextProbeAt = next,
                FirstSeenAt = DateTimeOffset.UtcNow,
            },
            CancellationToken.None);

        await targets.AttachGameAsync(id, game, CancellationToken.None);

        return id;
    }

    /// <summary>
    /// The stored schedule, read back.
    /// </summary>
    /// <remarks>
    /// Through <see cref="DateTime"/>, because Npgsql hands back a <c>DateTime</c> for a
    /// <c>timestamptz</c> and asking Dapper for a <see cref="DateTimeOffset"/> is an invalid cast —
    /// the same thing <c>NpgsqlClaimStore</c>'s own row type exists to work around.
    /// </remarks>
    private static async Task<DateTimeOffset> NextProbeAsync(TestDatabase db, Guid target)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();

        var at = await connection.ExecuteScalarAsync<DateTime>(
            "SELECT next_probe_at FROM crawl_target WHERE id = @target", new { target });

        return new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc));
    }
}
