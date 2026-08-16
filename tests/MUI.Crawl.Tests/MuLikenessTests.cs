using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// §7.8's rubric, tested against what real servers actually said on 16 August 2026.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every fixture here is a capture, not an invention.</b> The rubric exists because the
/// obvious version of it — "two signals, one of them behavioural" — was measured against the live
/// catalogue and refused 56% of it, including Achaea and BatMUD. The captures that decided each
/// clause are kept as its tests, so a later loosening has to argue with the servers rather than
/// with a paraphrase of them.
/// </para>
/// <para>
/// <b>The negative fixtures are the important half.</b> FICS is a chess server, telehack is a
/// simulated shell, and both answer a login-screen command as readily as any MUD — so "it replied"
/// was never a signal. What separates them is that a MU* talks about <em>characters</em> while
/// every other multi-user telnet service talks about accounts, logins and passwords. tirradyn and
/// stonia are real MUDs that say only the second kind of thing, and they must stay refused: a
/// vocabulary wide enough to catch them is a vocabulary that catches FICS.
/// </para>
/// </remarks>
public class MuLikenessTests
{
    [Test]
    public async Task AnsweringMsspIsEnoughOnItsOwn()
    {
        var probe = Probe(msspOutcome: MsspOutcome.Received);

        await Assert.That(MuLikeness.Signals(probe)).Contains("mssp");
    }

    [Test]
    public async Task AnMsspReportWeDroppedForSizeStillCounts()
    {
        // We asked, the server answered, and we chose not to hold it (§6.4). Reading our own
        // ceiling as the server's silence is the mistake MsspOutcome exists to prevent.
        var probe = Probe(msspOutcome: MsspOutcome.RejectedTooLarge);

        await Assert.That(MuLikeness.Signals(probe)).Contains("mssp");
    }

    [Test]
    public async Task NegotiatingAMuSpecificOptionIsEnoughOnItsOwn()
    {
        var probe = Probe(offered: ["GMCP"]);

        await Assert.That(MuLikeness.Signals(probe)).Contains("gmcp");
    }

    [Test]
    public async Task TheGenericTelnetOptionsAreNotSignals()
    {
        // Every telnet daemon on earth negotiates these, which is the whole reason the option set
        // is a list rather than "anything we saw".
        var probe = Probe(offered: ["TTYPE", "NAWS", "CHARSET", "EOR", "NEW-ENVIRON", "SUPPRESS GO AHEAD", "ECHO"]);

        await Assert.That(MuLikeness.Signals(probe)).IsEmpty();
    }

    [Test]
    public async Task AWhoThatParsedToACountIsEnoughOnItsOwn()
    {
        var probe = Probe(who: new WhoReading(WhoConfidence.Count, Count: 67));

        await Assert.That(MuLikeness.Signals(probe)).Contains("who");
    }

    [Test]
    public async Task AWhoThatCouldNotBeReadIsNotASignal()
    {
        var probe = Probe(who: WhoReading.Unreadable);

        await Assert.That(MuLikeness.Signals(probe)).IsEmpty();
    }

    [Test]
    public async Task BatmudsLoginMenuIsCharacterVocabulary()
    {
        // batmud.bat.org:23, 16 August 2026 — the reply to INFO. The server put our word into its
        // name prompt and answered, which no banner-echoing socket can do, and what it said is
        // about players and characters rather than accounts.
        var probe = Probe(info: """
            What is your name: info
            No such player. Please check your typing!
              1 - enter the game                    s - game status
              2 - visit the game                    w - who is playing at the moment
              3 - create a new character            q - quit
            Please enter your choice or name:
            """);

        await Assert.That(MuLikeness.Signals(probe)).Contains("vocabulary");
    }

    [Test]
    public async Task KingdomsOfTheLostMenuSurvivesItsAnsi()
    {
        // kotl.org:2221, same day. Colour codes run through the middle of every phrase, so a
        // matcher that does not strip them reads nothing at all.
        var probe = Probe(info: "\e[0;37m[\e[1;36m2\e[0;37m] \e[0;36mWho is\e[0;36m Online"
            + "              \e[0;37m[\e[1;36m6\e[0;37m] \e[0;36mCreate a \e[1;33mCharacter\r\n"
            + "\e[1;30m_______---\e[0;37m===\e[1;36m) \e[1;37mGame Status: Open");

        await Assert.That(MuLikeness.Signals(probe)).Contains("vocabulary");
    }

    [Test]
    public async Task AChessServerIsNotAGame()
    {
        // freechess.org:5000. It answered INFO instantly and at length — and every word of it is
        // the generic account idiom that a MUD shares with every other login server.
        var probe = Probe(info: """
            "who" is a registered name.  If it is yours, type the password.
            If not, just hit return to try another name.
            password:
            **** Invalid password! ****
                  If you are not a registered player, enter guest or a unique ID.
            login:
            """);

        await Assert.That(MuLikeness.Signals(probe)).IsEmpty();
    }

    [Test]
    public async Task AccountIdiomFromARealMudIsStillNotCharacterIdiom()
    {
        // tirradyn.com:9010 — a real MUD whose login screen is indistinguishable from the chess
        // server's. It waits for the operator queue rather than dragging FICS in behind it.
        var probe = Probe(info: """
            No account by that name exists.
            Type 'new' to create a new account.
            Enter an account name to login, or new to make a new account:
            """);

        await Assert.That(MuLikeness.Signals(probe)).IsEmpty();
    }

    [Test]
    public async Task ASimulatedShellIsNotAGame()
    {
        // telehack.com:23
        var probe = Probe(info: "%unrecognized command - type ? for a list");

        await Assert.That(MuLikeness.Signals(probe)).IsEmpty();
    }

    [Test]
    public async Task TheVocabularyIsReadFromElicitedTextAndNeverFromTheBanner()
    {
        // A banner is bytes anyone can paste, and the whole tier is built on the server having
        // processed our input to produce them. Reading the same words off the connect screen would
        // hand the rubric to the first host that copied one.
        var probe = Probe(banner: "Type CREATE A CHARACTER to begin, or WHO IS ONLINE to look around.");

        await Assert.That(MuLikeness.Signals(probe)).IsEmpty();
    }

    [Test]
    public async Task ABannerAndNothingElseIsNotEnough()
    {
        // achaea.com:23. A top-tier commercial MUD that offered no option we watch, whose WHO came
        // back unreadable and whose INFO reply arrived as an undecodable binary stream. It goes to
        // the queue, and that cost is stated in §7.8 rather than hidden here.
        var probe = Probe(
            banner: "Rapture Runtime Environment v2.4.9.1 -- (c) 2026 -- Iron Realms Entertainment",
            who: WhoReading.Unreadable,
            info: "*ð§_TØ\\µ +¬RG85U!");

        await Assert.That(MuLikeness.Signals(probe)).IsEmpty();
    }

    [Test]
    public async Task ConvergenceMushIsCorroboratedTwiceOver()
    {
        // game.convergencemush.org:10000 — the submission this rule was written for. It answers no
        // MSSP at all and negotiates nothing we watch, so every signal it has is one the original
        // rubric would have called insufficient on its own.
        var probe = Probe(
            who: new WhoReading(WhoConfidence.Count, Count: 67),
            info: """
                ### Begin INFO 1
                Name: Convergence MUSH
                Connected: 67
                Version: RhostMUSH 4.27.3
                ### End INFO
                """);

        await Assert.That(MuLikeness.Signals(probe)).Contains("who");
        await Assert.That(MuLikeness.LooksLikeAGame(probe)).IsTrue();
    }

    [Test]
    public async Task ACodebaseNamedInAnElicitedReplyIsASignalOfItsOwn()
    {
        // An INFO block that names a MU* family is the strongest thing a login screen can say: it is
        // elicited, it is structured, and no chess server produces it. Weaker evidence than this
        // already publishes through the vocabulary tier.
        var probe = Probe(info: """
            ### Begin INFO 1
            Name: Convergence MUSH
            Version: RhostMUSH 4.27.3
            ### End INFO
            """);

        await Assert.That(MuLikeness.Signals(probe)).Contains("codebase");
    }

    [Test]
    public async Task ACodebaseNameOnTheBannerIsNotASignal()
    {
        // The same words, pasted rather than answered. Every tier here is about what the server did.
        var probe = Probe(banner: "Welcome! Version: RhostMUSH 4.27.3");

        await Assert.That(MuLikeness.Signals(probe)).IsEmpty();
    }

    [Test]
    public async Task AProbeThatNeverGotInSaysNothingAboutTheHost()
    {
        var probe = new ProbeResult
        {
            Host = "mud.example.org",
            Port = 4201,
            ObservedAt = DateTimeOffset.UnixEpoch,
            Outcome = ProbeOutcome.Failed,
            Failure = new FailureDetail("refused"),
        };

        await Assert.That(MuLikeness.Signals(probe)).IsEmpty();
        await Assert.That(MuLikeness.LooksLikeAGame(probe)).IsFalse();
    }

    [Test]
    public async Task SignalsAreReportedInOneOrderSoTheStoredRecordIsStable()
    {
        var probe = Probe(
            msspOutcome: MsspOutcome.Received,
            offered: ["GMCP", "MCCP2"],
            who: new WhoReading(WhoConfidence.Count, Count: 3));

        await Assert.That(MuLikeness.Signals(probe)).IsEquivalentTo(["mssp", "gmcp", "mccp", "who"]);
    }

    private static ProbeResult Probe(
        MsspOutcome msspOutcome = MsspOutcome.NotOffered,
        string[]? offered = null,
        WhoReading? who = null,
        string? banner = null,
        string? info = null,
        string? version = null) => new()
    {
        Host = "mud.example.org",
        Port = 4201,
        ObservedAt = DateTimeOffset.UnixEpoch,
        Outcome = ProbeOutcome.Answered,
        OfferedOptions = new HashSet<string>(offered ?? [], StringComparer.Ordinal),
        MsspOutcome = msspOutcome,
        Banner = banner,
        Who = who ?? WhoReading.NotAsked,
        Info = info,
        Version = version,
    };
}
