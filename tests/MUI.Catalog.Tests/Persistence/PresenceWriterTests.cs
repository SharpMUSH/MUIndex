using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Support;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Spec §5.4 — the three states an hour can be in, at the writer that decides between them.
/// </summary>
/// <remarks>
/// Collapsing "probed but uncountable" into either neighbour is the worst bug this codebase could
/// ship, and it is the one the first cut of the spec actually shipped: a game whose <c>DOING</c>
/// header is customised past our parser would render as permanently dark while running perfectly
/// well.
/// </remarks>
public class PresenceWriterTests
{
    private static readonly Guid Game = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly DateTimeOffset At = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AMeasuredZeroIsAStoredCount()
    {
        // We got in and nobody was there. That is a real and useful fact about a game, and it is a
        // filled cell rather than an absence.
        var store = new InMemoryPresenceStore();

        var outcome = await new PresenceWriter(store).WriteAsync(
            Game, PresenceReading.Counted(0, FieldSource.Who), At);

        var sample = store.Samples.Single();

        await Assert.That(outcome).IsEqualTo(PresenceOutcome.Counted);
        await Assert.That(sample.Count).IsEqualTo(0);
        await Assert.That(sample.IsCounted).IsTrue();
        await Assert.That(sample.Reason).IsNull();
    }

    [Test]
    public async Task AProbeThatSucceededAndCouldNotCountWritesARowWithNoCountAndAReason()
    {
        var store = new InMemoryPresenceStore();

        var outcome = await new PresenceWriter(store).WriteAsync(
            Game, PresenceReading.Unmeasurable(UnmeasurableReason.WhoUnparseable), At);

        var sample = store.Samples.Single();

        await Assert.That(outcome).IsEqualTo(PresenceOutcome.RecordedUnmeasurable);
        await Assert.That(sample.Count).IsNull();
        await Assert.That(sample.Reason).IsEqualTo(UnmeasurableReason.WhoUnparseable);
    }

    [Test]
    public async Task NeverHavingAskedAndAskingAndFailingAreDifferentReasons()
    {
        // A game that answers no pre-login WHO is not a game whose WHO we could not read. Both are
        // hatched cells, and the reason is what tells a maintainer which of our two problems it is.
        var store = new InMemoryPresenceStore();
        var writer = new PresenceWriter(store);

        await writer.WriteAsync(Game, PresenceReading.Unmeasurable(UnmeasurableReason.WhoNotOffered), At);
        await writer.WriteAsync(
            Game, PresenceReading.Unmeasurable(UnmeasurableReason.WhoUnparseable), At.AddHours(1));

        await Assert.That(store.Samples.Select(s => s.Reason).ToList())
            .IsEquivalentTo(new List<UnmeasurableReason?>
            {
                UnmeasurableReason.WhoNotOffered,
                UnmeasurableReason.WhoUnparseable,
            });
    }

    [Test]
    public async Task TheThreeStatesAreDistinguishableByQueryRatherThanByConvention()
    {
        // Counted, uncountable, and no row at all. Hour two is a failed probe and is written by
        // nobody — which is the assertion.
        var store = new InMemoryPresenceStore();
        var writer = new PresenceWriter(store);

        await writer.WriteAsync(Game, PresenceReading.Counted(0, FieldSource.Who), At);
        await writer.WriteAsync(
            Game, PresenceReading.Unmeasurable(UnmeasurableReason.WhoUnparseable), At.AddHours(2));

        var samples = await store.ForGameAsync(Game, At, At.AddHours(3));

        await Assert.That(samples).Count().IsEqualTo(2);
        await Assert.That(samples.Count(s => s.IsCounted)).IsEqualTo(1);
        await Assert.That(samples.Count(s => s is { Count: null, Reason: not null })).IsEqualTo(1);
        await Assert.That(samples.Any(s => s.At == At.AddHours(1))).IsFalse();
    }

    [Test]
    public async Task AReadingWithNoCountAndNoReasonIsRefused()
    {
        // Without the reason, "we got in and could not count" is indistinguishable from "we never got
        // in", and the schema's own CHECK says the same thing. A writer that let this through would
        // put a third, unrenderable state into the series.
        var store = new InMemoryPresenceStore();
        var writer = new PresenceWriter(store);

        await Assert.That(async () => await writer.WriteAsync(
                Game, new PresenceReading(null, FieldSource.Who), At))
            .Throws<ArgumentException>();

        await Assert.That(store.Samples).IsEmpty();
    }

    [Test]
    public async Task ACountedReadingMayNotAlsoCarryAReason()
    {
        var store = new InMemoryPresenceStore();
        var writer = new PresenceWriter(store);

        await Assert.That(async () => await writer.WriteAsync(
                Game,
                new PresenceReading(4, FieldSource.Who, UnmeasurableReason.WhoUnparseable),
                At))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AnMsspSourcedCountIsMarkedAsSuchSoItCanBeRankedBelowWho()
    {
        var store = new InMemoryPresenceStore();

        await new PresenceWriter(store).WriteAsync(
            Game, PresenceReading.Counted(12, FieldSource.Mssp), At);

        await Assert.That(store.Samples.Single().Source).IsEqualTo(FieldSource.Mssp);
    }
}
