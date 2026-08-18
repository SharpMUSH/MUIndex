using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// The probe driven end to end against a server in this process.
/// </summary>
/// <remarks>
/// These pin properties of the conversation that no fixture can reach: an unterminated line is still
/// delivered, banner and <c>WHO</c> stay separate evidence, and the probe's own bookkeeping is never
/// counted as either. A real socket rather than a mock, because the behaviour under test is
/// TelnetNegotiationCore's line assembly meeting real timing.
/// </remarks>
public class ProbeSessionTests
{
    /// <summary>Short, so the suite settles in a moment rather than in the live defaults.</summary>
    private static ProbeOptions Fast() => new()
    {
        QuietPeriod = TimeSpan.FromMilliseconds(120),
        SilenceGrace = TimeSpan.FromMilliseconds(300),
        MaxPhase = TimeSpan.FromSeconds(3),
        BannerPatience = TimeSpan.FromMilliseconds(300),
        PollInterval = TimeSpan.FromMilliseconds(15),
        Timeout = TimeSpan.FromSeconds(20),
    };

    [Test]
    public async Task ALineTheServerNeverTerminatedIsStillDelivered()
    {
        // TelnetNegotiationCore submits a line only when a newline arrives, so a trailing partial
        // line sits in its buffer forever and OnSubmit never fires for it. Unterminated output —
        // a hanging name prompt — is common among real servers.
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\nA quiet little place.\r\n",
            BannerTail = "By what name do you wish to be known? ",
            WhoReply = "Player Name        On For Idle\r\n0 Players logged in, 22 record, no maximum.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Banner).Contains("By what name do you wish to be known?");
    }

    /// <summary>
    /// The whole path, against the bytes that exposed it: a server that negotiates UTF-8 and sends
    /// GBK is read losslessly, and an operator's override makes it right.
    /// </summary>
    /// <remarks>
    /// Real case: a server negotiates CHARSET UTF-8 cleanly but then sends its connect screen in GBK,
    /// because the actual encoding is chosen from an in-game menu rather than the negotiated option.
    /// Neither side negotiated wrongly — decoding on the strength of it was the bug.
    /// </remarks>
    [Test]
    public async Task AServerThatDeclaresOneEncodingAndSendsAnotherIsNotDestroyedOnTheWayIn()
    {
        // FakeGame writes Latin-1, so a string of U+00xx puts these exact bytes on the wire. This is
        // the game's own name, "北大侠客行", as GBK.
        const string titleInGbk = "\u00b1\u00b1  \u00b4\u00f3  \u00cf\u00c0  \u00bf\u00cd  \u00d0\u00d0";

        // Two servers, not two probes of one: FakeGame serves a single connection.
        await using var untold = new FakeGame
        {
            Banner = $"----====   {titleInGbk}  ====----\r\n",
            BannerTail = "Enter your name: ",
        };
        await using var game = new FakeGame
        {
            Banner = $"----====   {titleInGbk}  ====----\r\n",
            BannerTail = "Enter your name: ",
        };

        var asSent = await new TelnetProbe(Fast()).ProbeAsync(untold.Target);

        // The old code decoded on the negotiated encoding with a replacing fallback, so every one of
        // these bytes came back as U+FFFD, permanently — the source bytes are gone by then.
        await Assert.That(asSent.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(asSent.Banner).DoesNotContain("\ufffd");
        await Assert.That(asSent.ReadAs).IsEqualTo("iso-8859-1");
        await Assert.That(asSent.CharsetSource).IsEqualTo(WireCharset.Undetermined);

        var told = await new TelnetProbe(Fast()).ProbeAsync(game.Target with { Charset = "gbk" });

        await Assert.That(told.Banner).Contains("北  大  侠  客  行");
        await Assert.That(told.ReadAs).IsEqualTo("gb2312");
        await Assert.That(told.CharsetSource).IsEqualTo(WireCharset.Overridden);
    }

    [Test]
    public async Task AServerThatHangsUpOnTheFlushLineStillCountsAsHavingAnswered()
    {
        // Every DIKU descendant reads an empty line at "Who art thou:" as a goodbye and closes, so
        // the probe's own flush line ends the session and the WHO after it writes into a dead socket.
        // The banner is already in hand when that happens — reporting the whole probe as Failed would
        // record our own flush line as a fact about their server (CLAUDE.md rule 5). The server answered.
        await using var game = new FakeGame
        {
            Banner = "Welcome to Mortal Realms\r\nMrMud 1.4\r\n",
            BannerTail = "Who art thou: ",
            HangsUpOnBlankLine = true,
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Banner).Contains("Mortal Realms");
        await Assert.That(result.Banner).Contains("Who art thou:");

        // Nothing was asked, so nothing may claim to have been asked and found unreadable (rule 4).
        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.NotAsked);
        await Assert.That(result.Info).IsNull();
        await Assert.That(result.Version).IsNull();
    }

    [Test]
    public async Task AnAnswerAlreadyGivenSurvivesTheServerClosingRightAfterIt()
    {
        // A count is the most expensive thing a probe collects, so it must be recorded before
        // anything that can throw: a peer that goes away is not heard from until the *next* send,
        // by which point whoLines is already committed.
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\n",
            BannerTail = "Please enter a name: ",
            WhoReply = "Player Name        On For Idle\r\n7 Players logged in, 22 record, no maximum.\r\n",
            HangsUpAfterWho = true,
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.Count);
        await Assert.That(result.Who.Count).IsEqualTo(7);
        await Assert.That(result.Banner).Contains("Welcome to Nowhere");
    }

    [Test]
    public async Task AServerThatCloseBeforeSayingAnythingIsStillAFailure()
    {
        // No banner, no negotiation, nothing measured — carrying evidence forward must not turn an
        // empty session into an answered one.
        await using var game = new FakeGame { ClosesImmediately = true };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Failed);
        await Assert.That(result.Banner).IsNull();
    }

    [Test]
    public async Task AnUnterminatedWhoReplyIsReadRatherThanLost()
    {
        // A count that only arrives when the server happens to add a newline is a count we lose at random.
        await using var game = new FakeGame
        {
            Banner = "Welcome.\r\n",
            WhoReply = "Player Name        On For Idle   Doing\r\n",
            WhoTail = "7 Players logged in, 22 record, no maximum.",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.Count);
        await Assert.That(result.Who.Count).IsEqualTo(7);
    }

    [Test]
    public async Task TheBannerAndTheWhoAnswerStayDifferentEvidence()
    {
        // Merging the two would let connect-screen prose reach a parser whose job is counting
        // people, and let a WHO listing into a banner hash meant to identify the game.
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\nPlayers Currently Online: 42\r\n",
            WhoReply = "There are 3 players connected.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Banner).Contains("Welcome to Nowhere");
        await Assert.That(result.Banner).DoesNotContain("There are 3 players connected.");
        await Assert.That(result.Who.Count).IsEqualTo(3);
        await Assert.That(result.BannerPlayerCount).IsEqualTo(42);
    }

    [Test]
    public async Task WhatTheServerSaysBackToOurOwnFlushIsCountedAsNeither()
    {
        // The probe sends a bare line terminator between banner and WHO, to clear its own IAC DO
        // bytes out of a server that buffered them as text. Whatever that produces is a reaction to
        // our own byte sequence and must not be recorded as either the connect screen or the WHO answer.
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\n",
            BlankLineReply = "Huh? Type HELP for help.\r\n",
            WhoReply = "There are 5 players connected.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Banner).DoesNotContain("Huh?");
        await Assert.That(result.Who.Count).IsEqualTo(5);
    }

    /// <summary>
    /// The one case where the flush is an answer rather than a stray line.
    /// </summary>
    /// <remarks>
    /// Several real games gate their whole connect screen behind an ANSI keystroke prompt; the probe
    /// was already sending the keystroke and throwing the resulting screen away.
    /// </remarks>
    [Test]
    public async Task AScreenBehindAColourQuestionIsTheConnectScreen()
    {
        await using var game = new FakeGame
        {
            BannerTail = "Do you want ANSI? (Y/n) ",
            BlankLineReply =
                "Ansi enabled!\r\nWelcome to Adventures Unlimited\r\n"
                + "Based on CircleMUD 3.0, created by Jeremy Elson\r\n"
                + "Players Currently Online: 7\r\n",
            WhoReply = "Illegal name, try again.\r\nName: \r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Banner).Contains("Welcome to Adventures Unlimited");
        await Assert.That(result.BannerPlayerCount).IsEqualTo(7);

        // The WHO window still begins after all of it, so the game's refusal reads as a refusal
        // rather than part of its connect screen.
        await Assert.That(result.Banner).DoesNotContain("Illegal name");
        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.LoginPrompt);
    }

    [Test]
    public async Task AServerThatSimplyRepaintsIsStillCountedAsNeither()
    {
        // A screen that has already painted is not a gate however it ends, so what a stray Return
        // produces stays discarded.
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\nBy what name do you wish to be known? \r\n",
            BlankLineReply = "By what name do you wish to be known? \r\n",
            WhoReply = "There are 5 players connected.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Banner).Contains("Welcome to Nowhere");
        await Assert.That(result.Who.Count).IsEqualTo(5);
    }

    [Test]
    public async Task InfoAndVersionRepliesAreCapturedSeparately()
    {
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\n",
            WhoReply = "There are 5 players connected.\r\n",
            InfoReply = "Codebase: CorvidMUSH\r\n",
            VersionReply = "Version 1.2.3\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Who.Count).IsEqualTo(5);
        await Assert.That(result.Info).IsEqualTo("Codebase: CorvidMUSH");
        await Assert.That(result.Version).IsEqualTo("Version 1.2.3");
        await Assert.That(result.Banner).DoesNotContain("Codebase: CorvidMUSH");
    }

    [Test]
    public async Task AServerThatBuffersOurNegotiationAsTextStillAnswersWho()
    {
        // A server that doesn't parse telnet at its login screen: our IAC DO bytes land in its line
        // buffer and poison the next command. Before the flush the probe reported Unknown for a
        // game that answers WHO perfectly well.
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\n",
            WhoReply = "0 Players logged in, 22 record, no maximum.\r\n",
            SwallowsNegotiationAsText = true,
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.Count);
        await Assert.That(result.Who.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TheProbeNeverSendsAnythingButItsPermittedCommands()
    {
        // Everything the probe types must be a permitted command or an empty line — nothing that
        // logs in, creates, or changes anything, and nothing a login screen would read as a character name.
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\n",
            WhoReply = "There are 2 players connected.\r\n",
            SwallowsNegotiationAsText = true,
        };

        await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Received).DoesNotContain("MSSP-REQUEST");
        await Assert.That(game.Received.Any(line => line.Contains("\u00ff\u00fdF", StringComparison.Ordinal)))
            .IsFalse();

        foreach (var line in game.Received)
        {
            var spoken = line.Trim();
            if (spoken.Length == 0)
            {
                continue;
            }

            if (!spoken.All(c => c is >= ' ' and <= '~'))
            {
                continue;
            }

            await Assert.That(TelnetProbe.PermittedCommands).Contains(spoken);
        }
    }

    [Test]
    public async Task APromptGameSettlesInFarLessThanTheOldFixedDelays()
    {
        // A server that answers immediately must not be charged for the slow case.
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\n",
            WhoReply = "There are 2 players connected.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Who.Count).IsEqualTo(2);
        await Assert.That(result.Elapsed).IsLessThan(TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// A server that says it is not ready, then pauses for longer than a gap between lines.
    /// </summary>
    /// <remarks>
    /// tbaMUD's shape: a "Please Wait..." placeholder, then real silence, then the real screen. Under
    /// a plain quiet period the placeholder <em>was</em> the connect screen — its hash became the
    /// game's identity, so two unrelated tbaMUDs fingerprinted alike and collided as duplicates.
    /// </remarks>
    [Test]
    public async Task AScreenThatSaidItWasNotReadyIsWaitedFor()
    {
        await using var game = new FakeGame
        {
            Preamble = "Attempting to Detect Client, Please Wait...\r\n",
            BannerDelay = TimeSpan.FromMilliseconds(400),
            Banner = "         T B A M U D\r\n   Based on CircleMUD by Jeremy Elson\r\n",
            BannerTail = "By what name do you wish to be known? ",
        };

        var result = await new TelnetProbe(Fast() with { BannerPatience = TimeSpan.FromSeconds(2) })
            .ProbeAsync(game.Target);

        await Assert.That(result.Banner).Contains("T B A M U D");
        await Assert.That(result.Banner).Contains("By what name do you wish to be known?");
    }

    /// <summary>
    /// The patience is conditional, so a server that has already painted is not made to wait.
    /// </summary>
    /// <remarks>
    /// The cost side of the fix above: every probe pays it if the condition is wrong, and a crawler
    /// spending extra seconds per game is a different program.
    /// </remarks>
    [Test]
    public async Task AScreenThatIsAlreadyThereIsNotWaitedFor()
    {
        await using var game = new FakeGame
        {
            Banner = new string('=', 60) + "\r\nWelcome to Nowhere, a quiet little place with a "
                + "connect screen long enough to be one.\r\n" + new string('=', 60) + "\r\n",
            WhoReply = "0 Players logged in.\r\n",
        };

        var started = DateTime.UtcNow;
        var result = await new TelnetProbe(Fast() with { BannerPatience = TimeSpan.FromSeconds(5) })
            .ProbeAsync(game.Target);
        var elapsed = DateTime.UtcNow - started;

        await Assert.That(result.Banner).Contains("Welcome to Nowhere");
        await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(4));
    }

    /// <summary>
    /// A host being taken down is not a measurement, and must not come back as one.
    /// </summary>
    /// <remarks>
    /// A real production incident, and rule 5's worst case: our own outage published as theirs. The
    /// caller's token is linked into the probe's budget, so a stopping host and an expired budget both
    /// throw <see cref="OperationCanceledException"/> and <c>Classify</c> read either as
    /// <c>("timeout", "probe budget exhausted")</c> — returning that as a <see cref="ProbeResult"/>
    /// wrote healthy games down as unreachable for hours on an unrelated host restart.
    /// </remarks>
    [Test]
    public async Task AStoppingHostIsNotRecordedAsTheGameTimingOut()
    {
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\nA quiet little place.\r\n",
            WhoReply = "0 Players logged in.\r\n",
        };

        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        // Thrown, not returned: VisitAsync already knows what a cancelled cycle means, and a
        // ProbeResult is a measurement — there was none to make.
        await Assert.That(async () => await new TelnetProbe(Fast()).ProbeAsync(game.Target, stopping.Token))
            .Throws<OperationCanceledException>();
    }

    /// <summary>
    /// And the probe's <em>own</em> budget expiring is still a timeout we measured, which is the
    /// distinction the fix turns on rather than a case it suppresses.
    /// </summary>
    /// <remarks>
    /// A game that accepts a connection and says nothing until our ceiling runs out has been
    /// measured — that's a fact about the far end. The bug above was never that timeouts get
    /// recorded, it was our own shutdown dressed as one.
    /// </remarks>
    [Test]
    public async Task TheProbesOwnBudgetExpiringIsStillATimeoutWeMeasured()
    {
        await using var game = new FakeGame
        {
            BannerDelay = TimeSpan.FromSeconds(30),
            Banner = "A screen that arrives long after we have given up.\r\n",
        };

        var result = await new TelnetProbe(Fast() with
        {
            Timeout = TimeSpan.FromMilliseconds(400),
            MaxPhase = TimeSpan.FromSeconds(30),
            SilenceGrace = TimeSpan.FromSeconds(30),
            BannerPatience = TimeSpan.FromSeconds(30),
        }).ProbeAsync(game.Target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Failed);
        await Assert.That(result.Failure!.Cause).IsEqualTo("timeout");
    }

    [Test]
    public async Task TheProbeDialsTheAddressTheGuardApprovedRatherThanResolvingTheNameAgain()
    {
        // Every probe used to resolve its host twice: once in HostScopeGuard, which vetted the
        // addresses, and once inside TcpClient.ConnectAsync, which could land somewhere the guard
        // never saw. The name here cannot resolve (RFC 2606), so the probe can only answer if it
        // dialled the address it was handed.
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\n",
            WhoReply = "0 Players logged in.\r\n",
        };

        var target = new ProbeTarget("no-such-host.invalid", game.Target.Port)
        {
            Addresses = [IPAddress.Loopback],
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Host).IsEqualTo("no-such-host.invalid");
    }

    /// <summary>
    /// The interpreter is shut down when the probe has finished with it, rather than left for the
    /// garbage collector to notice.
    /// </summary>
    /// <remarks>
    /// <c>BuildAndStartAsync</c> hands back an interpreter owning a byte channel, a processing task
    /// and a dozen protocol plugins, including MCCP's zlib streams — real unmanaged resources, not
    /// just a managed object waiting its turn. A probe that walked away without disposing it leaked
    /// one session's worth of those per run. Asserted against the library's own transcript rather
    /// than a memory reading, since a slow per-run leak is invisible over the life of a single test:
    /// the interpreter logs <c>Disposing all plugins</c> on a clean shutdown and nothing before the fix.
    /// </remarks>
    [Test]
    public async Task AFinishedSessionIsShutDownRatherThanAbandoned()
    {
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\nA quiet little place.\r\n",
            BannerTail = "By what name do you wish to be known? ",
            WhoReply = "0 Players logged in, 22 record, no maximum.\r\n",
        };

        var transcript = new Transcript();

        var result = await new TelnetProbe(Fast(), transcript).ProbeAsync(game.Target);

        // Confirms the session really happened, so the shutdown assertion below is meaningful.
        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(transcript.Says("All plugins initialized successfully")).IsTrue();

        await Assert.That(transcript.Says("Disposing all plugins")).IsTrue();
        await Assert.That(transcript.Errors).IsEmpty();
    }

    /// <summary>
    /// Everything the probe and the library underneath it wrote down. Log callbacks arrive on the
    /// read loop as well as on the caller's thread, so it locks.
    /// </summary>
    /// <remarks>
    /// Trace is declined deliberately: TelnetNegotiationCore traces every byte with a formatted
    /// message, and every line these assertions read is Debug or above anyway.
    /// </remarks>
    private sealed class Transcript : ILogger
    {
        private readonly List<string> _lines = [];
        private readonly List<string> _errors = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel level) => level >= LogLevel.Debug;

        public void Log<TState>(
            LogLevel level,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level))
            {
                return;
            }

            var line = formatter(state, exception);

            lock (_lines)
            {
                _lines.Add(line);

                if (level >= LogLevel.Error)
                {
                    _errors.Add(line);
                }
            }
        }

        public bool Says(string fragment)
        {
            lock (_lines)
            {
                return _lines.Any(line => line.Contains(fragment, StringComparison.Ordinal));
            }
        }

        public IReadOnlyList<string> Errors
        {
            get
            {
                lock (_lines)
                {
                    return [.. _errors];
                }
            }
        }
    }

    private sealed class FakeGame : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _serving;
        private readonly List<string> _received = [];

        public FakeGame()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _serving = ServeAsync();
        }

        /// <summary>What the server says before it is ready, if anything.</summary>
        public string? Preamble { get; init; }

        /// <summary>How long after the preamble the real screen arrives.</summary>
        public TimeSpan BannerDelay { get; init; }

        public string Banner { get; init; } = string.Empty;

        /// <summary>A last line with no terminator — a hanging prompt, as real servers often send.</summary>
        public string? BannerTail { get; init; }

        public string WhoReply { get; init; } = string.Empty;
        public string? InfoReply { get; init; }
        public string? VersionReply { get; init; }

        /// <summary>The tail of the WHO reply, unterminated.</summary>
        public string? WhoTail { get; init; }

        public string? BlankLineReply { get; init; }

        /// <summary>
        /// Whether an empty line at the name prompt is a goodbye — true for every DIKU descendant,
        /// which is what makes the probe's own flush line fatal to them.
        /// </summary>
        public bool HangsUpOnBlankLine { get; init; }

        /// <summary>Whether the server accepts the connection and drops it without a word.</summary>
        public bool ClosesImmediately { get; init; }

        /// <summary>Whether the server answers <c>WHO</c> and then closes on us.</summary>
        public bool HangsUpAfterWho { get; init; }

        /// <summary>
        /// Whether this server fails to strip telnet negotiation at its login screen, so our IAC
        /// bytes end up prefixed to the next command we type.
        /// </summary>
        public bool SwallowsNegotiationAsText { get; init; }

        public ProbeTarget Target => new(
            IPAddress.Loopback.ToString(),
            ((IPEndPoint)_listener.LocalEndpoint).Port);

        public IReadOnlyList<string> Received
        {
            get
            {
                lock (_received)
                {
                    return [.. _received];
                }
            }
        }

        private async Task ServeAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                await using var stream = client.GetStream();

                if (ClosesImmediately)
                {
                    client.Client.Shutdown(SocketShutdown.Both);
                    return;
                }

                if (Preamble is not null)
                {
                    await SendAsync(stream, Preamble);
                }

                if (BannerDelay > TimeSpan.Zero)
                {
                    await Task.Delay(BannerDelay, _stopping.Token);
                }

                await SendAsync(stream, Banner);
                if (BannerTail is not null)
                {
                    await SendAsync(stream, BannerTail);
                }

                var pending = new StringBuilder();
                var buffer = new byte[4096];

                while (!_stopping.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, _stopping.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    var text = SwallowsNegotiationAsText
                        ? Encoding.Latin1.GetString(buffer, 0, read)
                        : Encoding.Latin1.GetString(StripNegotiation(buffer.AsSpan(0, read)));

                    pending.Append(text);

                    var farewell = false;
                    while (!farewell)
                    {
                        var whole = pending.ToString();
                        var breakAt = whole.IndexOf('\n');
                        if (breakAt < 0)
                        {
                            break;
                        }

                        var line = whole[..breakAt].TrimEnd('\r');
                        pending.Remove(0, breakAt + 1);
                        farewell = !await HandleAsync(stream, line);
                    }

                    if (farewell)
                    {
                        client.Client.Shutdown(SocketShutdown.Both);
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
        }

        /// <summary>Handles one line, and says whether the connection survives it.</summary>
        private async Task<bool> HandleAsync(NetworkStream stream, string line)
        {
            lock (_received)
            {
                _received.Add(line);
            }

            // A line carrying anything the server did not recognise is not a command it has.
            var command = line.Trim();
            var clean = command.All(c => c is >= ' ' and <= '~');

            if (command.Length == 0)
            {
                if (HangsUpOnBlankLine)
                {
                    return false;
                }

                if (BlankLineReply is not null)
                {
                    await SendAsync(stream, BlankLineReply);
                }

                return true;
            }

            if (!clean)
            {
                // What TinyMUSH does: redisplay the connect screen and answer nothing.
                await SendAsync(stream, Banner);
                return true;
            }

            if (command.Equals("WHO", StringComparison.OrdinalIgnoreCase))
            {
                await SendAsync(stream, WhoReply);
                if (WhoTail is not null)
                {
                    await SendAsync(stream, WhoTail);
                }

                return !HangsUpAfterWho;
            }

            if (command.Equals("INFO", StringComparison.OrdinalIgnoreCase))
            {
                if (InfoReply is not null)
                {
                    await SendAsync(stream, InfoReply);
                }

                return true;
            }

            if (command.Equals("VERSION", StringComparison.OrdinalIgnoreCase))
            {
                if (VersionReply is not null)
                {
                    await SendAsync(stream, VersionReply);
                }
            }

            return true;
        }

        private async Task SendAsync(NetworkStream stream, string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            var bytes = Encoding.Latin1.GetBytes(text);
            await stream.WriteAsync(bytes, _stopping.Token);
            await stream.FlushAsync(_stopping.Token);
        }

        /// <summary>What a server that does implement telnet does with an option request.</summary>
        private static byte[] StripNegotiation(ReadOnlySpan<byte> data)
        {
            var kept = new List<byte>(data.Length);
            for (var i = 0; i < data.Length; i++)
            {
                if (data[i] != 255)
                {
                    kept.Add(data[i]);
                    continue;
                }

                // IAC WILL/WONT/DO/DONT <option> is three bytes; anything else, skip two.
                i += i + 1 < data.Length && data[i + 1] is >= 251 and <= 254 ? 2 : 1;
            }

            return [.. kept];
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();

            try
            {
                await _serving;
            }
            catch (OperationCanceledException)
            {
            }

            _stopping.Dispose();
        }
    }
}
