namespace MUI.Discovery.Tests;

/// <summary>
/// The connect-screen fingerprint: stable enough to survive a host move, sensitive enough to change on
/// a redesign (spec §6.2, §7.3).
/// </summary>
public class BannerFingerprintTests
{
    [Test]
    public async Task TheSameScreenHashesTheSameWayTwice()
    {
        const string banner = "Welcome to Corvid.\r\nType 'connect <name> <password>'.\r\n";

        await Assert.That(BannerFingerprint.Of(banner)).IsEqualTo(BannerFingerprint.Of(banner));
        await Assert.That(BannerFingerprint.Of(banner).Length).IsEqualTo(64);
    }

    [Test]
    public async Task ColourChangesDoNotChangeTheFingerprint()
    {
        // The reason it is ANSI-stripped: a game that recolours its login screen has not become a
        // different game, and re-theming is common.
        var plain = BannerFingerprint.Of("Welcome to Corvid.\nType 'connect'.");
        var coloured = BannerFingerprint.Of("\e[1;36mWelcome to Corvid.\e[0m\nType 'connect'.");

        await Assert.That(coloured).IsEqualTo(plain);
    }

    [Test]
    public async Task LineEndingsAndRunsOfSpacesDoNotChangeTheFingerprint()
    {
        // The reason it is whitespace-collapsed: CRLF versus LF is a transport accident, and box-drawn
        // banners get re-padded when somebody edits one line.
        var unix = BannerFingerprint.Of("Welcome to Corvid.\nType 'connect'.");
        var dos = BannerFingerprint.Of("  Welcome   to  Corvid.\r\n\r\nType 'connect'.  \r\n");

        await Assert.That(dos).IsEqualTo(unix);
    }

    [Test]
    public async Task ADifferentScreenHashesDifferently()
    {
        var corvid = BannerFingerprint.Of("Welcome to Corvid.");
        var magpie = BannerFingerprint.Of("Welcome to Magpie.");

        await Assert.That(magpie).IsNotEqualTo(corvid);
    }

    [Test]
    public async Task AnEmptyOrWhitespaceBannerStillHashesRatherThanThrowing()
    {
        // Plenty of servers send nothing before the first prompt. That is a fact about them, not an
        // error, and it must not take a probe down. What it must not do is become a *signal* — see
        // IdentityMatcherTests.TwoSilentConnectScreensAreNotTheSameGame.
        await Assert.That(BannerFingerprint.Of("")).IsEqualTo(BannerFingerprint.Of("   \r\n  "));
        await Assert.That(BannerFingerprint.Flatten("   \r\n  ")).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task OtherControlSequencesAreStrippedToo()
    {
        // OSC title-setting and cursor moves show up in real connect screens; neither is content.
        var plain = BannerFingerprint.Of("Corvid");
        var noisy = BannerFingerprint.Of("\e]0;Corvid\a\e[2J\e[HCorvid");

        await Assert.That(noisy).IsEqualTo(plain);
    }

    [Test]
    public async Task FlattenIsWhatIsHashedSoTheBeaconReaderSearchesTheSameText()
    {
        // One normaliser with two readers cannot drift; two would. ClaimTokenBeacon searches Flatten's
        // output, so a token inside an SGR run is still found.
        await Assert.That(BannerFingerprint.Flatten("\e[1;36mMUINDEX-CLAIM:\e[0m  abc123\r\n"))
            .IsEqualTo("MUINDEX-CLAIM: abc123");
    }
}
