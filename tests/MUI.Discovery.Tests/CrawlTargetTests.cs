using System.Reflection;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// The registry's shape, including the states that are deliberately <em>not</em> in it.
/// </summary>
public class CrawlTargetTests
{
    private static readonly DateTimeOffset Then = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private static CrawlTarget Target(string host = "mud.example.org", int port = 4201, int depth = 0) => new()
    {
        Id = Guid.CreateVersion7(),
        Host = host,
        Port = port,
        NextProbeAt = Then,
        FirstSeenAt = Then,
        Depth = depth,
    };

    [Test]
    public async Task NothingInTheRegistryCanRetireATarget()
    {
        // Spec §7.4, held in place by reflection rather than by convention. This registry is permanent,
        // so it has no retirement verdict at all: a game that comes back must re-list itself with no
        // human involved, which is the one thing no incumbent managed. If this test ever fails, the fix
        // is to delete the member, not the test.
        var forbidden = new[] { "delete", "remove", "retire", "retired", "abandon", "disable", "forget", "purge" };

        var members = typeof(ICrawlTargetRepository).GetMethods().Select(m => m.Name)
            .Concat(typeof(CrawlTarget).GetProperties().Select(p => p.Name));

        var offenders = members
            .Where(member => forbidden.Any(word => member.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task ATargetIsGuardedUnlessSomebodySaidOtherwise()
    {
        // IsOperatorSeed exempts a target from the resolved-address gate, so its default decides what
        // happens to every target built by code that had not thought about it — every referral and
        // every import. The safe value must be the default, not the considered one.
        await Assert.That(Target().IsOperatorSeed).IsFalse();
    }

    [Test]
    public async Task AReferredTargetIsNotYetAGame()
    {
        // §7.2: a referred host is a candidate hostname until it answers MSSP for itself.
        await Assert.That(Target().GameId).IsNull();
    }

    [Test]
    public async Task ASecondSightingNeitherResetsTheScheduleNorRaisesTheDepth()
    {
        // §7.1: discovery is how a game is found, never how it is scheduled. A referral arriving again
        // — or arriving from further away — must not drag a target's next probe forward or push it back.
        var repository = new InMemoryCrawlTargetRepository();
        var first = Target(depth: 1) with { NextProbeAt = Then.AddDays(3), ConsecutiveFailures = 4 };

        var id = await repository.AddAsync(first, None);
        var again = await repository.AddAsync(Target(depth: 5) with { NextProbeAt = Then }, None);

        await Assert.That(again).IsEqualTo(id);
        var stored = repository.All.Single();
        await Assert.That(stored.NextProbeAt).IsEqualTo(Then.AddDays(3));
        await Assert.That(stored.ConsecutiveFailures).IsEqualTo(4);
        await Assert.That(stored.Depth).IsEqualTo(1);
    }

    [Test]
    public async Task ACloserSightingLowersTheRecordedDepth()
    {
        var repository = new InMemoryCrawlTargetRepository();
        await repository.AddAsync(Target(depth: 3), None);
        await repository.AddAsync(Target(depth: 1), None);

        await Assert.That(repository.All.Single().Depth).IsEqualTo(1);
    }

    [Test]
    public async Task OneAddressIsOneRowWhateverItsSpelling()
    {
        // Two spellings of one machine are two probes, twice the traffic at somebody else's server, and
        // eventually the duplicate listing §7.3 exists to prevent.
        var repository = new InMemoryCrawlTargetRepository();

        await repository.AddAsync(Target("MUD.Example.ORG."), None);
        await repository.AddAsync(Target("mud.example.org"), None);

        await Assert.That(repository.All.Count).IsEqualTo(1);
        await Assert.That(await repository.ByAddressAsync("Mud.Example.Org", 4201, None)).IsNotNull();
    }

    [Test]
    public async Task ATargetThatFailsForEverStaysDueForEver()
    {
        // The registry and the schedule together: a hundred failures still produce a finite next probe.
        var repository = new InMemoryCrawlTargetRepository();
        var target = Target();
        var id = await repository.AddAsync(target, None);

        var at = Then;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var stored = repository.All.Single();
            at = ProbeSchedule.NextProbeAt(at, stored.ConsecutiveFailures + 1, null);
            await repository.RecordAttemptAsync(id, at, succeeded: false, null, at, None);
        }

        var dark = repository.All.Single();
        await Assert.That(dark.ConsecutiveFailures).IsEqualTo(100);
        await Assert.That(await repository.DueAsync(dark.NextProbeAt, 10, None)).IsNotEmpty();
    }

    [Test]
    public async Task ASuccessClearsTheFailureCount()
    {
        var repository = new InMemoryCrawlTargetRepository();
        var id = await repository.AddAsync(Target(), None);

        await repository.RecordAttemptAsync(id, Then, succeeded: false, null, Then.AddHours(6), None);
        await repository.RecordAttemptAsync(id, Then, succeeded: true, null, Then.AddHours(6), None);

        await Assert.That(repository.All.Single().ConsecutiveFailures).IsEqualTo(0);
    }

    [Test]
    public async Task TheRegistryRemembersTheServersOwnCrawlDelayAndDoesNotForgetItOnASilentProbe()
    {
        // A server that stated CRAWL DELAY once and then stopped publishing MSSP has not withdrawn the
        // request, and forgetting it would speed us up on exactly the server that asked us to slow down.
        var repository = new InMemoryCrawlTargetRepository();
        var id = await repository.AddAsync(Target(), None);

        await repository.RecordAttemptAsync(id, Then, true, TimeSpan.FromDays(30), Then.AddDays(30), None);
        await repository.RecordAttemptAsync(id, Then, true, null, Then.AddDays(30), None);

        await Assert.That(repository.All.Single().CrawlDelay).IsEqualTo(TimeSpan.FromDays(30));
    }

    [Test]
    public async Task TheRecordCarriesWhoReferredItSoASubtreeCanBeTraced()
    {
        // §7.2: the referring game is recorded on the discovered entry so a hostile or careless
        // REFERRAL list can be traced and its whole subtree pruned.
        var referrer = Guid.CreateVersion7();
        var discovered = Target() with { DiscoveredFromGameId = referrer, Depth = 1 };

        await Assert.That(discovered.DiscoveredFromGameId).IsEqualTo(referrer);
        await Assert.That(typeof(CrawlTarget).GetProperty(nameof(CrawlTarget.DiscoveredFromGameId), BindingFlags.Public | BindingFlags.Instance))
            .IsNotNull();
    }
}
