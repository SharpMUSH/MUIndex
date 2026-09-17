using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler.Persistence;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>
/// The other seam that carries a fact about the game to the probe: whether §7.8 is still ahead of
/// it.
/// </summary>
/// <remarks>
/// <para>
/// Read by <see cref="ProbeTarget.AwaitingCorroboration"/>, which decides whether a count a game
/// states on its own connect screen may buy the same silence an MSSP report buys. It fails the same
/// silent way the charset override does — a subquery that always returned true would leave every
/// probe succeeding and every non-Postgres test passing, with nothing to show for it but a
/// <c>WHO</c> still going out at a login prompt every thirty minutes.
/// </para>
/// <para>
/// True is the safe answer and therefore the one a bug would hide behind, so each case here asserts
/// the false it is supposed to produce.
/// </para>
/// </remarks>
public class AwaitingCorroborationPostgresTests
{
    private static GameRecord Game(Guid id, DateTimeOffset? submittedAt = null) => new(
        id,
        Slug: "fantasy-space",
        Name: "Fantasy Space (狂想空間)",
        Tagline: null,
        State: LifecycleState.Active,
        IsClaimed: false,
        FirstSeenAt: DateTimeOffset.UtcNow,
        SubmittedAt: submittedAt);

    private static CrawlTarget Target(Guid? gameId) => new()
    {
        Id = Guid.CreateVersion7(),
        GameId = gameId,
        Host = "fs.twkang.net",
        Port = 5555,
        NextProbeAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        FirstSeenAt = DateTimeOffset.UtcNow,
    };

    private static async Task<CrawlTarget> OnlyDueAsync(NpgsqlCrawlTargetRepository targets) =>
        (await targets.DueAsync(DateTimeOffset.UtcNow, 10, default))[0];

    /// <summary>
    /// A game we found ourselves was never a submission, so it was never waiting on one.
    /// </summary>
    /// <remarks>
    /// The overwhelming majority of the catalogue, and the case the whole change is for: these games
    /// are past §7.8 from the moment they are minted, because §7.8 is about publishing a submission
    /// and nobody submitted them.
    /// </remarks>
    [Test]
    public async Task AGameWeFoundOurselvesIsNotAwaitingAnything()
    {
        await using var database = await PostgresFixture.MigratedAsync();

        var games = new NpgsqlGameStore(database.DataSource);
        var targets = new NpgsqlCrawlTargetRepository(database.DataSource);

        var gameId = Guid.CreateVersion7();
        await games.InsertAsync(Game(gameId));
        await targets.AddAsync(Target(gameId), default);

        await Assert.That((await OnlyDueAsync(targets)).AwaitingCorroboration).IsFalse();
        await Assert.That((await targets.ByAddressAsync("fs.twkang.net", 5555, default))!
            .AwaitingCorroboration).IsFalse();
    }

    /// <summary>
    /// A submission nothing has corroborated yet is the one case that still wants the question asked.
    /// </summary>
    [Test]
    public async Task AnUncorroboratedSubmissionIsStillWaiting()
    {
        await using var database = await PostgresFixture.MigratedAsync();

        var games = new NpgsqlGameStore(database.DataSource);
        var targets = new NpgsqlCrawlTargetRepository(database.DataSource);

        var gameId = Guid.CreateVersion7();
        await games.InsertAsync(Game(gameId, submittedAt: DateTimeOffset.UtcNow));
        await targets.AddAsync(Target(gameId), default);

        await Assert.That((await OnlyDueAsync(targets)).AwaitingCorroboration).IsTrue();

        // And stops waiting the moment a probe shows it to be a game, without anything else changing.
        await games.CorroborateAsync(gameId, DateTimeOffset.UtcNow, ["mssp"]);

        await Assert.That((await OnlyDueAsync(targets)).AwaitingCorroboration).IsFalse();
    }

    /// <summary>
    /// A target no game is bound to yet must load, and must read as still having to prove itself.
    /// </summary>
    /// <remarks>
    /// Many production targets have a null <c>game_id</c> — every address seeded or referred and not
    /// yet dialled. The subquery finds no row for them, and null has to become true rather than
    /// false: an address that has proved nothing is asked everything.
    /// </remarks>
    [Test]
    public async Task ATargetWithNoGameYetHasEverythingLeftToProve()
    {
        await using var database = await PostgresFixture.MigratedAsync();

        var targets = new NpgsqlCrawlTargetRepository(database.DataSource);
        await targets.AddAsync(Target(gameId: null), default);

        var due = await targets.DueAsync(DateTimeOffset.UtcNow, 10, default);

        await Assert.That(due).Count().IsEqualTo(1);
        await Assert.That(due[0].AwaitingCorroboration).IsTrue();
    }
}
