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
/// <para>
/// <b>This client never authenticates.</b> Everything it reads is what a server hands an anonymous
/// connection: the banner it sends unprompted, the options it negotiates, the MSSP report it
/// publishes for crawlers, and the pre-login commands games may answer before login.
/// <see cref="PermittedCommands"/> is the complete list of what may go on the wire, and
/// <c>connect</c> and <c>create</c> are not on it — enforced by test, not by good intentions.
/// </para>
/// <para>
/// <b>Layer 1 comes from the library's own callbacks, not from parsing bytes.</b> Each plugin below
/// fires only when the server actually negotiated that option, so being told by the thing that did
/// the negotiating is both simpler and more truthful than decoding the same exchange a second time.
/// </para>
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
    /// <para>
    /// The bare line terminator the probe sends between the banner and the <c>WHO</c> is deliberately
    /// not on this list, because it is not a command: it carries no text, names nothing, and asks for
    /// nothing. What it does is described at its call site.
    /// </para>
    /// <para>
    /// <c>MSSP-REQUEST</c> is not on it either, and that is a decision rather than an omission — see
    /// <see cref="ProbeOptions"/>. MSSP is asked for by negotiation, which a server that does not
    /// implement it simply ignores.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<string> PermittedCommands = [WhoCommand, InfoCommand, VersionCommand];

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

            // Disposed, and disposed *before* the socket it reads. The interpreter is not a view
            // onto the connection: it owns a byte channel, the task draining it, the inbound and
            // outbound byte transforms, and every plugin registered in Build — and MCCP's plugin
            // holds zlib streams, which no collection reclaims on our behalf. Nothing here used to
            // dispose it at all, so each probe abandoned one whole set, which is the six megabytes
            // an hour of drift the deployment was carrying.
            //
            // Declared here rather than beside `client` so that the interpreter goes first: this
            // block ends on the return below and on every exception that leaves it, while `client`
            // is scoped to the whole method and is disposed after. DisposeAsync completes the byte
            // channel and then *waits* for the processing task, so the interpreter is the party
            // still doing work during teardown and the transport it was handed should outlive it —
            // shutting a thing down while demolishing what it was built on is the wrong way round
            // whether or not it happens to survive it.
            //
            // It does happen to survive it, measured: with the two disposals swapped by hand the
            // transcript is identical and nothing faults, because the processing task is fed from a
            // channel rather than from the socket and never touches the transport on the way out.
            // That is a detail of how the library is put together today and not a promise, so this
            // is written as the order that cannot go wrong rather than as the only one that works.
            await using var telnet = built.Interpreter;

            // The read loop is observed rather than awaited, and awaiting it here is not an option.
            // It ends when the budget is cancelled or when the pipe under it completes, and on a
            // server that is simply sitting there — which is most of them, since the probe stops
            // asking long before the far end stops listening — that means when `client` is disposed
            // on the way out of this method, after this block. Waiting for it here would be waiting
            // for something only our own exit can cause.
            //
            // Dropping it entirely, which is what happened before, is the other half of the same
            // omission: a read loop that dies of a defect in the interpreter looks from out here
            // exactly like one that died of the socket being torn down under it, and only the second
            // is ordinary. The teardown shapes are the ones HungUp already names — a write racing
            // the transport's own disposal surfaces as ObjectDisposedException rather than as a
            // socket error — so those are expected and stay quiet; anything else is a fault that was
            // previously reaching nobody, and it is written down.
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
            if (LooksUnfinished(BannerSoFar(lines, Arrived())))
            {
                await SettleAsync(telnet, Arrived, Arrived(), _options.BannerPatience, budget.Token);
            }

            var bannerLines = Arrived();

            // Whether the flush below is a stray line or the answer to a question. Decided here,
            // before it is sent, because after it there is no way to tell the two apart.
            var gated = BannerGate.IsAnsweredByReturn(BannerSoFar(lines, bannerLines));

            // Phase 2 — an empty line, and everything it produces is thrown away.
            //
            // The IAC DO above is well-formed telnet, and a server that does not implement telnet at
            // its login screen does not know that: it takes the three bytes as typing and leaves them
            // sitting in its line buffer. The next thing we send is then not WHO but
            // "\xff\xfd\x46WHO", which is not a command it has, so it answers by redisplaying the
            // connect screen and the count is lost. Measured on chaos.caile.org:4444 (TinyMUSH),
            // where IAC DO 70 poisons the following line and IAC WILL NAWS does not.
            //
            // A bare terminator flushes that residue as a line of its own. What the server says back
            // is a reaction to a byte sequence *we* chose to send, so it is neither the game's
            // connect screen nor its answer to WHO, and recording it as either would be recording a
            // decision of ours as a measurement of theirs. It is dropped on the floor deliberately —
            // that is what the gap between bannerLines and flushLines is.
            //
            // It also ends the session outright on every DIKU descendant, which reads an empty line
            // at its name prompt as a goodbye — see HungUp for what that costs and what it must not.
            var flushLines = bannerLines;
            var whoLines = bannerLines;
            var infoLines = bannerLines;
            var versionLines = bannerLines;
            var asked = false;

            // Phases 3 to 5 — the questions we are allowed to ask, in order. SendAsync appends the
            // line ending itself, so each command is handed over bare.
            //
            // `sent` fires between the write and the wait, which is the only place it can be honest:
            // a question is asked when its bytes have gone, not when we decided to ask it. Live()
            // above narrows the window rather than closing it — the peer can still go in the moment
            // between the check and the write — and a flag set before the send would survive that as
            // a claim to have asked something that never left.
            async Task<int> AskAsync(string command, int baseline, Action? sent = null)
            {
                await telnet.SendAsync(Encoding.ASCII.GetBytes(command));
                sent?.Invoke();
                await SettleAsync(telnet, Arrived, baseline, _options.SilenceGrace, budget.Token);
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

                await telnet.SendAsync([]);
                await SettleAsync(telnet, Arrived, bannerLines, _options.QuietPeriod, budget.Token);
                flushLines = whoLines = infoLines = versionLines = Arrived();

                // …unless the connect screen was a question, in which case the terminator answered
                // it and what arrived is the screen the game meant to show. Seventeen active games
                // gate their whole screen behind one keystroke — see BannerGate for the three that
                // were dialled by hand, where 23, 101 and 29 characters of prompt were being stored
                // as the connect screen of a game with 3033, 2826 and 2197 characters behind it.
                //
                // Moving the boundary rather than concatenating: the banner is a contiguous run of
                // lines from the start of the session, and it stays one. What changes is where it
                // ends. The flush window closes to nothing, which is correct — on a gated server
                // there was no discarded reaction, because the reaction was the screen.
                if (gated)
                {
                    bannerLines = Arrived();
                }

                // Asking a socket that has already been closed is not asking. A write to a peer that
                // has sent FIN succeeds — the bytes go to a kernel buffer nobody will ever read, and
                // only the write *after* the RST throws — so an unguarded WHO here would come back
                // unanswered and be recorded as a WHO the game answered unreadably. That is a hatched
                // cell on the heatmap, and hatched means "we asked and could not read it", which
                // would be our own dead socket published as a fact about their parser (rule 5).
                // Checking costs nothing and also returns three silence graces of crawl budget that
                // were being spent waiting on a connection that had already gone.
                if (Live(client))
                {
                    whoLines = infoLines = versionLines =
                        await AskAsync(WhoCommand, flushLines, () => asked = true);
                }

                if (Live(client))
                {
                    infoLines = versionLines = await AskAsync(InfoCommand, whoLines);
                }

                if (Live(client))
                {
                    versionLines = await AskAsync(VersionCommand, infoLines);
                }
            }
            catch (Exception error) when (HungUp(error) && Measured(lines, bannerLines, seen))
            {
                // The server said its piece and then dropped us, which is a fact about the session
                // and not about the host: the connect screen, the handshake and any MSSP report are
                // all already in hand, and every one of them was measured before the socket died.
                //
                // Reporting the whole probe as Failed threw them away and wrote the game down as
                // unreachable — a game that answered, recorded as one that did not, on the strength
                // of a line *we* sent. That is the fifth rule, and it is why this is not a rescue of
                // a broken probe but the correct reading of a complete one: what we could obtain, we
                // obtained. The phases we never reached keep their pre-loop counts, so they yield
                // nothing rather than an empty answer, and `asked` keeps WHO honest about which.
                _logger.LogDebug(
                    "{Host}:{Port} closed the session after its connect screen ({Error}); keeping what it said",
                    target.Host, target.Port, error.Message);
            }

            // One decision, over the whole session, taken here because here is the first moment
            // there is a whole session to decide from.
            WireReading reading;
            lock (lines)
            {
                reading = WireEncoding.Read(lines, target.Charset);
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
                Who = asked ? new WhoParser().Parse(whoText) : WhoReading.NotAsked,
                WhoShape = asked ? PayloadRedaction.Replayable(whoText) : null,
                Info = infoText.Length == 0 ? null : infoText,
                Version = versionText.Length == 0 ? null : versionText,
                BannerPlayerCount = BannerCount.Find(banner),
                Mssp = viaOption ? MsspReport.From(seen.Mssp) : MsspReport.Empty,
                MsspOutcome = seen.MsspOutcome,
                MsspBytesRejected = seen.MsspRejectedBytes,
                MsspTransport = viaOption ? MsspTransport.TelnetOption70 : MsspTransport.None,
                Elapsed = Stopwatch.GetElapsedTime(started),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // OUR CALLER IS GOING AWAY, WHICH IS NOT A MEASUREMENT OF ANYTHING (rule 5).
            //
            // `budget` links the caller's token to this probe's own timeout, and by the time an
            // exception arrives both look identical: an OperationCanceledException, which Classify
            // reads as ("timeout", "probe budget exhausted"). That reading is right for the budget
            // and a lie for the caller. Returning Failed here hands CrawlCycle a result it has every
            // reason to store, so a host that was killed mid-cycle published a timeout against every
            // game it happened to be dialling — twenty of them inside one minute, each perfectly
            // reachable, each then held at that verdict for six hours by the failure backoff.
            //
            // Rethrowing puts it back on the path that already knows what it means: VisitAsync's
            // `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)`,
            // which writes nothing and leaves the target due. Ask the token which one cancelled,
            // never the exception.
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
    /// <para>
    /// This is fire-and-forget on purpose and it outlives <c>ProbeAsync</c> by design — the loop
    /// only ends once the socket goes, which is the last thing the probe does. Nothing downstream
    /// waits on it and nothing it finds can change a <see cref="ProbeResult"/>: by the time it has
    /// anything to say, the measurement is already made and returned. It exists so the ending is
    /// visible, not so it can be acted on.
    /// </para>
    /// <para>
    /// The ordinary ending is the connection being torn down under the loop, which arrives as one
    /// of the <see cref="HungUp"/> shapes or as the probe's budget being cancelled, and neither is
    /// a fact about anything. Everything else is a defect — in the interpreter, in a plugin, or in
    /// how this class drives them — and it is logged at debug against the host it happened on,
    /// which is where a leak or a stall would first show a symptom.
    /// </para>
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
    /// A readable socket with nothing readable on it is a socket the peer has closed, which is the
    /// only way to tell the difference before writing into it — TCP accepts the first write after a
    /// FIN and reports nothing. Every read the probe cares about has already been drained by the
    /// interpreter's own loop by the time this is called, between phases, so "readable" here means
    /// the close and not a pending line.
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
    /// <para>
    /// Deliberately not <see cref="OperationCanceledException"/>, which is the probe budget expiring
    /// and belongs on the failure path where <c>FailureReading</c> can read it as a stalled handshake
    /// (spec §5.3). A closed socket is the opposite case: the session is over rather than overrunning,
    /// and there is nothing left to wait for.
    /// </para>
    /// <para>
    /// <see cref="ObjectDisposedException"/> is here because the interpreter's own transport is what
    /// gets torn down when the peer goes, so a write racing that teardown surfaces as a disposal
    /// rather than as the underlying socket error.
    /// </para>
    /// </remarks>
    private static bool HungUp(Exception error) => error
        is IOException
        or SocketException
        or ObjectDisposedException;

    /// <summary>
    /// Whether the session yielded anything before it ended.
    /// </summary>
    /// <remarks>
    /// The guard on carrying evidence forward, and the reason a hang-up is not a blanket amnesty. A
    /// host that accepts a connection and drops it without a word has told us nothing, and calling
    /// that <c>Answered</c> would fabricate a measurement out of a TCP handshake. Either a connect
    /// screen arrived or the far end spoke telnet back; with neither, the probe failed.
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
    /// Read with the Latin-1 fallback rather than the session's eventual encoding, because the
    /// session does not have one yet and this is not the text anybody will see. Its only reader is
    /// <see cref="LooksUnfinished"/>, which measures a length and looks at one ASCII punctuation
    /// mark — questions whose answers no 8-bit byte changes. The decision proper is taken once, at
    /// the end, in <see cref="WireEncoding.Read"/>.
    /// </remarks>
    private static string BannerSoFar(List<byte[]> lines, int count)
    {
        lock (lines)
        {
            return string.Join("\n", lines.Take(count).Select(WireEncoding.Fallback.GetString));
        }
    }

    /// <summary>
    /// Whether what we have is a screen that has not finished arriving.
    /// </summary>
    /// <remarks>
    /// Two conditions, and both are needed. <b>Slight</b> is measured on the text rather than the
    /// bytes, so a screenful of colour codes carrying one word counts as one word. <b>Not at a
    /// prompt</b> is what keeps the wait off servers whose screen really is that short: a screen
    /// ending in <c>Please enter a name:</c> has reached the point where it is waiting for us, and
    /// there is nothing more to wait for. tbaMUD's placeholder ends <c>Please Wait...</c>, which is
    /// the opposite statement.
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
    /// <para>
    /// Two fixed delays used to make every probe cost six seconds regardless of how fast the game
    /// answered. Settling on a gap gets that back: a phase ends when nothing new has arrived for
    /// <see cref="ProbeOptions.QuietPeriod"/>, bounded above by <see cref="ProbeOptions.MaxPhase"/>
    /// so a server that never stops talking cannot stall the crawl, and by the caller's
    /// <see cref="ProbeOptions.Timeout"/> beyond that.
    /// </para>
    /// <para>
    /// Silence and a gap are different facts and get different budgets. A gap between lines means
    /// the server has finished; silence from the start of a phase means it has not begun, and the
    /// answer may still be in flight over a slow link.
    /// </para>
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
    /// <para>
    /// <b>Unterminated output is normal in this hobby and a line-oriented callback loses it
    /// systematically.</b> TelnetNegotiationCore submits a line only on entering its <c>Act</c>
    /// state, which nothing but a newline reaches, so a trailing partial line sits in the
    /// interpreter's buffer until the connection closes and <c>OnSubmit</c> never fires for it.
    /// Measured: <c>aardmud.org:4000</c> ends its connect screen with
    /// <c>What be thy name, adventurer?</c> and its <c>WHO</c> reply with <c>Name:</c>,
    /// <c>realms.reichel.net:4000</c> with <c>By what name do you wish to be known?</c>, and
    /// <c>resort.org:2323</c> ends <em>both</em> with <c>Please enter a name:</c> — five of twelve
    /// reference servers leave the last line hanging.
    /// </para>
    /// <para>
    /// Losing those is not cosmetic. The guard that stops a busy DIKU being read as a measured zero
    /// works by recognising a login prompt, and a login prompt is exactly the kind of line a server
    /// leaves unterminated.
    /// </para>
    /// <para>
    /// The newline goes in through <c>InterpretAsync</c>, which is the same channel the read loop
    /// feeds, so it is ordered behind every byte already received and races with nothing. Nothing
    /// goes on the wire, and line assembly, IAC handling and encoding all stay the library's — this
    /// is not a second decoder, it is the terminator the server omitted. When the buffer is empty,
    /// which is the common case, the library discards it and no line is produced.
    /// </para>
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
    /// Each plugin is here to be <em>watched</em>, not used. This client renders nothing, so MXP and
    /// MCCP buy it no features — what they buy is the knowledge that this server offers them, which
    /// is what the capability matrix is made of.
    /// <para>
    /// A protocol is recorded from inside its own callback, because that is the event that proves it
    /// ran. The <c>OnEnabledAsync</c> overrides in <see cref="Watched"/> are a second, cheaper signal
    /// for the same thing — but they are not sufficient on their own: measured against live servers
    /// they did not fire, while <c>OnMSSP</c> and <c>OnCharsetChange</c> did. Keeping both means a
    /// protocol counts as supported if either the library says it enabled or it demonstrably did
    /// something, and neither route can silently become the only one.
    /// </para>
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
                // The encoding this callback offers is deliberately ignored. It is whatever CHARSET
                // settled on, which is a declaration about the session and not a measurement of
                // these bytes — pkuxkx negotiates UTF-8 and sends GBK — and decoding on it here used
                // the *replacing* fallback, so a byte it could not read became U+FFFD before anybody
                // downstream could object. The bytes are kept whole and read once, at the end of the
                // session, by WireEncoding. The declaration is still recorded; it is just recorded
                // as a declaration.
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
