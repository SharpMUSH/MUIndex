using System.Net.Sockets;

using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// What a failed dial is written down as.
/// </summary>
/// <remarks>
/// This mapping is the whole of a game's public reachability history on the days it does not answer,
/// so a fault in our own resolver or network must never be published as a fact about the far end
/// (spec rule 5).
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
        // TryAgain is EAI_AGAIN — "temporary failure in name resolution". Left in the catch-all it
        // becomes "timeout", publishing a resolver fault as a game that did not answer.
        await Assert.That(DialFailure.Classify(new SocketException((int)code)).Cause).IsEqualTo(DialFailureCause.Dns);
    }

    [Test]
    public async Task ARefusalIsTheFarEndSpeakingAndSaysSo()
    {
        await Assert.That(DialFailure.Classify(new SocketException((int)SocketError.ConnectionRefused)).Cause)
            .IsEqualTo(DialFailureCause.Refused);
    }

    [Test]
    [Arguments(SocketError.TimedOut)]
    public async Task ADialThatRanOutOfTimeIsATimeout(SocketError code)
    {
        await Assert.That(DialFailure.Classify(new SocketException((int)code)).Cause).IsEqualTo(DialFailureCause.Timeout);
    }

    [Test]
    public async Task TheProbeBudgetExpiringIsATimeoutAndSaysWhose()
    {
        await Assert.That(DialFailure.Classify(new OperationCanceledException()))
            .IsEqualTo(new FailureDetail(DialFailureCause.Timeout, "probe budget exhausted"));
    }

    [Test]
    public async Task WhatTheDialActuallySaidIsAlwaysKept()
    {
        // The one thing every branch owes its caller: if the detail is dropped on the way to storage,
        // "why is this game dark" has no answer that outlives the container's log retention.
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
    [Arguments(SocketError.NetworkUnreachable)]
    [Arguments(SocketError.HostUnreachable)]
    [Arguments(SocketError.NetworkDown)]
    public async Task NoPathFromHereToThereSaysSoRatherThanClaimingATimeout(SocketError code)
    {
        // Left in the catch-all these become "timeout" — a missing route published as a game that
        // did not answer in time. Collapsed to one word here; the errno stays in the detail.
        await Assert.That(DialFailure.Classify(new SocketException((int)code)).Cause).IsEqualTo(DialFailureCause.NoRoute);
    }

    [Test]
    public async Task AnErrorWithNoWordForItStillCarriesWhatItSaid()
    {
        // The catch-all is now genuinely a catch-all rather than the place three named network
        // errors were quietly landing. Whatever falls here keeps its message.
        var unknown = DialFailure.Classify(new IOException("the peer went away"));

        await Assert.That(unknown.Cause).IsEqualTo(DialFailureCause.Error);
        await Assert.That(unknown.Detail).IsEqualTo("the peer went away");
    }

}
