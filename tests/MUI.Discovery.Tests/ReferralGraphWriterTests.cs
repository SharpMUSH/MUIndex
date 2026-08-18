using MUI.Crawl;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// "Referrals are candidate hostnames, not facts" (spec §7.2). The writer's whole job is to write the
/// provenance, add a target that is explicitly <em>not</em> a game, and refuse the rest out loud.
/// </summary>
public class ReferralGraphWriterTests
{
    private static readonly Guid Source = Guid.CreateVersion7();
    private static readonly CancellationToken None = CancellationToken.None;

    private static (ReferralGraphWriter Writer, InMemoryReferralRepository Edges, InMemoryCrawlTargetRepository Targets)
        Build(DiscoveryOptions? options = null)
    {
        var edges = new InMemoryReferralRepository();
        var targets = new InMemoryCrawlTargetRepository();
        var writer = new ReferralGraphWriter(edges, targets, options ?? new DiscoveryOptions(), new ManualTimeProvider());
        return (writer, edges, targets);
    }

    private static ProbeResult Referring(string host, int port, params string[] referrals) =>
        ProbeResults.Answered(
            host: host,
            port: port,
            mssp: ProbeResults.Mssp(("REFERRAL", ProbeResults.List(referrals))));

    private static ProbeResult Referring(params string[] referrals) =>
        Referring("mud.example.org", 4201, referrals);

    [Test]
    public async Task AReferredHostBecomesACrawlTargetThatIsNotYetAGame()
    {
        // The single most important assertion in this file. §7.2: a referred host must independently
        // answer MSSP with its own NAME before it is listed, so it is crawled long before it is a game.
        var (writer, edges, targets) = Build();

        var intake = await writer.ApplyAsync(Source, fromDepth: 0, Referring("b.example.org 4000"), None);

        await Assert.That(intake.Added).IsEqualTo(1);
        var target = targets.All.Single();
        await Assert.That(target.Host).IsEqualTo("b.example.org");
        await Assert.That(target.Port).IsEqualTo(4000);
        await Assert.That(target.GameId).IsNull();
        await Assert.That(target.DiscoveredFromGameId).IsEqualTo(Source);
        await Assert.That(target.Depth).IsEqualTo(1);
        await Assert.That(target.IsOperatorSeed).IsFalse();
        await Assert.That(edges.All.Single().Present).IsTrue();
    }

    [Test]
    public async Task ANewTargetIsDueImmediatelySoADiscoveryIsNotAlsoADelay()
    {
        var time = new ManualTimeProvider();
        var targets = new InMemoryCrawlTargetRepository();
        var writer = new ReferralGraphWriter(
            new InMemoryReferralRepository(), targets, new DiscoveryOptions(), time);

        await writer.ApplyAsync(Source, 0, Referring("b.example.org 4000"), None);

        await Assert.That(targets.All.Single().NextProbeAt).IsEqualTo(time.GetUtcNow());
    }

    [Test]
    [Arguments("127.0.0.1 4201")]
    [Arguments("::1 4201")]
    [Arguments("10.1.2.3 4201")]
    [Arguments("192.168.1.1 4201")]
    [Arguments("172.16.0.1 4201")]
    [Arguments("100.64.0.1 4201")]
    [Arguments("fd00::1 4201")]
    [Arguments("169.254.169.254 80")]
    [Arguments("239.255.255.250 1900")]
    public async Task AReferralIntoOurOwnNetworkIsRefused(string referral)
    {
        // A stranger must not be able to aim our crawler at our own network. 169.254.169.254 is the one
        // that matters most: it is the cloud metadata address, and it hands out credentials.
        var (writer, edges, targets) = Build();

        var intake = await writer.ApplyAsync(Source, 0, Referring(referral), None);

        await Assert.That(intake.Added).IsEqualTo(0);
        await Assert.That(intake.Count(ReferralVerdict.NotRoutable)).IsEqualTo(1);
        await Assert.That(targets.All).IsEmpty();

        // And no edge either: an edge is a claim we are willing to render, and rendering "this game
        // refers to 169.254.169.254" is publishing an attacker's payload.
        await Assert.That(edges.All).IsEmpty();
    }

    [Test]
    public async Task AServerReferringToItselfIsCountedAndNotFollowed()
    {
        var (writer, _, targets) = Build();

        var intake = await writer.ApplyAsync(
            Source, 0, Referring("mud.example.org", 4201, "MUD.Example.ORG. 4201"), None);

        await Assert.That(intake.Count(ReferralVerdict.SelfReferral)).IsEqualTo(1);
        await Assert.That(targets.All).IsEmpty();
    }

    [Test]
    public async Task AnotherPortOnTheSameMachineIsNotASelfReferral()
    {
        // A game advertising a second port is telling us something real. Only the exact address it was
        // reached on is itself.
        var (writer, _, targets) = Build();

        var intake = await writer.ApplyAsync(
            Source, 0, Referring("mud.example.org", 4201, "mud.example.org 4202"), None);

        await Assert.That(intake.Added).IsEqualTo(1);
        await Assert.That(targets.All.Single().Port).IsEqualTo(4202);
    }

    [Test]
    public async Task ADepthBeyondTheCapIsRefused()
    {
        var (writer, _, targets) = Build(new DiscoveryOptions { MaxDepth = 2 });

        await Assert.That((await writer.ApplyAsync(Source, 1, Referring("b.example.org 4000"), None))
            .Count(ReferralVerdict.Added)).IsEqualTo(1);
        await Assert.That((await writer.ApplyAsync(Source, 2, Referring("c.example.org 4000"), None))
            .Count(ReferralVerdict.TooDeep)).IsEqualTo(1);

        await Assert.That(targets.All.Count).IsEqualTo(1);
    }

    [Test]
    public async Task FanOutIsCappedPerSource()
    {
        // Referral graphs are unbounded by construction; without this a single hostile list walks as far
        // as MSSP reaches, unattended.
        var (writer, _, targets) = Build(new DiscoveryOptions { MaxFanOutPerSource = 3 });
        var referrals = Enumerable.Range(0, 10).Select(i => $"h{i}.example.org 4000").ToArray();

        var intake = await writer.ApplyAsync(Source, 0, Referring(referrals), None);

        await Assert.That(intake.Added).IsEqualTo(3);
        await Assert.That(intake.Count(ReferralVerdict.FanOutExceeded)).IsEqualTo(7);
        await Assert.That(targets.All.Count).IsEqualTo(3);
    }

    [Test]
    public async Task AKnownTargetIsRecognisedAndItsScheduleIsLeftAlone()
    {
        var (writer, _, targets) = Build();

        await writer.ApplyAsync(Source, 0, Referring("b.example.org 4000"), None);
        var intake = await writer.ApplyAsync(Guid.CreateVersion7(), 0, Referring("b.example.org 4000"), None);

        await Assert.That(intake.Added).IsEqualTo(0);
        await Assert.That(intake.Count(ReferralVerdict.AlreadyKnown)).IsEqualTo(1);
        await Assert.That(targets.All.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AnEdgeThatStopsBeingListedGoesAbsentAndTheTargetSurvives()
    {
        // §7.1 in one test: the edge changes, the schedule does not. Discovery is how a game is found,
        // never how it is scheduled.
        var (writer, edges, targets) = Build();

        await writer.ApplyAsync(Source, 0, Referring("b.example.org 4000", "c.example.org 4000"), None);
        await writer.ApplyAsync(Source, 0, Referring("c.example.org 4000"), None);

        var gone = edges.All.Single(e => e.ToHost == "b.example.org");
        await Assert.That(gone.Present).IsFalse();
        await Assert.That(targets.All.Count).IsEqualTo(2);
        await Assert.That(targets.All.Any(t => t.Host == "b.example.org")).IsTrue();
    }

    [Test]
    public async Task AProbeThatLearnedNoMsspAtAllLeavesTheEdgesAlone()
    {
        // MSSP being absent means we learned nothing about referrals, not that they went away —
        // marking them absent would erase the graph on any hour MSSP was unavailable.
        var (writer, edges, _) = Build();

        await writer.ApplyAsync(Source, 0, Referring("b.example.org 4000"), None);
        var intake = await writer.ApplyAsync(Source, 0, ProbeResults.Answered(), None);

        await Assert.That(intake).IsEqualTo(ReferralIntake.Nothing);
        await Assert.That(edges.All.Single().Present).IsTrue();
    }

    [Test]
    public async Task AnMsspReportWeDroppedForBeingTooLargeIsAlsoNotAWithdrawal()
    {
        // Dropping a report is a decision of ours. Reading it as "this game lists no referrals" would
        // put our own size limit into the graph as a fact about the game.
        var (writer, edges, _) = Build();
        await writer.ApplyAsync(Source, 0, Referring("b.example.org 4000"), None);

        var dropped = ProbeResults.Answered(mssp: ProbeResults.Mssp(("REFERRAL", "b.example.org 4000")))
            with { MsspOutcome = MsspOutcome.RejectedTooLarge };

        await Assert.That(await writer.ApplyAsync(Source, 0, dropped, None)).IsEqualTo(ReferralIntake.Nothing);
        await Assert.That(edges.All.Single().Present).IsTrue();
    }

    [Test]
    public async Task AFailedProbeContributesNothing()
    {
        var (writer, edges, targets) = Build();

        var intake = await writer.ApplyAsync(Source, 0, ProbeResults.Failed(), None);

        await Assert.That(intake).IsEqualTo(ReferralIntake.Nothing);
        await Assert.That(edges.All).IsEmpty();
        await Assert.That(targets.All).IsEmpty();
    }

    [Test]
    public async Task ReferralsAreNotFollowedAtAllWhenTheDeploymentSaysNotTo()
    {
        var (writer, edges, targets) = Build(new DiscoveryOptions { FollowReferrals = false });

        var intake = await writer.ApplyAsync(Source, 0, Referring("b.example.org 4000"), None);

        await Assert.That(intake.Count(ReferralVerdict.ReferralsDisabled)).IsEqualTo(1);
        await Assert.That(targets.All).IsEmpty();
        await Assert.That(edges.All).IsEmpty();
    }

    [Test]
    public async Task ASubtreeCanBeTracedFromAPoisonedSourceAndACycleDoesNotHangIt()
    {
        // §7.2: "the referring game is recorded on the discovered entry so that a hostile or careless
        // REFERRAL list can be traced and its whole subtree pruned". A → B → C → A.
        var (writer, edges, _) = Build();
        var a = Source;
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();
        var unrelated = Guid.CreateVersion7();

        edges.Attributed[("b.example.org", 4000)] = b;
        edges.Attributed[("c.example.org", 4000)] = c;
        edges.Attributed[("a.example.org", 4000)] = a;
        edges.Attributed[("d.example.org", 4000)] = unrelated;

        await writer.ApplyAsync(a, 0, Referring("a1.example.org", 4201, "b.example.org 4000"), None);
        await writer.ApplyAsync(b, 1, Referring("b1.example.org", 4201, "c.example.org 4000"), None);
        await writer.ApplyAsync(c, 2, Referring("c1.example.org", 4201, "a.example.org 4000"), None);
        await writer.ApplyAsync(unrelated, 0, Referring("d1.example.org", 4201, "d.example.org 4000"), None);

        var subtree = await edges.SubtreeOfAsync(a, None);

        await Assert.That(subtree.Select(e => e.ToHost).Distinct().Order())
            .IsEquivalentTo(new[] { "a.example.org", "b.example.org", "c.example.org" });
    }
}
