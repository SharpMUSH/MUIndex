using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// Reading <c>REFERRAL</c>, which is not written the same way twice in the wild.
/// </summary>
public class MsspReferralsTests
{
    [Test]
    [Arguments("b.example.org 4000")]
    [Arguments("b.example.org\t4000")]
    [Arguments("b.example.org:4000")]
    [Arguments("Bravo\tb.example.org\t4000\tPennMUSH")]
    public async Task EveryShapeARealServerSendsReadsAsOneAddress(string entry)
    {
        var candidates = MsspReferrals.Parse(entry);

        await Assert.That(candidates.Count).IsEqualTo(1);
        await Assert.That(candidates[0].Host).IsEqualTo("b.example.org");
        await Assert.That(candidates[0].Port).IsEqualTo(4000);
    }

    [Test]
    public async Task SeveralEntriesInOneValueAreSeveralCandidates()
    {
        var candidates = MsspReferrals.Parse("a.example.org 4000\nb.example.org 4001;c.example.org 4002");

        await Assert.That(candidates.Count).IsEqualTo(3);
        await Assert.That(candidates.Select(c => c.Port)).IsEquivalentTo(new[] { 4000, 4001, 4002 });
    }

    [Test]
    public async Task TheHostIsStoredInItsCanonicalForm()
    {
        // Otherwise the registry's "one address, one row" is a promise about strings rather than
        // machines.
        await Assert.That(MsspReferrals.Parse("B.Example.ORG. 4000")[0].Host).IsEqualTo("b.example.org");
    }

    [Test]
    [Arguments("b.example.org")]
    [Arguments("intranet 80")]
    [Arguments("b.example.org 0")]
    [Arguments("b.example.org 70000")]
    [Arguments("b.example.org notaport")]
    [Arguments("")]
    public async Task AnEntryThatIsNotAWholeAddressIsDroppedRatherThanGuessedAt(string entry)
    {
        // No partial credit and no default port. Guessing 4201 would send the crawler at a socket
        // nobody advertised, and "intranet" is a single-label name that only resolves inside somebody's
        // network — which is the shape §7.2 exists to refuse.
        await Assert.That(MsspReferrals.Parse(entry)).IsEmpty();
    }

    [Test]
    public async Task TheVariableIsMatchedCaseInsensitively()
    {
        var mssp = ProbeResults.Mssp(("Referral", "b.example.org 4000"));

        await Assert.That(MsspReferrals.From(mssp).Count).IsEqualTo(1);
    }

    [Test]
    public async Task AnAbsentVariableIsNoCandidatesRatherThanAnError()
    {
        await Assert.That(MsspReferrals.From(ProbeResults.Mssp(("NAME", "Corvid")))).IsEmpty();
    }

    [Test]
    [Arguments("10.0.0.5", false)]
    [Arguments("127.0.0.1", false)]
    [Arguments("169.254.169.254", false)]
    [Arguments("203.0.113.10", true)]
    [Arguments("games.example.com", true)]
    public async Task ALiteralAddressIsClassifiedAndANameIsNot(string host, bool crawlable)
    {
        // A name is crawlable *here* because nothing can be known about a name until DNS answers —
        // which is exactly why HostScopeGuard exists and why this check is not the gate.
        await Assert.That(new ReferralCandidate(host, 4201).IsCrawlable).IsEqualTo(crawlable);
    }
}
