using System.Net.Sockets;

using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// What a failed dial is written down as.
/// </summary>
/// <remarks>
/// This mapping is the whole of a game's public reachability history on the days it does not answer,
/// and until 2026-08-18 three of the entries below were wrong in the same direction: a fault in our
/// own resolver or network was published as a fact about the far end. Spec rule 5.
/// </remarks>
public class DialFailureTests
{
    [Test]
    [Arguments(SocketError.HostNotFound)]
    [Arguments(SocketError.TryAgain)]
    [Arguments(SocketError.NoData)]
    [Arguments(SocketError.NoRecovery)]
    public async Task EveryWayANameFailsToResolveIsReadAsDns(SocketError code)
    {
        // TryAgain is EAI_AGAIN — "temporary failure in name resolution" — and it was landing in the
        // catch-all, which ProbeIngestor.FailureReading turns into "timeout". A resolver that timed
        // out was being published as a game that did not answer, which is the opposite of what
        // happened: measured on the production box, cold lookups for the flapping hosts take 1.6s to
        // 10s and some of them do not come back at all.
        await Assert.That(DialFailure.Classify(new SocketException((int)code)).Cause).IsEqualTo("dns");
    }

    [Test]
    public async Task ARefusalIsTheFarEndSpeakingAndSaysSo()
    {
        await Assert.That(DialFailure.Classify(new SocketException((int)SocketError.ConnectionRefused)).Cause)
            .IsEqualTo("refused");
    }

    [Test]
    [Arguments(SocketError.TimedOut)]
    public async Task ADialThatRanOutOfTimeIsATimeout(SocketError code)
    {
        await Assert.That(DialFailure.Classify(new SocketException((int)code)).Cause).IsEqualTo("timeout");
    }

    [Test]
    public async Task TheProbeBudgetExpiringIsATimeoutAndSaysWhose()
    {
        await Assert.That(DialFailure.Classify(new OperationCanceledException()))
            .IsEqualTo(new FailureDetail("timeout", "probe budget exhausted"));
    }

    [Test]
    public async Task WhatTheDialActuallySaidIsAlwaysKept()
    {
        // The one thing every branch owes its caller. Of 182 dark episodes over four days in
        // production, 157 carried cause "timeout" and nothing else, because the message was computed
        // here and then dropped on the way to storage — so "why is this game dark" had no answer
        // that outlived the container's half-hour of logs.
        var errors = new Exception[]
        {
            new SocketException((int)SocketError.HostNotFound),
            new SocketException((int)SocketError.ConnectionRefused),
            new SocketException((int)SocketError.TimedOut),
            new SocketException((int)SocketError.NetworkUnreachable),
            new IOException("the peer went away"),
        };

        foreach (var error in errors)
        {
            await Assert.That(DialFailure.Classify(error).Detail).IsNotNull().And.IsNotEmpty();
        }
    }

    [Test]
    public async Task AnErrorWithNoWordForItIsNotSilentlyCalledSomethingElse()
    {
        // It still has to become one of the catalogue's causes downstream, but it must not claim to
        // be a socket timeout here: a network we could not reach is our problem, not the game's.
        var unreachable = DialFailure.Classify(new SocketException((int)SocketError.NetworkUnreachable));

        await Assert.That(unreachable.Cause).IsEqualTo("error");
        await Assert.That(unreachable.Detail).IsNotNull().And.IsNotEmpty();
    }
}
