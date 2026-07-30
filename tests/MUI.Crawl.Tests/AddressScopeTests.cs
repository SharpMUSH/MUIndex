using System.Net;
using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// The address gate (spec §7.2). `REFERRAL` is attacker-controlled, so every one of these is a case
/// somebody can arrange deliberately.
/// </summary>
public class AddressScopeTests
{
    [Test]
    [Arguments("8.8.8.8")]
    [Arguments("1.1.1.1")]
    [Arguments("203.0.113.9")]
    [Arguments("2606:4700:4700::1111")]
    public async Task APublicAddressIsDialable(string address)
    {
        await Assert.That(AddressScope.IsGloballyRoutable(IPAddress.Parse(address))).IsTrue();
    }

    [Test]
    [Arguments("127.0.0.1")]
    [Arguments("10.0.0.5")]
    [Arguments("172.16.4.1")]
    [Arguments("172.31.255.254")]
    [Arguments("192.168.1.1")]
    [Arguments("0.0.0.0")]
    [Arguments("100.64.0.1")]
    [Arguments("224.0.0.1")]
    [Arguments("255.255.255.255")]
    public async Task APrivateOrSpecialAddressIsRefused(string address)
    {
        await Assert.That(AddressScope.IsGloballyRoutable(IPAddress.Parse(address))).IsFalse();
    }

    [Test]
    public async Task TheCloudMetadataAddressIsRefused()
    {
        // 169.254.169.254 is the single most valuable address to an attacker who can steer our DNS:
        // on most cloud providers it hands out instance credentials to anything that asks.
        await Assert.That(AddressScope.IsGloballyRoutable(IPAddress.Parse("169.254.169.254"))).IsFalse();
    }

    [Test]
    [Arguments("::1")]
    [Arguments("fe80::1")]
    [Arguments("fc00::1")]
    [Arguments("fd12:3456::1")]
    [Arguments("ff02::1")]
    public async Task AnIpV6PrivateOrLocalAddressIsRefused(string address)
    {
        await Assert.That(AddressScope.IsGloballyRoutable(IPAddress.Parse(address))).IsFalse();
    }

    [Test]
    [Arguments("::ffff:127.0.0.1")]
    [Arguments("::ffff:10.0.0.5")]
    [Arguments("::ffff:169.254.169.254")]
    public async Task AnIpV4MappedPrivateAddressIsRefusedRatherThanTreatedAsIpV6(string address)
    {
        // The obvious bypass: wrap a forbidden v4 address in v6 notation and hope the v6 branch
        // never unwraps it. Every one of these must be refused for the same reason its bare form is.
        await Assert.That(AddressScope.IsGloballyRoutable(IPAddress.Parse(address))).IsFalse();
    }

    [Test]
    public async Task TheRulingDistinguishesRefusalFromFailureToResolve()
    {
        // "We looked it up and will not go there" is a decision of ours. "It did not resolve" is a
        // fact about the world and gets ordinary backoff. Only one of them is a refusal, and
        // recording either as downtime would be recording our own policy as a measurement.
        var refused = new HostScopeDecision(HostScopeRuling.RefusedNonGlobal, [IPAddress.Loopback]);
        var unresolved = new HostScopeDecision(HostScopeRuling.Unresolvable, []);

        await Assert.That(refused.IsAllowed).IsFalse();
        await Assert.That(unresolved.IsAllowed).IsFalse();
        await Assert.That(refused.Ruling).IsNotEqualTo(unresolved.Ruling);
    }

    [Test]
    public async Task ARefusalHasNoRepresentationInAProbeOutcome()
    {
        // Structural, not incidental. ProbeOutcome has exactly two members and both mean the socket
        // was opened, so a scope refusal cannot reach the writers by any honest route. The tempting
        // shortcut is ProbeResult.Failed(Refused, ...), which is wrong twice over: FailureCause
        // .Refused means the far end sent an RST — a real measurement of a real host — and once a
        // policy refusal is dressed as a probe failure, nothing downstream can separate them again.
        var outcomes = Enum.GetValues<ProbeOutcome>();

        await Assert.That(outcomes).Count().IsEqualTo(2);
        await Assert.That(outcomes).Contains(ProbeOutcome.Answered);
        await Assert.That(outcomes).Contains(ProbeOutcome.Failed);
        await Assert.That(Enum.GetNames<ProbeOutcome>()).DoesNotContain("Refused");
    }
}
