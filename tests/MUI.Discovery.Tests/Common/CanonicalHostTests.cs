namespace MUI.Discovery.Tests;

/// <summary>
/// One spelling per machine. Every other guarantee in the registry — "one address, one row", the
/// self-referral check, the per-host rate limit — rests on this being one rule rather than several.
/// </summary>
public class CanonicalHostTests
{
    [Test]
    [Arguments("MUD.Example.ORG", "mud.example.org")]
    [Arguments("mud.example.org.", "mud.example.org")]
    [Arguments("  mud.example.org  ", "mud.example.org")]
    [Arguments("MUD.Example.ORG.", "mud.example.org")]
    public async Task ANameIsLowerCasedTrimmedAndLosesItsRootDot(string raw, string expected)
    {
        await Assert.That(CanonicalHost.Normalize(raw)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("[2001:0db8:0000:0000:0000:0000:0000:0001]", "2001:db8::1")]
    [Arguments("2001:0DB8::0001", "2001:db8::1")]
    [Arguments("203.0.113.10", "203.0.113.10")]
    public async Task AnAddressLiteralIsReducedToItsOwnCanonicalText(string raw, string expected)
    {
        // Two spellings of one address must be one key, or the registry holds two rows for one socket.
        await Assert.That(CanonicalHost.Normalize(raw)).IsEqualTo(expected);
    }

    [Test]
    public async Task AnOctalLookingOctetIsCanonicalisedTheSameWayTheScopeCheckReadsIt()
    {
        // `203.000.113.010` is not 203.0.113.10: the platform reads a leading zero as octal, so this
        // literal is 203.0.113.8. It must normalise through the *same* parse AddressScope classifies,
        // or the registry key and the scope guard could disagree about one address.
        await Assert.That(CanonicalHost.Normalize("203.000.113.010")).IsEqualTo("203.0.113.8");
        await Assert.That(CanonicalHost.Normalize("010.0.0.1")).IsEqualTo("8.0.0.1");
    }

    [Test]
    public async Task SameIsTheComparisonEverythingElseUses()
    {
        await Assert.That(CanonicalHost.Same("MUD.Example.ORG.", "mud.example.org")).IsTrue();
        await Assert.That(CanonicalHost.Same("mud.example.org", "other.example.org")).IsFalse();
    }
}
