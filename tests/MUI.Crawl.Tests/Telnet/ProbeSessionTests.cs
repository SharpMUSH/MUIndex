using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MUI.Crawl;
using TelnetNegotiationCore.Builders;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Models;
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
        MsspSettleGrace = TimeSpan.FromMilliseconds(400),
        PromptHold = TimeSpan.FromMilliseconds(120),
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
        // The probe's own flush line ends the session on some servers, and the WHO after it writes
        // into a dead socket. The banner is already in hand when that happens — reporting the whole
        // probe as Failed would record our own flush line as a fact about their server (rule 5).
        //
        // The flush now only reaches a server that has not demonstrably parsed our negotiation, and
        // for exactly those servers it is not an empty line at all: it carries the IAC bytes they had
        // been holding as typing. So this models what such a server really receives — an
        // unrecognisable line at its name prompt — and drops us on it, the way a DIKU refusing an
        // illegal name does.
        await using var game = new FakeGame
        {
            SwallowsNegotiationAsText = true,
            Banner = "Welcome to Mortal Realms\r\nMrMud 1.4\r\n",
            BannerTail = "Who art thou: ",
            HangsUpOnUnrecognisedLine = true,
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
    /// A prompt the server ends with <c>IAC GA</c> instead of a newline is still captured as text.
    /// </summary>
    /// <remarks>
    /// TelnetNegotiationCore 2.11 (#90) reads <c>IAC GA</c> as the prompt boundary RFC 854 defines,
    /// which is the shape a DIKU login prompt actually arrives in: "What is your name:" with no line
    /// ending and a Go-Ahead behind it. The payload has to reach us as ordinary line content — the
    /// guard that stops a busy DIKU being read as a measured zero works by recognising that prompt,
    /// so a boundary marker that consumed the text instead of delimiting it would take the guard with
    /// it. Written on the raw-socket path so the GA bytes (0xFF 0xF9) go on the wire exactly as
    /// stated, rather than through a server-side interpreter that might reframe them.
    /// </remarks>
    [Test]
    public async Task APromptEndedByGoAheadIsCapturedAsText()
    {
        const string goAhead = "\u00ff\u00f9";

        await using var game = new FakeGame
        {
            SwallowsNegotiationAsText = true,
            Banner = "Welcome to Mortal Realms\r\nMrMud 1.4\r\n",
            BannerTail = $"By what name do you wish to be known? {goAhead}",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Banner).Contains("Welcome to Mortal Realms");

        // The payload, not just the lines before it.
        await Assert.That(result.Banner).Contains("By what name do you wish to be known?");

        // And the marker itself is a measurement, not something to store as screen content.
        await Assert.That(result.Banner).DoesNotContain(goAhead);

        // The load-bearing half: this must pass *because the Go-Ahead was understood*, not because
        // our own end-of-phase flush happened to rescue the line. If GA were being ignored, the text
        // would still arrive by that route and every assertion above would pass while #90 did nothing.
        await Assert.That(result.Negotiation.SendsPromptMarkers).IsTrue();
    }

    /// <summary>
    /// The real shape a Go-Ahead arrives in, taken off the wire from <c>tdome.nukefire.org:4000</c>.
    /// </summary>
    /// <remarks>
    /// NukeFire sends no GA in its banner. It sends one in reply to <c>WHO</c>: reading the word as a
    /// character name, it answers <c>Password: </c> with no line ending, hides the reply with
    /// <c>IAC WILL ECHO</c>, and marks the boundary with <c>IAC GA</c> — captured verbatim as
    /// <c>50 61 73 73 77 6f 72 64 3a 20 ff fb 01 ff f9</c>. The GA is the only delimiter that line
    /// ever gets, which is precisely why it has to be read as one: the payload is what tells
    /// <see cref="WhoParser"/> the server ate our word instead of answering it, and that reading is
    /// what keeps a busy DIKU from being published as a measured zero.
    /// </remarks>
    [Test]
    public async Task TheGoAheadOnAPasswordPromptIsReadAsABoundary()
    {
        // "Password: " then IAC WILL ECHO then IAC GA, byte for byte as NukeFire sends it.
        const string nukefireWhoReply = "Password: \u00ff\u00fb\u0001\u00ff\u00f9";

        await using var game = new FakeGame
        {
            SwallowsNegotiationAsText = true,
            Banner = "Welcome to:\r\nNukeFire : Beyond THUNDERDOME\r\n",
            BannerTail = "What's your name, freejack?",
            WhoReply = nukefireWhoReply,
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Negotiation.SendsPromptMarkers).IsTrue();

        // The payload reached the parser, which is how the login prompt is recognised for what it is
        // rather than counted as a roster row.
        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.LoginPrompt);
    }

    /// <summary>
    /// A server that has demonstrably parsed our negotiation is not sent the residue flush, so a DIKU
    /// that would read it as a goodbye survives to be asked <c>WHO</c>.
    /// </summary>
    /// <remarks>
    /// The flush clears our own IAC bytes out of the line buffer of a server that did not interpret
    /// them at its login screen. A server that negotiated an option interpreted them, so there is no
    /// residue for it to clear — and sending it anyway costs the whole session on the DIKU family,
    /// which reads an empty line at a name prompt as a goodbye. Measured across sixteen live games:
    /// none of the eight that negotiate an option needed the flush, while four of the twelve that
    /// negotiate nothing could not answer <c>WHO</c> without it (see docs/codebase-survey-2026-07-30.md).
    /// <c>tdome.nukefire.org:4000</c> is the worked example — it negotiates GMCP, MSDP and MSSP, and
    /// before this the probe hung up on itself before ever asking.
    /// </remarks>
    [Test]
    public async Task AServerThatParsedOurNegotiationIsNotSentTheFlush()
    {
        await using var game = new FakeGame
        {
            AnnouncesMssp = true,

            // A report with no PLAYERS in it, so this stays a test about the flush. A game that
            // states its count is no longer asked WHO at all (see
            // AGameThatPublishedItsCountOverMsspIsNotTypedAtAgain), which would leave the assertions
            // below reading NotAsked for the right reason and proving nothing about the branch here.
            MsspPlayers = null,
            Banner = "Welcome to:\r\nNukeFire : Beyond THUNDERDOME\r\n",
            BannerTail = "What's your name, freejack?",
            HangsUpOnBlankLine = true,
            WhoReply = "Player Name        On For Idle\r\n7 Players logged in, 22 record, no maximum.\r\n",
        };

        // A longer QuietPeriod than Fast() gives, for one specific reason. This test's precondition is
        // that negotiation has landed *before* the flush decision reads seen.Supported — and the probe
        // deliberately does not guarantee that ordering, since a server that negotiates late is
        // treated exactly like one that negotiated nothing. Fast()'s 120ms banner settle sits inside
        // the margin a loaded CI runner can take to finish the MSSP exchange (Windows' default timer
        // granularity is ~15.6ms, so a 15ms poll is really 15-31ms and 120ms buys only a handful of
        // them). When it lost that race the flush went out, HangsUpOnBlankLine ended the session, and
        // WHO came back NotAsked — a red build about runner timing rather than about the branch under
        // test. Observed once on windows-latest and green on the retry of the same commit.
        var options = Fast() with { QuietPeriod = TimeSpan.FromMilliseconds(500) };

        var result = await new TelnetProbe(options).ProbeAsync(game.Target);

        // Diagnostic first: if this is empty the fixture never negotiated, which is a different
        // problem from the flush decision.
        await Assert.That(result.OfferedOptions.Count).IsGreaterThan(0);

        // Then the mechanism, before its consequence: the blank line is the thing under test, and a
        // failure here names the cause outright instead of leaving WHO's confidence to imply it.
        await Assert.That(game.Received).DoesNotContain(string.Empty);

        // FakeGame's telnet mode negotiates, so the flush is withheld and the goodbye never happens.
        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.Count);
        await Assert.That(result.Who.Count).IsEqualTo(7);

        // Pin *which* branch withheld the flush. All three of `alreadyFlushed`'s sources and
        // `parsedOurNegotiation` suppress it, and only the last is under test here — so assert nothing
        // but the permitted commands went out. Were this banner ever classified as a prompt or a menu,
        // the probe would answer it, the flush would be withheld for the other reason, and this test
        // would stay green while the negotiation branch it guards quietly stopped being exercised.
        await Assert.That(game.Received.Where(line => line.Trim().Length > 0)
            .All(line => TelnetProbe.PermittedCommands.Contains(line.Trim()))).IsTrue();
    }

    /// <summary>
    /// A game that has already stated its player count is not typed at.
    /// </summary>
    /// <remarks>
    /// Asked for by an operator who found our <c>WHO</c> in their login-screen logs beside an MSSP
    /// report already carrying <c>PLAYERS</c>. They are right: a question whose answer the server has
    /// volunteered is not a measurement, it is noise on somebody else's console. <c>INFO</c> and
    /// <c>VERSION</c> are unaffected — they ask something MSSP has not answered here, and narrowing
    /// them is a separate decision from this one.
    /// </remarks>
    [Test]
    public async Task AGameThatPublishedItsCountOverMsspIsNotTypedAtAgain()
    {
        await using var game = new FakeGame
        {
            AnnouncesMssp = true,
            MsspPlayers = 42,
            Banner = "Welcome to Mortal Realms\r\nMrMud 1.4\r\n",
            BannerTail = "Who art thou: ",

            // Deliberately a different number from the report: were WHO asked anyway, the reading
            // would be 7 and the assertions below would say so rather than quietly agreeing.
            WhoReply = "Player Name        On For Idle\r\n7 Players logged in, 22 record, no maximum.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.MsspOutcome).IsEqualTo(MsspOutcome.Received);

        // The measurement that matters to the complaint: the word never went on the wire.
        await Assert.That(game.Received.Select(line => line.Trim())).DoesNotContain(TelnetProbe.WhoCommand);

        // And it is recorded as not asked, not as an unreadable answer — §5.4's distinction.
        await Assert.That(result.Who.Attempted).IsFalse();
        await Assert.That(result.Who.HasCount).IsFalse();

        // Scope: this narrows WHO and nothing else.
        await Assert.That(game.Received.Select(line => line.Trim())).Contains(TelnetProbe.InfoCommand);
        await Assert.That(game.Received.Select(line => line.Trim())).Contains(TelnetProbe.VersionCommand);
    }
    /// A game whose report names it, names its engine and states a count is asked nothing at all.
    /// </summary>
    /// <remarks>
    /// The shape <c>playdecay.com:3003</c> sends. Its operator raised this, and the old probe typed
    /// <c>WHO</c> at "By what name do you wish to be known?", had it taken as a character name, was
    /// asked for a password, and sent <c>INFO</c> as the password — reproducibly, on every crawl.
    /// </remarks>
    [Test]
    public async Task AGameWhoseReportDescribesItIsAskedNothingAtAll()
    {
        await using var game = new FakeGame
        {
            AnnouncesMssp = true,
            MsspPlayers = 4,
            MsspExtras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CODEBASE"] = "FluffOS v2025",
            },
            Banner = "Welcome to Mortal Realms\r\nMrMud 1.4\r\n",
            BannerTail = "By what name do you wish to be known? ",
            WhoReply = "Player Name        On For Idle\r\n7 Players logged in, 22 record, no maximum.\r\n",
            InfoReply = "### Begin INFO 1\r\nName: Mortal Realms\r\nConnected: 7\r\n### End INFO\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.MsspOutcome).IsEqualTo(MsspOutcome.Received);

        foreach (var command in TelnetProbe.PermittedCommands)
        {
            await Assert.That(game.Received.Select(line => line.Trim())).DoesNotContain(command);
        }

        // Nothing was asked, so nothing was answered — and the count still comes from the report.
        await Assert.That(result.Who.Attempted).IsFalse();
        await Assert.That(result.Info).IsNull();
        await Assert.That(result.Version).IsNull();
        await Assert.That(MsspPresence.Read(result.Mssp).Count).IsEqualTo(4);
    }

    /// <summary>
    /// A report that states a count but names no engine still gets <c>INFO</c> and <c>VERSION</c>.
    /// </summary>
    /// <remarks>
    /// The two questions are gated separately because they ask different things. Silencing all three
    /// on a count alone would cost the codebase reading that <c>game.convergencemush.org:10000</c> —
    /// RhostMUSH, no MSSP at all — depends on, and every game whose report is half-filled.
    /// </remarks>
    [Test]
    public async Task ACountAloneDoesNotSilenceTheOtherTwoQuestions()
    {
        await using var game = new FakeGame
        {
            AnnouncesMssp = true,
            MsspPlayers = 4,
            Banner = "Welcome to Mortal Realms\r\nMrMud 1.4\r\n",
            BannerTail = "By what name do you wish to be known? ",
            InfoReply = "### Begin INFO 1\r\nName: Mortal Realms\r\nVersion: RhostMUSH 4.27.3\r\n### End INFO\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        // WHO is silenced — the count answered it — and the other two are not.
        await Assert.That(game.Received.Select(line => line.Trim())).DoesNotContain(TelnetProbe.WhoCommand);
        await Assert.That(game.Received.Select(line => line.Trim())).Contains(TelnetProbe.InfoCommand);
        await Assert.That(game.Received.Select(line => line.Trim())).Contains(TelnetProbe.VersionCommand);

        await Assert.That(LoginCommandReading.MeaningfulCodebase(result.Info, result.Version))
            .IsEqualTo("RhostMUSH 4.27.3");
    }

    /// <summary>
    /// A roster published in the report answers the question too, even with no <c>PLAYERS</c> beside
    /// it.
    /// </summary>
    /// <remarks>
    /// The shape <c>dead-souls.net:8000</c> sends — <c>WHO</c> once per player — with the stated
    /// count taken away, which is the case the roster rung exists for. A game that has published who
    /// is online has answered this probe's question as surely as one that published the number, and
    /// the count it yields is what <c>PresenceChoice</c> then has to publish (§5.2).
    /// </remarks>
    [Test]
    public async Task ARosterInTheReportAnswersTheQuestionWithoutAStatedCount()
    {
        await using var game = new FakeGame
        {
            AnnouncesMssp = true,
            MsspPlayers = null,
            MsspExtras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PLAYERNAMES"] = "Ninja, Cratylus, Joshua",
            },

            // Deliberately late, and this is the second thing the test proves. WILL MSSP lands during
            // the option handshake while the report is a second round trip, so the decision below can
            // run with the answer still in flight — which is not hypothetical: it sent WHO on
            // windows-latest against a server on the same machine before the probe learned to wait.
            MsspReportDelay = TimeSpan.FromMilliseconds(250),
            Banner = "Welcome to Mortal Realms\r\nMrMud 1.4\r\n",
            BannerTail = "Who art thou: ",
            WhoReply = "Player Name        On For Idle\r\n7 Players logged in, 22 record, no maximum.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.MsspOutcome).IsEqualTo(MsspOutcome.Received);
        await Assert.That(result.MsspField("PLAYERNAMES")).IsEqualTo("Ninja, Cratylus, Joshua");

        await Assert.That(game.Received.Select(line => line.Trim())).DoesNotContain(TelnetProbe.WhoCommand);
        await Assert.That(result.Who.Attempted).IsFalse();

        // Not asking implies publishing: the roster the probe stayed quiet for is a count.
        await Assert.That(MsspPresence.Read(result.Mssp).Count).IsEqualTo(3);
        await Assert.That(MsspPresence.Read(result.Mssp).Kind).IsEqualTo(MsspCountKind.Roster);
    }

    /// <summary>

    /// <summary>
    /// A connect screen that states the count does the same, when the session has other evidence
    /// this is a game at all.
    /// </summary>
    /// <remarks>
    /// The count is carried into <see cref="ProbeResult.BannerPlayerCount"/> rather than left behind:
    /// declining to ask and then publishing nothing would reach <c>PresenceChoice</c> as
    /// <c>who_not_offered</c> — <em>the game answers no pre-login WHO</em> — which would be our own
    /// decision written down as a fact about them.
    /// </remarks>
    [Test]
    public async Task AConnectScreenThatStatesTheCountIsNotAskedForItAgain()
    {
        await using var game = new FakeGame
        {
            // MSSP with no PLAYERS: the protocol signal is present, the count is not, so the screen
            // is the only thing that can be answering here.
            AnnouncesMssp = true,
            MsspPlayers = null,
            Banner = "Welcome to Mortal Realms\r\nPlayers Currently Online: 12\r\n",
            BannerTail = "Who art thou: ",
            WhoReply = "Player Name        On For Idle\r\n7 Players logged in, 22 record, no maximum.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Received.Select(line => line.Trim())).DoesNotContain(TelnetProbe.WhoCommand);
        await Assert.That(result.Who.Attempted).IsFalse();

        // Not asking implies publishing, which is the whole of why the count is passed forward.
        await Assert.That(result.BannerPlayerCount).IsEqualTo(12);
    }

    /// <summary>
    /// A screen count on its own is not enough, when a parseable <c>WHO</c> is the only thing that
    /// could show this is a game.
    /// </summary>
    /// <remarks>
    /// <c>MuLikeness</c> (§7.8) is what lists a submitted game without waiting on a claim, and for a
    /// server that negotiates nothing and publishes no MSSP, a parseable <c>WHO</c> is its one
    /// protocol-tier signal. Talking ourselves out of asking on the strength of a number
    /// pattern-matched from ASCII art would cost such a game its listing — so the banner rung buys
    /// silence only where the session has already shown a MU* protocol.
    /// </remarks>
    [Test]
    public async Task AScreenCountAloneDoesNotBuySilenceFromAServerThatNegotiatedNothing()
    {
        await using var game = new FakeGame
        {
            Banner = "Welcome to Mortal Realms\r\nPlayers Currently Online: 12\r\n",
            BannerTail = "Who art thou: ",
            WhoReply = "Player Name        On For Idle\r\n7 Players logged in, 22 record, no maximum.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        // Precondition, asserted rather than assumed: FakeGame's default telnet path attaches no
        // plugins, so it frames telnet correctly and agrees to nothing.
        await Assert.That(result.OfferedOptions).IsEmpty();

        await Assert.That(game.Received.Select(line => line.Trim())).Contains(TelnetProbe.WhoCommand);
        await Assert.That(result.Who.Count).IsEqualTo(7);
    }

    /// <summary>

    /// <summary>
    /// A compressed session is not corrupted by the probe settling its own phases.
    /// </summary>
    /// <remarks>
    /// The bug this fixture exists for. Every phase used to end by pushing a newline into
    /// <c>InterpretAsync</c> to shake loose a line the server never terminated — but that is the
    /// <em>inbound</em> channel, the one an MCCP inflater sits at the head of, so on a compressed
    /// session the byte was spliced into the middle of the peer's deflate stream. The inflater never
    /// recovered: every reply afterwards arrived shredded, with fragments of two of them overlaid.
    /// <para>
    /// Found on the five Evennia games in the catalogue, whose <c>CODEBASE</c> had been stored as
    /// <c>enniaA 5.0.1</c> and <c>enniaF 6.0.0 (rev ea0da3ed8)R ##D HRINFO0m</c> for months. Neither
    /// Evennia nor TelnetNegotiationCore was at fault: the same captured stream decodes perfectly
    /// through Python's zlib, through TNC's own inflate transform byte for byte, and through a plain
    /// TNC client against the live server.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ACompressedSessionIsNotShreddedByOurOwnSettling()
    {
        await using var game = new FakeGame
        {
            AnnouncesMccp = true,
            Banner = "Welcome to Mortal Realms\r\nMrMud 1.4\r\n",
            BannerTail = "By what name do you wish to be known? ",
            WhoReply = "Player Name        On For Idle\r\n7 Players logged in, 22 record, no maximum.\r\n",
            InfoReply = "### Begin INFO 1\r\nName: Mortal Realms\r\nConnected: 7\r\nVersion: Evennia 6.1.0\r\n### End INFO\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);

        // The precondition, asserted rather than assumed: this session really was compressed.
        await Assert.That(result.OfferedOptions).Contains("MCCP2");

        // Every reply intact, and read rather than merely present.
        await Assert.That(result.Who.Count).IsEqualTo(7);
        await Assert.That(result.Info).Contains("Name: Mortal Realms");
        await Assert.That(LoginCommandReading.MeaningfulCodebase(result.Info, result.Version))
            .IsEqualTo("Evennia 6.1.0");
        await Assert.That(LoginCommandReading.ConnectedPlayers(result.Info)).IsEqualTo(7);
    }

    /// <summary>
    /// And the unterminated line survives compression too, which is what the newline was for.
    /// </summary>
    /// <remarks>
    /// The fix is not "stop flushing on a compressed session" — that would trade one loss for
    /// another, and the guard that keeps a busy DIKU from reading as a measured zero depends on
    /// seeing exactly this kind of unterminated prompt. TelnetNegotiationCore 2.12.0's
    /// <c>PacketPatchProtocol</c> infers the boundary from silence on its own byte-processing loop,
    /// where the line buffer has one writer and nothing is pushed into the peer's stream.
    /// </remarks>
    [Test]
    public async Task AnUnterminatedPromptIsStillDeliveredOnACompressedSession()
    {
        await using var game = new FakeGame
        {
            AnnouncesMccp = true,
            Banner = "Welcome to Nowhere\r\nA quiet little place.\r\n",
            BannerTail = "By what name do you wish to be known? ",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.OfferedOptions).Contains("MCCP2");
        await Assert.That(result.Banner).Contains("By what name do you wish to be known?");
    }

    /// <summary>
    /// A server that has negotiated MSSP but whose report is still in flight is waited for, rather
    /// than having the flush read <c>Supported</c> at whatever instant it happens to still be empty.
    /// </summary>
    /// <remarks>
    /// The production bug this fixes: <c>capability.mssp.measured</c> flapping true/false across
    /// ordinary crawl cycles for the same DIKU-family games (God Wars Legends, GodWars: Apocalypse),
    /// which answer MSSP cleanly on every direct <c>mui-probe</c>. <c>WILL MSSP</c> negotiates on
    /// connect, same as it always has, but the report itself is a second round trip; before
    /// <c>MsspSettleGrace</c> existed, a server slow enough for that round trip to still be open when
    /// this flush decision ran was flushed exactly like one that had negotiated nothing at all — and
    /// this fixture also hangs up on that blank line, the DIKU shape that made the loss permanent
    /// rather than just late. <see cref="AServerThatParsedOurNegotiationIsNotSentTheFlush"/> covers the
    /// case where the report has already landed by the time this line runs; this covers the case
    /// where it hasn't yet, but is still coming.
    /// </remarks>
    [Test]
    public async Task AMsspReportStillInFlightIsWaitedForBeforeTheFlush()
    {
        await using var game = new FakeGame
        {
            AnnouncesMssp = true,

            // No PLAYERS, for the same reason as the test above — and here it also removes a race
            // this fixture would otherwise carry: whether the delayed report beats the WHO decision
            // is exactly the timing this test refuses to depend on.
            MsspPlayers = null,
            MsspReportDelay = TimeSpan.FromMilliseconds(200),
            Banner = "Welcome to Mortal Realms\r\nMrMud 1.4\r\n",
            BannerTail = "Who art thou: ",
            HangsUpOnBlankLine = true,
            WhoReply = "Player Name        On For Idle\r\n7 Players logged in, 22 record, no maximum.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        // The report landed, so nothing was flushed and the session survived to answer WHO.
        await Assert.That(game.Received).DoesNotContain(string.Empty);

        // The measurement itself: MSSP was seen, not written off as absent.
        await Assert.That(result.OfferedOptions).Contains("MSSP");
        await Assert.That(result.MsspOutcome).IsEqualTo(MsspOutcome.Received);

        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.Count);
        await Assert.That(result.Who.Count).IsEqualTo(7);
    }

    /// <summary>
    /// A server that did <em>not</em> parse our negotiation still gets the flush, because its line
    /// buffer is holding our IAC bytes and the next thing we type would arrive glued to them.
    /// </summary>
    /// <remarks>
    /// Still reproducible on <c>chaos.caile.org:4444</c> four TelnetNegotiationCore versions after it
    /// was first measured: <c>IAC DO 70</c> then <c>WHO</c> returns 1644 bytes of connect screen,
    /// while the same with a blank line between returns "0 Players logged in, 22 record, no maximum."
    /// FakeGame's raw path models it exactly — an unclean line is answered with a redisplay.
    /// </remarks>
    [Test]
    public async Task AServerThatSwallowedOurNegotiationStillGetsTheFlush()
    {
        await using var game = new FakeGame
        {
            SwallowsNegotiationAsText = true,
            Banner = "Welcome to Nowhere\r\n",
            BannerTail = "Please enter a name: ",
            WhoReply = "Player Name        On For Idle\r\n3 Players logged in, 22 record, no maximum.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        // Without the flush the WHO arrives as \xff\xfd\x46WHO and is answered with a redisplay.
        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.Count);
        await Assert.That(result.Who.Count).IsEqualTo(3);

        // The flush line is not empty when it lands: it carries the IAC bytes the server had been
        // holding as typing, which is the whole reason it is sent. WHO arrives clean behind it.
        await Assert.That(game.Received.Any(line => line.Contains('\u00ff'))).IsTrue();
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
    /// A menu selection is a line through the server's buffer exactly as a prompt answer is, so the
    /// residue flush that follows it buys nothing — and on a DIKU descendant costs the rest of the
    /// session.
    /// </summary>
    /// <remarks>
    /// The gap this closes: <c>whoAlreadyAnswered</c> already kept the literal <c>WHO</c> from being
    /// asked twice, so the count survived — but the blank line still went out, the game read it as a
    /// goodbye, and <c>INFO</c>/<c>VERSION</c> were lost to a flush that was never needed. Negotiates
    /// nothing on purpose, so the negotiation branch cannot be what withholds the flush and this test
    /// pins the menu branch specifically.
    /// </remarks>
    [Test]
    public async Task AMenuSelectionSpendsTheFlushSoADikuKeepsItsFollowUps()
    {
        // Default mode on purpose: it frames telnet correctly, so the menu reaches the gate
        // unpolluted, but attaches no plugin and so agrees to nothing — leaving OfferedOptions empty
        // and the negotiation branch unable to be what withholds the flush.
        await using var game = new FakeGame
        {
            BannerTail = "(C)onnect\r\n(N)ew character\r\nW - See who is online\r\n(Q)uit",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["W"] = "There are 12 players connected.\r\n",
            },
            HangsUpOnBlankLine = true,
            InfoReply = "Harbourlight, a world of small boats.\r\n",
            VersionReply = "Harbourlight 2.4.1\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        // Diagnostic first: if the fixture negotiated after all, the flush would be withheld for the
        // *other* reason and this test would pass without exercising the menu branch at all.
        await Assert.That(result.OfferedOptions).IsEmpty();

        await Assert.That(game.Received).DoesNotContain(string.Empty);
        await Assert.That(result.Who.Count).IsEqualTo(12);

        // The point of the fix: the session outlived the menu, so the questions after it still landed.
        await Assert.That(result.Info).IsNotNull();
        await Assert.That(result.Info!).Contains("small boats");
        await Assert.That(result.Version!).Contains("2.4.1");
    }

    /// <summary>
    /// A Pueblo server's menu reply is stripped even though the reply itself carries no marker — the
    /// proof that the session is Pueblo is back in the connect screen, which the reply's own slice
    /// does not include.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The roster goes straight to <c>WhoParser</c>, which counts a table by its rows — so one whose
    /// rows are still welded together by <c>&lt;br&gt;</c> is a single line, and the count it yields
    /// is not "unreadable" but <b>zero</b>. A busy game published as empty is the worst shape this
    /// codebase can produce (rule 4: an unreadable WHO yields unknown, never zero), and it is the
    /// defect this project has already been bitten by once on a DIKU.
    /// </para>
    /// <para>
    /// The fixture is deliberately a header-and-rows table with no summary sentence in it: a roster
    /// that ends "There are 2 players connected." is read off that sentence whether or not its lines
    /// were ever separated, and would pass this test with the bug still in place.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AMenuReplyIsStrippedFromTheMarkerLeftBackInTheBanner()
    {
        await using var game = new FakeGame
        {
            // The marker lives here, and nowhere in the reply below.
            BannerTail = "<!EL RName FLAG=\"RoomName\" OPEN><samp>The Keep</samp>\r\n"
                + "(C)onnect\r\nW - See who is online\r\n(Q)uit",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["W"] = "Player Name          On For   Idle<br>"
                    + "Xperta               9m 21s     9m<br>"
                    + "Thoran              11m 48s    11m<br>"
                    + "gelatin          7h 46m 19s     7h<br>",
            },
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Received).Contains("W");
        await Assert.That(result.Who.Count).IsEqualTo(3);
    }

    /// <summary>
    /// A throttled who's-online menu gets the same patience a throttled literal <c>WHO</c> does — it
    /// is the same question by another route, and the same rung of the count ladder (spec §5.2).
    /// </summary>
    /// <remarks>
    /// Modelled on the measured case WhoGrace was introduced for: <c>twyst.org:3333</c> and
    /// <c>rupert.twyst.org:6666</c> both answer <c>WHO</c> after 5.05 seconds on purpose. Settling the
    /// menu on QuietPeriod instead read that silence as "no roster" and published our own timing as a
    /// fact about their server. The delay here sits past <c>Fast()</c>'s QuietPeriod and inside its
    /// WhoGrace, so it fails on the former and passes on the latter.
    /// </remarks>
    [Test]
    public async Task AThrottledWhosOnlineMenuIsWaitedForRatherThanReadAsEmpty()
    {
        await using var game = new FakeGame
        {
            BannerTail = "(C)onnect\r\n(N)ew character\r\nW - See who is online\r\n(Q)uit",
            Replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["W"] = "There are 12 players connected.\r\n",
            },
            ReplyDelay = TimeSpan.FromMilliseconds(350),
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Received).Contains("W");
        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.Count);
        await Assert.That(result.Who.Count).IsEqualTo(12);
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
        await Assert.That(result.Failure!.Cause).IsEqualTo(DialFailureCause.Timeout);
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
        /// How long a <see cref="Replies"/> answer is withheld — the counterpart to
        /// <see cref="WhoDelay"/>, which reaches only the literal <c>WHO</c> branch. A throttled
        /// who's-online <em>menu</em> answers through this dictionary, so without it there is no way
        /// to model the very server WhoGrace exists for arriving by the menu route.
        /// </summary>
        public TimeSpan ReplyDelay { get; init; }

        /// <summary>
        /// Whether an empty line at the name prompt is a goodbye — true for every DIKU descendant,
        /// which is what makes the probe's own flush line fatal to them.
        /// </summary>
        public bool HangsUpOnBlankLine { get; init; }

        /// <summary>Whether the server accepts the connection and drops it without a word.</summary>
        public bool ClosesImmediately { get; init; }

        /// <summary>
        /// Whether a line the server cannot make sense of ends the session, as a DIKU that does not
        /// parse telnet does when our IAC bytes reach its name prompt as typing.
        /// </summary>
        public bool HangsUpOnUnrecognisedLine { get; init; }

        /// <summary>Whether the server answers <c>WHO</c> and then closes on us.</summary>
        public bool HangsUpAfterWho { get; init; }

        /// <summary>
        /// Whether this server fails to strip telnet negotiation at its login screen, so our IAC
        /// bytes end up prefixed to the next command we type.
        /// </summary>
        public bool SwallowsNegotiationAsText { get; init; }

        /// <summary>
        /// Whether the server negotiates a protocol rather than merely framing telnet correctly.
        /// </summary>
        /// <remarks>
        /// The default <see cref="ServeOverTelnetAsync"/> attaches no plugins, so it parses telnet and
        /// agrees to nothing — which the probe reads, correctly, as no positive evidence that our IAC
        /// bytes were interpreted. Attaching one is what makes a fixture a server that demonstrably
        /// negotiated, and so one the residue flush may be withheld from.
        /// </remarks>
        public bool AnnouncesMssp { get; init; }

        /// <summary>
        /// The <c>PLAYERS</c> <see cref="AnnouncesMssp"/>'s report carries, or null for a report that
        /// carries none.
        /// </summary>
        /// <remarks>
        /// Load-bearing, not decoration: a report that states a count is why the probe stops typing
        /// <c>WHO</c> at a login screen, so a fixture about the flush or about <c>WHO</c> itself has to
        /// be able to say which kind of MSSP server it is. Real servers exist both ways — <c>PLAYERS</c>
        /// is one of MSSP's three required variables and is still routinely absent.
        /// </remarks>
        public int? MsspPlayers { get; init; } = 99;
        /// Variables MSSP does not define, which the report carries verbatim — a roster among them.
        /// </summary>
        /// <remarks>
        /// Every roster convention a real codebase uses lives outside MSSP's published set, so a
        /// fixture cannot reach one through <see cref="MSSPConfig"/>'s typed properties.
        /// </remarks>
        public IReadOnlyDictionary<string, string> MsspExtras { get; init; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>

        /// <summary>

        /// <summary>
        /// Whether the server compresses everything after its option handshake, as MCCP2 servers do.
        /// </summary>
        /// <remarks>
        /// Load-bearing rather than decoration: with compression on, anything the probe pushes into
        /// its own inbound channel is spliced into the peer's deflate stream, and every reply after
        /// it comes out shredded. That is what this fixture exists to catch.
        /// </remarks>
        public bool AnnouncesMccp { get; init; }

        /// <summary>
        /// How long <see cref="AnnouncesMssp"/>'s report is withheld after our <c>DO MSSP</c> arrives
        /// — <c>WILL MSSP</c> still negotiates on connect, same as always, so this models a server
        /// that has agreed to MSSP but whose report is still in flight, not one that never offered it.
        /// </summary>
        public TimeSpan MsspReportDelay { get; init; }

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
            var builder = new TelnetInterpreterBuilder()
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
                });

            if (AnnouncesMssp)
            {
                // MSSP because it is the protocol real servers announce *early* — the report arrives
                // during the option handshake, well before the flush decision. A plugin that merely
                // negotiates and then says nothing would leave Supported empty, which the probe reads
                // (correctly) as no evidence at all.
                var mssp = new MSSPProtocol();
                // Deliberately not the WHO fixture's 7: if MSSP ever leaked into the WHO reading, two
                // matching numbers would hide it.
                mssp.SetMSSPConfig(() =>
                {
                    // Blocking on purpose: this runs inside the library's own DO-MSSP handler, so a
                    // delay here holds back only the report, after WILL/DO have already gone both
                    // ways — the shape MsspReportDelay exists to model. TelnetNegotiationCore gives
                    // this callback no async form to delay through instead.
                    if (MsspReportDelay > TimeSpan.Zero)
                    {
                        Thread.Sleep(MsspReportDelay);
                    }

                    var config = new MSSPConfig { Name = "NukeFire" };

                    if (MsspPlayers is { } players)
                    {
                        config.Players = players;
                    }

                    if (MsspExtras.Count > 0)
                    {
                        config.Extended = MsspExtras.ToDictionary(
                            entry => entry.Key, entry => (object)entry.Value);
                    }

                    return config;
                });
                builder = builder.AddPlugin(mssp);
            }

            if (AnnouncesMccp)
            {
                builder = builder.AddPlugin(new MCCPProtocol());
            }

            var built = await builder.BuildAndStartAsync(client, _stopping.Token);

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
                if (HangsUpOnUnrecognisedLine)
                {
                    return false;
                }

                // What TinyMUSH does: redisplay the connect screen and answer nothing.
                await reply(Banner);
                return true;
            }

            if (Replies.TryGetValue(command, out var configured))
            {
                if (ReplyDelay > TimeSpan.Zero)
                {
                    await Task.Delay(ReplyDelay, _stopping.Token);
                }

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
