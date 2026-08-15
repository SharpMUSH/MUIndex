using MUI.Crawl;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// Spec §11's opt-out: an MSSP field, a DNS TXT record, or a request — honoured within one cycle and
/// recorded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two of the three routes need no account with us, deliberately.</b> The operator most likely to
/// want the crawler gone is the least likely to have signed up for anything here first, and an
/// opt-out available only to people who had already opted in would be a joke at their expense.
/// </para>
/// <para>
/// Everything asserted here is about whether we <em>dial</em>. Nothing an opt-out does may reach a
/// game's record — that half is pinned in <c>MUI.Crawler.Tests</c> against a real database, because
/// "no availability row was written" is a claim about storage and an in-memory fake cannot make it.
/// </para>
/// </remarks>
public class OptOutTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken None = CancellationToken.None;

    private static CrawlTarget Target(string host = "corvid.example.org", int port = 4201) => new()
    {
        Id = Guid.CreateVersion7(),
        Host = host,
        Port = port,
        NextProbeAt = Start,
        FirstSeenAt = Start,
    };

    private static (OptOutGate Gate, InMemoryCrawlOptOutRepository Rows, FakeDnsTxtResolver Dns, ManualTimeProvider Time)
        World(FakeDnsTxtResolver? dns = null)
    {
        var rows = new InMemoryCrawlOptOutRepository();
        var resolver = dns ?? new FakeDnsTxtResolver();
        var time = new ManualTimeProvider(Start);

        return (new OptOutGate(rows, resolver, time), rows, resolver, time);
    }

    [Test]
    public async Task AGamePublishingTheMsspFieldIsNotDialledAgain()
    {
        var (gate, _, _, _) = World();
        var target = Target();

        await Assert.That(await gate.RuleOnAsync(target, None)).IsNull();

        await gate.HearAsync(
            target,
            ProbeResults.Answered(mssp: ProbeResults.Mssp(
                ("NAME", "Corvid"), (OptOutVocabulary.MsspVariable, "1"))),
            None);

        // Honoured within one cycle means honoured at the very next dial, which is this one.
        await Assert.That(await gate.RuleOnAsync(target, None)).IsNotNull();
    }

    [Test]
    public async Task AnOptOutIsRecordedWithItsRouteItsDateAndWhatWeRead()
    {
        var (gate, rows, _, _) = World();

        await gate.HearAsync(
            Target(),
            ProbeResults.Answered(mssp: ProbeResults.Mssp((OptOutVocabulary.MsspVariable, "1"))),
            None);

        var recorded = (await rows.AllAsync(None)).Single();

        await Assert.That(recorded.Source).IsEqualTo(OptOutSource.Mssp);
        await Assert.That(recorded.RecordedAt).IsEqualTo(Start);
        await Assert.That(recorded.Detail).Contains(OptOutVocabulary.MsspVariable);
        await Assert.That(recorded.Standing).IsTrue();
    }

    [Test]
    public async Task AnMsspFieldStopsTheListenerThatPublishedItAndNotItsNeighbour()
    {
        // §8.3's objection, which is why this is not host-wide: MU* hosting routinely puts unrelated
        // games on one domain, separated only by port. A game must not be able to silence the game
        // next door by editing its own config.
        var (gate, _, _, _) = World();

        await gate.HearAsync(
            Target(port: 4201),
            ProbeResults.Answered(mssp: ProbeResults.Mssp((OptOutVocabulary.MsspVariable, "1"))),
            None);

        await Assert.That(await gate.RuleOnAsync(Target(port: 4201), None)).IsNotNull();
        await Assert.That(await gate.RuleOnAsync(Target(port: 4202), None)).IsNull();
    }

    [Test]
    [Arguments("0")]
    [Arguments("no")]
    [Arguments("false")]
    [Arguments("off")]
    [Arguments("")]
    public async Task AFlagTurnedOffIsNotAnOptOut(string value)
    {
        // An operator who changed their mind sets the line to 0 rather than deleting it, and a
        // crawler that read that as consent withdrawn would have no way back in.
        var (gate, rows, _, _) = World();

        await gate.HearAsync(
            Target(),
            ProbeResults.Answered(mssp: ProbeResults.Mssp((OptOutVocabulary.MsspVariable, value))),
            None);

        await Assert.That(await rows.AllAsync(None)).IsEmpty();
        await Assert.That(await gate.RuleOnAsync(Target(), None)).IsNull();
    }

    [Test]
    [Arguments("MUINDEX OPT-OUT")]
    [Arguments("MUINDEX_OPT_OUT")]
    [Arguments("MUINDEX OPTOUT")]
    [Arguments("CRAWL_OPT_OUT")]
    public async Task EverySpellingWeDocumentIsAccepted(string variable)
    {
        // A variable name does not reliably survive a config file. Of all the places to be strict,
        // the one where somebody is asking us to go away is the worst.
        var (gate, _, _, _) = World();

        await gate.HearAsync(
            Target(), ProbeResults.Answered(mssp: ProbeResults.Mssp((variable, "1"))), None);

        await Assert.That(await gate.RuleOnAsync(Target(), None)).IsNotNull();
    }

    [Test]
    public async Task AValueNobodyAnticipatedIsReadAsStopRatherThanAsCarryOn()
    {
        // Of the two ways to misread "MUINDEX OPT-OUT please stop", only one keeps connecting to
        // somebody who asked us not to.
        var (gate, _, _, _) = World();

        await gate.HearAsync(
            Target(),
            ProbeResults.Answered(mssp: ProbeResults.Mssp((OptOutVocabulary.MsspVariable, "please stop"))),
            None);

        await Assert.That(await gate.RuleOnAsync(Target(), None)).IsNotNull();
    }

    [Test]
    public async Task AFailedProbeCarriesNoOptOutBecauseItCarriesNothing()
    {
        var (gate, rows, _, _) = World();

        await gate.HearAsync(Target(), ProbeResults.Failed(), None);

        await Assert.That(await rows.AllAsync(None)).IsEmpty();
    }

    [Test]
    public async Task ATxtRecordStopsAHostWeHaveNeverListed()
    {
        // The route for an address with no game behind it — a referral that answered, or one we have
        // never reached at all. Nothing about it requires us to have written anything down first.
        var dns = new FakeDnsTxtResolver()
            .Publishing(OptOutVocabulary.DnsNameFor("mystery.example"), OptOutVocabulary.DnsValue);

        var (gate, rows, _, _) = World(dns);

        var ruling = await gate.RuleOnAsync(Target("mystery.example"), None);

        await Assert.That(ruling).IsNotNull();
        await Assert.That(ruling!.Source).IsEqualTo(OptOutSource.DnsTxt);

        // Host-wide: the record's owner is the domain's operator, speaking about a machine they run.
        await Assert.That(ruling.Port).IsNull();
        await Assert.That(await gate.RuleOnAsync(Target("mystery.example", 7777), None)).IsNotNull();

        await Assert.That((await rows.AllAsync(None)).Single().Detail).Contains(OptOutVocabulary.DnsValue);
    }

    [Test]
    [Arguments("opt-out")]
    [Arguments("OPT-OUT")]
    [Arguments("optout")]
    [Arguments("v=muindex1; opt-out")]
    [Arguments("opt-out=4201")]
    [Arguments("opt-out:4201")]
    [Arguments("opt-out=4200,4201,4202")]
    public async Task TheRecordFormsAZoneFileActuallyContainsAreRead(string record)
    {
        var dns = new FakeDnsTxtResolver()
            .Publishing(OptOutVocabulary.DnsNameFor("corvid.example.org"), record);

        var (gate, _, _, _) = World(dns);

        await Assert.That(await gate.RuleOnAsync(Target(port: 4201), None)).IsNotNull();
    }

    [Test]
    public async Task ARecordNamingAnotherPortSaysNothingAboutThisOne()
    {
        var dns = new FakeDnsTxtResolver()
            .Publishing(OptOutVocabulary.DnsNameFor("corvid.example.org"), "opt-out=4202");

        var (gate, rows, _, _) = World(dns);

        await Assert.That(await gate.RuleOnAsync(Target(port: 4201), None)).IsNull();
        await Assert.That(await rows.AllAsync(None)).IsEmpty();

        var qualified = await gate.RuleOnAsync(Target(port: 4202), None);

        await Assert.That(qualified).IsNotNull();
        await Assert.That(qualified!.Port).IsEqualTo(4202);
    }

    [Test]
    public async Task AnUnrelatedTxtRecordAtThatNameIsNotAnOptOut()
    {
        var dns = new FakeDnsTxtResolver()
            .Publishing(OptOutVocabulary.DnsNameFor("corvid.example.org"), "v=spf1 -all");

        var (gate, _, _, _) = World(dns);

        await Assert.That(await gate.RuleOnAsync(Target(), None)).IsNull();
    }

    [Test]
    public async Task RemovingTheRecordLetsUsBackIn()
    {
        // The DNS route is the one an operator can undo without asking anybody, and it is the only
        // route that can: everything else would have to be re-read over a connection they told us not
        // to make.
        var dns = new FakeDnsTxtResolver()
            .Publishing(OptOutVocabulary.DnsNameFor("corvid.example.org"), OptOutVocabulary.DnsValue);

        var (gate, rows, _, time) = World(dns);

        await Assert.That(await gate.RuleOnAsync(Target(), None)).IsNotNull();

        time.Advance(TimeSpan.FromDays(7));
        dns.Publishing(OptOutVocabulary.DnsNameFor("corvid.example.org"));

        await Assert.That(await gate.RuleOnAsync(Target(), None)).IsNull();

        // Withdrawn, not deleted. "They asked us to stop and later asked us back" is a thing the
        // record has to be able to say.
        var row = (await rows.AllAsync(None)).Single();

        await Assert.That(row.WithdrawnAt).IsEqualTo(Start + TimeSpan.FromDays(7));
        await Assert.That(row.RecordedAt).IsEqualTo(Start);
    }

    [Test]
    public async Task AResolverThatDidNotAnswerWithdrawsNothing()
    {
        // "No such record" and "the resolver did not reply" are different facts and only the first is
        // a withdrawal. Otherwise a SERVFAIL puts us back on somebody's doorstep.
        var dns = new FakeDnsTxtResolver()
            .Publishing(OptOutVocabulary.DnsNameFor("corvid.example.org"), OptOutVocabulary.DnsValue);

        var (gate, _, _, _) = World(dns);

        await Assert.That(await gate.RuleOnAsync(Target(), None)).IsNotNull();

        dns.Silent(OptOutVocabulary.DnsNameFor("corvid.example.org"));

        await Assert.That(await gate.RuleOnAsync(Target(), None)).IsNotNull();
    }

    [Test]
    public async Task DnsIsNotAskedAboutAHostThatOptedOutSomeOtherWay()
    {
        // Nothing DNS could answer would change the ruling, and a lookup we cannot act on is traffic
        // at somebody's nameserver for nothing.
        var (gate, _, dns, _) = World();

        await gate.RecordRequestAsync(
            "corvid.example.org", null, "mail from rowan@example.org, 2026-01-01", None);

        await Assert.That(await gate.RuleOnAsync(Target(), None)).IsNotNull();
        await Assert.That(dns.Asked).IsEmpty();
    }

    [Test]
    public async Task ARecordedRequestHasToSayWhoAsked()
    {
        // The ContactedMaintainer defect, in the one other place this codebase makes a claim about
        // somebody else's wishes: a gate like that is satisfied by a caller who can make the claim,
        // never by a default.
        var (gate, _, _, _) = World();

        await Assert.That(async () => await gate.RecordRequestAsync("corvid.example.org", null, "  ", None))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ARequestedOptOutMayNameOnePortOrTheWholeHost()
    {
        var (gate, _, _, _) = World();

        await gate.RecordRequestAsync("corvid.example.org", 4201, "asked at the con, 2026-01-01", None);

        await Assert.That(await gate.RuleOnAsync(Target(port: 4201), None)).IsNotNull();
        await Assert.That(await gate.RuleOnAsync(Target(port: 4202), None)).IsNull();

        await gate.RecordRequestAsync("corvid.example.org", null, "and the rest of it too", None);

        await Assert.That(await gate.RuleOnAsync(Target(port: 4202), None)).IsNotNull();
    }

    [Test]
    public async Task WithdrawingOneRouteNeverSpeaksForAnother()
    {
        var dns = new FakeDnsTxtResolver()
            .Publishing(OptOutVocabulary.DnsNameFor("corvid.example.org"), OptOutVocabulary.DnsValue);

        var (gate, _, _, _) = World(dns);

        await gate.RecordRequestAsync("corvid.example.org", 4201, "mail from rowan, 2026-01-01", None);
        await Assert.That(await gate.RuleOnAsync(Target(), None)).IsNotNull();

        dns.Publishing(OptOutVocabulary.DnsNameFor("corvid.example.org"));

        var ruling = await gate.RuleOnAsync(Target(), None);

        await Assert.That(ruling).IsNotNull();
        await Assert.That(ruling!.Source).IsEqualTo(OptOutSource.Request);
    }

    [Test]
    public async Task ConfirmingAnOptOutKeepsTheDayTheyFirstAsked()
    {
        var dns = new FakeDnsTxtResolver()
            .Publishing(OptOutVocabulary.DnsNameFor("corvid.example.org"), OptOutVocabulary.DnsValue);

        var (gate, rows, _, time) = World(dns);

        await gate.RuleOnAsync(Target(), None);
        time.Advance(TimeSpan.FromDays(30));
        await gate.RuleOnAsync(Target(), None);

        var row = (await rows.AllAsync(None)).Single();

        await Assert.That(row.RecordedAt).IsEqualTo(Start);
        await Assert.That(row.LastConfirmedAt).IsEqualTo(Start + TimeSpan.FromDays(30));
    }

    [Test]
    public async Task TheNameWeAskAboutIsTheOneWePublish()
    {
        // The record's name is a published contract with operators exactly as the MSSP variable is,
        // and it is derived here rather than restated, so the about page and this lookup cannot drift.
        await Assert.That(OptOutVocabulary.DnsNameFor("Corvid.Example.ORG."))
            .IsEqualTo("_muindex.corvid.example.org");

        await Assert.That(OptOutVocabulary.DnsLabel).IsEqualTo(ClaimTokenBeacon.DnsLabel);
    }

    [Test]
    public async Task ARefusalIsNotAProbeResultAndCannotBecomeOne()
    {
        // The rule this whole file exists under: DialRefusal.OptedOut names a dial that never
        // happened, and ProbeOutcome has two members that both mean the socket was opened. A Refused
        // member on that enum would make our politeness indistinguishable from a game's RST.
        await Assert.That(Enum.GetNames<ProbeOutcome>()).IsEquivalentTo(new[] { "Answered", "Failed" });
        await Assert.That(Enum.GetNames<DialRefusal>())
            .IsEquivalentTo(new[] { "None", "OutOfScope", "OptedOut" });
    }
}
