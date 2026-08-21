using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;
using MUI.Catalog.Tests.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Spec §7.4, §7.5 — tiered archiving, and un-archiving that needs nobody's permission.
/// </summary>
/// <remarks>
/// The formula is <see cref="ArchivePolicy"/>'s and is never restated here. These tests are about
/// what the sweeper feeds it and what it does with the answer.
/// </remarks>
public class ArchiveSweeperTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    private static readonly Guid Game = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private sealed record Scene(
        InMemoryGameStore Games,
        InMemoryAvailabilityStore Availability,
        InMemoryReachableHistory History,
        ArchiveSweeper Sweeper);

    private static Scene Build(
        LifecycleState state = LifecycleState.Dark,
        bool isClaimed = false)
    {
        var games = new InMemoryGameStore();
        var availability = new InMemoryAvailabilityStore();
        var history = new InMemoryReachableHistory();

        games.Seed(new GameRecord(
            Game, "gaslight-row", "Gaslight Row", Tagline: null, state, isClaimed,
            FirstSeenAt: Now.AddYears(-5),
            LastReachableAt: Now.AddDays(-100),
            ArchivedAt: state is LifecycleState.Archived ? Now.AddDays(-1) : null));

        return new Scene(games, availability, history, new ArchiveSweeper(games, availability, history));
    }

    private static AvailabilityInterval Dark(double daysAgo, FailureCause cause = FailureCause.Dns) =>
        new()
        {
            GameId = Game,
            State = AvailabilityState.Unreachable,
            FromAt = Now.AddDays(-daysAgo),
            Cause = cause,
        };

    [Test]
    public async Task AYoungGameGetsTheFloorAndSurvivesFiftyNineDaysDark()
    {
        var scene = Build();
        await scene.Availability.OpenAsync(Dark(59));
        scene.History.FirstParty[Game] = TimeSpan.FromDays(30);

        var swept = await scene.Sweeper.SweepAsync(Now);

        await Assert.That(swept).IsEqualTo(new ArchiveSweep(Considered: 1, Archived: 0));
        await Assert.That((await scene.Games.ByIdAsync(Game))!.State).IsNotEqualTo(LifecycleState.Archived);
    }

    [Test]
    public async Task AYoungGameIsArchivedAtSixtyDaysDark()
    {
        var scene = Build();
        await scene.Availability.OpenAsync(Dark(60));
        scene.History.FirstParty[Game] = TimeSpan.FromDays(30);

        var swept = await scene.Sweeper.SweepAsync(Now);
        var game = await scene.Games.ByIdAsync(Game);

        await Assert.That(swept.Archived).IsEqualTo(1);
        await Assert.That(game!.State).IsEqualTo(LifecycleState.Archived);
        await Assert.That(game.ArchivedAt).IsEqualTo(Now);
    }

    [Test]
    public async Task AVenerableGameEarnsTheCeilingAndOutlastsAYearOfDarkness()
    {
        // Four years of measured reachable time is 365 days of grace, and no more — the clamp stops
        // a decade-old institution being unarchivable.
        var scene = Build();
        await scene.Availability.OpenAsync(Dark(364));
        scene.History.FirstParty[Game] = TimeSpan.FromDays(365 * 6);

        var swept = await scene.Sweeper.SweepAsync(Now);

        await Assert.That(swept.Archived).IsEqualTo(0);
    }

    [Test]
    public async Task AGameWeHaveOnlyJustFoundGetsTheFloorHoweverItWasFound()
    {
        // The backfill imports no history (spec §7.6) — it contributes addresses only, every fact is
        // measured here — so a game found via a listing and one found via a referral start at the
        // same place: the floor.
        var scene = Build();
        await scene.Availability.OpenAsync(Dark(59));

        await Assert.That((await scene.Sweeper.SweepAsync(Now)).Archived).IsEqualTo(0);

        var later = Build();
        await later.Availability.OpenAsync(Dark(61));

        await Assert.That((await later.Sweeper.SweepAsync(Now)).Archived).IsEqualTo(1);
    }

    [Test]
    public async Task AClaimedGameReceivesTheCeilingOutright()
    {
        // Demonstrated server access is worth a year regardless of how long we've watched (§7.5, §8).
        var scene = Build(isClaimed: true);
        await scene.Availability.OpenAsync(Dark(364));
        scene.History.FirstParty[Game] = TimeSpan.Zero;

        await Assert.That((await scene.Sweeper.SweepAsync(Now)).Archived).IsEqualTo(0);
    }

    [Test]
    public async Task AGameThatIsStillReachableIsNeverArchivedHoweverLongWeHaveKnownIt()
    {
        var scene = Build(LifecycleState.Active);
        await scene.Availability.OpenAsync(new AvailabilityInterval
        {
            GameId = Game,
            State = AvailabilityState.Reachable,
            FromAt = Now.AddYears(-3),
        });

        await Assert.That((await scene.Sweeper.SweepAsync(Now)).Archived).IsEqualTo(0);
    }

    [Test]
    public async Task ADegradedGameIsNotDark()
    {
        // Its socket answered and the session didn't finish. Archiving it would record our own probe
        // timeout as the game's absence (rule 5).
        var scene = Build();
        await scene.Availability.OpenAsync(new AvailabilityInterval
        {
            GameId = Game,
            State = AvailabilityState.Degraded,
            FromAt = Now.AddDays(-400),
            Cause = FailureCause.HandshakeStalled,
        });

        await Assert.That((await scene.Sweeper.SweepAsync(Now)).Archived).IsEqualTo(0);
    }

    [Test]
    public async Task AGameNobodyHasProbedYetHasNoDarknessToMeasure()
    {
        var scene = Build();

        await Assert.That((await scene.Sweeper.SweepAsync(Now)).Archived).IsEqualTo(0);
    }

    [Test]
    public async Task OneSuccessfulProbeUnarchivesAGameImmediately()
    {
        // Automatic and immediate, with no human on either side of the transition.
        var scene = Build(LifecycleState.Archived);

        var restored = await scene.Sweeper.RestoreAsync(Game, Now);
        var game = await scene.Games.ByIdAsync(Game);

        await Assert.That(restored).IsTrue();
        await Assert.That(game!.State).IsEqualTo(LifecycleState.Active);
        await Assert.That(game.ArchivedAt).IsNull();
    }

    [Test]
    public async Task RestoringAGameThatWasNotArchivedChangesNothing()
    {
        var scene = Build(LifecycleState.Active);

        await Assert.That(await scene.Sweeper.RestoreAsync(Game, Now)).IsFalse();
        await Assert.That((await scene.Games.ByIdAsync(Game))!.State).IsEqualTo(LifecycleState.Active);
    }

    [Test]
    public async Task ArchivingKeepsEverythingItPromisedToKeep()
    {
        // §7.5: archiving is a presentation change, never a deletion.
        var scene = Build();
        await scene.Availability.OpenAsync(Dark(400));

        await scene.Sweeper.SweepAsync(Now);
        var game = await scene.Games.ByIdAsync(Game);

        await Assert.That(game!.State).IsEqualTo(LifecycleState.Archived);
        await Assert.That(game.Slug).IsEqualTo("gaslight-row");
        await Assert.That(game.Name).IsEqualTo("Gaslight Row");
        await Assert.That(game.FirstSeenAt).IsEqualTo(Now.AddYears(-5));
        await Assert.That(await scene.Availability.ForGameAsync(Game)).Count().IsEqualTo(1);
    }
}
