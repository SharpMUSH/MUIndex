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
/// <c>WHO</c> is further conditional: a game that has already published its count, over MSSP or on
/// the connect screen itself, is not asked for it again (see <see cref="PublishedCountAsync"/>).
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

            var prompts = new PromptSink(lines);
            var built = await Build(seen, lines, prompts).BuildAndStartAsync(client, budget.Token);

            // Before anything can be read from the socket in practice — see PromptSink.Reads.
            prompts.Reads(built.Interpreter);

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
            await SettleInitialBannerAsync(telnet, Arrived, lines, budget.Token);

            var answeredAPrompt = await AnswerPromptsAsync(telnet, Arrived, lines, client, budget.Token);

            // Every cursor below is a line count into the same `lines` list, marking where one
            // phase's slice ends and the next begins. Named fields rather than a bag of same-typed
            // locals: a phase method writes `cursors.Who`, never a bare `whoLines` that could be
            // handed to the wrong parameter by a future edit that reorders phases.
            var cursors = new PhaseCursors { Banner = Arrived() };

            // A who's-online menu option is different from every category the prompt loop above
            // answers: selecting it doesn't reveal a second screen behind this one — for every real
            // game measured (BatMUD, ZombieMUD, discworld.starturtle.net) the menu already settled
            // into cursors.Banner *is* the game's permanent connect screen. So it is classified once
            // here rather than as one more round of that loop, and its own answer/reply are kept out
            // of Banner entirely, the same way the ordinary WHO phase below never becomes part of it
            // — parsed through the identical WhoParser a literal WHO answer would be, so
            // PresenceChoice.From (spec §5.2) cannot tell which route produced the reading.
            var (whoFromMenu, whoFromMenuShape) =
                await TryAnswerWhoMenuAsync(telnet, Arrived, lines, client, cursors.Banner, budget.Token);

            // Phase 2 — an empty line, and everything it produces is thrown away.
            //
            // A server that does not implement telnet at its login screen does not recognise our own
            // IAC DO negotiation bytes as telnet: it takes them as typing and leaves them in its line
            // buffer, so the next thing we send is read as garbage-prefixed and not as WHO — the
            // count is lost. A bare terminator flushes that residue as its own line first. What comes
            // back is a reaction to bytes *we* sent, not the game's connect screen or its WHO answer,
            // so it must not be recorded as either (rule 5) — dropped deliberately, which is what the
            // gap between cursors.Banner and cursors.Flush is.
            //
            // This also ends the session outright on every DIKU descendant, which reads an empty line
            // at its name prompt as a goodbye — see HungUp for what that costs.
            cursors.Flush = cursors.Who = cursors.Info = cursors.Version = cursors.Banner;

            var asked = whoFromMenu is not null;

            // Phases 3 to 5 — the questions we are allowed to ask, in order — and everything that has
            // to happen first for WHO to land cleanly (the MSDP ask, the residue flush). A hang-up
            // partway through is not a failure (rule 5): whatever cursors already advanced stay where
            // they are, and phases never reached keep the banner's cursor.
            var published = await AskFollowUpsAsync(
                telnet,
                Arrived,
                client,
                lines,
                seen,
                cursors,
                // Either kind of answer above was a line through the server's buffer, so either one
                // has already done the flushing. Computed here rather than read off
                // whoAlreadyAnswered inside, so that flag keeps its one meaning: if WHO ever comes to
                // be answered by a route that sends nothing, the flush must not silently go with it.
                alreadyFlushed: answeredAPrompt || whoFromMenu is not null,
                whoAlreadyAnswered: whoFromMenu is not null,
                markAsked: () => asked = true,
                target,
                budget.Token);

            // One decision, over the whole session, taken here because here is the first moment
            // there is a whole session to decide from — and MSSP is part of that session, not a
            // second one. A game whose connect screen is ASCII and whose name is GBK says so only in
            // its report, so the report's bytes decide alongside the screen's.
            WireReading reading;
            lock (lines)
            {
                reading = WireEncoding.Read(lines, target.Charset, MsspReport.RawValues(seen.Mssp));
            }

            return BuildAnsweredResult(
                target, observedAt, started, seen, reading, cursors,
                whoFromMenu, whoFromMenuShape, asked, published, telnet.CurrentEncoding);
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
        // Narrowed to the shapes DialFailure.Classify actually names meaningfully — SocketException
        // and OperationCanceledException, its two pattern-matched types — plus IOException and
        // ObjectDisposedException, the rest of HungUp's "the far end went away" shape, which can
        // surface here too (e.g. a disconnect during SettleInitialBannerAsync, before
        // AskFollowUpsAsync's own narrower catch is even reached). Anything else is a defect in our
        // own code, not a measurement of theirs (rule 5): handing it to DialFailure.Classify would
        // land it in the catch-all and get misfiled as a measured game timeout downstream, so it is
        // left to propagate — CrawlCycle.VisitAsync catches it one level up, logs it loudly as an
        // error, and moves on to the next target without publishing anything about this one.
        catch (Exception error) when (error is SocketException
            or OperationCanceledException
            or IOException
            or ObjectDisposedException)
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
    /// Settles the connect screen, giving it a second, longer wait if the first pass looks like it
    /// hasn't finished painting.
    /// </summary>
    /// <remarks>
    /// A screen that has told us it is not ready has not settled, it has paused. Waiting once more is
    /// conditional on there being almost nothing there, so a server that has already painted pays
    /// nothing — see <see cref="ProbeOptions.BannerPatience"/> for the server this is measured against
    /// and for what recording the placeholder cost.
    /// </remarks>
    private async Task SettleInitialBannerAsync(
        TelnetInterpreter telnet,
        Func<int> arrived,
        List<byte[]> lines,
        CancellationToken cancellationToken)
    {
        await SettleAsync(telnet, arrived, 0, _options.SilenceGrace, cancellationToken);

        if (LooksUnfinished(BannerSoFar(lines, 0, arrived())))
        {
            await SettleAsync(telnet, arrived, arrived(), _options.BannerPatience, cancellationToken);
        }
    }

    /// <summary>
    /// Answers whatever questions the server asks before it paints its connect screen — colour, a
    /// press-enter gate, an age check — and reports whether it answered anything at all.
    /// </summary>
    /// <remarks>
    /// Some connect screens are not the screen at all, but one or more of those questions. Each round
    /// classifies whatever newly arrived since the last answer and sends the specific reply
    /// <see cref="LoginPromptGate"/> says resolves it, then settles again; bounded by
    /// <see cref="ProbeOptions.MaxPromptRounds"/> so a misread screen cannot spin the probe against
    /// itself. A blind Return sent unconditionally — the whole of what this loop replaces — was never
    /// enough: several real games only accept an explicit letter, and a server that did not recognise
    /// a blind Return simply re-printed the same question, which a probe run before this fix stored as
    /// the connect screen. <see cref="LoginPromptCategory.WhoMenu"/> is excluded from this loop on
    /// purpose — it is answered once, separately, against the settled screen (see
    /// <see cref="TryAnswerWhoMenuAsync"/>) rather than as one more round here.
    /// </remarks>
    private async Task<bool> AnswerPromptsAsync(
        TelnetInterpreter telnet,
        Func<int> arrived,
        List<byte[]> lines,
        TcpClient client,
        CancellationToken cancellationToken)
    {
        var roundStart = 0;
        var answeredAPrompt = false;

        for (var round = 0; round < _options.MaxPromptRounds; round++)
        {
            if (LoginPromptGate.Classify(BannerSoFar(lines, roundStart, arrived()))
                    is not { Category: not LoginPromptCategory.WhoMenu } prompt
                || !IsPermittedPromptAnswer(prompt.Answer)
                || !Live(client))
            {
                break;
            }

            roundStart = arrived();
            answeredAPrompt = true;
            await telnet.SendAsync(Encoding.ASCII.GetBytes(prompt.Answer));
            await SettleAsync(telnet, arrived, roundStart, _options.QuietPeriod, cancellationToken);
        }

        return answeredAPrompt;
    }

    /// <summary>
    /// Answers a who's-online menu option on the settled connect screen, if the screen offers one.
    /// </summary>
    /// <remarks>
    /// Parsed through the identical <see cref="WhoParser"/> a literal <c>WHO</c> answer would be, so
    /// <c>PresenceChoice.From</c> (spec §5.2) cannot tell which route produced the reading.
    /// </remarks>
    private async Task<(WhoReading? Who, string? Shape)> TryAnswerWhoMenuAsync(
        TelnetInterpreter telnet,
        Func<int> arrived,
        List<byte[]> lines,
        TcpClient client,
        int bannerLines,
        CancellationToken cancellationToken)
    {
        if (!Live(client)
            || LoginPromptGate.Classify(BannerSoFar(lines, 0, bannerLines))
                is not { Category: LoginPromptCategory.WhoMenu } menu
            || !IsPermittedPromptAnswer(menu.Answer))
        {
            return (null, null);
        }

        var menuBaseline = arrived();
        await telnet.SendAsync(Encoding.ASCII.GetBytes(menu.Answer));

        // WhoGrace, not QuietPeriod: this selection asks the same question the literal WHO does, so
        // it earns the same patience. A codebase that throttles a login-screen WHO throttles the menu
        // route to it too, and giving up after QuietPeriod would read the roster as absent — our
        // timing published as a fact about their server (rule 5), the exact failure WhoGrace was
        // introduced for. Costs nothing in the ordinary case: grace applies only while the reply has
        // produced no line at all, and nothing here stacks — a menu that answers means the literal
        // WHO below is skipped, so the worst-case run of graces is unchanged.
        await SettleAsync(telnet, arrived, menuBaseline, _options.WhoGrace, cancellationToken);

        var menuReply = BannerSoFar(lines, menuBaseline, arrived());
        return (new WhoParser().Parse(menuReply), PayloadRedaction.Replayable(menuReply));
    }

    /// <summary>
    /// MSDP, the residue flush, and the three login-screen questions this probe may ask — in order,
    /// advancing <paramref name="cursors"/> as each one lands.
    /// </summary>
    /// <remarks>
    /// A hang-up that has already yielded a measurement is a fact about the session, not the host
    /// (rule 5): everything already measured (connect screen, handshake, MSSP) stays, and phases never
    /// reached keep their pre-loop cursor rather than recording an empty answer.
    /// <para>
    /// Returns the count the game had already published by the time <c>WHO</c> came up, when that is
    /// why <c>WHO</c> was not asked — see <see cref="PublishedCountAsync"/> for why the caller needs it.
    /// </para>
    /// </remarks>
    private async Task<int?> AskFollowUpsAsync(
        TelnetInterpreter telnet,
        Func<int> arrived,
        TcpClient client,
        List<byte[]> lines,
        Observations seen,
        PhaseCursors cursors,
        bool alreadyFlushed,
        bool whoAlreadyAnswered,
        Action markAsked,
        ProbeTarget target,
        CancellationToken cancellationToken)
    {
        // `sent` fires between the write and the wait: a question is asked when its bytes have gone,
        // not when we decided to ask it, so a flag set before the send could claim to have asked
        // something that never left.
        async Task<int> AskAsync(string command, int baseline, TimeSpan grace, Action? sent = null)
        {
            await telnet.SendAsync(Encoding.ASCII.GetBytes(command));
            sent?.Invoke();
            await SettleAsync(telnet, arrived, baseline, grace, cancellationToken);
            return arrived();
        }

        int? published = null;

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

            // …unless there is no residue for it to clear, which is the only thing it is for.
            //
            // Three ways to know that. A prompt answer above was itself a line through the server's
            // buffer and has already done the flushing — and so was a who's-online menu selection,
            // which is sent by the identical path and is the later of the two when both happen. And
            // a server that negotiated an option *interpreted* our IAC bytes rather than leaving
            // them in its line buffer as typing — so nothing is stuck behind them. Any of the three
            // and the empty line buys nothing, and it is not free: a game sitting at its name prompt
            // reads it as a goodbye and ends the session before WHO can be asked.
            //
            // The menu case was missed when the menu was added: a game that offers one, negotiates
            // nothing, and hangs up on a blank line kept its WHO (that much whoAlreadyAnswered
            // already protected) but lost INFO and VERSION to a goodbye it need never have been
            // sent.
            //
            // Measured both ways across sixteen live games (docs/codebase-survey-2026-07-30.md):
            // none of the eight that negotiate an option needed the flush, while four of the
            // twelve that negotiate nothing could not answer WHO without it —
            // chaos.caile.org:4444 still returns its connect screen instead of a count, four
            // TelnetNegotiationCore versions after that was first written down.
            //
            // The test is positive evidence, so the uncertain cases fall the safe way: a server
            // that declined everything is indistinguishable from one that parsed nothing, and is
            // flushed exactly as it is today. MSSP is the one exception, and only here — elsewhere
            // "negotiated late" and "negotiated nothing" are rightly the same case, but MSSP's WILL
            // lands on connect while its report is a second round trip (our DO, then the server's
            // subnegotiation), so reading Supported at whatever instant this line happens to run
            // could catch that round trip mid-flight. A server slow enough for the gap to matter is
            // exactly the kind this flush hangs up on, so losing the race did not use to mean "no
            // measurement this cycle" — it meant the flush ending the session before a report
            // already in transit could land, recorded downstream as the honest negative
            // FieldObservations.Measured writes for a game that never offered MSSP at all.
            //
            // Watched.Mssp notes "MSSP" the moment TelnetNegotiationCore's own state machine calls
            // OnNegotiatedAsync(true) from OnWillMSSPAsync — the real WILL, not the report and not
            // plugin construction — so Supported is already correct here in the overwhelming
            // majority of probes, well before this line runs; the wait below is a bounded backstop
            // for a WILL delayed by the network, not the primary mechanism. Bounded and paid only by
            // the games already about to be flushed blind: anything that has negotiated something
            // else by now already has Supported non-empty and never enters the loop.
            var parsedOurNegotiation = seen.Supported.Count > 0;

            if (!alreadyFlushed && !parsedOurNegotiation)
            {
                var deadline = DateTime.UtcNow + _options.MsspSettleGrace;
                while (seen.Supported.Count == 0 && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(_options.PollInterval, cancellationToken);
                }

                parsedOurNegotiation = seen.Supported.Count > 0;
            }

            if (!alreadyFlushed && !parsedOurNegotiation)
            {
                await telnet.SendAsync([]);
                await SettleAsync(telnet, arrived, cursors.Banner, _options.QuietPeriod, cancellationToken);
            }

            cursors.Flush = cursors.Who = cursors.Info = cursors.Version = arrived();

            // Asking a socket that has already been closed is not asking. A write to a peer that
            // sent FIN still succeeds — only the write *after* the RST throws — so an unguarded
            // WHO here would come back unanswered and be recorded as unreadable, publishing our
            // own dead socket as a fact about their parser (rule 5).
            //
            // A who's-online menu already answered this probe's WHO question — asking the literal
            // word WHO at whatever this game's screen looks like now would either repeat the menu
            // or be read as a character name, corrupting a good reading with a worse one.
            //
            // And a game that already published a count did not need to be typed at either. Asked
            // for by an operator whose logs showed our WHO arriving at a login screen that states
            // the number three lines up and whose MSSP report states it again: a question whose
            // answer we have been given is not a measurement, it is noise on somebody's console.
            // Decided here rather than earlier because here is the latest moment before the send,
            // so an MSSP report still in flight through the flush above is counted.
            published = await PublishedCountAsync(lines, seen, cursors, target, cancellationToken);

            if (Live(client) && !whoAlreadyAnswered && published is null)
            {
                // WhoGrace, not SilenceGrace: some codebases sit on a login-screen WHO for
                // seconds on purpose, and giving up early does not merely lose the count — it
                // sends INFO while the roster is still in flight, so the roster lands in INFO's
                // window and is recorded as the game's INFO block.
                cursors.Who = cursors.Info = cursors.Version =
                    await AskAsync(WhoCommand, cursors.Flush, _options.WhoGrace, markAsked);
            }

            if (Live(client))
            {
                cursors.Info = cursors.Version = await AskAsync(InfoCommand, cursors.Who, _options.SilenceGrace);
            }

            if (Live(client))
            {
                cursors.Version = await AskAsync(VersionCommand, cursors.Info, _options.SilenceGrace);
            }
        }
        catch (Exception error) when (HungUp(error) && Measured(lines, cursors.Banner, seen))
        {
            _logger.LogDebug(
                "{Host}:{Port} closed the session after its connect screen ({Error}); keeping what it said",
                target.Host, target.Port, error.Message);
        }

        return published;
    }

    /// <summary>
    /// The count this game has already published — over MSSP, or on the connect screen itself — by
    /// the moment <c>WHO</c> would be typed, or null when it has published none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Non-null means <c>WHO</c> is not asked and the returned count is what
    /// <see cref="ProbeResult.BannerPlayerCount"/> falls back to. Both halves of that matter. A probe
    /// that declines to ask and then publishes nothing would reach
    /// <c>PresenceChoice.ReasonFor</c> and write <c>who_not_offered</c> — <em>the game answers no
    /// pre-login WHO</em> — about a game we never asked. That is a decision of ours recorded as a
    /// measurement of theirs, and the reason vocabulary deliberately has no member for it (see
    /// <c>UnmeasurableReason.I3NoReply</c>, which says so). So the count that bought the silence is
    /// carried forward rather than left behind, and the invariant holds by construction: not asking
    /// implies publishing.
    /// </para>
    /// <para>
    /// The MSSP half needs no encoding decision — a stated <c>PLAYERS</c> is digits, and a roster is
    /// counted by its delimiters, so every encoding this crawler reads agrees about both. It is read
    /// here through the same <see cref="MsspPresence"/> the publisher uses and cannot disagree with
    /// it, roster rung included: a game that published who is online has answered this probe's
    /// question as surely as one that published the number. The banner half is
    /// read from the connect screen alone, which is complete by now (<c>cursors.Banner</c> stopped
    /// moving before this method was called); the session-wide charset decision at the end of the
    /// probe may still land elsewhere, so this is deliberately the <em>fallback</em> for the count
    /// rather than its replacement, and a screen the final decode reads better is still read better.
    /// </para>
    /// <para>
    /// The banner half additionally requires the session to have shown a protocol signal. A game whose
    /// only evidence of being a game is a parseable <c>WHO</c> (<c>MuLikeness</c>, <c>§7.8</c>) would
    /// otherwise be talked out of the one answer that gets it listed, on the strength of a number
    /// pattern-matched out of somebody's ASCII art. MSSP carries no such risk: a report *is* the
    /// signal.
    /// </para>
    /// </remarks>
    private async Task<int?> PublishedCountAsync(
        List<byte[]> lines,
        Observations seen,
        PhaseCursors cursors,
        ProbeTarget target,
        CancellationToken cancellationToken)
    {
        // A report we have already been promised is worth the wait. `WILL MSSP` lands during the
        // option handshake while the report itself is a second round trip, so a server can have
        // agreed to MSSP and still have its answer in flight when this line runs — and the flush
        // above only waits for that when it also has to decide whether to send a blank line, which
        // for a server that negotiated is exactly the case it skips.
        //
        // Without this the decision is a coin toss on timing, and it lost: CI on windows-latest sent
        // WHO to a fixture whose report arrived a moment later, against a server on the same
        // machine. Across a real network the gap is wider, not narrower. Bounded by MsspSettleGrace
        // and paid only by a game that said it would answer — a server that never offered MSSP has
        // an empty Supported and never enters the loop.
        if (seen.Supported.Contains("MSSP") && seen.MsspOutcome is MsspOutcome.NotOffered)
        {
            var deadline = DateTime.UtcNow + _options.MsspSettleGrace;

            while (seen.MsspOutcome is MsspOutcome.NotOffered && DateTime.UtcNow < deadline)
            {
                await Task.Delay(_options.PollInterval, cancellationToken);
            }
        }

        if (seen.MsspOutcome is MsspOutcome.Received
            && MsspPresence.Read(MsspReport.From(seen.Mssp, WireEncoding.Fallback)) is { Found: true } declared)
        {
            return declared.Count;
        }

        if (seen.Supported.Count == 0)
        {
            return null;
        }

        List<byte[]> screen;
        lock (lines)
        {
            screen = [.. lines.Take(cursors.Banner)];
        }

        var text = WireEncoding.Read(screen, target.Charset, MsspReport.RawValues(seen.Mssp)).Lines;
        var banner = string.Join("\n", text);

        return BannerCount.Find(PuebloSignal.IsPresent(banner) ? PuebloSignal.StripKnown(banner) : banner);
    }

    /// <summary>Builds the <see cref="ProbeOutcome.Answered"/> result from a completed session.</summary>
    private static ProbeResult BuildAnsweredResult(
        ProbeTarget target,
        DateTimeOffset observedAt,
        long started,
        Observations seen,
        WireReading reading,
        PhaseCursors cursors,
        WhoReading? whoFromMenu,
        string? whoFromMenuShape,
        bool asked,
        int? published,
        Encoding? negotiatedEncoding)
    {
        var read = reading.Lines;

        // The screen as it arrived, kept for the protocol questions below — MXP is a fact about what
        // the server sent, so it must be read before anything is stripped out of it.
        var asSent = string.Join("\n", read.Take(cursors.Banner));

        var whoSent = string.Join("\n", read.Skip(cursors.Flush).Take(cursors.Who - cursors.Flush));
        var infoSent = string.Join("\n", read.Skip(cursors.Who).Take(cursors.Info - cursors.Who));
        var versionSent = string.Join("\n", read.Skip(cursors.Info).Take(cursors.Version - cursors.Info));

        // Decided over the whole session and applied to all four, the same way MxpObserved is read
        // below: a Pueblo server marks up everything it sends, but only some of it carries a marker.
        // Elendor's connect screen is unmistakable while its INFO reply is merely
        // "### Begin INFO 1<br>Name: ElendorMUSH<br>Connected: 0<br>…" — asked about that fragment
        // alone, the gate would rightly decline, and the game's INFO block would be stored as one
        // run-on line of tags.
        var pueblo = PuebloSignal.IsPresent(asSent)
            || PuebloSignal.IsPresent(whoSent)
            || PuebloSignal.IsPresent(infoSent)
            || PuebloSignal.IsPresent(versionSent);

        var banner = pueblo ? PuebloSignal.StripKnown(asSent) : asSent;
        var whoText = pueblo ? PuebloSignal.StripKnown(whoSent) : whoSent;
        var infoText = pueblo ? PuebloSignal.StripKnown(infoSent) : infoSent;
        var versionText = pueblo ? PuebloSignal.StripKnown(versionSent) : versionSent;

        if (negotiatedEncoding is not null)
        {
            seen.Charset ??= negotiatedEncoding.WebName;
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
            MxpObserved = MxpSignal.IsPresent(asSent)
                || MxpSignal.IsPresent(whoSent)
                || MxpSignal.IsPresent(infoSent)
                || MxpSignal.IsPresent(versionSent),
            Who = whoFromMenu ?? (asked ? new WhoParser().Parse(whoText) : WhoReading.NotAsked),
            WhoShape = whoFromMenu is not null
                ? whoFromMenuShape
                : (asked ? PayloadRedaction.Replayable(whoText) : null),
            Info = infoText.Length == 0 ? null : infoText,
            Version = versionText.Length == 0 ? null : versionText,
            // `published` is the count that bought this game its silence — see PublishedCountAsync for
            // why not asking must imply publishing. Second, not first: the session-wide charset
            // decision is made above and a screen this decode reads better is read better.
            BannerPlayerCount = BannerCount.Find(banner) ?? published,
            Mssp = viaOption ? MsspReport.From(seen.Mssp, reading.Encoding) : MsspReport.Empty,
            MsspOutcome = seen.MsspOutcome,
            MsspBytesRejected = seen.MsspRejectedBytes,
            MsspTransport = viaOption ? MsspTransport.TelnetOption70 : MsspTransport.None,
            Elapsed = Stopwatch.GetElapsedTime(started),
        };
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
        string joined;
        bool pueblo;

        lock (lines)
        {
            var decoded = lines.Take(to).Select(WireEncoding.Fallback.GetString).ToList();
            var received = string.Join("\n", decoded);

            joined = from == 0 ? received : string.Join("\n", decoded.Skip(from));

            // Asked of everything received so far, not of this slice. Most callers want a slice —
            // the prompt loop reads from the last answer, the who's-online menu reads only its own
            // reply — and the marker that proves a server is Pueblo is almost always up in the
            // connect screen, behind them. Gating on the slice would leave exactly the fragments
            // that matter unstripped: the menu's roster reply goes straight to WhoParser, and a
            // roster whose rows are still separated by <br> is one run-on line it cannot read.
            pueblo = PuebloSignal.IsPresent(received);
        }

        // Normalised here rather than by each reader, because this is where lines become a screen and
        // a Pueblo server's line endings are <br> rather than newlines. Doing it downstream is too
        // late for anything that works a line at a time: BannerText.Flatten collapses all whitespace,
        // so a <br> turned into a newline in there is a space again before LoginPromptGate's menu
        // reader ever splits on it — and three of that server's screen lines arrive as one.
        return pueblo ? PuebloSignal.StripKnown(joined) : joined;
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

            // Not settled while the library is still holding an unterminated line. The two clocks
            // start at different moments — ours at the last line, PacketPatchProtocol's at the
            // fragment that arrived after it — so ours always expires first, and a phase that ended
            // here would push its own prompt into the next phase's slice. MaxPhase still bounds the
            // wait, so a server that holds a fragment for ever is not waited on for ever.
            if (telnet.HasPartialLine)
            {
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
    private static Task FlushPendingLineAsync(TelnetInterpreter telnet) =>
        telnet.WaitForProcessingAsync(maxWaitMs: 500, additionalDelayMs: 25).AsTask();

    /// <summary>
    /// Where a prompt the library took goes: onto the end of this probe's line list.
    /// </summary>
    /// <remarks>
    /// All three of the library's prompt boundaries — <c>IAC EOR</c>, <c>IAC GA</c>, and
    /// <c>PacketPatchProtocol</c> inferring one from silence — take the standing partial line into
    /// <c>LastPromptBytes</c> and then call back. They do not submit it: a prompt is not a line and
    /// the library will not pretend otherwise, so nothing reaches <c>OnSubmit</c> and the probe would
    /// never see an unterminated login prompt at all.
    /// <para>
    /// Every callback runs on the interpreter's own byte-processing loop, which is what makes this
    /// the right place to do it: the prompt lands in <c>lines</c> in the order it was taken, between
    /// the lines either side of it, rather than being swept up afterwards at a phase boundary.
    /// </para>
    /// </remarks>
    private sealed class PromptSink(List<byte[]> lines)
    {
        private TelnetInterpreter? _telnet;

        /// <summary>
        /// Names the interpreter to read prompts from, once the builder has produced it.
        /// </summary>
        /// <remarks>
        /// Assigned immediately after the build rather than during it, because the callbacks are
        /// registered before the interpreter they read from exists. Nothing is lost in between: the
        /// earliest prompt a peer can produce needs either a marker it has not sent yet or
        /// <c>PromptHold</c> of silence, and this runs in the same continuation as the build.
        /// </remarks>
        public void Reads(TelnetInterpreter telnet) => _telnet = telnet;

        public ValueTask TakeAsync()
        {
            if (_telnet?.LastPromptBytes is not { IsEmpty: false } prompt)
            {
                return ValueTask.CompletedTask;
            }

            lock (lines)
            {
                lines.Add(prompt.ToArray());
            }

            return ValueTask.CompletedTask;
        }
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
    /// Every <c>Watched.X</c> plugin is built and wired in one fluent expression — construction,
    /// <c>WithMaxMessageSize</c> where the protocol has one, and its callbacks — rather than as a
    /// separate statement apiece; the TelnetNegotiationCore plugin builders are already self-typed
    /// fluent, so this is the shape the library wants, not a new abstraction layered over it.
    /// </remarks>
    private TelnetInterpreterBuilder Build(Observations seen, List<byte[]> lines, PromptSink prompts)
    {
        void Note(string protocol) => seen.Note(protocol);

        var mssp = new Watched.Mssp(Note)
            .WithMaxMessageSize(_options.MaxSubnegotiationBytes)
            .OnMSSP(config =>
            {
                seen.Mssp = config;
                seen.MsspOutcome = MsspOutcome.Received;
                Note("MSSP");
                return ValueTask.CompletedTask;
            })
            // 2.7.0 drops an oversized report whole rather than truncating it. Kept as its own
            // outcome: we asked, they answered, we declined to hold it — which is not "no MSSP".
            .OnMSSPMessageTooLarge(sizes =>
            {
                seen.MsspOutcome = MsspOutcome.RejectedTooLarge;
                seen.MsspRejectedBytes = (int)Math.Min(sizes.Item1, int.MaxValue);
                Note("MSSP");
                return ValueTask.CompletedTask;
            });

        var gmcp = new Watched.Gmcp(Note)
            .WithMaxMessageSize(_options.MaxSubnegotiationBytes)
            .OnGMCPMessage(message =>
            {
                seen.Gmcp(message.Item1);
                Note("GMCP");
                return ValueTask.CompletedTask;
            });

        var msdp = new Watched.Msdp(Note)
            .WithMaxMessageSize(_options.MaxSubnegotiationBytes)
            .OnMSDPMessage((_, message) =>
            {
                seen.Msdp(message);
                Note("MSDP");
                return ValueTask.CompletedTask;
            });

        var newEnviron = new Watched.NewEnviron(Note)
            .OnEnvironmentVariables((requested, _) =>
            {
                seen.Environment(requested.Keys);
                Note("NEW-ENVIRON");
                return ValueTask.CompletedTask;
            });

        var mccp = new Watched.Mccp(Note)
            .OnCompressionEnabled((version, _) =>
            {
                seen.CompressionVersion = version;
                Note($"MCCP{version}");
                return ValueTask.CompletedTask;
            });

        var eor = new Watched.Eor(Note)
            .OnPrompt(() =>
            {
                seen.Prompts = true;
                Note("EOR");
                return prompts.TakeAsync();
            });

        // The other prompt marker, and the one most of this hobby actually uses. RFC 854 makes a bare
        // IAC GA the server-to-user prompt boundary, so a default NVT — negotiating neither EOR nor
        // SUPPRESS-GO-AHEAD, which is most MU* servers — ends every prompt with it and nothing else.
        // TelnetNegotiationCore 2.11 (#90) started delivering that; before, it was discarded as noise
        // and SendsPromptMarkers could only ever be true for the EOR minority.
        //
        // Deliberately not Note()d as a protocol. `Supported` means "observed active", and receiving a
        // GA means SUPPRESS-GO-AHEAD is *not* in effect (RFC 858 makes a suppressed GA a NOP) — so
        // recording its name here would assert the opposite of what the byte proves. The plugin's own
        // OnEnabledAsync still notes it if the option is actually negotiated.
        var goAhead = new Watched.SuppressGoAhead(Note)
            .OnPrompt(() =>
            {
                seen.Prompts = true;
                return prompts.TakeAsync();
            });

        // CharsetProtocol takes its order as a property rather than a builder call, and exposes
        // OnCharsetChange on the instance — so the settled encoding is captured here rather than
        // read off the interpreter afterwards, where a default is indistinguishable from a result.
        var charset = new Watched.Charset(Note) { CharsetOrder = [Encoding.UTF8, Encoding.Latin1] }
            .OnCharsetChange(encoding =>
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
        var terminalType = new Watched.TerminalType(Note).WithTerminalTypes([.. _options.TerminalTypes]);

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
            .AddPlugin(goAhead)
            .AddPlugin(new Watched.Naws(Note))
            .AddPlugin(terminalType)
            .AddPlugin(new Watched.Echo(Note))

            // What FlushPendingLineAsync used to do by hand, done on the interpreter's own
            // byte-processing loop where the line buffer has exactly one writer — and, crucially,
            // without pretending the peer sent anything. It retires itself the moment a server
            // marks a real prompt with IAC GA or IAC EOR.
            .AddPlugin(new PacketPatchProtocol()
                .WithHoldTime(_options.PromptHold)
                .OnPrompt(prompts.TakeAsync));
    }

    /// <summary>
    /// Where each phase's slice of the session's <c>lines</c> ends — a running line count into the
    /// same list, one field per phase.
    /// </summary>
    /// <remarks>
    /// Threading five same-typed <c>int</c> cursors through <c>ProbeAsync</c> as loose locals made it
    /// possible to hand one phase's baseline to the wrong parameter and have the compiler say nothing
    /// — they were all just <c>int</c>. A named field on this type doesn't stop that by construction,
    /// but every write and read now says which phase's boundary it means (<c>cursors.Who</c>, not a
    /// bare <c>whoLines</c> sitting next to four look-alikes), which is what made the original
    /// transposition bug easy to introduce and hard to spot in review.
    /// </remarks>
    private sealed class PhaseCursors
    {
        /// <summary>Where the connect screen — banner, answered prompts, WHO menu reply — ends.</summary>
        public int Banner;

        /// <summary>Where the discarded flush-line reaction ends. Starts equal to <see cref="Banner"/>.</summary>
        public int Flush;

        /// <summary>Where the WHO answer ends. Starts equal to <see cref="Banner"/>.</summary>
        public int Who;

        /// <summary>Where the INFO answer ends. Starts equal to <see cref="Banner"/>.</summary>
        public int Info;

        /// <summary>Where the VERSION answer ends. Starts equal to <see cref="Banner"/>.</summary>
        public int Version;
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

        // One lock guards every mutable collection below. The four separate locks this replaced were
        // never nested — each was acquired, used and released before the next was touched — but a
        // snapshot in ToNegotiation still had to take and drop four locks one at a time to read
        // values no caller ever contends over. A single lock keeps that same "never nested" property
        // trivially (there is only one) and lets ToNegotiation take one critical section instead of
        // four.
        private readonly Lock _gate = new();
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
                lock (_gate)
                {
                    return _supported.ToHashSet(StringComparer.Ordinal);
                }
            }
        }

        public void Note(string protocol)
        {
            lock (_gate)
            {
                _supported.Add(protocol);
            }
        }

        public void Environment(IEnumerable<string> names)
        {
            lock (_gate)
            {
                _environment.AddRange(names);
            }
        }

        public void Gmcp(string package)
        {
            lock (_gate)
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
            lock (_gate)
            {
                if (_msdp.Count < MaxMsdpMessages)
                {
                    _msdp.Add(message);
                }
            }
        }

        /// <remarks>
        /// One lock for the whole snapshot — see <see cref="_gate"/> — so the four collections are
        /// read as they stood at a single instant rather than one at a time as a writer might still be
        /// touching the next one.
        /// </remarks>
        public Negotiation ToNegotiation()
        {
            HashSet<string> supported;
            List<string> environment;
            List<string> gmcp;
            List<string> msdp;

            lock (_gate)
            {
                supported = _supported.ToHashSet(StringComparer.Ordinal);
                environment = [.. _environment];
                gmcp = [.. _gmcp];
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
