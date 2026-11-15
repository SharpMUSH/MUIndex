using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// A SharpMUSH game reads as SharpMUSH, not as the PennMUSH it is compatible with.
/// </summary>
/// <remarks>
/// The strings are SharpMUSH's own, from its source (SharpMUSH/SharpMUSH, main, 2026-11-15):
/// <c>MsspReportService</c> for the report, <c>SocketDescriptorCommands</c> for <c>INFO</c> and
/// <c>VERSION</c>, <c>VersionInfoGenerator</c> for the version line, and <c>mushcnf.dst</c> for the
/// default <c>mud_name</c>. No SharpMUSH game has been probed yet, so these are what the server is
/// written to send, not a capture.
/// </remarks>
public class SharpMushRecognitionTests
{
    private const string DefaultName = "PennMUSH Emulation by SharpMUSH";

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Report(string name) =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["NAME"] = [name],
            ["PLAYERS"] = ["3"],
            ["CODEBASE"] = ["SharpMUSH 1.0.0"],
            ["FAMILY"] = ["TinyMUD"],
        };

    [Test]
    public async Task TheVersionCommandNamesSharpMushThoughItMentionsPennMushFirst()
    {
        // An unedited install: both lines mention PennMUSH. Before SharpMUSH was a known family the
        // first line alone answered, and the game was read as plain "PennMUSH".
        var version = $"You are connected to {DefaultName}\nPennMUSH emulated in SharpMUSH 1.0.0";

        await Assert.That(LoginCommandReading.MeaningfulCodebase(null, version)).IsEqualTo("SharpMUSH 1.0.0");
    }

    [Test]
    public async Task TheInfoBlockNamesSharpMush()
    {
        var info = string.Join('\n',
            "### Begin INFO 1.1",
            "Name: Ravenmoor",
            "Address: https://ravenmoor.example",
            "Uptime: Sat Nov 14 20:00:00 2026",
            "Connected: 3",
            "Size: 120",
            "Version: SharpMUSH 1.0.0",
            "### End INFO");

        await Assert.That(LoginCommandReading.MeaningfulCodebase(info, null)).IsEqualTo("SharpMUSH 1.0.0");
        await Assert.That(LoginCommandReading.MeaningfulName(info, null)).IsEqualTo("Ravenmoor");
        await Assert.That(LoginCommandReading.ConnectedPlayers(info)).IsEqualTo(3);
    }

    [Test]
    public async Task TheShippedNameIsNoName()
    {
        // Every unedited SharpMUSH publishes it, so reading it as a name would match them all to
        // each other — NAME "PennMUSH" again.
        await Assert.That(MsspDefaults.IsPlaceholder(DefaultName)).IsTrue();
        await Assert.That(MsspDefaults.IsPlaceholder("SharpMUSH")).IsTrue();
        await Assert.That(MsspDefaults.MeaningfulName("SharpMUSH", "SharpMUSH 1.0.0")).IsNull();
    }

    [Test]
    public async Task AReportThatNamesTheGameAnswersTheLoginCommands()
    {
        // CODEBASE "SharpMUSH 1.0.0" names an engine we know, so INFO and VERSION go untyped.
        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(Report("Ravenmoor"))).IsTrue();
    }

    [Test]
    public async Task AnUneditedInstallIsStillAsked()
    {
        await Assert.That(MsspSelfDescription.AnswersTheLoginCommands(Report(DefaultName))).IsFalse();
    }

    [Test]
    public async Task AScreenCreditingBothServersIsSharpMush()
    {
        var banner = "Ravenmoor is powered by SharpMUSH, a PennMUSH-compatible server.";

        await Assert.That(CodebaseCredits.Named(banner)).IsEqualTo("SharpMUSH");
    }

    [Test]
    public async Task TheStockWelcomeLineIsNotACredit()
    {
        // connect.txt's "Welcome to SharpMUSH!" is a greeting, not attribution; the MSSP CODEBASE
        // is what identifies the game.
        await Assert.That(CodebaseCredits.Named("Welcome to SharpMUSH!")).IsNull();
    }
}
