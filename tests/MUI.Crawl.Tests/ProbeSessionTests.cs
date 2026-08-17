using System.Net;
using System.Net.Sockets;
using System.Text;
using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// The probe driven end to end against a server in this process.
/// </summary>
/// <remarks>
/// <para>
/// These pin the parts of a session that no fixture can reach, because they are properties of the
/// conversation rather than of any one string: that a line the server never terminated is still
/// delivered, that the banner and the <c>WHO</c> answer stay separate evidence, and that what the
/// server says in reply to the probe's own bookkeeping is counted as neither.
/// </para>
/// <para>
/// A socket rather than a mock, deliberately — the behaviour under test is TelnetNegotiationCore's
/// line assembly meeting real timing, and a fake that hands the probe pre-split lines would agree
/// with the code while the wire disagreed.
/// </para>
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
        // The defect this closes. TelnetNegotiationCore submits a line only when a newline arrives,
        // so a trailing partial line sits in its buffer for ever and OnSubmit never fires for it.
        // Unterminated output is normal in this hobby: five of the twelve reference servers end
        // their connect screen or their WHO reply with a hanging prompt — aardmud.org:4000 with
        // "What be thy name, adventurer?", realms.reichel.net:4000 with "By what name do you wish
        // to be known?", resort.org:2323 with "Please enter a name:" on both.
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
    /// Measured on <c>mud.pkuxkx.net:8080</c>, which offers CHARSET, answers the REQUEST with a list
    /// containing UTF-8 and nothing else, accepts our ACCEPTED, and then sends its whole connect
    /// screen in GBK — the encoding on that game is chosen from a menu on the login screen
    /// (<c>Input 1 for GBK, 2 for UTF8, 3 for BIG5</c>) rather than from the option just negotiated.
    /// Neither side negotiated anything wrongly. What was wrong was decoding on the strength of it.
    /// </remarks>
    [Test]
    public async Task AServerThatDeclaresOneEncodingAndSendsAnotherIsNotDestroyedOnTheWayIn()
    {
        // FakeGame writes Latin-1, so a string of U+00xx puts these exact bytes on the wire. This is
        // the game's own name, "北大侠客行", as GBK.
        const string titleInGbk = "\u00b1\u00b1  \u00b4\u00f3  \u00cf\u00c0  \u00bf\u00cd  \u00d0\u00d0";

        // Two servers rather than two probes of one, because FakeGame serves a single connection.
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

        // Nothing lost. The old code decoded on the negotiated encoding with a replacing fallback,
        // and every one of these bytes came back as U+FFFD — permanently, because the bytes are gone
        // by the time anything downstream sees the string.
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
        // Measured on game.mortalrealms.com:4321 (MrMud 1.4), and on five of fourteen live servers
        // sampled beside it — alteraeon, tbamud, ageofinsanity, addictmud and avatar.outland. Every
        // DIKU descendant reads an empty line at "Who art thou:" as a goodbye and closes, so the
        // probe's own flush line ends the session and the WHO after it writes into a dead socket.
        //
        // The banner is already in hand when that happens. Reporting the whole probe as Failed threw
        // it away and wrote the game down as unreachable, which is our flush line recorded as a fact
        // about their server — the thing CLAUDE.md's fifth rule forbids. The server answered.
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
        // A count is the most expensive thing a probe collects, so the phase boundary that delimits
        // it must be recorded before anything that can throw. It is: SettleAsync only awaits a
        // timer, and the flush feeds the interpreter a local byte, so a peer that goes away is not
        // heard from until the *next* send — by which point whoLines is already committed and
        // Live() declines to make that send at all.
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
        // The other side of the line above: no banner, no negotiation, nothing measured. Carrying
        // evidence forward must not turn an empty session into an answered one.
        await using var game = new FakeGame { ClosesImmediately = true };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Failed);
        await Assert.That(result.Banner).IsNull();
    }

    [Test]
    public async Task AnUnterminatedWhoReplyIsReadRatherThanLost()
    {
        // chaos.caile.org:4444's shape, with the summary line left hanging. A count that only
        // arrives when the server happens to add a newline is a count we lose at random.
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
        // Layer 2 is a display asset and a codebase fingerprint; layer 3 is a measurement. Merging
        // them would let connect-screen prose reach a parser whose job is to count people, and let
        // a WHO listing into a banner hash that is supposed to identify the game.
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
        // The probe sends a bare line terminator between the banner and the WHO, to clear its own
        // IAC DO bytes out of a server that buffered them as text. Whatever that produces is a
        // reaction to a byte sequence we chose to send, so it is neither the game's connect screen
        // nor its answer to WHO — recording it as either would be recording a decision of ours as a
        // measurement of theirs.
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
    /// <b>Measured on three live games.</b> tharel.net:5005 stored 23 characters —
    /// <c>Do you want ANSI? (Y/n)</c> — as its connect screen, and one Return produces 3033;
    /// mdhoria.net:6996 stored 101 and has 2826 behind it; cleftofdimension.com:9000 stored 29 and
    /// has 2197. Seventeen active games gate their whole screen behind that keystroke, and the probe
    /// was already sending it and throwing the answer away.
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

        // And the WHO window still begins after all of it, so the game's refusal is read as the
        // refusal it is rather than as part of its connect screen.
        await Assert.That(result.Banner).DoesNotContain("Illegal name");
        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.LoginPrompt);
    }

    [Test]
    public async Task AServerThatSimplyRepaintsIsStillCountedAsNeither()
    {
        // The negative that pays for the case above. A screen that has already painted is not a
        // gate however it ends, so what a stray Return produces stays discarded — which on a
        // TinyMUSH is the screen repeated and on a DIKU is a goodbye.
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
        // chaos.caile.org:4444 (TinyMUSH) exactly: it does not parse telnet at its login screen, so
        // IAC DO 70 lands in its line buffer and the next line it reads is "\xff\xfd\x46WHO", which
        // is not a command it has. Before the flush the probe reported Unknown for a game that
        // answers WHO perfectly well.
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
        // The restraint test with a wire under it. Everything the probe types must be the permitted
        // command or an empty line; nothing that logs in, creates or changes anything — and, since
        // the plaintext form belongs upstream rather than here, nothing that a login screen would
        // read as a character name.
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
        // Task 4's point, measured rather than asserted about the options record: a server that
        // answers immediately must not be charged for the slow case.
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\n",
            WhoReply = "There are 2 players connected.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Who.Count).IsEqualTo(2);
        await Assert.That(result.Elapsed).IsLessThan(TimeSpan.FromSeconds(3));
    }

    /// <summary>A MU* server, to the extent the probe can tell.</summary>
    /// <summary>
    /// A server that says it is not ready, then pauses for longer than a gap between lines.
    /// </summary>
    /// <remarks>
    /// tbaMUD's shape, measured on <c>tbamud.com:4000</c>: <c>Attempting to Detect Client, Please
    /// Wait...</c>, then about a second and a half of silence, then the real screen. Under a plain
    /// quiet period the placeholder <em>was</em> the connect screen — it became the stored banner and
    /// its hash became the game's identity, so two unrelated tbaMUDs fingerprinted alike and the
    /// second went into a duplicate review instead of onto the site.
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
    /// This is the cost side of the fix. Every probe pays it if the condition is wrong, and a crawler
    /// spending two extra seconds per game is a different program.
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
    /// <para>
    /// <b>This shipped, and it is the fifth rule's worst case: our own outage published as theirs.</b>
    /// The caller's token is linked into this probe's budget, so by the time an exception reaches the
    /// bottom of <c>ProbeAsync</c> a stopping host and an expired budget look identical — both are an
    /// <see cref="OperationCanceledException"/>, and <c>Classify</c> read either as
    /// <c>("timeout", "probe budget exhausted")</c>. Returning that as a <see cref="ProbeResult"/>
    /// handed <c>CrawlCycle</c> something it had every reason to store.
    /// </para>
    /// <para>
    /// On the one deployment, a crash loop in an unrelated background service stopped the host every
    /// ninety seconds. One cycle's worth of in-flight probes — <b>twenty games in fifty-six
    /// seconds</b> — were written down as unreachable with cause <c>timeout</c>, and the failure
    /// backoff then held each of them at that verdict for six hours. Fourteen of fifteen spot-checked
    /// afterwards answered a socket from that same host in under a second.
    /// </para>
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

        // Thrown, not returned. VisitAsync already knows what a cancelled cycle means and writes
        // nothing for it; a ProbeResult is a measurement and there was none to make.
        await Assert.That(async () => await new TelnetProbe(Fast()).ProbeAsync(game.Target, stopping.Token))
            .Throws<OperationCanceledException>();
    }

    /// <summary>
    /// And the probe's <em>own</em> budget expiring is still a timeout we measured, which is the
    /// distinction the fix turns on rather than a case it suppresses.
    /// </summary>
    /// <remarks>
    /// A game that accepts a connection and then says nothing until our ceiling runs out has been
    /// measured: we dialled it, we waited, and it did not finish. That is a fact about the far end
    /// and it belongs in the record — the bug was never that timeouts are recorded, it was that our
    /// shutdown was dressed as one.
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

        /// <summary>
        /// What the server says before it is ready, if anything — tbaMUD's "Attempting to Detect
        /// Client, Please Wait...".
        /// </summary>
        public string? Preamble { get; init; }

        /// <summary>How long after the preamble the real screen arrives.</summary>
        public TimeSpan BannerDelay { get; init; }

        public string Banner { get; init; } = string.Empty;

        /// <summary>A last line with no terminator — a hanging prompt, as five of twelve real servers send.</summary>
        public string? BannerTail { get; init; }

        public string WhoReply { get; init; } = string.Empty;
        public string? InfoReply { get; init; }
        public string? VersionReply { get; init; }

        /// <summary>The tail of the WHO reply, unterminated.</summary>
        public string? WhoTail { get; init; }

        public string? BlankLineReply { get; init; }

        /// <summary>
        /// Whether an empty line at the name prompt is a goodbye. Every DIKU descendant reads one
        /// that way, which is what makes the probe's own flush line fatal to them.
        /// </summary>
        public bool HangsUpOnBlankLine { get; init; }

        /// <summary>Whether the server accepts the connection and drops it without a word.</summary>
        public bool ClosesImmediately { get; init; }

        /// <summary>Whether the server answers <c>WHO</c> and then closes on us.</summary>
        public bool HangsUpAfterWho { get; init; }

        /// <summary>
        /// Whether this server fails to strip telnet negotiation at its login screen, so our IAC
        /// bytes end up prefixed to the next command we type. TinyMUSH does exactly this.
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

            // A line carrying anything the server did not recognise — including our negotiation
            // bytes, when it is the kind of server that swallows them — is not a command it has.
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
