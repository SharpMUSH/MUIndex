using System.Net;
using System.Net.Sockets;
using System.Reflection;
using MUI.Crawl;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// Spec §7.2 closed on the resolved address. The literal-address check a referral string gets is
/// bypassed by anybody who owns a domain, so this is the gate that actually holds.
/// </summary>
public class HostScopeGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private static CrawlTarget Target(string host, bool operatorSeed = false) => new()
    {
        Id = Guid.CreateVersion7(),
        Host = host,
        Port = 4201,
        NextProbeAt = Now,
        FirstSeenAt = Now,
        IsOperatorSeed = operatorSeed,
    };

    [Test]
    public async Task ANameResolvingToPublicSpaceIsDialled()
    {
        var guard = new HostScopeGuard(new FakeHostResolver().Resolving("mud.example.org", "203.0.113.10"));

        var decision = await guard.RuleOnAsync(Target("mud.example.org"), None);

        await Assert.That(decision.Ruling).IsEqualTo(HostScopeRuling.Allowed);
        await Assert.That(decision.IsAllowed).IsTrue();
        await Assert.That(decision.Addresses.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("127.0.0.1")]
    [Arguments("10.0.0.5")]
    [Arguments("192.168.1.1")]
    [Arguments("172.16.0.1")]
    [Arguments("100.64.0.1")]
    [Arguments("169.254.169.254")]
    [Arguments("::1")]
    [Arguments("fd00::1")]
    [Arguments("fe80::1")]
    [Arguments("239.255.255.250")]
    [Arguments("::ffff:10.0.0.5")]
    public async Task ANameResolvingIntoOurOwnNetworkIsRefused(string address)
    {
        // The whole point. Every one of these passes a name check when it arrives as
        // "internal.example.org", because a name says nothing until DNS answers. Publishing the A
        // record costs an attacker nothing; 169.254.169.254 is the cloud metadata address and hands
        // out credentials.
        var guard = new HostScopeGuard(new FakeHostResolver().Resolving("internal.example.org", address));

        var decision = await guard.RuleOnAsync(Target("internal.example.org"), None);

        await Assert.That(decision.Ruling).IsEqualTo(HostScopeRuling.RefusedNonGlobal);
        await Assert.That(decision.IsAllowed).IsFalse();
        await Assert.That(decision.Detail).IsNotNull();
    }

    [Test]
    public async Task ANameResolvingToBothPublicAndPrivateIsRefusedRatherThanFiltered()
    {
        // The DNS-rebinding shape. Taking the address we liked and proceeding is the bug: a host whose
        // records straddle the boundary is telling us something inconsistent, and the safe reading of
        // an inconsistent answer is no.
        var guard = new HostScopeGuard(new FakeHostResolver()
            .Resolving("rebind.example.org", "203.0.113.10", "10.0.0.5"));

        var decision = await guard.RuleOnAsync(Target("rebind.example.org"), None);

        await Assert.That(decision.Ruling).IsEqualTo(HostScopeRuling.RefusedNonGlobal);
        await Assert.That(decision.Detail).Contains("10.0.0.5");
    }

    [Test]
    public async Task TheOrderOfTheAnswersDoesNotChangeTheRuling()
    {
        // Guards against the "first address wins" implementation, which passes the test above by luck
        // whenever the private record happens to sort first — and which DNS reordering would then
        // silently turn into an allow.
        var guard = new HostScopeGuard(new FakeHostResolver()
            .Resolving("a.example.org", "10.0.0.5", "203.0.113.10")
            .Resolving("b.example.org", "203.0.113.10", "10.0.0.5"));

        await Assert.That((await guard.RuleOnAsync(Target("a.example.org"), None)).IsAllowed).IsFalse();
        await Assert.That((await guard.RuleOnAsync(Target("b.example.org"), None)).IsAllowed).IsFalse();
    }

    [Test]
    public async Task AnOperatorSeedPointingAtLoopbackIsAllowedWithoutAskingDns()
    {
        // Operator seeds may be exempted and nothing else may. Somebody running the crawler against
        // their own test server has said what they mean — and the exemption short-circuits, because
        // there is nothing to verify about an address a human typed.
        //
        // This is the one place in the suite where 127.0.0.1 appears as a thing we would dial, and it
        // is explicitly the operator-seed exemption doing its job.
        var resolver = new FakeHostResolver();
        var guard = new HostScopeGuard(resolver);

        var decision = await guard.RuleOnAsync(Target("127.0.0.1", operatorSeed: true), None);

        await Assert.That(decision.IsAllowed).IsTrue();
        await Assert.That(decision.Detail).IsEqualTo(HostScopeGuard.OperatorSeedDetail);
        await Assert.That(resolver.Asked).IsEmpty();
    }

    [Test]
    public async Task TheExemptionIsNeverInferredFromTheAddress()
    {
        // The same loopback address without the flag is refused. The exemption is a stored property
        // that defaults to not-exempt and is never granted by a referral or an import, so the dangerous
        // paths are guarded by not having to remember to guard them.
        var guard = new HostScopeGuard(new FakeHostResolver());

        var decision = await guard.RuleOnAsync(Target("127.0.0.1"), None);

        await Assert.That(decision.Ruling).IsEqualTo(HostScopeRuling.RefusedNonGlobal);
    }

    [Test]
    public async Task AnImportedTargetNamingLoopbackDirectlyIsStillRefusedHere()
    {
        // The referral writer already refuses this shape, but the guard must not assume it ran: an
        // imported target never passed through the referral writer at all.
        var guard = new HostScopeGuard(new FakeHostResolver());

        await Assert.That((await guard.InspectAsync("127.0.0.1", None)).Ruling)
            .IsEqualTo(HostScopeRuling.RefusedNonGlobal);
    }

    [Test]
    public async Task ANameThatResolvesToNothingIsADeadHostRatherThanARefusal()
    {
        // Distinct on purpose: "we could not look it up" and "we looked it up and will not go there"
        // are different facts. The first is an ordinary DNS failure about the world and gets ordinary
        // backoff; the second is a decision of ours.
        var guard = new HostScopeGuard(new FakeHostResolver().Failing("gone.example.org"));

        var decision = await guard.RuleOnAsync(Target("gone.example.org"), None);

        await Assert.That(decision.Ruling).IsEqualTo(HostScopeRuling.Unresolvable);
        await Assert.That(decision.IsAllowed).IsFalse();
    }

    [Test]
    public async Task AResolverThatThrowsIsUnresolvableAndNotAnAllow()
    {
        // Fail closed. A guard that lets a dial through because DNS was briefly unhappy is not a guard.
        var guard = new HostScopeGuard(new ThrowingResolver());

        var decision = await guard.RuleOnAsync(Target("mud.example.org"), None);

        await Assert.That(decision.Ruling).IsEqualTo(HostScopeRuling.Unresolvable);
        await Assert.That(decision.IsAllowed).IsFalse();
    }

    [Test]
    public async Task ARefusalCannotBecomeAProbeResultBecauseThereIsNoOutcomeForIt()
    {
        // §7.2: "a refusal writes no availability sample". This is satisfied structurally rather than
        // by a check somebody has to remember — the guard returns a decision, never a ProbeResult, and
        // ProbeOutcome has exactly two members, both of which mean the socket was opened.
        //
        // The tempting shortcut is ProbeResult.Failed(refused, …), and it is wrong twice:
        // FailureCause.Refused already means the far end sent an RST — a real measurement of a real
        // host — so dressing a policy refusal as a probe failure makes the two permanently inseparable
        // downstream, and it writes our own security policy into a game's public reachability history.
        var outcomes = Enum.GetNames<ProbeOutcome>();

        await Assert.That(outcomes.Length).IsEqualTo(2);
        await Assert.That(outcomes).IsEquivalentTo(new[] { "Answered", "Failed" });

        var returns = typeof(HostScopeGuard)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.ReturnType)
            .ToList();

        await Assert.That(returns.Any(t => t == typeof(Task<ProbeResult>))).IsFalse();
        await Assert.That(returns.All(t => t == typeof(Task<HostScopeDecision>))).IsTrue();
    }

    [Test]
    public async Task AGuardedDialResolvesEveryTimeAndCachesNothing()
    {
        // Caching resolutions would widen the time-of-check-to-time-of-use window rather than close
        // it, so the crawler resolves per dial (§7.2). This guard is not airtight and must never be
        // described as such — it raises the cost of the attack, it does not close it.
        var resolver = new FakeHostResolver().Resolving("mud.example.org", "203.0.113.10");
        var guard = new HostScopeGuard(resolver);

        await guard.RuleOnAsync(Target("mud.example.org"), None);
        await guard.RuleOnAsync(Target("mud.example.org"), None);

        await Assert.That(resolver.Asked.Count).IsEqualTo(2);
    }

    [Test]
    public async Task TheGuardIsTheCrawlContractsGuardRatherThanATypeOfItsOwn()
    {
        // Implementing MUI.Crawl's interface is what makes this substitutable for the probe engine's
        // own gate; a second parallel abstraction would be two rules that must agree for ever.
        await Assert.That(new HostScopeGuard(new FakeHostResolver())).IsAssignableTo<IHostScopeGuard>();
    }

    private sealed class ThrowingResolver : IHostResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken = default) =>
            throw new SocketException(11001);
    }
}
