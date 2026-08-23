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
    /// A flag whose value is missing is an error, not a silent null — the next argument being eaten
    /// as a credential is how a pass runs against the wrong thing.
    /// </summary>
    [Test]
    public async Task AFlagWithNoValueIsRefused()
    {
        await Assert.That(() => Arguments.Parse(["--ares-key"])).ThrowsException();
    }
}
