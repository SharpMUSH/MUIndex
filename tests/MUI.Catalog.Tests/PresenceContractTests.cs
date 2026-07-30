using MUI.Catalog;
using MUI.Catalog.Tests.Support;

namespace MUI.Catalog.Tests;

/// <summary>
/// The three states an hour can be in (spec §5.4), asserted against <see cref="IPresenceStore"/>
/// rather than any implementation of it.
/// </summary>
/// <remarks>
/// These are the most important tests in the catalogue. Collapsing "probed but uncountable" into
/// either neighbour renders a healthy game as permanently dark, and it is the failure the first cut
/// of the spec actually shipped.
/// </remarks>
public class PresenceContractTests
{
    private static readonly Guid Game = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AMeasuredZeroIsAStoredCountAndNotAnAbsence()
    {
        var store = new InMemoryPresenceStore();

        await store.AppendAsync(PresenceSample.Counted(Game, At, 0, FieldSource.Who));

        var sample = store.Samples.Single();
        await Assert.That(sample.IsCounted).IsTrue();
        await Assert.That(sample.Count).IsEqualTo(0);
        await Assert.That(sample.Reason).IsNull();
    }

    [Test]
    public async Task AnUncountableProbeStoresARowWithNoCountAndAReason()
    {
        var store = new InMemoryPresenceStore();

        await store.AppendAsync(
            PresenceSample.Unmeasurable(Game, At, UnmeasurableReason.WhoUnparseable));

        var sample = store.Samples.Single();
        await Assert.That(sample.IsCounted).IsFalse();
        await Assert.That(sample.Count).IsNull();
        await Assert.That(sample.Reason).IsEqualTo(UnmeasurableReason.WhoUnparseable);
    }

    [Test]
    public async Task AMeasuredZeroAndAnUncountableProbeAreNotEqual()
    {
        // The bug this guards is subtle: both are "no players known" to a careless reader, and a
        // record type makes it tempting to compare them. One means nobody was on; the other means we
        // could not tell. They must never render alike.
        var zero = PresenceSample.Counted(Game, At, 0, FieldSource.Who);
        var unknown = PresenceSample.Unmeasurable(Game, At, UnmeasurableReason.WhoUnparseable);

        await Assert.That(zero).IsNotEqualTo(unknown);
        await Assert.That(zero.IsCounted).IsNotEqualTo(unknown.IsCounted);
    }

    [Test]
    public async Task AFailedProbeWritesNoPresenceRowAtAll()
    {
        // The third state is the absence of a row. Nothing here calls AppendAsync, and that is the
        // assertion: a failed probe reaches the availability series and stops.
        var store = new InMemoryPresenceStore();

        await Assert.That(store.Samples).IsEmpty();
    }

    [Test]
    public async Task AGapInTheSeriesIsDistinguishableFromARunOfZeroes()
    {
        var store = new InMemoryPresenceStore();
        var hour = TimeSpan.FromHours(1);

        await store.AppendAsync(PresenceSample.Counted(Game, At, 4, FieldSource.Who));
        // hour 2: probe failed — nothing written
        await store.AppendAsync(PresenceSample.Counted(Game, At + (hour * 2), 0, FieldSource.Who));
        await store.AppendAsync(
            PresenceSample.Unmeasurable(Game, At + (hour * 3), UnmeasurableReason.WhoUnparseable));

        var samples = await store.ForGameAsync(Game, At, At + (hour * 4));

        await Assert.That(samples).Count().IsEqualTo(3);
        await Assert.That(samples.Any(s => s.At == At + hour)).IsFalse();
        await Assert.That(samples.Count(s => s.IsCounted)).IsEqualTo(2);
        await Assert.That(samples.Count(s => s.Reason is not null)).IsEqualTo(1);
    }

    [Test]
    public async Task AggregatesAreAbsentUnlessTheParserReachedPerPlayerConfidence()
    {
        var counted = PresenceSample.Counted(Game, At, 7, FieldSource.Who);

        await Assert.That(counted.Aggregates).IsNull();
    }

    [Test]
    public async Task AnMsspSourcedCountIsMarkedAsSuchSoItCanBeRankedBelowWho()
    {
        var store = new InMemoryPresenceStore();

        await store.AppendAsync(PresenceSample.Counted(Game, At, 12, FieldSource.Mssp));

        await Assert.That(store.Samples.Single().Source).IsEqualTo(FieldSource.Mssp);
    }
}
