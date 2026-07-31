using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Spec §5.3 — only a change of state or cause writes a transition.
/// </summary>
public class AvailabilityWriterTests
{
    private static readonly Guid Game = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly DateTimeOffset Start = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task TheFirstObservationOpensTheFirstInterval()
    {
        var store = new InMemoryAvailabilityStore();

        var outcome = await new AvailabilityWriter(store).ObserveAsync(
            Game, AvailabilityState.Reachable, FailureCause.None, Start);

        await Assert.That(outcome).IsEqualTo(AvailabilityOutcome.Opened);
        await Assert.That(store.Intervals).Count().IsEqualTo(1);
        await Assert.That(store.Intervals[0].IsOpen).IsTrue();
    }

    [Test]
    public async Task AHundredConsecutiveTimeoutsAreOneIntervalNotAHundred()
    {
        // The property the whole table exists for. On a sample series this would be a hundred rows
        // and "longest outage" would be a scan; here it is one row and a subtraction.
        var store = new InMemoryAvailabilityStore();
        var writer = new AvailabilityWriter(store);

        for (var probe = 0; probe < 100; probe++)
        {
            await writer.ObserveAsync(
                Game, AvailabilityState.Unreachable, FailureCause.Timeout, Start.AddHours(probe));
        }

        await Assert.That(store.Intervals).Count().IsEqualTo(1);
        await Assert.That(store.Intervals[0].FromAt).IsEqualTo(Start);
        await Assert.That(store.Intervals[0].Cause).IsEqualTo(FailureCause.Timeout);
    }

    [Test]
    public async Task ARepeatedObservationWritesNothingAtAll()
    {
        var store = new InMemoryAvailabilityStore();
        var writer = new AvailabilityWriter(store);

        await writer.ObserveAsync(Game, AvailabilityState.Reachable, FailureCause.None, Start);
        var second = await writer.ObserveAsync(
            Game, AvailabilityState.Reachable, FailureCause.None, Start.AddHours(1));

        await Assert.That(second).IsEqualTo(AvailabilityOutcome.Extended);
        await Assert.That(store.Intervals).Count().IsEqualTo(1);

        // The open interval's from_at is untouched, which is what makes its duration the length of
        // the outage rather than the length of the last probe cycle.
        await Assert.That(store.Intervals[0].FromAt).IsEqualTo(Start);
    }

    [Test]
    public async Task AChangeOfCauseAloneWritesATransition()
    {
        // Unreachable throughout, but the reason moved from "the host is gone" to "the host is
        // there and refusing". Those are different facts about a game and the series must show both.
        var store = new InMemoryAvailabilityStore();
        var writer = new AvailabilityWriter(store);

        await writer.ObserveAsync(Game, AvailabilityState.Unreachable, FailureCause.Dns, Start);
        var moved = await writer.ObserveAsync(
            Game, AvailabilityState.Unreachable, FailureCause.Refused, Start.AddHours(1));

        await Assert.That(moved).IsEqualTo(AvailabilityOutcome.Transitioned);
        await Assert.That(store.Intervals).Count().IsEqualTo(2);
        await Assert.That(store.Intervals[0].ToAt).IsEqualTo(Start.AddHours(1));
        await Assert.That(store.Intervals[1].IsOpen).IsTrue();
        await Assert.That(store.Intervals[1].Cause).IsEqualTo(FailureCause.Refused);
    }

    [Test]
    public async Task AChangeOfStateWritesATransitionAndLeavesTheClosedIntervalInTheHistory()
    {
        var store = new InMemoryAvailabilityStore();
        var writer = new AvailabilityWriter(store);

        await writer.ObserveAsync(Game, AvailabilityState.Reachable, FailureCause.None, Start);
        await writer.ObserveAsync(
            Game, AvailabilityState.Unreachable, FailureCause.Timeout, Start.AddDays(1));

        await Assert.That(store.Intervals).Count().IsEqualTo(2);
        await Assert.That(store.Intervals[0].State).IsEqualTo(AvailabilityState.Reachable);
        await Assert.That(store.Intervals[0].IsOpen).IsFalse();
        await Assert.That(await store.OpenIntervalAsync(Game)).IsNotNull();
    }

    [Test]
    public async Task DegradedIsItsOwnIntervalRatherThanEitherNeighbour()
    {
        // We got in and could not finish. Neither reachable nor unreachable, and the design draws it
        // as its own bar — so a transition into it is a transition.
        var store = new InMemoryAvailabilityStore();
        var writer = new AvailabilityWriter(store);

        await writer.ObserveAsync(Game, AvailabilityState.Reachable, FailureCause.None, Start);
        var moved = await writer.ObserveAsync(
            Game, AvailabilityState.Degraded, FailureCause.HandshakeStalled, Start.AddHours(1));

        await Assert.That(moved).IsEqualTo(AvailabilityOutcome.Transitioned);
        await Assert.That(store.Intervals[1].State).IsEqualTo(AvailabilityState.Degraded);
    }

    [Test]
    public async Task AReachableIntervalMayNotCarryAFailureCause()
    {
        var writer = new AvailabilityWriter(new InMemoryAvailabilityStore());

        await Assert.That(async () => await writer.ObserveAsync(
                Game, AvailabilityState.Reachable, FailureCause.Timeout, Start))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AnIntervalThatIsNotReachableMustSayWhatStoppedUs()
    {
        // Rule 5: never record a decision of ours as a measurement of theirs. An unreachable interval
        // with no cause is a shrug filed as a fact about a game.
        var writer = new AvailabilityWriter(new InMemoryAvailabilityStore());

        await Assert.That(async () => await writer.ObserveAsync(
                Game, AvailabilityState.Unreachable, FailureCause.None, Start))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task OneOpenIntervalPlusTheArithmeticGivesTheLongestOutage()
    {
        var store = new InMemoryAvailabilityStore();
        var writer = new AvailabilityWriter(store);

        await writer.ObserveAsync(Game, AvailabilityState.Reachable, FailureCause.None, Start);

        for (var hour = 0; hour < 50; hour++)
        {
            await writer.ObserveAsync(
                Game, AvailabilityState.Unreachable, FailureCause.Timeout, Start.AddDays(1).AddHours(hour));
        }

        await writer.ObserveAsync(
            Game, AvailabilityState.Reachable, FailureCause.None, Start.AddDays(1).AddHours(50));

        var intervals = await store.ForGameAsync(Game);
        var longest = Reachability.LongestOutage(
            intervals, TimeSpan.FromDays(90), Start.AddDays(10));

        await Assert.That(intervals).Count().IsEqualTo(3);
        await Assert.That(longest!.Value.TotalHours).IsEqualTo(50).Within(0.001);
    }
}
