using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MUI.Crawl;
using TelnetNegotiationCore.Builders;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Models;
using TelnetNegotiationCore.Protocols;

namespace MUI.Crawl.Tests;

/// <summary>
/// The plaintext <c>MSSP-REQUEST</c>, end to end against servers in this process.
/// </summary>
/// <remarks>
/// TelnetNegotiationCore owns the exchange itself and tests it there. What is pinned here is ours:
/// the line goes out only when the target says so, never to a game negotiating option 70, never
/// inside the session that measures a game for the first time, and what comes back is recorded as
/// MSSP without being recorded as a negotiation.
/// </remarks>
public class PlaintextMsspTests
{
    private const string Request = "MSSP-REQUEST";

    private static ProbeOptions Fast() => new()
    {
        QuietPeriod = TimeSpan.FromMilliseconds(120),
        SilenceGrace = TimeSpan.FromMilliseconds(300),
        MaxPhase = TimeSpan.FromSeconds(3),
        BannerPatience = TimeSpan.FromMilliseconds(300),
        WhoGrace = TimeSpan.FromMilliseconds(700),
        PollInterval = TimeSpan.FromMilliseconds(15),
        Timeout = TimeSpan.FromSeconds(20),
        MsspSettleGrace = TimeSpan.FromMilliseconds(400),
        PromptHold = TimeSpan.FromMilliseconds(120),
        PlaintextMsspGrace = TimeSpan.FromMilliseconds(800),
    };

    [Test]
    public async Task NothingIsSentUnlessTheTargetSaysSo()
    {
        await using var game = new OldRealms { AnswersRequest = true };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.PlaintextMssp).IsEqualTo(PlaintextMsspOutcome.NotAsked);
        await Assert.That(game.Sessions).Count().IsEqualTo(1);
        await Assert.That(game.Sessions.SelectMany(lines => lines)).DoesNotContain(Request);
    }

    [Test]
    public async Task ATrialAsksOnADialOfItsOwnAndKeepsTheReport()
    {
        await using var game = new OldRealms { AnswersRequest = true };

        var result = await new TelnetProbe(Fast()).ProbeAsync(
            game.Target with { PlaintextMssp = PlaintextMsspAsk.Trial });

        await Assert.That(result.PlaintextMssp).IsEqualTo(PlaintextMsspOutcome.Answered);
        await Assert.That(result.MsspOutcome).IsEqualTo(MsspOutcome.Received);
        await Assert.That(result.MsspTransport).IsEqualTo(MsspTransport.PlaintextRequest);
        await Assert.That(MsspReport.Last(result.Mssp, "NAME")).IsEqualTo("Old Realms");
        await Assert.That(MsspReport.Last(result.Mssp, "PLAYERS")).IsEqualTo("4");

        // A line of text is not a telnet negotiation, and the handshake field is a measurement of
        // negotiations. Noting MSSP here would publish option 70 for a game that never offered it.
        await Assert.That(result.OfferedOptions).DoesNotContain("MSSP");

        // The measurement saw none of it, and the trial asked nothing else.
        await Assert.That(game.Sessions).Count().IsEqualTo(2);
        await Assert.That(game.Sessions[0]).DoesNotContain(Request);
        await Assert.That(game.Sessions[0]).Contains("WHO");
        await Assert.That(game.Sessions[1]).IsEquivalentTo(new[] { Request });
        await Assert.That(result.Who.Count).IsEqualTo(3);
    }

    /// <summary>
    /// The reason the trial has a dial of its own.
    /// </summary>
    /// <remarks>
    /// The shape <c>playdecay.com:3003</c> was measured in: an unknown name is answered with a
    /// password prompt, and the next line is read as the password and ends the session. Had the
    /// request gone out in the measuring session, <c>WHO</c> would have been typed as a password and
    /// the hour recorded as uncountable — our experiment published as a fact about the game.
    /// </remarks>
    [Test]
    public async Task AGameThatReadsTheRequestAsANameHasItsMeasurementLeftAlone()
    {
        await using var game = new OldRealms { AsksForAPasswordAfterAName = true };

        var result = await new TelnetProbe(Fast()).ProbeAsync(
            game.Target with { PlaintextMssp = PlaintextMsspAsk.Trial });

        await Assert.That(result.PlaintextMssp).IsEqualTo(PlaintextMsspOutcome.Unanswered);
        await Assert.That(result.MsspOutcome).IsEqualTo(MsspOutcome.NotOffered);
        await Assert.That(result.MsspTransport).IsEqualTo(MsspTransport.None);
        await Assert.That(result.Who.Count).IsEqualTo(3);
        await Assert.That(game.Sessions[1]).IsEquivalentTo(new[] { Request });
    }

    [Test]
    public async Task AGameThatHangsUpOnTheRequestHasAnsweredIt()
    {
        await using var game = new OldRealms { HangsUpOnAName = true };

        var result = await new TelnetProbe(Fast()).ProbeAsync(
            game.Target with { PlaintextMssp = PlaintextMsspAsk.Trial });

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.PlaintextMssp).IsEqualTo(PlaintextMsspOutcome.Unanswered);
        await Assert.That(result.Who.Count).IsEqualTo(3);
    }

    [Test]
    public async Task AGameNegotiatingOption70IsNeverSentIt()
    {
        foreach (var ask in new[] { PlaintextMsspAsk.Trial, PlaintextMsspAsk.Ask })
        {
            await using var game = new OldRealms { AnswersRequest = true, OffersOption70 = true };

            var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target with { PlaintextMssp = ask });

            await Assert.That(result.MsspTransport).IsEqualTo(MsspTransport.TelnetOption70);
            await Assert.That(result.PlaintextMssp).IsEqualTo(PlaintextMsspOutcome.NotAsked);
            await Assert.That(game.Sessions).Count().IsEqualTo(1);
            await Assert.That(game.Sessions[0]).DoesNotContain(Request);
        }
    }

    [Test]
    public async Task AKnownAnswererIsAskedBeforeWhoAndSparedTheWho()
    {
        await using var game = new OldRealms { AnswersRequest = true };

        var result = await new TelnetProbe(Fast()).ProbeAsync(
            game.Target with { PlaintextMssp = PlaintextMsspAsk.Ask });

        await Assert.That(result.PlaintextMssp).IsEqualTo(PlaintextMsspOutcome.Answered);
        await Assert.That(result.MsspTransport).IsEqualTo(MsspTransport.PlaintextRequest);
        await Assert.That(MsspReport.Last(result.Mssp, "PLAYERS")).IsEqualTo("4");
        await Assert.That(result.OfferedOptions).DoesNotContain("MSSP");

        // One session: the request first, and WHO never, because the report stated a count.
        await Assert.That(game.Sessions).Count().IsEqualTo(1);
        await Assert.That(game.Sessions[0].First()).IsEqualTo(Request);
        await Assert.That(game.Sessions[0]).DoesNotContain("WHO");

        // The reply is protocol, not screen, and the prompt it re-printed is our doing.
        await Assert.That(result.Banner).DoesNotContain("MSSP");
        await Assert.That(result.Banner).Contains("Welcome to Old Realms");
    }

    /// <summary>
    /// A stated count does not stand in for <c>WHO</c> at an address known to answer it.
    /// </summary>
    /// <remarks>
    /// <c>tapestries.fur.com:2069</c> answers <c>MSSP-REQUEST</c> with <c>PLAYERS = 26842</c>, its
    /// player objects, while its <c>WHO</c> counts a few hundred. The report spared it the
    /// <c>WHO</c>, and the site published the database as the population.
    /// </remarks>
    [Test]
    public async Task AKnownAnswererKnownToAnswerWhoIsStillAskedWho()
    {
        await using var game = new OldRealms { AnswersRequest = true };

        var result = await new TelnetProbe(Fast()).ProbeAsync(
            game.Target with { PlaintextMssp = PlaintextMsspAsk.Ask, WhoAnswers = true });

        await Assert.That(MsspReport.Last(result.Mssp, "PLAYERS")).IsEqualTo("4");
        await Assert.That(game.Sessions).Count().IsEqualTo(1);
        await Assert.That(game.Sessions[0].First()).IsEqualTo(Request);
        await Assert.That(game.Sessions[0]).Contains("WHO");
        await Assert.That(result.Who.Count).IsEqualTo(3);
    }

    [Test]
    public async Task AKnownAnswererThatFallsSilentIsRecordedAsSuch()
    {
        await using var game = new OldRealms();

        var result = await new TelnetProbe(Fast()).ProbeAsync(
            game.Target with { PlaintextMssp = PlaintextMsspAsk.Ask });

        await Assert.That(result.PlaintextMssp).IsEqualTo(PlaintextMsspOutcome.Unanswered);
        await Assert.That(result.MsspOutcome).IsEqualTo(MsspOutcome.NotOffered);
        await Assert.That(game.Sessions).Count().IsEqualTo(1);
    }

    /// <summary>
    /// A connect screen at a name prompt, served to as many connections as arrive.
    /// </summary>
    /// <remarks>
    /// A TelnetNegotiationCore server for the framing, with the plaintext reply written by hand:
    /// the library's own server half needs <c>MSSPProtocol</c> registered, which would offer option
    /// 70 as well, and the game under test here is one that does not. The reply is written the way
    /// SMAUG's <c>send_mssp_data</c> writes it.
    /// </remarks>
    private sealed class OldRealms : IAsyncDisposable
    {
        private const string Prompt = "By what name do you wish to be known? ";

        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stopping = new();
        private readonly List<List<string>> _sessions = [];
        private readonly Task _serving;

        public OldRealms()
        {
            _listener.Start();
            _serving = AcceptAsync();
        }

        public bool AnswersRequest { get; init; }

        public bool OffersOption70 { get; init; }

        /// <summary>Whether an unknown name is followed by a password prompt that ends the session.</summary>
        public bool AsksForAPasswordAfterAName { get; init; }

        public bool HangsUpOnAName { get; init; }

        public ProbeTarget Target => new(
            IPAddress.Loopback.ToString(),
            ((IPEndPoint)_listener.LocalEndpoint).Port);

        public IReadOnlyList<IReadOnlyList<string>> Sessions
        {
            get
            {
                lock (_sessions)
                {
                    return [.. _sessions.Select(lines => (IReadOnlyList<string>)[.. lines])];
                }
            }
        }

        private async Task AcceptAsync()
        {
            var serving = new List<Task>();

            try
            {
                while (!_stopping.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                    var lines = new List<string>();

                    lock (_sessions)
                    {
                        _sessions.Add(lines);
                    }

                    serving.Add(ServeAsync(client, lines));
                }
            }
            catch (Exception error) when (error is OperationCanceledException or SocketException)
            {
            }

            await Task.WhenAll(serving);
        }

        private async Task ServeAsync(TcpClient client, List<string> lines)
        {
            using var _ = client;
            var awaitingPassword = false;

            try
            {
                var builder = new TelnetInterpreterBuilder()
                    .UseMode(TelnetInterpreter.TelnetMode.Server)
                    .UseLogger(NullLogger.Instance)
                    .OnSubmit(async (bytes, encoding, telnet) =>
                    {
                        var line = encoding.GetString(bytes).Trim();

                        lock (_sessions)
                        {
                            lines.Add(line);
                        }

                        if (awaitingPassword)
                        {
                            await WriteAsync(telnet, "Wrong password.\r\n");
                            client.Client.Shutdown(SocketShutdown.Both);
                            return;
                        }

                        switch (line.ToUpperInvariant())
                        {
                            case "":
                                await WriteAsync(telnet, Prompt);
                                return;

                            case Request when AnswersRequest:
                                await WriteAsync(
                                    telnet,
                                    "\r\nMSSP-REPLY-START\r\nNAME\tOld Realms\r\nPLAYERS\t4\r\n"
                                    + "UPTIME\t1700000000\r\nMSSP-REPLY-END\r\n" + Prompt);
                                return;

                            case "WHO":
                                await WriteAsync(
                                    telnet,
                                    "Player Name        On For Idle\r\n"
                                    + "3 Players logged in, 22 record, no maximum.\r\n" + Prompt);
                                return;

                            case "INFO" or "VERSION":
                                await WriteAsync(telnet, Prompt);
                                return;
                        }

                        if (HangsUpOnAName)
                        {
                            client.Client.Shutdown(SocketShutdown.Both);
                        }
                        else if (AsksForAPasswordAfterAName)
                        {
                            awaitingPassword = true;
                            await WriteAsync(telnet, "Password: ");
                        }
                        else
                        {
                            await WriteAsync(telnet, "Illegal name, try another.\r\n" + Prompt);
                        }
                    });

                if (OffersOption70)
                {
                    var mssp = new MSSPProtocol();
                    mssp.SetMSSPConfig(() => new MSSPConfig { Name = "Old Realms", Players = 4 });
                    builder = builder.AddPlugin(mssp);
                }

                var built = await builder.BuildAndStartAsync(client, _stopping.Token);
                await using var telnet = built.Interpreter;

                await WriteAsync(telnet, "Welcome to Old Realms\r\nA SMAUG-shaped place.\r\n" + Prompt);
                await built.ReadTask;
            }
            catch (Exception error) when (error is OperationCanceledException
                or IOException
                or SocketException
                or ObjectDisposedException)
            {
            }
        }

        private static ValueTask WriteAsync(TelnetInterpreter telnet, string text) =>
            telnet.WriteToNetworkAsync(Encoding.Latin1.GetBytes(text));

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();
            await _serving;
            _stopping.Dispose();
        }
    }
}
