using MUI.Crawler.Cli;

namespace MUI.Crawler.Tests;

/// <summary>
/// The flags that ask <c>mui-crawl</c> for one AresCentral pass.
/// </summary>
/// <remarks>
/// This is how a running deployment is administered — <c>docker compose run --entrypoint mui-crawl</c>
/// — so a flag that silently parses as something else is a pass somebody thinks they ran.
/// </remarks>
public class AresArgumentsTests
{
    [Test]
    public async Task TheAresFlagAsksForOnePassOverTheHub()
    {
        var parsed = Arguments.Parse(["--ares"]);

        await Assert.That(parsed.Ares).IsTrue();

        // Distinct passes: asking for one must not quietly ask for the other as well.
        await Assert.That(parsed.I3).IsFalse();
    }

    [Test]
    public async Task NothingAsksForAnAresPassByDefault()
    {
        await Assert.That(Arguments.Parse([]).Ares).IsFalse();
    }

    [Test]
    public async Task CredentialsCanBeGivenOnTheCommandLine()
    {
        var parsed = Arguments.Parse(
            ["--ares", "--ares-client-id", "muindex", "--ares-key", "not-a-real-key"]);

        await Assert.That(parsed.AresClientId).IsEqualTo("muindex");
        await Assert.That(parsed.AresKey).IsEqualTo("not-a-real-key");
    }

    /// <summary>
    /// Asking for both passes at once is refused rather than silently half-honoured.
    /// </summary>
    /// <remarks>
    /// <c>Program</c> runs the Intermud-3 pass and returns, so <c>--i3 --ares</c> would do the I3
    /// pass and skip the AresCentral one without saying so — an operator would read the I3 summary
    /// and believe both had run. Each flag means "instead of a crawl cycle", and two of those is not
    /// a thing to guess at.
    /// </remarks>
    [Test]
    public async Task AskingForBothPassesAtOnceIsRefused()
    {
        await Assert.That(() => Arguments.Parse(["--i3", "--ares"])).Throws<ArgumentException>();
        await Assert.That(() => Arguments.Parse(["--ares", "--i3"])).Throws<ArgumentException>();
    }

    /// <summary>
    /// A flag whose value is missing is an error, not a silent null — the next argument being eaten
    /// as a credential is how a pass runs against the wrong thing.
    /// </summary>
    [Test]
    public async Task AFlagWithNoValueIsRefused()
    {
        await Assert.That(() => Arguments.Parse(["--ares-key"])).ThrowsException();
    }
}
