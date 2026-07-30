using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// What the probe is allowed to put on the wire.
/// </summary>
/// <remarks>
/// The crawler is an anonymous guest on someone else's server. Everything it reads is published to
/// any connection that arrives — the banner, the option handshake, the MSSP report, and the
/// pre-login <c>WHO</c> the TinyMUD family answers before login. It authenticates against nothing
/// and changes nothing. These tests exist so that stays true by construction rather than by anyone
/// remembering it.
/// </remarks>
public class ProbeRestraintTests
{
    [Test]
    public async Task TheProbeNeverLogsInAndNeverCreatesACharacter()
    {
        var forbidden = new[] { "connect", "create", "quit", "@shutdown", "pemit", "ch" };

        foreach (var command in TelnetProbe.PermittedCommands)
        {
            foreach (var word in forbidden)
            {
                await Assert.That(command).DoesNotContain(word, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Test]
    public async Task TheOnlyThingItAsksForIsWho()
    {
        // A short list is the point. Every addition here is a new way to affect a stranger's server,
        // so it should be hard to grow and obvious when it does.
        await Assert.That(TelnetProbe.PermittedCommands).Count().IsEqualTo(1);
        await Assert.That(TelnetProbe.PermittedCommands[0]).IsEqualTo("WHO");
    }

    [Test]
    public async Task ItIdentifiesItselfSoAnAdminCanFindOutWhoIsKnocking()
    {
        // Spec §11: an admin reading their logs must be able to work out who we are and how to make
        // us stop. That is a politeness obligation, not a cosmetic string.
        var options = new ProbeOptions();

        await Assert.That(options.TerminalTypes).IsNotEmpty();
        await Assert.That(string.Join(" ", options.TerminalTypes)).Contains("MUINDEX");
        await Assert.That(options.InfoUrl).StartsWith("https://");
    }
}
