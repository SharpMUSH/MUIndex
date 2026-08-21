using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// An empty MSSP report has three different meanings, and only one of them is "this game has no
/// MSSP". TelnetNegotiationCore 2.7.0 bounds the payload and drops it whole at the ceiling, so the
/// crawler acquired a third outcome the moment it took that version.
/// </summary>
public class MsspOutcomeTests
{
    private static ProbeResult Result(MsspOutcome outcome, int? rejectedBytes = null) => new()
    {
        Host = "corvid.example",
        Port = 4201,
        ObservedAt = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero),
        Outcome = ProbeOutcome.Answered,
        MsspOutcome = outcome,
        MsspBytesRejected = rejectedBytes,
    };

    [Test]
    public async Task ADroppedReportIsNotAnAbsentOne()
    {
        // The whole point. Both have an empty Mssp dictionary; only one of them means the server
        // does not speak MSSP. Publishing "no MSSP" because our own ceiling refused a report would
        // be recording a decision of ours as a measurement of theirs.
        var dropped = Result(MsspOutcome.RejectedTooLarge, rejectedBytes: 900_000);
        var absent = Result(MsspOutcome.NotOffered);

        await Assert.That(dropped.Mssp).IsEmpty();
        await Assert.That(absent.Mssp).IsEmpty();
        await Assert.That(dropped.MsspOutcome).IsNotEqualTo(absent.MsspOutcome);
    }

    [Test]
    public async Task AnEmptyReportThatArrivedIsTheServersAnswerAndNotAnAbsence()
    {
        // A server may legitimately answer MSSP with nothing useful. That is a measurement, and it
        // is different again from never having answered.
        var received = Result(MsspOutcome.Received);

        await Assert.That(received.Mssp).IsEmpty();
        await Assert.That(received.MsspOutcome).IsEqualTo(MsspOutcome.Received);
        await Assert.That(received.MsspOutcome).IsNotEqualTo(MsspOutcome.NotOffered);
    }

    [Test]
    public async Task TheRejectedByteCountIsKeptSoTheCeilingCanBeTunedAgainstRealServers()
    {
        var dropped = Result(MsspOutcome.RejectedTooLarge, rejectedBytes: 900_000);

        await Assert.That(dropped.MsspBytesRejected).IsEqualTo(900_000);
    }

    [Test]
    public async Task AProbeThatWasNeverAskedRecordsNoTransport()
    {
        await Assert.That(Result(MsspOutcome.NotOffered).MsspTransport).IsEqualTo(MsspTransport.None);
    }

    [Test]
    public async Task TheTransportIsRecordedBecauseTheTwoRoutesCanDisagree()
    {
        // Telnet option 70 and the plaintext MSSP-REQUEST reply are read by different code paths, so
        // which one produced a value is part of that value's provenance rather than trivia.
        var viaOption = Result(MsspOutcome.Received) with { MsspTransport = MsspTransport.TelnetOption70 };
        var viaPlaintext = Result(MsspOutcome.Received) with { MsspTransport = MsspTransport.PlaintextRequest };

        await Assert.That(viaOption.MsspTransport).IsNotEqualTo(viaPlaintext.MsspTransport);
    }

    [Test]
    public async Task TheDefaultCeilingIsFarBelowTheLibrarysAndFarAboveAnyRealReport()
    {
        // A real report is a few kilobytes; the official vocabulary is 45 variables. 128 KiB leaves
        // orders of magnitude of headroom while bounding what a stranger can make us allocate.
        var options = new ProbeOptions();

        await Assert.That(options.MaxSubnegotiationBytes).IsEqualTo(128 * 1024);
        await Assert.That(options.MaxSubnegotiationBytes).IsLessThan(1024 * 1024);
    }

    [Test]
    public async Task TheCrawlerNamesItselfSoAnAdminReadingLogsCanFindUs()
    {
        var options = new ProbeOptions();

        await Assert.That(options.TerminalTypes.First()).Contains("MUINDEX");
        await Assert.That(options.InfoUrl).IsNotEmpty();
    }

    [Test]
    public async Task EveryProbeIsHardBoundedInTime()
    {
        // Not hygiene: the crawler shares a process with the web tier, so an unbounded probe against
        // a black-holed host starves request threads.
        var options = new ProbeOptions();

        await Assert.That(options.Timeout).IsGreaterThan(TimeSpan.Zero);
        await Assert.That(options.Timeout).IsLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }
}
