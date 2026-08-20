using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TelnetNegotiationCore.Builders;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Models;
using TelnetNegotiationCore.Protocols;

namespace MUI.Crawl;

/// <summary>
/// One telnet session against one game, yielding a <see cref="ProbeResult"/>.
/// </summary>
/// <remarks>
/// Never authenticates — everything read is what a server hands an anonymous connection (banner,
/// negotiated options, MSSP, pre-login commands). Two things go on the wire and both are bounded by
/// test: <see cref="PermittedCommands"/>, the commands it may ask, which <c>connect</c> and
/// <c>create</c> are not on; and a classified answer to a pre-login prompt, which is not a command and
/// is held to <see cref="IsPermittedPromptAnswer"/> — at most two alphanumeric characters, checked at
/// the send rather than trusted from the classifier.
/// Protocol support is recorded from the library's own negotiation callbacks rather than by
/// re-parsing bytes it already decoded.
/// </remarks>
public sealed class TelnetProbe(ProbeOptions? options = null, ILogger? logger = null) : IProbe
{
    /// <summary>The login-screen commands this probe is allowed to ask.</summary>
    public const string WhoCommand = "WHO";
    public const string InfoCommand = "INFO";
    public const string VersionCommand = "VERSION";

    /// <summary>
    /// Every command this probe is allowed to send. Anything that logs in, creates a character, or
    /// changes server state is absent by construction.
    /// </summary>
    /// <remarks>
    /// The bare line terminator sent between the banner and <c>WHO</c> is not on this list — it's not
    /// a command, it carries no text. <c>MSSP-REQUEST</c> is also absent by design (see
    /// <see cref="ProbeOptions"/>): MSSP is asked for by negotiation, which a server without it ignores.
    /// The other thing that goes on the wire is a classified prompt answer, which is not a command
    /// either and is bounded separately by <see cref="IsPermittedPromptAnswer"/>.
    /// </remarks>
    public static readonly IReadOnlyList<string> PermittedCommands = [WhoCommand, InfoCommand, VersionCommand];

    /// <summary>
    /// The longest a classified prompt answer may be. Two characters covers every answer
    /// <see cref="LoginPromptGate"/> can produce — <c>y</c>, <c>no</c>, and a one- or two-character
    /// menu token — and is far short of anything that could log in.
    /// </summary>
    public const int LongestPromptAnswer = 2;

    /// <summary>
    /// Whether text is a prompt answer this probe may send, as opposed to a command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enforced at the send, not merely described here, so the guarantee survives a category being
    /// added to <see cref="LoginPromptGate"/> later: an answer that fails this is dropped and the
    /// round ends rather than putting unvetted text on somebody's login screen.
    /// </para>
    /// <para>
    /// The bound is what makes the answers safe to type at a prompt whose meaning we inferred. Two
    /// alphanumeric characters cannot be <c>connect</c>, <c>create</c> or a password; the worst a
    /// misclassification can do is offer a one-letter character name, which no server accepts as a
    /// login and which is the same exposure the bare line terminator already carried.
    /// </para>
    /// </remarks>
    public static bool IsPermittedPromptAnswer(string? answer) =>
        string.IsNullOrEmpty(answer)
        || (answer.Length <= LongestPromptAnswer && answer.All(char.IsLetterOrDigit));

    private const byte NewLine = (byte)'\n';

    private readonly ProbeOptions _options = options ?? new ProbeOptions();
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public async Task<ProbeResult> ProbeAsync(ProbeTarget target, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var observedAt = DateTimeOffset.UtcNow;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.Timeout);

        // Bytes, not text, and the whole reason is WireEncoding: which encoding these are in cannot
        // be known from one line, and reading them wrongly is not recoverable. They are decoded once
        // at the end of the session, when there is a whole screen to test.
        var lines = new List<byte[]>();
        var seen = new Observations();

        using var client = new TcpClient();

        try
        {
            // The vetted addresses when the caller has them, the name otherwise. See
            // ProbeTarget.Addresses for why re-resolving a name the scope guard already ruled on is
            // both a hole in the guard and a second chance to fail on a transient lookup.
            await (target.Addresses.Count > 0
                ? client.ConnectAsync([.. target.Addresses], target.Port, budget.Token)
                : client.ConnectAsync(target.Host, target.Port, budget.Token));

            var built = await Build(seen, lines).BuildAndStartAsync(client, budget.Token);

            // Must be disposed *before* the socket it reads. The interpreter owns a byte channel, the
            // draining task, and every plugin — MCCP's holds zlib streams that nothing else reclaims,
            // and an undisposed interpreter per probe was measured leaking megabytes an hour.
            // Declared here (not beside `client`, which is disposed after this method returns) so the
            // interpreter — still doing work during its own teardown — goes first, before the
            // transport it depends on is torn down under it.
            await using var telnet = built.Interpreter;

            // Observed rather than awaited — on a server that just sits there (most of them), the
            // loop only ends when `client` is disposed on the way out of this method, so awaiting it
            // here would wait on our own exit. Not dropped entirely either: a defect in the
            // interpreter would otherwise look identical to an ordinary socket teardown from out
            // here, so it's logged if it's not one of the expected HungUp shapes.
            _ = ObserveReadLoopAsync(built.ReadTask, target);

            int Arrived()
            {
                lock (lines)
                {
                    return lines.Count;
                }
            }

            // Phase 1 — the connect screen. Banner and WHO answer are kept apart because they are
            // different evidence: one is a display asset and codebase fingerprint, the other is a
            // measurement.
            await SettleAsync(telnet, Arrived, 0, _options.SilenceGrace, budget.Token);

            // A screen that has told us it is not ready has not settled, it has paused. Waiting once
            // more is conditional on there being almost nothing there, so a server that has already
            // painted pays nothing — see ProbeOptions.BannerPatience for the server this is measured
            // against and for what recording the placeholder cost.
            if (LooksUnfinished(BannerSoFar(lines, 0, Arrived())))
            {
                await SettleAsync(telnet, Arrived, Arrived(), _options.BannerPatience, budget.Token);
            }

            // Some connect screens are not the screen at all, but one or more questions the server
            // asks before it paints one — colour, a press-enter gate, an age check. Each round
            // classifies whatever newly arrived since the last answer and sends the specific reply
            // LoginPromptGate says resolves it, then settles again; bounded by MaxPromptRounds so a
            // misread screen cannot spin the probe against itself. A blind Return sent unconditionally
            // here — the whole of what this loop replaces — was never enough: several real games only
            // accept an explicit letter, and a server that did not recognise a blind Return simply
            // re-printed the same question, which a probe run before this fix stored as the connect
            // screen. LoginPromptCategory.WhoMenu is excluded from this loop on purpose — it is
            // answered once, separately, against the settled screen below rather than as one more round
            // here (see the block after bannerLines is taken).
            var roundStart = 0;
            var answeredAPrompt = false;
            for (var round = 0; round < _options.MaxPromptRounds; round++)
            {
                if (LoginPromptGate.Classify(BannerSoFar(lines, roundStart, Arrived()))
                        is not { Category: not LoginPromptCategory.WhoMenu } prompt
                    || !IsPermittedPromptAnswer(prompt.Answer)
                    || !Live(client))
                {
                    break;
                }

                roundStart = Arrived();
                answeredAPrompt = true;
                await telnet.SendAsync(Encoding.ASCII.GetBytes(prompt.Answer));
                await SettleAsync(telnet, Arrived, roundStart, _options.QuietPeriod, budget.Token);
            }

            var bannerLines = Arrived();

            // A who's-online menu option is different from every category the loop above answers:
            // selecting it doesn't reveal a second screen behind this one — for every real game
            // measured (BatMUD, ZombieMUD, discworld.starturtle.net) the menu already settled into
            // bannerLines above *is* the game's permanent connect screen. So it is classified once here
            // rather than as one more round of that loop, and its own answer/reply are kept out of
            // Banner entirely, the same way the ordinary WHO phase below never becomes part of it —
            // parsed through the identical WhoParser a literal WHO answer would be, so
            // PresenceChoice.From (spec §5.2) cannot tell which route produced the reading.
            WhoReading? whoFromMenu = null;
            string? whoFromMenuShape = null;
            if (Live(client)
                && LoginPromptGate.Classify(BannerSoFar(lines, 0, bannerLines))
                    is { Category: LoginPromptCategory.WhoMenu } menu
                && IsPermittedPromptAnswer(menu.Answer))
            {
                var menuBaseline = Arrived();
                await telnet.SendAsync(Encoding.ASCII.GetBytes(menu.Answer));
                await SettleAsync(telnet, Arrived, menuBaseline, _options.QuietPeriod, budget.Token);

                var menuReply = BannerSoFar(lines, menuBaseline, Arrived());
                whoFromMenu = new WhoParser().Parse(menuReply);
                whoFromMenuShape = PayloadRedaction.Replayable(menuReply);
            }

            // Phase 2 — an empty line, and everything it produces is thrown away.
            //
            // A server that does not implement telnet at its login screen does not recognise our own
            // IAC DO negotiation bytes as telnet: it takes them as typing and leaves them in its line
            // buffer, so the next thing we send is read as garbage-prefixed and not as WHO — the
            // count is lost. A bare terminator flushes that residue as its own line first. What comes
            // back is a reaction to bytes *we* sent, not the game's connect screen or its WHO answer,
            // so it must not be recorded as either (rule 5) — dropped deliberately, which is what the
            // gap between bannerLines and flushLines is.
            //
            // This also ends the session outright on every DIKU descendant, which reads an empty line
            // at its name prompt as a goodbye — see HungUp for what that costs.
            var flushLines = bannerLines;
            var whoLines = bannerLines;
            var infoLines = bannerLines;
            var versionLines = bannerLines;
            var asked = whoFromMenu is not null;

            // Phases 3 to 5 — the questions we are allowed to ask, in order. SendAsync appends the
            // line ending itself, so each command is handed over bare.
            //
            // `sent` fires between the write and the wait: a question is asked when its bytes have
            // gone, not when we decided to ask it, so a flag set before the send could claim to have
            // asked something that never left.
            async Task<int> AskAsync(string command, int baseline, TimeSpan grace, Action? sent = null)
            {
                await telnet.SendAsync(Encoding.ASCII.GetBytes(command));
                sent?.Invoke();
                await SettleAsync(telnet, Arrived, baseline, grace, budget.Token);
                return Arrived();
            }

            try
            {
                // MSDP's request vocabulary — SEND, REPORT, LIST, RESET, UNREPORT — has no plaintext
                // form, so it is not on PermittedCommands for the same reason MSSP-REQUEST is not: it
                // is asked for by protocol, not by typing. Gated on TelnetNegotiationCore 2.9.0's
                // IsNegotiated (see Watched.Msdp), which reflects the peer's real WILL/DO acceptance —
                // unlike the pre-2.9.0 OnEnabledAsync, which was true from plugin construction
                // regardless of the wire (TelnetNegotiationCore#85). By this point in the probe the
                // banner phase above has already given negotiation time to settle, so a server that
                // never agreed to MSDP is not asked at all: no bytes go to a peer that said nothing
                // about this option, one connect screen fewer that might read a subnegotiation as
                // literal typing. PLAYERS is MSDP's conventional variable name for a player count; see
                // docs/codebase-survey-2026-07-30.md for what asking real servers for it found — every
                // server tested answered with an unsolicited SERVER_ID instead, never PLAYERS.
                if (telnet.PluginManager?.GetPlugin<MSDPProtocol>() is { IsNegotiated: true })
                {
                    await telnet.SendMSDPCommand("SEND", "PLAYERS");
                }

                // …unless a prompt above was already answered, in which case that answer was itself a
                // line through the server's buffer and has already flushed whatever residue this
                // exists to clear. Sending a second, empty one buys nothing — and costs a count, since
                // a game that gated its screen behind a question is sitting at its *name* prompt by
                // now, where a DIKU reads an empty line as a goodbye. Measured on
                // mud.arcadia.net:4000: the flush ended the session and WHO was never asked, where
                // before the prompt loop existed the flush had been the answer to the colour question
                // and the session survived it.
                if (!answeredAPrompt)
                {
                    await telnet.SendAsync([]);
                    await SettleAsync(telnet, Arrived, bannerLines, _options.QuietPeriod, budget.Token);
                }

                flushLines = whoLines = infoLines = versionLines = Arrived();

                // Asking a socket that has already been closed is not asking. A write to a peer that
                // sent FIN still succeeds — only the write *after* the RST throws — so an unguarded
                // WHO here would come back unanswered and be recorded as unreadable, publishing our
                // own dead socket as a fact about their parser (rule 5).
                //
                // A who's-online menu already answered this probe's WHO question — asking the literal
                // word WHO at whatever this game's screen looks like now would either repeat the menu
                // or be read as a character name, corrupting a good reading with a worse one.
                if (Live(client) && whoFromMenu is null)
                {
                    // WhoGrace, not SilenceGrace: some codebases sit on a login-screen WHO for
                    // seconds on purpose, and giving up early does not merely lose the count — it
                    // sends INFO while the roster is still in flight, so the roster lands in INFO's
                    // window and is recorded as the game's INFO block.
                    whoLines = infoLines = versionLines =
                        await AskAsync(WhoCommand, flushLines, _options.WhoGrace, () => asked = true);
                }

                if (Live(client))
                {
                    infoLines = versionLines =
                        await AskAsync(InfoCommand, whoLines, _options.SilenceGrace);
                }

                if (Live(client))
                {
                    versionLines = await AskAsync(VersionCommand, infoLines, _options.SilenceGrace);
                }
            }
            catch (Exception error) when (HungUp(error) && Measured(lines, bannerLines, seen))
            {
                // The server said its piece and then dropped us — a fact about the session, not the
                // host. Everything already measured (connect screen, handshake, MSSP) stays; failing
                // the whole probe here would record an answering game as unreachable (rule 5). Phases
                // never reached keep their pre-loop counts, yielding nothing rather than an empty
                // answer, and `asked` keeps WHO honest about which.
                _logger.LogDebug(
                    "{Host}:{Port} closed the session after its connect screen ({Error}); keeping what it said",
                    target.Host, target.Port, error.Message);
            }

            // One decision, over the whole session, taken here because here is the first moment
            // there is a whole session to decide from — and MSSP is part of that session, not a
            // second one. A game whose connect screen is ASCII and whose name is GBK says so only in
            // its report, so the report's bytes decide alongside the screen's.
            WireReading reading;
            lock (lines)
            {
                reading = WireEncoding.Read(lines, target.Charset, MsspReport.RawValues(seen.Mssp));
            }

            var read = reading.Lines;
            var banner = string.Join("\n", read.Take(bannerLines));
            var whoText = string.Join("\n", read.Skip(flushLines).Take(whoLines - flushLines));
            var infoText = string.Join("\n", read.Skip(whoLines).Take(infoLines - whoLines));
            var versionText = string.Join("\n", read.Skip(infoLines).Take(versionLines - infoLines));

            if (telnet.CurrentEncoding is not null)
            {
                seen.Charset ??= telnet.CurrentEncoding.WebName;
            }

            var viaOption = seen.MsspOutcome is MsspOutcome.Received;

            return new ProbeResult
            {
                Host = target.Host,
                Port = target.Port,
                ObservedAt = observedAt,
                Outcome = ProbeOutcome.Answered,
                OfferedOptions = seen.Supported,
                Negotiation = seen.ToNegotiation(),
                ReadAs = reading.Charset,
                CharsetSource = reading.Source,
                Banner = banner,

                // Every phase, not just the banner: a server may answer the connect screen in plain
                // text and then mark up its WHO table, and one occurrence anywhere is the same fact.
                MxpObserved = MxpSignal.IsPresent(banner)
                    || MxpSignal.IsPresent(whoText)
                    || MxpSignal.IsPresent(infoText)
                    || MxpSignal.IsPresent(versionText),
                Who = whoFromMenu ?? (asked ? new WhoParser().Parse(whoText) : WhoReading.NotAsked),
                WhoShape = whoFromMenu is not null
                    ? whoFromMenuShape
                    : (asked ? PayloadRedaction.Replayable(whoText) : null),
                Info = infoText.Length == 0 ? null : infoText,
                Version = versionText.Length == 0 ? null : versionText,
                BannerPlayerCount = BannerCount.Find(banner),
                Mssp = viaOption ? MsspReport.From(seen.Mssp, reading.Encoding) : MsspReport.Empty,
                MsspOutcome = seen.MsspOutcome,
                MsspBytesRejected = seen.MsspRejectedBytes,
                MsspTransport = viaOption ? MsspTransport.TelnetOption70 : MsspTransport.None,
                Elapsed = Stopwatch.GetElapsedTime(started),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Our caller going away is not a measurement of anything (rule 5). `budget` links the
            // caller's token to this probe's own timeout, so by the time the exception arrives both
            // look like the same OperationCanceledException — but returning Failed here would record
            // a false timeout against every game a killed crawl cycle happened to be dialling.
            // Rethrowing lets VisitAsync's own cancellation check write nothing and leave the target
            // due. Ask the token which one cancelled, never the exception.
            throw;
        }
        catch (Exception error)
        {
            return new ProbeResult
            {
                Host = target.Host,
                Port = target.Port,
                ObservedAt = observedAt,
                Outcome = ProbeOutcome.Failed,
                OfferedOptions = seen.Supported,
                Negotiation = seen.ToNegotiation(),
                Failure = DialFailure.Classify(error),
                Elapsed = Stopwatch.GetElapsedTime(started),
            };
        }
    }

    /// <summary>
    /// Waits for the interpreter's network read loop to end, so that a fault in it is written down
    /// somewhere instead of being collected in silence.
    /// </summary>
    /// <remarks>
    /// Fire-and-forget on purpose and outlives <c>ProbeAsync</c> — the loop only ends once the socket
    /// goes, by which point the measurement is already made and returned; nothing it finds can change
    /// a <see cref="ProbeResult"/>. The ordinary ending is one of the <see cref="HungUp"/> shapes or
    /// the probe's budget being cancelled; anything else is a defect, logged at debug.
    /// </remarks>
    private async Task ObserveReadLoopAsync(Task readLoop, ProbeTarget target)
    {
        try
        {
            await readLoop;
        }
        catch (Exception error) when (HungUp(error) || error is OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            _logger.LogDebug(
                error,
                "{Host}:{Port} read loop ended badly after the session was over",
                target.Host, target.Port);
        }
    }

    /// <summary>Whether the far end is still there to be asked anything.</summary>
    /// <remarks>
    /// A readable socket with nothing to read on it is one the peer has closed — TCP accepts the
    /// first write after a FIN silently, so this is the only way to tell beforehand. Called between
    /// phases, after the interpreter's own loop has drained every pending line, so "readable" here
    /// means the close.
    /// </remarks>
    private static bool Live(TcpClient client)
    {
        try
        {
            return !client.Client.Poll(0, SelectMode.SelectRead) || client.Client.Available > 0;
        }
        catch (Exception error) when (error is SocketException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Whether an exception is the far end having gone, rather than us having given up.</summary>
    /// <remarks>
    /// Deliberately not <see cref="OperationCanceledException"/>, which is the probe budget expiring
    /// and belongs on the failure path instead (spec §5.3) — a closed socket means the session is
    /// over, not overrunning. <see cref="ObjectDisposedException"/> is included because a write racing
    /// the interpreter's transport teardown surfaces as a disposal rather than a socket error.
    /// </remarks>
    private static bool HungUp(Exception error) => error
        is IOException
        or SocketException
        or ObjectDisposedException;

    /// <summary>
    /// Whether the session yielded anything before it ended.
    /// </summary>
    /// <remarks>
    /// A hang-up is not blanket amnesty: a host that accepts a connection and drops it without a word
    /// has told us nothing, and calling that <c>Answered</c> would fabricate a measurement out of a
    /// bare TCP handshake.
    /// </remarks>
    private static bool Measured(List<byte[]> lines, int bannerLines, Observations seen)
    {
        if (seen.Supported.Count > 0)
        {
            return true;
        }

        lock (lines)
        {
            return bannerLines > 0 && lines.Take(bannerLines).Any(line => line.Length > 0);
        }
    }

    /// <summary>The connect screen as it stands part-way through the phase that is collecting it.</summary>
    /// <remarks>
    /// Read with the Latin-1 fallback, not the session's eventual encoding — there isn't one yet, and
    /// this text is never shown to anyone. Its readers (<see cref="LooksUnfinished"/>,
    /// <see cref="LoginPromptGate.Classify"/>) only check length, punctuation and a handful of
    /// vocabulary words, none of which an 8-bit byte changes the answer to. The real decoding decision
    /// happens once, at the end, in <see cref="WireEncoding.Read"/>.
    /// </remarks>
    private static string BannerSoFar(List<byte[]> lines, int from, int to)
    {
        lock (lines)
        {
            return string.Join(
                "\n", lines.Skip(from).Take(to - from).Select(WireEncoding.Fallback.GetString));
        }
    }

    /// <summary>
    /// Whether what we have is a screen that has not finished arriving.
    /// </summary>
    /// <remarks>
    /// Both conditions are needed: short (measured on flattened text, so a screenful of colour codes
    /// carrying one word counts as one word) and not already at a prompt — a screen ending in
    /// <c>Please enter a name:</c> is waiting for us, not still arriving.
    /// </remarks>
    private bool LooksUnfinished(string banner)
    {
        var text = BannerText.Flatten(banner);

        return text.Length <= _options.SlightBannerLength
            && (text.Length == 0 || text[^1] is not (':' or '?' or '>' or '#'));
    }

    /// <summary>
    /// Waits for the server to stop talking, then flushes any line it left unterminated.
    /// </summary>
    /// <param name="telnet">The live interpreter, so the flush goes through its own line assembly.</param>
    /// <param name="arrived">How many lines have arrived in total, so far.</param>
    /// <param name="baseline">How many had arrived when this phase began.</param>
    /// <param name="grace">How long to wait for this phase to say anything at all.</param>
    /// <param name="cancellationToken">The probe's overall budget.</param>
    /// <remarks>
    /// A phase ends when nothing new has arrived for <see cref="ProbeOptions.QuietPeriod"/>, bounded
    /// above by <see cref="ProbeOptions.MaxPhase"/> so a server that never stops talking can't stall
    /// the crawl. Silence from the start of a phase gets a separate, longer budget (<c>grace</c>) than
    /// a gap between lines — the answer may still be in flight over a slow link.
    /// </remarks>
    private async Task SettleAsync(
        TelnetInterpreter telnet,
        Func<int> arrived,
        int baseline,
        TimeSpan grace,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _options.MaxPhase;
        var seen = arrived() - baseline;
        var lastChange = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(_options.PollInterval, cancellationToken);

            var now = arrived() - baseline;
            if (now != seen)
            {
                seen = now;
                lastChange = DateTime.UtcNow;
                continue;
            }

            if (DateTime.UtcNow - lastChange >= (seen == 0 ? grace : _options.QuietPeriod))
            {
                break;
            }
        }

        await FlushPendingLineAsync(telnet);
    }

    /// <summary>
    /// Delivers a line the server never terminated, by feeding the interpreter the newline it is
    /// waiting for.
    /// </summary>
    /// <remarks>
    /// Unterminated output is normal in this hobby, and a line-oriented callback loses it
    /// systematically: TelnetNegotiationCore submits a line only on a newline, so a trailing partial
    /// line (often a login prompt like <c>Name:</c> with no line ending) sits in the buffer until the
    /// connection closes and never fires. Losing it is not cosmetic — the guard that stops a busy DIKU
    /// being read as a measured zero depends on recognising exactly that kind of unterminated prompt.
    /// The newline goes in through <c>InterpretAsync</c>, the same channel the read loop feeds, so
    /// nothing goes on the wire and line assembly/IAC/encoding stay the library's; when the buffer is
    /// empty the library discards it and no line is produced.
    /// </remarks>
    private static async Task FlushPendingLineAsync(TelnetInterpreter telnet)
    {
        await telnet.InterpretAsync(NewLine);
        await telnet.WaitForProcessingAsync(maxWaitMs: 500, additionalDelayMs: 25);
    }

    /// <summary>
    /// Registers every protocol worth observing and hooks the callback that proves it engaged.
    /// </summary>
    /// <remarks>
    /// Each plugin is here to be <em>watched</em>, not used — this client renders nothing, so MXP and
    /// MCCP buy it only the knowledge that a server offers them. A protocol is recorded from inside
    /// its own callback, which is the event that proves it ran; <see cref="Watched"/>'s
    /// <c>OnEnabledAsync</c> overrides are a second, cheaper signal that was measured not firing on
    /// its own for every protocol, so both routes are kept.
    /// </remarks>
    private TelnetInterpreterBuilder Build(Observations seen, List<byte[]> lines)
    {
        void Note(string protocol) => seen.Note(protocol);

        var mssp = new Watched.Mssp(Note);
        mssp.WithMaxMessageSize(_options.MaxSubnegotiationBytes);
        mssp.OnMSSP(config =>
        {
            seen.Mssp = config;
            seen.MsspOutcome = MsspOutcome.Received;
            Note("MSSP");
            return ValueTask.CompletedTask;
        });
        // 2.7.0 drops an oversized report whole rather than truncating it. Kept as its own outcome:
        // we asked, they answered, we declined to hold it — which is not "no MSSP".
        mssp.OnMSSPMessageTooLarge(sizes =>
        {
            seen.MsspOutcome = MsspOutcome.RejectedTooLarge;
            seen.MsspRejectedBytes = (int)Math.Min(sizes.Item1, int.MaxValue);
            Note("MSSP");
            return ValueTask.CompletedTask;
        });

        var gmcp = new Watched.Gmcp(Note);
        gmcp.WithMaxMessageSize(_options.MaxSubnegotiationBytes);
        gmcp.OnGMCPMessage(message =>
        {
            seen.Gmcp(message.Item1);
            Note("GMCP");
            return ValueTask.CompletedTask;
        });

        var msdp = new Watched.Msdp(Note);
        msdp.WithMaxMessageSize(_options.MaxSubnegotiationBytes);
        msdp.OnMSDPMessage((_, message) =>
        {
            seen.Msdp(message);
            Note("MSDP");
            return ValueTask.CompletedTask;
        });

        var newEnviron = new Watched.NewEnviron(Note);
        newEnviron.OnEnvironmentVariables((requested, _) =>
        {
            seen.Environment(requested.Keys);
            Note("NEW-ENVIRON");
            return ValueTask.CompletedTask;
        });

        var mccp = new Watched.Mccp(Note);
        mccp.OnCompressionEnabled((version, _) =>
        {
            seen.CompressionVersion = version;
            Note($"MCCP{version}");
            return ValueTask.CompletedTask;
        });

        var eor = new Watched.Eor(Note);
        eor.OnPrompt(() =>
        {
            seen.Prompts = true;
            Note("EOR");
            return ValueTask.CompletedTask;
        });

        // CharsetProtocol takes its order as a property rather than a builder call, and exposes
        // OnCharsetChange on the instance — so the settled encoding is captured here rather than
        // read off the interpreter afterwards, where a default is indistinguishable from a result.
        var charset = new Watched.Charset(Note) { CharsetOrder = [Encoding.UTF8, Encoding.Latin1] };
        charset.OnCharsetChange(encoding =>
        {
            seen.Charset = encoding.WebName;
            seen.CharsetNegotiated = true;
            Note("CHARSET");
            return ValueTask.CompletedTask;
        });

        // The verbatim TTYPE list from options is passed to WithTerminalTypes so it reaches the
        // wire exactly as configured. WithClientIdentity on the builder feeds the same name into
        // the MNES/NEW-ENVIRON side (CLIENT_NAME), which reads it from shared state independently
        // of the TTYPE list.
        var terminalType = new Watched.TerminalType(Note);
        terminalType.WithTerminalTypes([.. _options.TerminalTypes]);

        return new TelnetInterpreterBuilder()
            .UseMode(TelnetInterpreter.TelnetMode.Client)
            .UseLogger(_logger)
            .WithClientIdentity(_options.TerminalTypes.Count > 0 ? _options.TerminalTypes[0] : "MUINDEX-CRAWLER")
            .OnSubmit((bytes, _, _) =>
            {
                // The encoding this callback offers is ignored deliberately: it's whatever CHARSET
                // declared, not a measurement of these bytes — pkuxkx negotiates UTF-8 but sends GBK.
                // Bytes are kept whole and decoded once, at the end, by WireEncoding; the declaration
                // is still recorded, just as a declaration.
                lock (lines)
                {
                    lines.Add(bytes);
                }

                return ValueTask.CompletedTask;
            })
            .AddPlugin(mssp)
            .AddPlugin(charset)
            .AddPlugin(newEnviron)
            .AddPlugin(gmcp)
            .AddPlugin(msdp)
            .AddPlugin(eor)
            .AddPlugin(mccp)
            .AddPlugin(new Watched.Mxp(Note))
            .AddPlugin(new Watched.SuppressGoAhead(Note))
            .AddPlugin(new Watched.Naws(Note))
            .AddPlugin(terminalType)
            .AddPlugin(new Watched.Echo(Note));
    }

    /// <summary>Mutable scratch for one probe. Callbacks arrive on the read loop, so it locks.</summary>
    private sealed class Observations
    {
        // A ceiling on how many distinct MSDP messages one probe keeps, not on any one message's
        // size — WithMaxMessageSize (ProbeOptions.MaxSubnegotiationBytes) already bounds that. MSDP
        // messages are deliberately not deduplicated (see Msdp below), so nothing else stops a
        // hostile or broken server from sending an unbounded number of small, distinct messages for
        // as long as the probe's phase budget allows. No real server measured has sent more than one;
        // this is headroom for legitimate variety, not a number anything has approached.
        private const int MaxMsdpMessages = 64;

        private readonly HashSet<string> _supported = new(StringComparer.Ordinal);
        private readonly List<string> _environment = [];
        private readonly List<string> _gmcp = [];
        private readonly List<string> _msdp = [];

        public MSSPConfig? Mssp;
        public MsspOutcome MsspOutcome = MsspOutcome.NotOffered;
        public int? MsspRejectedBytes;
        public string? Charset;
        public bool Prompts;
        public int? CompressionVersion;
        public bool CharsetNegotiated;

        public IReadOnlySet<string> Supported
        {
            get
            {
                lock (_supported)
                {
                    return _supported.ToHashSet(StringComparer.Ordinal);
                }
            }
        }

        public void Note(string protocol)
        {
            lock (_supported)
            {
                _supported.Add(protocol);
            }
        }

        public void Environment(IEnumerable<string> names)
        {
            lock (_environment)
            {
                _environment.AddRange(names);
            }
        }

        public void Gmcp(string package)
        {
            lock (_gmcp)
            {
                _gmcp.Add(package);
            }
        }

        /// <summary>
        /// Records one MSDP message exactly as TelnetNegotiationCore delivered it. Not deduplicated,
        /// unlike <see cref="Gmcp"/>'s package names — each message is a distinct answer rather than a
        /// repeated declaration, and collapsing "PLAYERS":"3" and "PLAYERS":"4" as duplicates because
        /// their JSON differs only in value would be silently correct today and silently wrong the
        /// moment two different answers arrived. Bounded at <see cref="MaxMsdpMessages"/>: further
        /// messages are dropped rather than grown into, the same "stop rather than fabricate a
        /// smaller version of the truth" choice <see cref="Gmcp"/>'s dedup makes for repetition.
        /// </summary>
        public void Msdp(string message)
        {
            lock (_msdp)
            {
                if (_msdp.Count < MaxMsdpMessages)
                {
                    _msdp.Add(message);
                }
            }
        }

        /// <remarks>
        /// Each collection is snapshotted under its own lock, released before the next is taken —
        /// not nested, so this can never wait on a lock order some future caller takes in reverse.
        /// </remarks>
        public Negotiation ToNegotiation()
        {
            var supported = Supported;

            List<string> environment;
            lock (_environment)
            {
                environment = [.. _environment];
            }

            List<string> gmcp;
            lock (_gmcp)
            {
                gmcp = [.. _gmcp];
            }

            List<string> msdp;
            lock (_msdp)
            {
                msdp = [.. _msdp];
            }

            return new Negotiation
            {
                Supported = supported,
                Charset = Charset,
                CompressionVersion = CompressionVersion,
                CharsetNegotiated = CharsetNegotiated,
                EnvironmentRequested = environment.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                GmcpPackages = gmcp.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                MsdpMessages = msdp,
                SendsPromptMarkers = Prompts,
            };
        }
    }
}
