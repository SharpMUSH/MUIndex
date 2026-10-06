using MUI.Crawl;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// When the plaintext <c>MSSP-REQUEST</c> is sent, and what each answer teaches.
/// </summary>
public class MsspRoutesTests
{
    private static ProbeResult Plaintext(PlaintextMsspOutcome outcome) =>
        ProbeResults.Answered() with { PlaintextMssp = outcome };

    private static ProbeResult PlaintextReport() =>
        ProbeResults.Answered(mssp: ProbeResults.Mssp(("NAME", "Old Realms"))) with
        {
            MsspTransport = MsspTransport.PlaintextRequest,
            PlaintextMssp = PlaintextMsspOutcome.Answered,
        };

    [Test]
    public async Task OnlyAnUnknownAddressIsGivenATrialAndOnlyAKnownAnswererIsAsked()
    {
        await Assert.That(MsspRoutes.Ask(null)).IsEqualTo(PlaintextMsspAsk.Trial);
        await Assert.That(MsspRoutes.Ask(MsspRoute.Plaintext)).IsEqualTo(PlaintextMsspAsk.Ask);
        await Assert.That(MsspRoutes.Ask(MsspRoute.Telnet)).IsEqualTo(PlaintextMsspAsk.Never);
        await Assert.That(MsspRoutes.Ask(MsspRoute.None)).IsEqualTo(PlaintextMsspAsk.Never);
    }

    [Test]
    public async Task AnOption70ReportSettlesItFromEveryState()
    {
        var report = ProbeResults.Answered(mssp: ProbeResults.Mssp(("NAME", "Old Realms")));

        await Assert.That(MsspRoutes.Learn(null, report)).IsEqualTo(MsspRoute.Telnet);
        await Assert.That(MsspRoutes.Learn(MsspRoute.None, report)).IsEqualTo(MsspRoute.Telnet);
        await Assert.That(MsspRoutes.Learn(MsspRoute.Plaintext, report)).IsEqualTo(MsspRoute.Telnet);
    }

    [Test]
    public async Task AReportTooLargeToKeepIsStillAReportOverOption70()
    {
        var dropped = ProbeResults.Answered() with { MsspOutcome = MsspOutcome.RejectedTooLarge };

        await Assert.That(MsspRoutes.Learn(null, dropped)).IsEqualTo(MsspRoute.Telnet);
    }

    [Test]
    public async Task OnceOption70IsKnownASessionWithoutItReopensNothing()
    {
        await Assert.That(MsspRoutes.Learn(MsspRoute.Telnet, ProbeResults.Answered())).IsEqualTo(MsspRoute.Telnet);
    }

    [Test]
    public async Task AnAnswerMakesItAPlaintextAddress()
    {
        await Assert.That(MsspRoutes.Learn(null, PlaintextReport())).IsEqualTo(MsspRoute.Plaintext);
        await Assert.That(MsspRoutes.Learn(MsspRoute.Plaintext, PlaintextReport())).IsEqualTo(MsspRoute.Plaintext);
    }

    [Test]
    public async Task AnOversizedPlaintextAnswerIsStillAnAnswer()
    {
        var dropped = Plaintext(PlaintextMsspOutcome.Answered) with { MsspOutcome = MsspOutcome.RejectedTooLarge };

        await Assert.That(MsspRoutes.Learn(null, dropped)).IsEqualTo(MsspRoute.Plaintext);
    }

    [Test]
    public async Task NoAnswerToTheTrialIsFinal()
    {
        await Assert.That(MsspRoutes.Learn(null, Plaintext(PlaintextMsspOutcome.Unanswered)))
            .IsEqualTo(MsspRoute.None);
    }

    [Test]
    public async Task AFormerAnswererThatMissesOnceGetsOneMoreTrialRatherThanBeingDropped()
    {
        await Assert.That(MsspRoutes.Learn(MsspRoute.Plaintext, Plaintext(PlaintextMsspOutcome.Unanswered)))
            .IsNull();
    }

    [Test]
    public async Task NotAskingAndFailingToDialTeachNothing()
    {
        await Assert.That(MsspRoutes.Learn(null, ProbeResults.Answered())).IsNull();
        await Assert.That(MsspRoutes.Learn(MsspRoute.Plaintext, ProbeResults.Answered())).IsEqualTo(MsspRoute.Plaintext);
        await Assert.That(MsspRoutes.Learn(MsspRoute.None, ProbeResults.Failed())).IsEqualTo(MsspRoute.None);
        await Assert.That(MsspRoutes.Learn(null, ProbeResults.Failed())).IsNull();
    }
}
