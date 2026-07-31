using MUI.Catalog;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// §7.3: "auto-merge into the existing game, recording the endpoint change as a FieldChange". The
/// change feed is a table of events that actually happened (spec §5.1), and a game moving house is
/// exactly such an event.
/// </summary>
public class MergeApplierTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class World
    {
        public InMemoryEndpointDirectory Endpoints { get; } = new();

        public InMemoryGameFieldStore Fields { get; } = new();

        public InMemoryMergeLog Merges { get; } = new();

        public ManualTimeProvider Time { get; } = new();

        public MergeApplier Applier => new(Endpoints, Fields, Merges, Time);
    }

    private static IdentityScore Score(double value = 1.1) =>
        new(null, value, [new IdentitySignal("Endpoint", 1.0, true), new IdentitySignal("BannerHash", 0.5, false)]);

    [Test]
    public async Task AFirstSightingAttachesTheEndpointAndIsAnEvent()
    {
        var world = new World();
        var corvid = Guid.CreateVersion7();

        await world.Applier.AttachAsync(corvid, ProbeResults.Answered(), None);

        var endpoint = world.Endpoints.All.Single();
        await Assert.That(endpoint.GameId).IsEqualTo(corvid);
        await Assert.That(endpoint.Host).IsEqualTo("mud.example.org");

        var change = world.Fields.Changes.Single();
        await Assert.That(change.Field).IsEqualTo(IdentityFields.Endpoint);
        await Assert.That(change.OldValue).IsNull();
        await Assert.That(change.NewValue).IsEqualTo("mud.example.org 4201");
    }

    [Test]
    public async Task ASecondSightingOfAnAddressWeAlreadyAttributeIsNotAnEvent()
    {
        // A change feed of events that actually happened. Writing one per probe would bury the real
        // ones under a row every six hours for every game in the catalogue.
        var world = new World();
        var corvid = Guid.CreateVersion7();

        await world.Applier.AttachAsync(corvid, ProbeResults.Answered(), None);
        await world.Applier.AttachAsync(corvid, ProbeResults.Answered(), None);

        await Assert.That(world.Fields.Changes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AnAddressChangingHandsIsAnEventThatNamesBothSides()
    {
        var world = new World();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        await world.Applier.AttachAsync(first, ProbeResults.Answered(), None);
        await world.Applier.AttachAsync(second, ProbeResults.Answered(), None);

        var change = world.Fields.Changes.Last();
        await Assert.That(change.GameId).IsEqualTo(second);
        await Assert.That(change.OldValue).IsEqualTo("mud.example.org 4201");
        await Assert.That(world.Endpoints.All.Single().GameId).IsEqualTo(second);
    }

    [Test]
    public async Task TheFirstSeenDateSurvivesEveryLaterSighting()
    {
        var world = new World();
        var corvid = Guid.CreateVersion7();
        var first = world.Time.GetUtcNow();

        await world.Applier.AttachAsync(corvid, ProbeResults.Answered(), None);
        world.Time.Advance(TimeSpan.FromDays(400));
        await world.Applier.AttachAsync(corvid, ProbeResults.Answered(), None);

        var endpoint = world.Endpoints.All.Single();
        await Assert.That(endpoint.FirstSeenAt).IsEqualTo(first);
        await Assert.That(endpoint.LastSeenAt).IsEqualTo(first.AddDays(400));
    }

    [Test]
    public async Task TheBannerIsFingerprintedSoTheSignalCanEverFire()
    {
        // Nothing else writes this field. Without it a game would have to move house before we had
        // fingerprinted it, and the banner signal could never contribute to a first move.
        var world = new World();
        var corvid = Guid.CreateVersion7();
        const string banner = "Welcome to Corvid.";

        await world.Applier.AttachAsync(corvid, ProbeResults.Answered(banner: banner), None);

        var stored = (await world.Fields.ForGameAsync(corvid, None)).Single();
        await Assert.That(stored.Field).IsEqualTo(IdentityFields.BannerHash);
        await Assert.That(stored.Value).IsEqualTo(BannerFingerprint.Of(banner));
        await Assert.That(stored.Source).IsEqualTo(FieldSource.Banner);
    }

    [Test]
    public async Task AProbeThatSawNoBannerWritesNoFingerprintBecauseSilenceIsNotARedesign()
    {
        var world = new World();
        var corvid = Guid.CreateVersion7();

        await world.Applier.AttachAsync(corvid, ProbeResults.Answered(banner: null), None);
        await world.Applier.AttachAsync(corvid, ProbeResults.Answered(banner: "  \r\n "), None);

        await Assert.That(await world.Fields.ForGameAsync(corvid, None)).IsEmpty();
    }

    [Test]
    public async Task AMergeIsARedirectThatIsLoggedWithEverySignalWeighed()
    {
        // Including the ones that did not fire: a merge that has to be explained a year later is
        // explained by what was considered, not only by what agreed.
        var world = new World();
        var into = Guid.CreateVersion7();
        var from = Guid.CreateVersion7();

        var id = await world.Applier.MergeGamesAsync(into, from, Score(), None);

        var record = world.Merges.All.Single();
        await Assert.That(record.Id).IsEqualTo(id);
        await Assert.That(record.IntoGameId).IsEqualTo(into);
        await Assert.That(record.FromGameId).IsEqualTo(from);
        await Assert.That(record.At).IsEqualTo(world.Time.GetUtcNow());
        await Assert.That(record.SignalsJson).Contains("BannerHash");
        await Assert.That(world.Merges.RedirectedTo[from]).IsEqualTo(into);
    }

    [Test]
    public async Task AMergeIsReversibleAndTheReversalIsAlsoKept()
    {
        // "Merges are reversible and logged" (§7.3). Nothing was moved, so nothing has to be moved back
        // — and the record of both the merge and its reversal survives, because nothing is ever deleted.
        var world = new World();
        var into = Guid.CreateVersion7();
        var from = Guid.CreateVersion7();
        var id = await world.Applier.MergeGamesAsync(into, from, Score(), None);

        world.Time.Advance(TimeSpan.FromDays(1));
        await world.Merges.RevertAsync(id, world.Time.GetUtcNow(), None);

        await Assert.That(world.Merges.RedirectedTo.ContainsKey(from)).IsFalse();
        var record = world.Merges.All.Single();
        await Assert.That(record.IsInForce).IsFalse();
        await Assert.That(record.RevertedAt).IsEqualTo(world.Time.GetUtcNow());
    }

    [Test]
    public async Task RevertingTwiceDoesNotRewriteWhenTheFirstRevertHappened()
    {
        var world = new World();
        var id = await world.Applier.MergeGamesAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), Score(), None);

        var first = world.Time.GetUtcNow();
        await world.Merges.RevertAsync(id, first, None);
        await world.Merges.RevertAsync(id, first.AddDays(30), None);

        await Assert.That(world.Merges.All.Single().RevertedAt).IsEqualTo(first);
    }

    [Test]
    public async Task BothSidesOfAMergeCanFindIt()
    {
        var world = new World();
        var into = Guid.CreateVersion7();
        var from = Guid.CreateVersion7();
        await world.Applier.MergeGamesAsync(into, from, Score(), None);

        await Assert.That((await world.Merges.ForGameAsync(into, None)).Count).IsEqualTo(1);
        await Assert.That((await world.Merges.ForGameAsync(from, None)).Count).IsEqualTo(1);
    }

    [Test]
    public async Task AGameCannotBeMergedIntoItself()
    {
        var world = new World();
        var corvid = Guid.CreateVersion7();

        await Assert.That(async () => await world.Applier.MergeGamesAsync(corvid, corvid, Score(), None))
            .Throws<ArgumentException>();
    }
}
