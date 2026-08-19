using MUI.Catalog;

namespace MUI.Discovery.Tests;

/// <summary>
/// Spec §8.3's third claim channel: a TXT record at <c>_muindex.&lt;host&gt;</c>, port-qualified.
/// </summary>
/// <remarks>
/// The grammar deliberately diverges from <see cref="OptOutVocabulary.ReadDns"/> in the two places
/// where the safe direction reverses. An opt-out that cannot be parsed reads as "stop dialling",
/// because the cost of guessing wrong is crawling somebody who asked us not to; a claim that cannot
/// be parsed reads as "not yet", because the cost of guessing wrong is handing a listing to whoever
/// controls the domain. Same shape of record, opposite failure direction, and the two are pinned
/// here so a later tidy-up cannot quietly make them agree.
/// </remarks>
public class ClaimDnsTests
{
    private const string Token = "muidx-a2b3c4d5e6f7g8h9j2k3";

    [Test]
    public async Task APortQualifiedTokenIsReadForThatPort()
    {
        await Assert.That(ClaimTokenBeacon.ReadDns([$"{Token}=4201"], 4201)).IsEqualTo(Token);
    }

    [Test]
    public async Task ATokenQualifiedForAnotherPortSaysNothingAboutThisOne()
    {
        await Assert.That(ClaimTokenBeacon.ReadDns([$"{Token}=4202"], 4201)).IsNull();
    }

    [Test]
    public async Task AnUnqualifiedTokenVerifiesNothing()
    {
        // §8.3's whole objection to DNS is that a hostname is not a game. The qualifier is what
        // makes the record name a listener, so a record without one is not an answer about any port
        // — the opposite of the opt-out grammar, where bare means the whole host.
        await Assert.That(ClaimTokenBeacon.ReadDns([Token], 4201)).IsNull();
    }

    [Test]
    public async Task AQualifierMayNameSeveralPorts()
    {
        await Assert.That(ClaimTokenBeacon.ReadDns([$"{Token}=4201,4202"], 4202)).IsEqualTo(Token);
    }

    [Test]
    public async Task AColonQualifiesAsWellAsAnEquals()
    {
        await Assert.That(ClaimTokenBeacon.ReadDns([$"{Token}:4201"], 4201)).IsEqualTo(Token);
    }

    [Test]
    public async Task AQualifierThatNamesNoPortAtAllIsNotAnAnswer()
    {
        await Assert.That(ClaimTokenBeacon.ReadDns([$"{Token}="], 4201)).IsNull();
    }

    [Test]
    public async Task AQualifierWeCannotParseFailsTowardsNotVerifying()
    {
        // OptOutVocabulary reads an unparseable qualifier as the whole host, because the safe
        // direction there is to stop dialling. Here the safe direction is the other one.
        await Assert.That(ClaimTokenBeacon.ReadDns([$"{Token}=all"], 4201)).IsNull();
    }

    [Test]
    public async Task TheTokenMayShareTheRecordWithOtherTokens()
    {
        await Assert.That(ClaimTokenBeacon.ReadDns([$"v=muindex1; {Token}=4201"], 4201)).IsEqualTo(Token);
    }

    [Test]
    public async Task SeveralGamesOnOneHostPublishSeveralRecords()
    {
        var other = "muidx-w2x3y4z5a6b7c8d9e2f3";

        await Assert.That(ClaimTokenBeacon.ReadDns([$"{other}=4201", $"{Token}=4202"], 4202))
            .IsEqualTo(Token);
    }

    [Test]
    public async Task ATokenAZoneEditorUpcasedIsStillTheTokenWeMinted()
    {
        // The mint alphabet is lower-case, so lower-casing recovers the exact bytes we issued. Some
        // DNS control panels normalise a value's case on the way in and an operator who pasted what
        // we gave them must not be told their claim failed.
        await Assert.That(ClaimTokenBeacon.ReadDns([$"{Token.ToUpperInvariant()}=4201"], 4201))
            .IsEqualTo(Token);
    }

    [Test]
    public async Task SomethingThatIsNotTokenShapedIsNotAToken()
    {
        await Assert.That(ClaimTokenBeacon.ReadDns(["muidx-short=4201", "google-site-verification=4201"], 4201))
            .IsNull();
    }

    [Test]
    public async Task TheNameWeAskAboutIsTheOneWeTellOperatorsToCreate()
    {
        // Derived rather than restated, so the claim page and this lookup cannot drift — and the
        // same label §11's opt-out already uses, because one deployment gets one underscore label.
        await Assert.That(ClaimTokenBeacon.DnsNameFor("Corvid.Example.ORG."))
            .IsEqualTo("_muindex.corvid.example.org");

        await Assert.That(ClaimTokenBeacon.DnsNameFor("corvid.example.org"))
            .IsEqualTo(OptOutVocabulary.DnsNameFor("corvid.example.org"));
    }
}
