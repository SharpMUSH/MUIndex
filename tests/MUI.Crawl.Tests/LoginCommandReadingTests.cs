using MUI.Crawl;

namespace MUI.Crawl.Tests;

public class LoginCommandReadingTests
{
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
        // darcness.net:4201, verbatim. "RetroMUX" contains "rom", so the family search returned ROM —
        // a Diku derivative — and prefixed it to a TinyMUD derivative's version, producing
        // "ROM MUX 2.12.0.10" for a game that had claimed neither. The game's own answer is the
        // version line and nothing more.
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
        // The unlabelled path, where recognising a family is the only reason a line is taken at all.
        // Every one of these was a codebase before the boundary check: "from" and "Rome" carry rom,
        // "smooth" carries moo, "mucked" carries muck. A wrong codebase is worse than none — it is
        // the value the page shows, on exactly the games with no MSSP to contradict it.
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
    /// <summary>A line that merely mentions a codebase yields the codebase, not the line.</summary>
    /// <remarks>
    /// <para>
    /// Every input here is a real value that reached production and was stored as a game's
    /// <c>CODEBASE</c>, because a line mentioning a known codebase used to be returned intact. Nine
    /// of the eighty-two bars on the ecosystem dashboard's codebase chart were sentences, and four
    /// separate LambdaMOOs each had a bar named after one.
    /// </para>
    /// <para>
    /// The LambdaMOO case is also why <c>lambdamoo</c> had to join the marker list. With only
    /// <c>moo</c> in it the extraction produced <c>MOO</c>, which <see cref="MsspDefaults"/> refuses
    /// as a placeholder — so the choice was the whole sentence or nothing, and the whole sentence won.
    /// </para>
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
        // Abridged from the 697-character value a Pueblo-enabled PennMUSH put on the dashboard. The
        // version is one space past the name, which is precisely what the first cut of the scan
        // could not step over — and with no version to pin, the line was kept entire.
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
        // "MUCK" on its own is a placeholder, so these lines carry no identification at all — and a
        // copyright year is not a release. Nothing is the right answer; the sentence never was.
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
    /// <remarks>
    /// The real reply from <c>game.convergencemush.org:10000</c>, which offers no MSSP at all and was
    /// therefore refused a listing for a fortnight while answering every probe perfectly. RhostMUSH,
    /// TinyMUX and TinyMUSH all answer <c>INFO</c> this way.
    /// </remarks>
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

        // And the codebase still reads out of the same block, unchanged.
        await Assert.That(LoginCommandReading.MeaningfulCodebase(Info, null)).IsEqualTo("RhostMUSH 4.27.3");
    }

    /// <summary>
    /// A name that only restates the codebase identifies nobody, whichever command carried it.
    /// </summary>
    /// <remarks>
    /// The same rule <c>MsspDefaults.MeaningfulName</c> applies to <c>NAME</c>, and the reason it has
    /// to apply here too: every unedited install on the internet answers this way, so admitting one
    /// would let a submitter mint a listing per default install they can point at — and take the
    /// slug the real game will want.
    /// </remarks>
    [Test]
    [Arguments("Name: PennMUSH")]
    [Arguments("Name: PennMUSH 1.8.8p0")]
    // Template text, from MsspDefaults' own list — the vocabulary is shared rather than restated,
    // which is the point of routing this through MeaningfulName instead of writing a second filter.
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
    /// PennMUSH's <c>dump_info()</c>, verbatim in shape. <c>Uptime</c> is a ctime string and not a
    /// timestamp, which is why nothing here may go looking for "the number on the line".
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
    /// Evennia's, which reimplements the same block deliberately differently: two hashes, an
    /// uppercase BEGIN and END, and no <c>Address</c> line at all.
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
        // INFO_VERSION is a string a codebase bumps when it likes. Keying on today's value would
        // make this reader silently stop measuring the day somebody ships 1.2.
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
        // The delimiters are the whole defence. Outside a block that opened and closed, "Connected:"
        // is a word on a connect screen — a countdown, a last-reboot line, or the game telling you
        // that you are — and reading a number out of it would be rule 4's fabrication with a colon
        // in front of it.
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
