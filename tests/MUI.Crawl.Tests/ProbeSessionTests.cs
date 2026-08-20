using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MUI.Crawl;
using TelnetNegotiationCore.Builders;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Protocols;

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
        WhoGrace = TimeSpan.FromMilliseconds(700),
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
    /// The one case where the loop's answer is itself the trigger for the real screen, rather than a
    /// stray line.
    /// </summary>
    /// <remarks>
    /// Several real games gate their whole connect screen behind an ANSI keystroke prompt; the probe
    /// now sends the explicit letter LoginPromptGate says answers a colour question ("y"), not a blank
    /// line — see AColourGateThatRequiresAnExplicitLetterIsAnsweredCorrectly below for why a blind
    /// Return was never enough against every real server.
    /// </remarks>
    [Test]
    public async Task AScreenBehindAColourQuestionIsTheConnectScreen()
    {
        await using var game = new FakeGame
        {
            BannerTail = "Do you want ANSI? (Y/n) ",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["y"] =
                    "Ansi enabled!\r\nWelcome to Adventures Unlimited\r\n"
                    + "Based on CircleMUD 3.0, created by Jeremy Elson\r\n"
                    + "Players Currently Online: 7\r\n",
            },
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

    /// <summary>
    /// A server that requires the literal letter, not a blank default — a blind Return against this
    /// fixture would just see the same question echoed back, which is what production stored before
    /// this fix (see docs/login-prompt-scan/pre_login_prompts_report.md, cthulhumud/arcadia-mud-style
    /// strict colour gates).
    /// </summary>
    [Test]
    public async Task AColourGateThatRequiresAnExplicitLetterIsAnsweredCorrectly()
    {
        await using var game = new FakeGame
        {
            BannerTail = "Do you want ANSI color (Y/N)?",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["y"] = "Welcome to Arcadia MUD\r\nBased on Merc 2.1\r\n",
            },
            WhoReply = "Illegal name, try again.\r\nName: \r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Banner).Contains("Welcome to Arcadia MUD");
        await Assert.That(game.Received).Contains("y");
    }

    /// <summary>
    /// A press-enter gate the old BannerGate never recognised — the real screen behind it was thrown
    /// away as flush residue and the raw "Press Enter..." line stored as the connect screen instead.
    /// </summary>
    [Test]
    public async Task APressEnterGateRevealsTheRealScreenBehindIt()
    {
        await using var game = new FakeGame
        {
            BannerTail = "Press Enter to log in...",
            BlankLineReply = "Rites of Passage\r\nA game of legend.\r\n",
            WhoReply = "Illegal name, try again.\r\nName: \r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Banner).Contains("Rites of Passage");
        await Assert.That(result.Banner).DoesNotContain("Illegal name");
    }

    /// <summary>
    /// Two gates in a row — colour, then a press-enter — both answered before the real screen is
    /// treated as settled. Proves the loop, not just one round of it.
    /// </summary>
    [Test]
    public async Task StackedGatesAreAnsweredInOrder()
    {
        await using var game = new FakeGame
        {
            BannerTail = "Do you want ANSI? (Y/n)",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["y"] = "Ansi enabled!\r\nPress Enter to continue...",
            },
            BlankLineReply = "Welcome to New Haven\r\n",
            WhoReply = "Illegal name, try again.\r\nName: \r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Banner).Contains("Welcome to New Haven");
        await Assert.That(game.Received).Contains("y");
    }

    /// <summary>
    /// A misclassified/runaway gate must not be able to spin the probe past MaxPromptRounds.
    /// </summary>
    [Test]
    public async Task ARepeatingGateStopsAtTheRoundBound()
    {
        await using var game = new FakeGame
        {
            BannerTail = "Do you want ANSI? (Y/n)",
            // "y" always gets the same question back — an adversarial/broken server that never
            // actually accepts an answer. The loop must still terminate.
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["y"] = "Do you want ANSI? (Y/n)",
            },
        };

        var options = Fast() with { MaxPromptRounds = 2 };
        var result = await new TelnetProbe(options).ProbeAsync(game.Target);

        // Bounded: exactly MaxPromptRounds "y"s went out, not an unbounded stream of them.
        await Assert.That(game.Received.Count(line => line == "y")).IsEqualTo(2);
    }

    /// <summary>
    /// A gated DIKU must not lose its <c>WHO</c> to the flush line that answering the gate made
    /// redundant.
    /// </summary>
    /// <remarks>
    /// Measured against <c>mud.arcadia.net:4000</c>, which asks a colour question and is a DIKU
    /// descendant. Before the prompt loop existed, the flush line *was* the answer to the question, so
    /// the session survived it and <c>WHO</c> was asked. Once the loop answers with an explicit "y",
    /// the game paints its screen and sits at its name prompt — where the flush that follows is read
    /// as a goodbye, and the probe recorded <c>NotAsked</c> where it used to record an attempt. The
    /// answer already flushed the negotiation residue the flush line exists for, so sending a second
    /// one buys nothing and costs the count.
    /// </remarks>
    [Test]
    public async Task AnsweringAGateSpendsTheFlushSoADikuKeepsItsWho()
    {
        await using var game = new FakeGame
        {
            BannerTail = "Do you want ANSI? (Y/n) ",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["y"] = "Welcome to Arcadia!\r\nBy what name shall you be known? ",
            },
            HangsUpOnBlankLine = true,
            WhoReply = "Player Name        On For Idle\r\n7 Players logged in, 22 record, no maximum.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Banner).Contains("Welcome to Arcadia!");

        // The session is still alive to be asked, because no blank line went to the name prompt.
        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.Count);
        await Assert.That(result.Who.Count).IsEqualTo(7);
    }

    /// <summary>
    /// A server that throttles its login-screen commands still has its <c>WHO</c> read, rather than
    /// having the answer land in the next question's window.
    /// </summary>
    /// <remarks>
    /// Measured on twyst.org:3333 and rupert.twyst.org:6666, two EW-too talkers that answer WHO after
    /// a fixed 5.05s — identical to two decimal places, so it is a deliberate throttle rather than
    /// network weather. Under one grace for every phase the probe gave up at 2.5s, sent INFO, and the
    /// WHO table arrived inside the INFO window: the count was lost *and* a WHO roster was filed as
    /// the game's INFO block. WhoGrace buys the count back without slowing a game that answers
    /// promptly, since a phase that produces a line settles on QuietPeriod instead.
    /// </remarks>
    [Test]
    public async Task AThrottledWhoIsStillReadRatherThanLandingInTheNextPhase()
    {
        await using var game = new FakeGame
        {
            Banner = "Welcome to the talker.\r\n",
            BannerTail = "Please enter a name: ",
            WhoDelay = TimeSpan.FromMilliseconds(450),
            WhoReply = "Player Name        On For Idle\r\n7 Players logged in, 22 record, no maximum.\r\n",
            InfoReply = "This is the INFO block, and it is not a WHO table.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.Count);
        await Assert.That(result.Who.Count).IsEqualTo(7);

        // The other half of the defect: the roster must not be filed as the INFO block.
        await Assert.That(result.Info ?? string.Empty).DoesNotContain("Players logged in");
    }

    /// <summary>
    /// A gate on a server that does not implement telnet at its login screen is still answered, and
    /// the negotiation residue still gets cleared.
    /// </summary>
    /// <remarks>
    /// The worry the round loop has to survive: such a server takes our IAC bytes as typing, so the
    /// first line we send arrives garbage-prefixed and the gate answer does not match. Skipping the
    /// blank flush after sending an answer would then leave both problems in place. It does not,
    /// because that first line still flushed the residue the same way the blank one would have, and
    /// the loop gets another round — the second "y" arrives clean and opens the screen.
    /// </remarks>
    [Test]
    public async Task AGateOnAServerThatSwallowsNegotiationIsStillAnswered()
    {
        await using var game = new FakeGame
        {
            SwallowsNegotiationAsText = true,
            Banner = "Do you want ANSI color (Y/N)?",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["y"] = "Welcome to the game behind the question\r\n",
            },
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Banner).Contains("Welcome to the game behind the question");
    }

    /// <summary>
    /// Answering a charset menu's UTF-8 option is a request, not a fact — WireEncoding still
    /// independently proves the encoding from the bytes that actually arrive afterward (rule 5).
    /// </summary>
    [Test]
    public async Task AnsweringTheCharsetMenuLetsWireEncodingProveUtf8()
    {
        await using var game = new FakeGame
        {
            BannerTail = "1. KOI8-U\n2. ALT (CP866)\n3. WIN (CP1251)\n4. ISO (ISO-8859-5)\n5. MAC\n"
                + "6. Translit\n7. UTF-8\nPlease select your Ukrainian or Russian codepage: ",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["7"] = "Ласкаво просимо до Dreamland\r\n",
            },
            WhoReply = "Illegal name, try again.\r\nName: \r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Received).Contains("7");
        await Assert.That(result.Banner).Contains("Dreamland");
        await Assert.That(result.ReadAs).IsEqualTo("utf-8");
        await Assert.That(result.CharsetSource).IsEqualTo(WireCharset.Proven);
    }

    /// <summary>
    /// Selecting a who's-online menu option feeds the exact same WhoReading pipeline the literal WHO
    /// command does — PresenceChoice.From does not need to know which route produced it.
    /// </summary>
    [Test]
    public async Task AWhosOnlineMenuOptionIsHarvestedAsTheWhoReading()
    {
        await using var game = new FakeGame
        {
            BannerTail = "(C)onnect\r\n(N)ew character\r\nW - See who is online\r\n(Q)uit",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["W"] = "There are 12 players connected.\r\n",
            },
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Received).Contains("W");
        await Assert.That(result.Who.HasCount).IsTrue();
        await Assert.That(result.Who.Count).IsEqualTo(12);
        // The menu itself is this game's actual connect screen and stays in Banner; the roster
        // reply harvested from selecting "W" must not pollute it.
        await Assert.That(result.Banner).Contains("who is online");
        await Assert.That(result.Banner).DoesNotContain("12 players connected");
    }

    /// <summary>
    /// Once the menu has already answered WHO, the later literal WHO phase must not run and
    /// overwrite a good reading with whatever a stray "WHO" typed at this screen produces.
    /// </summary>
    [Test]
    public async Task TheLiteralWhoPhaseIsSkippedOnceTheMenuAlreadyAnsweredIt()
    {
        await using var game = new FakeGame
        {
            BannerTail = "(C)onnect\r\nW - See who is online\r\n(Q)uit",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["W"] = "There are 12 players connected.\r\n",
                // If the probe wrongly also sent the literal word WHO at this menu, FakeGame's ordinary
                // WHO handler would reply with this and the count would be corrupted to 0.
                ["WHO"] = "That is not a valid choice.\r\n",
            },
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Who.Count).IsEqualTo(12);
        await Assert.That(game.Received).DoesNotContain("WHO");
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

    /// <summary>
    /// Wiring proof for TelnetNegotiationCore PR #84 (v2.8.3): once MSDP negotiates, the probe asks
    /// for <c>PLAYERS</c> and captures whatever comes back, through <c>SendMSDPCommand</c> and the
    /// new <c>OnMSDPMessage</c> hook.
    /// </summary>
    /// <remarks>
    /// <see cref="MsdpTestServer"/> is TelnetNegotiationCore itself, run in Server mode — not a
    /// hand-rolled byte parser reimplementing MSDP's wire framing, which is exactly the kind of
    /// compensating logic CLAUDE.md says belongs upstream rather than here. The library already
    /// implements both ends of the protocol; this proves the client side against the real server side.
    /// <para>
    /// Live testing across the DIKU/ROM/EmpireMUD-family and custom-coded servers sampled for
    /// <c>docs/codebase-survey-2026-07-30.md</c> found nothing exposing a pre-login player-count
    /// variable, so this fake exists to prove the request/response plumbing works end to end
    /// (negotiate, ask, capture), not to claim any real server answers this way.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AServerOfferingMsdpIsAskedForPlayersAndTheAnswerIsCaptured()
    {
        await using var server = new MsdpTestServer { PlayersReply = "7" };

        var result = await new TelnetProbe(Fast()).ProbeAsync(server.Target);

        await Assert.That(server.ReceivedMessages).Count().IsEqualTo(1);
        var sent = JsonSerializer.Deserialize<Dictionary<string, string>>(
            server.ReceivedMessages[0]);
        await Assert.That(sent!["SEND"]).IsEqualTo("PLAYERS");

        await Assert.That(result.Negotiation.Supported).Contains("MSDP");
        await Assert.That(result.Negotiation.MsdpMessages).IsNotEmpty();

        var received = JsonSerializer.Deserialize<Dictionary<string, string>>(
            result.Negotiation.MsdpMessages[0]);
        await Assert.That(received!["PLAYERS"]).IsEqualTo("7");
    }

    /// <summary>
    /// The more realistic case, per the live sample: MSDP negotiates but the server never answers
    /// <c>SEND PLAYERS</c> because it never advertised that variable. Nothing times out waiting for an
    /// answer that was never coming, and nothing is fabricated in its place (rule 4) — including
    /// <c>Supported</c> itself. TelnetNegotiationCore's <c>OnEnabledAsync</c> (see <c>Watched.Msdp</c>
    /// and <c>Build</c>'s remarks) is wired to a manual enable/disable API that nothing in negotiation
    /// ever calls, so it is not a signal this codebase can read as "the server offered MSDP" — the only
    /// signal that is, an MSDP message actually arriving, never arrives here. <c>MSDP</c> is absent
    /// from <c>Supported</c> for the same reason a game that never speaks is absent from any other
    /// protocol's capability set: not observed, not claimed.
    /// </summary>
    [Test]
    public async Task AServerThatNegotiatesMsdpButHasNoPlayersVariableAnswersNothing()
    {
        await using var server = new MsdpTestServer { PlayersReply = null };

        var result = await new TelnetProbe(Fast()).ProbeAsync(server.Target);

        await Assert.That(server.ReceivedMessages).Count().IsEqualTo(1);
        var sent = JsonSerializer.Deserialize<Dictionary<string, string>>(
            server.ReceivedMessages[0]);
        await Assert.That(sent!["SEND"]).IsEqualTo("PLAYERS");

        await Assert.That(result.Negotiation.Supported).DoesNotContain("MSDP");
        await Assert.That(result.Negotiation.MsdpMessages).IsEmpty();
    }

    /// <summary>
    /// The probe cannot know in advance whether a server offered MSDP — see the request's own remarks
    /// in <c>TelnetProbe.ProbeAsync</c> for why — so it is sent speculatively to every server, the same
    /// way MSSP is asked for by negotiation regardless of whether the far end implements it. A
    /// compliant server that never negotiated MSDP still recognises <c>IAC SB … IAC SE</c> as telnet
    /// subnegotiation framing (RFC 855) and discards the unknown option without incident, so nothing is
    /// recorded as supported and the rest of the session is unaffected.
    /// </summary>
    [Test]
    public async Task ATelnetCompliantServerThatDoesNotOfferMsdpIsUnaffectedByBeingAsked()
    {
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\n",
            WhoReply = "There are 5 players connected.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Negotiation.Supported).DoesNotContain("MSDP");
        await Assert.That(result.Negotiation.MsdpMessages).IsEmpty();
        await Assert.That(result.Who.Count).IsEqualTo(5);
        await Assert.That(result.Banner).Contains("Welcome to Nowhere");
    }

    /// <summary>
    /// The regression this design is built to avoid. chaos.caile.org:4444 (TinyMUSH) does not parse
    /// telnet at its login screen at all — <see cref="AServerThatBuffersOurNegotiationAsTextStillAnswersWho"/>
    /// covers the same shape for the client's own automatic negotiation replies. The MSDP request is
    /// sent before the probe's existing flush line for exactly this reason: whatever a server like this
    /// makes of six-plus bytes of raw subnegotiation it cannot parse lands in the same discarded window
    /// as any other stray reaction, and <c>WHO</c> comes back uncorrupted regardless.
    /// </summary>
    [Test]
    public async Task AServerThatSwallowsTelnetAsTextStillAnswersWhoDespiteTheMsdpRequest()
    {
        await using var game = new FakeGame
        {
            Banner = "Welcome to Nowhere\r\n",
            WhoReply = "0 Players logged in, 22 record, no maximum.\r\n",
            SwallowsNegotiationAsText = true,
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.Count);
        await Assert.That(result.Who.Count).IsEqualTo(0);
        await Assert.That(result.Negotiation.Supported).DoesNotContain("MSDP");
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
    [Arguments("Welcome to Nowhere\r\n")]
    // A gated screen, so the run includes the classified answers as well as the commands. Without
    // this the guarantee was only ever checked on a session that never answered a prompt.
    [Arguments("Do you want ANSI color (Y/N)?")]
    [Arguments("1. KOI8-U\n2. ALT (CP866)\n7. UTF-8\nEnter Charset: ")]
    [Arguments("w - who is playing at the moment")]
    public async Task TheProbeNeverSendsAnythingButItsPermittedCommands(string banner)
    {
        // Everything the probe types must be a permitted command, a bounded prompt answer, or an
        // empty line — nothing that logs in, creates, or changes anything.
        await using var game = new FakeGame
        {
            Banner = banner,
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

            var permitted = TelnetProbe.PermittedCommands.Contains(spoken)
                || TelnetProbe.IsPermittedPromptAnswer(spoken);

            await Assert.That(permitted).IsTrue();
        }
    }

    /// <summary>
    /// Nothing <see cref="LoginPromptGate"/> can classify produces an answer the probe is not allowed
    /// to type — the bound holds over the whole corpus, not just the categories that exist today.
    /// </summary>
    [Test]
    [Arguments("Do you want ANSI color (Y/N)?")]
    [Arguments("Screen reader user? Yes or No")]
    [Arguments("Press Enter to log in...")]
    [Arguments("您是否是中小学学生或年龄更小？(yes/no)")]
    [Arguments("1. KOI8-U\n2. ALT (CP866)\n7. UTF-8\nEnter Charset: ")]
    [Arguments("w - who is playing at the moment")]
    [Arguments("[2]....See who is currently logged in.")]
    public async Task EveryClassifiedAnswerIsWithinTheWireBound(string banner)
    {
        var answer = LoginPromptGate.Classify(banner);

        await Assert.That(answer).IsNotNull();
        await Assert.That(TelnetProbe.IsPermittedPromptAnswer(answer!.Answer)).IsTrue();

        // And it is emphatically not a command that would log in or create.
        await Assert.That(answer.Answer).DoesNotContain("connect", StringComparison.OrdinalIgnoreCase);
        await Assert.That(answer.Answer).DoesNotContain("create", StringComparison.OrdinalIgnoreCase);
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

        /// <summary>
        /// How long the server sits on a <c>WHO</c> before answering it — the EW-too talkers throttle
        /// login-screen commands by a fixed five seconds.
        /// </summary>
        public TimeSpan WhoDelay { get; init; }

        public string? BlankLineReply { get; init; }

        /// <summary>
        /// What a specific command gets back, keyed case-insensitively — the general-purpose sibling
        /// of <see cref="BlankLineReply"/>, for fixtures that need to prove the probe sent a specific
        /// answer (e.g. "y" to a colour question) rather than a blank line.
        /// </summary>
        public IReadOnlyDictionary<string, string> Replies { get; init; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

                if (ClosesImmediately)
                {
                    client.Client.Shutdown(SocketShutdown.Both);
                    return;
                }

                if (SwallowsNegotiationAsText)
                {
                    await ServeRawAsync(client);
                }
                else
                {
                    await ServeOverTelnetAsync(client);
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

        /// <summary>
        /// The naive path — no telnet awareness at all, matching a server that does not implement RFC
        /// 854 at its login screen and reads our negotiation bytes as typed text. TinyMUSH's shape,
        /// and the one case in this fixture that must not go through TelnetNegotiationCore: a real
        /// interpreter is telnet-compliant by construction, so it cannot stand in for a peer whose
        /// entire defining trait is that it isn't.
        /// </summary>
        private async Task ServeRawAsync(TcpClient client)
        {
            await using var stream = client.GetStream();

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

                pending.Append(Encoding.Latin1.GetString(buffer, 0, read));

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
                    farewell = !await HandleAsync(line, text => new ValueTask(SendAsync(stream, text)));
                }

                if (farewell)
                {
                    client.Client.Shutdown(SocketShutdown.Both);
                    break;
                }
            }
        }

        /// <summary>
        /// The compliant path — a real TelnetNegotiationCore Server-mode interpreter, no plugins
        /// attached, in place of a hand-rolled reimplementation of RFC 855 framing. The same reasoning
        /// <see cref="MsdpTestServer"/> already applies to MSDP specifically applies to every option
        /// the client might negotiate here: a real interpreter reassembles a subnegotiation frame
        /// correctly regardless of how the peer's writes land across TCP reads, which a hand-rolled
        /// scanner over one buffer at a time cannot promise and, once, did not (a request sent as its
        /// own write ahead of the probe's main flush could arrive as two reads, and the old scanner
        /// read the frame's tail back as literal text). No plugin is attached because none is
        /// needed — <c>OnSubmit</c> only ever sees genuine typed text once the interpreter has done
        /// its own job, whether or not it recognises what was negotiated.
        /// </summary>
        private async Task ServeOverTelnetAsync(TcpClient client)
        {
            var built = await new TelnetInterpreterBuilder()
                .UseMode(TelnetInterpreter.TelnetMode.Server)
                .UseLogger(NullLogger.Instance)
                .OnSubmit(async (bytes, encoding, telnet) =>
                {
                    var line = encoding.GetString(bytes).TrimEnd('\r');
                    var alive = await HandleAsync(line, text => WriteRawAsync(telnet, text));

                    if (!alive)
                    {
                        client.Client.Shutdown(SocketShutdown.Both);
                    }
                })
                .BuildAndStartAsync(client, _stopping.Token);

            await using var telnet = built.Interpreter;

            if (Preamble is not null)
            {
                await WriteRawAsync(telnet, Preamble);
            }

            if (BannerDelay > TimeSpan.Zero)
            {
                await Task.Delay(BannerDelay, _stopping.Token);
            }

            await WriteRawAsync(telnet, Banner);
            if (BannerTail is not null)
            {
                await WriteRawAsync(telnet, BannerTail);
            }

            await built.ReadTask;
        }

        /// <summary>
        /// Writes exactly the bytes given, with nothing appended. <c>TelnetInterpreter.SendAsync</c>
        /// always adds a trailing CR LF (right for the client's own bare one-line commands, wrong
        /// here — <see cref="BannerTail"/> and <see cref="WhoTail"/> exist specifically to test a line
        /// the peer never terminated, and <see cref="Banner"/> is already a complete multi-line block
        /// with whatever terminators the test gave it). <c>WriteToNetworkAsync</c> is the primitive
        /// every <c>Send*</c> method builds on before adding its own framing.
        /// </summary>
        private static ValueTask WriteRawAsync(TelnetInterpreter telnet, string text) =>
            text.Length == 0 ? default : telnet.WriteToNetworkAsync(Encoding.Latin1.GetBytes(text));

        /// <summary>Handles one already-assembled line, and says whether the connection survives it.</summary>
        private async Task<bool> HandleAsync(string line, Func<string, ValueTask> reply)
        {
            lock (_received)
            {
                _received.Add(line);
            }

            // A line carrying anything the server did not recognise — including our negotiation
            // bytes, when it is the kind of server that swallows them — is not a command it has.
            // Unreachable via ServeOverTelnetAsync, where a submitted line is never anything else,
            // but the check is the same either way rather than a second copy of what "clean" means.
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
                    await reply(BlankLineReply);
                }

                return true;
            }

            if (!clean)
            {
                // What TinyMUSH does: redisplay the connect screen and answer nothing.
                await reply(Banner);
                return true;
            }

            if (Replies.TryGetValue(command, out var configured))
            {
                await reply(configured);
                return true;
            }

            if (command.Equals("WHO", StringComparison.OrdinalIgnoreCase))
            {
                if (WhoDelay > TimeSpan.Zero)
                {
                    await Task.Delay(WhoDelay, _stopping.Token);
                }

                await reply(WhoReply);
                if (WhoTail is not null)
                {
                    await reply(WhoTail);
                }

                return !HangsUpAfterWho;
            }

            if (command.Equals("INFO", StringComparison.OrdinalIgnoreCase))
            {
                if (InfoReply is not null)
                {
                    await reply(InfoReply);
                }

                return true;
            }

            if (command.Equals("VERSION", StringComparison.OrdinalIgnoreCase))
            {
                if (VersionReply is not null)
                {
                    await reply(VersionReply);
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

    /// <summary>
    /// The server side of an MSDP round trip, built from TelnetNegotiationCore's own Server-mode
    /// interpreter rather than a hand-rolled reimplementation of MSDP's wire framing.
    /// </summary>
    /// <remarks>
    /// Negotiation (<c>IAC WILL MSDP</c>, unprompted, and the <c>IAC DO MSDP</c> that answers it) is
    /// entirely the library's own — <c>MSDPProtocol.ConfigureStateMachine</c> registers it as an
    /// initial negotiation in Server mode, the same as it does for every other option. This exists to
    /// answer <c>SEND PLAYERS</c> the way a real MSDP-speaking server would, over the actual
    /// subnegotiation channel, so the test proves the client side against protocol-correct behaviour
    /// rather than against a guess at what that behaviour is.
    /// </remarks>
    private sealed class MsdpTestServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _serving;
        private readonly List<string> _received = [];

        public MsdpTestServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _serving = ServeAsync();
        }

        /// <summary>
        /// What to answer a <c>SEND PLAYERS</c> request with. Null means MSDP negotiates but the
        /// server never answers — the shape every server sampled in
        /// <c>docs/codebase-survey-2026-07-30.md</c> turned out to have, pre-login.
        /// </summary>
        public string? PlayersReply { get; init; }

        public ProbeTarget Target => new(
            IPAddress.Loopback.ToString(),
            ((IPEndPoint)_listener.LocalEndpoint).Port);

        /// <summary>Every raw MSDP message this server received, exactly as the library delivered it.</summary>
        public IReadOnlyList<string> ReceivedMessages
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

                var msdp = new MSDPProtocol();
                msdp.OnMSDPMessage((telnet, message) =>
                {
                    lock (_received)
                    {
                        _received.Add(message);
                    }

                    return HandleAsync(telnet, message);
                });

                var built = await new TelnetInterpreterBuilder()
                    .UseMode(TelnetInterpreter.TelnetMode.Server)
                    .UseLogger(NullLogger.Instance)
                    .OnSubmit((_, _, _) => ValueTask.CompletedTask)
                    .AddPlugin(msdp)
                    .BuildAndStartAsync(client, _stopping.Token);

                await using var telnet = built.Interpreter;
                await built.ReadTask;
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
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>Answers a <c>SEND PLAYERS</c> request, over MSDP's own wire shape, if asked to.</summary>
        /// <remarks>
        /// Answered with <c>SendMSDPCommand</c> — the same client-side method under test, reused here
        /// because it is mode-agnostic (it is a raw <c>IAC SB MSDP MSDP_VAR … MSDP_VAL … IAC SE</c>
        /// write with no client/server branch) and it is the shape MSDP defines for one variable's
        /// value, which is what a <c>SEND PLAYERS</c> reply is. <c>MSDPLibrary.Report</c> — the
        /// function <see cref="Handlers.MSDPServerHandler"/> uses — wraps a JSON *object* in
        /// <c>MSDP_TABLE_OPEN</c>/<c>CLOSE</c>, which is right for reporting a table of variables but
        /// wrong for this flat single-pair answer, so it is not used here.
        /// </remarks>
        private async ValueTask HandleAsync(TelnetInterpreter telnet, string message)
        {
            if (PlayersReply is null)
            {
                return;
            }

            var request = JsonSerializer.Deserialize<Dictionary<string, string>>(message);
            if (request is not { } fields
                || !fields.TryGetValue("SEND", out var wanted)
                || !wanted.Equals("PLAYERS", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await telnet.SendMSDPCommand("PLAYERS", PlayersReply);
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
