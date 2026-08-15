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
}
