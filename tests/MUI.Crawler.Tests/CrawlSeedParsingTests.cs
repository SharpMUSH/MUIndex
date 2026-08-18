namespace MUI.Crawler.Tests;

/// <summary>
/// What counts as an address, for <c>mui-crawl --seed</c> and for a deployment's seed list alike.
/// </summary>
/// <remarks>
/// The half worth reading is <see cref="AnAmbiguousAddressIsRefusedRatherThanGuessedAt"/>: an
/// ambiguous address could never bypass §7.2 (every target is still resolved and ruled on by
/// <c>HostScopeGuard</c>), but it could dial a host nobody wrote down, which rule 4 forbids.
/// </remarks>
public class CrawlSeedParsingTests
{
    [Test]
    [Arguments("mush.pennmush.org:4201", "mush.pennmush.org", 4201)]
    [Arguments("aardmud.org:4000", "aardmud.org", 4000)]
    [Arguments("  aardmud.org:4000  ", "aardmud.org", 4000)]
    [Arguments("192.0.2.10:23", "192.0.2.10", 23)]
    [Arguments("[2001:db8::1]:4201", "2001:db8::1", 4201)]
    [Arguments("[::1]:4201", "::1", 4201)]
    public async Task AnAddressIsAHostAndAPort(string input, string host, int port)
    {
        var seed = CrawlSeed.Parse(input);

        await Assert.That(seed.Host).IsEqualTo(host);
        await Assert.That(seed.Port).IsEqualTo(port);
    }

    [Test]
    [Arguments("[2001:db8::1:4201")]      // opens a bracket it never closes
    [Arguments("[10.0.0.5:4201")]         // the same, with an address the gate would have refused
    [Arguments("[2001:db8::1]")]          // closes it, and then names no port
    [Arguments("[evil.example]junk:4201")] // closes it somewhere that is not before the port
    [Arguments("[a[b]:4201")]             // opens twice
    [Arguments("2001:db8::1:4201")]       // a bare IPv6 literal: which colon is the port?
    [Arguments("mush.pennmush.org")]      // no port at all
    [Arguments(":4201")]                  // no host
    [Arguments("mush.pennmush.org:")]     // no port after the colon
    [Arguments("mush.pennmush.org:http")] // a port that is not a number
    [Arguments("mush.pennmush.org:0")]    // not a port
    [Arguments("mush.pennmush.org:70000")]
    public async Task AnAmbiguousAddressIsRefusedRatherThanGuessedAt(string input)
    {
        await Assert.That(() => CrawlSeed.Parse(input)).Throws<ArgumentException>();
    }

    [Test]
    public async Task TheExemptionComesFromTheCallerAndNeverFromTheText()
    {
        // §7.2: no spelling of an address may claim the exemption for itself.
        await Assert.That(CrawlSeed.Parse("127.0.0.1:4201").IsOperatorSeed).IsFalse();
        await Assert.That(CrawlSeed.Parse("[::1]:4201").IsOperatorSeed).IsFalse();
        await Assert.That(CrawlSeed.Parse("127.0.0.1:4201", isOperatorSeed: true).IsOperatorSeed).IsTrue();
    }

    [Test]
    public async Task WhatItPrintsIsWhatItReads()
    {
        // The round trip matters because ToString is what a log and a summary show, and an address
        // that cannot be pasted back into --seed is an address somebody will mistype.
        foreach (var input in new[] { "aardmud.org:4000", "[2001:db8::1]:4201" })
        {
            await Assert.That(CrawlSeed.Parse(input).ToString()).IsEqualTo(input);
        }
    }
}
