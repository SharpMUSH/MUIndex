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
