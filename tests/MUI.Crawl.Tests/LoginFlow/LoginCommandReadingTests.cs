using MUI.Crawl;

namespace MUI.Crawl.Tests;

public class LoginCommandReadingTests
{
    /// <summary>
    /// The Dead Souls mudlib reads as the engine it is.
    /// </summary>
    /// <remarks>
    /// Nine games in the catalogue answer this, and every one of them was previously read as naming
    /// no engine at all. A two-word marker is matched on word boundaries, so a game that merely
    /// mentions the dead is not swept in.
    /// </remarks>
    [Test]
    [Arguments("Version: Dead Souls 3.9", "Dead Souls 3.9")]
    [Arguments("Codebase: Dead Souls 3.8.2", "Dead Souls 3.8.2")]
    public async Task DeadSoulsIsReadAsACodebase(string line, string expected)
    {
        await Assert.That(LoginCommandReading.MeaningfulCodebase(line, null)).IsEqualTo(expected);
    }

    [Test]
    public async Task NamesAKnownFamilyKnowsDeadSouls()
    {
        await Assert.That(LoginCommandReading.NamesAKnownFamily("Dead Souls 3.9")).IsTrue();
        await Assert.That(LoginCommandReading.NamesAKnownFamily("Galaxy Engine 2.2")).IsFalse();
    }

    [Test]
    public async Task ALabelledInfoVersionValueIsRead()
    {
        var info = """
            ### Begin INFO 1
            Name: Convergence MUSH
            Uptime: Tue Sep 16 23:39:43 2025
            Connected: 60
            Size: 1929
            Version: RhostMUSH 4.27.3
            ### End INFO
            """;

        var read = LoginCommandReading.MeaningfulCodebase(info, null);

        await Assert.That(read).IsEqualTo("RhostMUSH 4.27.3");
    }

    [Test]
    public async Task ALabelledCodebaseFieldWinsWhenPresent()
    {
        var info = "Codebase: TinyMUX 2.13";

        var read = LoginCommandReading.MeaningfulCodebase(info, "Version: ignored");

        await Assert.That(read).IsEqualTo("TinyMUX 2.13");
    }

    [Test]
    public async Task AnUnlabelledVersionLineWithKnownFamilyIsRead()
    {
        var version = """
            TinyMUX 2.14.0.4 #22
            Copyright 1995-2026 TinyMUX Team
            """;

        var read = LoginCommandReading.MeaningfulCodebase(null, version);

        await Assert.That(read).IsEqualTo("TinyMUX 2.14.0.4 #22");
    }

    [Test]
    public async Task AFamilyHeadingCanPrefixANumericVersionField()
    {
        var version = """
            TinyMUSH Engine
            ---------------
            Version : 4.0 stable
            """;

        var read = LoginCommandReading.MeaningfulCodebase(null, version);

        await Assert.That(read).IsEqualTo("TinyMUSH 4.0 stable");
    }

    [Test]
    public async Task AFamilyNameInsideAnotherWordIsNotAFamily()
    {
        // Real capture: "RetroMUX" contains "rom", so a naive family search misidentified it as ROM
        // (a Diku derivative) and prefixed that to the actual version line.
        var info = """
            ### Begin INFO 1.1
            Name: RetroMUX
            Uptime: Fri Jul 10 19:02:46 2026
            Connected: 4
            Size: 22908
            Version: MUX 2.12.0.10
            ### End INFO
            """;

        var read = LoginCommandReading.MeaningfulCodebase(info, null);

        await Assert.That(read).IsEqualTo("MUX 2.12.0.10");
    }

    [Test]
    public async Task TheWordsThatUsedToMatchAFamilyNoLongerDo()
    {
        // Substring matches without a word boundary: "from"/"Rome" carry rom, "smooth" carries moo,
        // "mucked" carries muck. A wrong codebase is worse than none on games with no MSSP to contradict it.
        foreach (var line in new[]
                 {
                     "3 messages from the staff, build 2",
                     "Welcome to Rome 2",
                     "smooth 1.0",
                     "mucked about 2.1",
                 })
        {
            await Assert.That(LoginCommandReading.MeaningfulCodebase(null, line)).IsNull();
        }
    }

    [Test]
    public async Task AFamilyFollowedByItsVersionDigitsIsStillAFamily()
    {
        // The boundary must not be so tight that it rejects how these are actually written.
        await Assert.That(LoginCommandReading.MeaningfulCodebase(null, "ROM24 b6")).IsEqualTo("ROM24 b6");
        await Assert.That(LoginCommandReading.MeaningfulCodebase(null, "(CircleMUD 3.1)")).IsEqualTo("(CircleMUD 3.1)");
    }

    [Test]
    public async Task GenericInfoWithoutCodebaseHintsReturnsNull()
    {
        var info = """
            Name: Convergence MUSH
            Connected: 60
            Size: 1929
            """;

        var read = LoginCommandReading.MeaningfulCodebase(info, null);

        await Assert.That(read).IsNull();
    }
    /// <summary>A line that merely mentions a codebase yields the codebase, not the whole sentence.</summary>
    /// <remarks>
    /// <c>lambdamoo</c> is on the marker list because extracting only <c>moo</c> from it produces
    /// <c>MOO</c>, which <see cref="MsspDefaults"/> refuses as a placeholder.
    /// </remarks>
    [Test]
    [Arguments(
        "The MOO is currently running version 1.8.3+47 of the LambdaMOO server code.",
        "LambdaMOO 1.8.3+47")]
    [Arguments(
        "The MOO is currently running version 0.1.0beta8 of the LambdaMOO server code.",
        "LambdaMOO 0.1.0beta8")]
    [Arguments("Welcome to Pegasus, Currently running TinyMUCK2.3b2! Maintained by", "TinyMUCK 2.3b2")]
    public async Task ACodebaseNamedInProseIsReadWithoutTheProse(string line, string expected) =>
        await Assert.That(LoginCommandReading.MeaningfulCodebase(null, line)).IsEqualTo(expected);

    [Test]
    public async Task AWholeConnectScreenIsNotACodebase()
    {
        // Abridged real capture: a Pueblo-enabled PennMUSH banner where the version sits one space
        // past the name — a first-cut scan without a version anchor kept the whole line instead.
        const string Banner =
            "\" ATT=\"src height width border=0 ismap=0\" EMPTY> Welcome to The Original Tolkien "
            + "Middle-earth MUSH! http://www.elendor.net Founded October 1991 Running: PennMUSH "
            + "1.7.1 pl3 with Elendor Mods Pueblo enhanced mode!";

        await Assert.That(LoginCommandReading.MeaningfulCodebase(null, Banner))
            .IsEqualTo("PennMUSH 1.7.1");
    }

    [Test]
    public async Task ProseThatNamesOnlyACodebaseFamilyIsRefused()
    {
        // "MUCK" alone is a placeholder and a copyright year is not a release — neither identifies anything.
        await Assert.That(LoginCommandReading.MeaningfulCodebase(
            null, "Tapestries MUCK Copyright 1991-2020 by tapestries.fur.com. All rights reserved."))
            .IsNull();

        await Assert.That(LoginCommandReading.MeaningfulCodebase(
            null, "This MUCK is rated NC-17. If you are not 18 or are offended by this, type 'QUIT'"))
            .IsNull();
    }

    /// <summary>
    /// A game that names itself over <c>INFO</c> has named itself, MSSP or no MSSP.
    /// </summary>
    /// <remarks>RhostMUSH, TinyMUX and TinyMUSH all answer <c>INFO</c> this way, with no MSSP at all.</remarks>
    [Test]
    public async Task AnInfoBlockThatNamesTheGameIsRead()
    {
        const string Info = """
            ### Begin INFO 1
            Name: Convergence MUSH
            Uptime: Tue Sep 16 23:39:43 2025
            Connected: 64
            Size: 1944
            Version: RhostMUSH 4.27.3
            ### End INFO
            """;

        await Assert.That(LoginCommandReading.MeaningfulName(Info, null)).IsEqualTo("Convergence MUSH");
        await Assert.That(LoginCommandReading.MeaningfulCodebase(Info, null)).IsEqualTo("RhostMUSH 4.27.3");
    }

    /// <summary>
    /// A name that only restates the codebase identifies nobody, whichever command carried it.
    /// </summary>
    /// <remarks>
    /// Same rule as <c>MsspDefaults.MeaningfulName</c> for <c>NAME</c>: every unedited install answers
    /// this way, so admitting one would let a submitter mint a listing per default install.
    /// </remarks>
    [Test]
    [Arguments("Name: PennMUSH")]
    [Arguments("Name: PennMUSH 1.8.8p0")]
    // Template text from MsspDefaults' own list, routed through MeaningfulName rather than duplicated.
    [Arguments("Name: Unnamed")]
    [Arguments("Name: Your MUD Name")]
    [Arguments("Name:")]
    public async Task AnInfoNameThatIsOnlyTheCodebaseOrAPlaceholderIsRefused(string line)
    {
        var info = $"### Begin INFO 1\n{line}\nVersion: PennMUSH 1.8.8p0\n### End INFO";

        await Assert.That(LoginCommandReading.MeaningfulName(info, null)).IsNull();
    }

    /// <summary>No <c>INFO</c>, or one that never names anything, yields nothing.</summary>
    [Test]
    public async Task AnInfoBlockWithNoNameYieldsNothing()
    {
        await Assert.That(LoginCommandReading.MeaningfulName(null, null)).IsNull();
        await Assert.That(LoginCommandReading.MeaningfulName(string.Empty, "PennMUSH 1.8.8p0")).IsNull();
        await Assert.That(LoginCommandReading.MeaningfulName("Connected: 12\nSize: 900", null)).IsNull();
    }

    /// <summary>
    /// PennMUSH's <c>dump_info()</c> shape. <c>Uptime</c> is a ctime string, not a timestamp.
    /// </summary>
    private const string PennInfo = """
        ### Begin INFO 1.1
        Name: M*U*S*H
        Address: mush.pennmush.org
        Uptime: Tue Sep 16 23:39:43 2025
        Connected: 60
        Size: 1929
        Version: PennMUSH 1.8.8p0
        ### End INFO
        """;

    /// <summary>
    /// Evennia's variant: two hashes, uppercase BEGIN/END, no <c>Address</c> line.
    /// </summary>
    private const string EvenniaInfo = """
        ## BEGIN INFO 1.1
        Name: Evennia Demo
        Uptime: Mon Aug 11 02:14:07 2026
        Connected: 7
        Version: Evennia 5.0.1
        ## END INFO
        """;

    [Test]
    public async Task ThePennAndEvenniaInfoBlocksBothYieldTheirConnectedCount()
    {
        await Assert.That(LoginCommandReading.ConnectedPlayers(PennInfo)).IsEqualTo(60);
        await Assert.That(LoginCommandReading.ConnectedPlayers(EvenniaInfo)).IsEqualTo(7);
    }

    [Test]
    public async Task AConnectedZeroIsAMeasuredZeroAndNotAnAbsentReading()
    {
        var info = "### Begin INFO 1.1\nName: Quiet MUSH\nConnected: 0\n### End INFO";

        await Assert.That(LoginCommandReading.ConnectedPlayers(info)).IsEqualTo(0);
    }

    [Test]
    [Arguments("1.1")]
    [Arguments("1")]
    [Arguments("2.0")]
    [Arguments("")]
    public async Task TheBlockVersionIsNeverPartOfTheContract(string version)
    {
        // INFO_VERSION is a string a codebase bumps freely — keying on today's value would silently
        // break this reader the day one ships a new number.
        var info = $"### Begin INFO {version}\nConnected: 4\n### End INFO";

        await Assert.That(LoginCommandReading.ConnectedPlayers(info)).IsEqualTo(4);
    }

    [Test]
    [Arguments("Connected: 41")]
    [Arguments("Welcome!\nConnected: 41\nType 'connect <name> <password>'.")]
    [Arguments("### Begin INFO 1.1\nConnected: 41")]
    [Arguments("Connected: 41\n### End INFO")]
    [Arguments("### Begin MSSP\nConnected: 41\n### End MSSP")]
    public async Task ALooseConnectedLineIsNotAnInfoBlockAndIsNotRead(string info)
    {
        // The delimiters are the whole defence: outside an opened-and-closed block, "Connected:" is
        // just a word on a connect screen, and reading a number out of it would be fabrication.
        await Assert.That(LoginCommandReading.ConnectedPlayers(info)).IsNull();
    }

    [Test]
    [Arguments("Connected: lots")]
    [Arguments("Connected: -3")]
    [Arguments("Connected:")]
    [Arguments("Size: 1929")]
    public async Task ABlockWithNothingCountableInItYieldsNothing(string line)
    {
        var info = $"### Begin INFO 1.1\nName: Some MUSH\n{line}\n### End INFO";

        await Assert.That(LoginCommandReading.ConnectedPlayers(info)).IsNull();
    }

    [Test]
    public async Task NoInfoReplyAtAllYieldsNothing()
    {
        await Assert.That(LoginCommandReading.ConnectedPlayers(null)).IsNull();
        await Assert.That(LoginCommandReading.ConnectedPlayers("   ")).IsNull();
    }
}
