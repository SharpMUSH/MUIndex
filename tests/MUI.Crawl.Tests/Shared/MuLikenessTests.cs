using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// §7.8's rubric, tested against captures from real servers.
/// </summary>
/// <remarks>
/// Every fixture is a capture, not an invention. Negative fixtures matter most: FICS and telehack
/// answer a login-screen command as readily as any MUD, so "it replied" is not a signal — only
/// character vocabulary distinguishes a MU* from another multi-user telnet service.
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
        // Our own size ceiling (§6.4) is not the server's silence — RejectedTooLarge still counts as an answer.
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
        // Generic telnet options every daemon negotiates — hence an explicit allowlist rather than "anything offered".
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
        // Real capture: login menu talks about players/characters, not accounts.
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
        // ANSI colour codes run through the middle of the phrase — matcher must strip them or match nothing.
        var probe = Probe(info: "\e[0;37m[\e[1;36m2\e[0;37m] \e[0;36mWho is\e[0;36m Online"
            + "              \e[0;37m[\e[1;36m6\e[0;37m] \e[0;36mCreate a \e[1;33mCharacter\r\n"
            + "\e[1;30m_______---\e[0;37m===\e[1;36m) \e[1;37mGame Status: Open");

        await Assert.That(MuLikeness.Signals(probe)).Contains("vocabulary");
    }

    [Test]
    public async Task AChessServerIsNotAGame()
    {
        // Chess server; its login idiom is identical to any account-based MUD login screen.
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
        // Real MUD with account idiom, not character idiom — must stay refused like the chess server.
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
        var probe = Probe(info: "%unrecognized command - type ? for a list");

        await Assert.That(MuLikeness.Signals(probe)).IsEmpty();
    }

    [Test]
    public async Task TheVocabularyIsReadFromElicitedTextAndNeverFromTheBanner()
    {
        // A banner is bytes anyone can paste; vocabulary must come from elicited replies only.
        var probe = Probe(banner: "Type CREATE A CHARACTER to begin, or WHO IS ONLINE to look around.");

        await Assert.That(MuLikeness.Signals(probe)).IsEmpty();
    }

    [Test]
    public async Task ABannerAndNothingElseIsNotEnough()
    {
        // Real MUD with no usable signals at all — correctly falls through to the manual queue (§7.8).
        var probe = Probe(
            banner: "Rapture Runtime Environment v2.4.9.1 -- (c) 2026 -- Iron Realms Entertainment",
            who: WhoReading.Unreadable,
            info: "*ð§_TØ\\µ +¬RG85U!");

        await Assert.That(MuLikeness.Signals(probe)).IsEmpty();
    }

    [Test]
    public async Task ConvergenceMushIsCorroboratedTwiceOver()
    {
        // Corroboration case: no MSSP, no watched options — only combined weak signals clear the bar.
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
        // A named codebase in an elicited INFO reply is the strongest signal a login screen can give.
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
        // Same words on the banner, not elicited — doesn't count.
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
            Failure = new FailureDetail(DialFailureCause.Refused),
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
