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
    public async Task TheOnlyThingsItAsksForAreThePreLoginReadOnlyCommands()
    {
        // A short list is the point. Every addition here is a new way to affect a stranger's server,
        // so it should be hard to grow and obvious when it does.
        await Assert.That(TelnetProbe.PermittedCommands).Count().IsEqualTo(3);
        await Assert.That(TelnetProbe.PermittedCommands).Contains("WHO");
        await Assert.That(TelnetProbe.PermittedCommands).Contains("INFO");
        await Assert.That(TelnetProbe.PermittedCommands).Contains("VERSION");
    }

    [Test]
    public async Task PlaintextMsspRequestIsNeverTypedAtALoginScreen()
    {
        // The plaintext MSSP-REQUEST form is text at a login screen, and eight of twenty games tried
        // read it as a character name — "Illegal name, try another." on realms.reichel.net:4000 and
        // tsosmud.org:7070, "'MSSP-REQUEST' does not exist." on eternitymud.com:23. It belongs in
        // TelnetNegotiationCore (issue #61), not here, and the three games that did answer it all
        // answer option 70 as well.
        await Assert.That(TelnetProbe.PermittedCommands).DoesNotContain("MSSP-REQUEST");
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

    [Test]
    public async Task TheContactAddressStaysAPlaceholderEvenThoughTheDomainIsDecided()
    {
        // mu-index.com is settled (§15.1) and this default is still example-domain, on purpose.
        // Compiling one deployment's contact page in would have every fork and every laptop run
        // announce it to the servers THEY dial — a claim about somebody else's crawl, in the shape
        // of the ContactedMaintainer defect. The real address is configuration.
        await Assert.That(new ProbeOptions().InfoUrl).DoesNotContain("mu-index.com");
    }

    [Test]
    public async Task AContactAddressNobodyCouldOpenIsRefused()
    {
        // Not a degraded crawl: a crawl that cannot be complained to. It fails while somebody is
        // still watching the terminal rather than being announced to strangers for six months.
        await Assert.That(() => new ProbeOptions { InfoUrl = "mu-index.com/crawler" }.Validate())
            .Throws<ArgumentException>();
        await Assert.That(() => new ProbeOptions { InfoUrl = string.Empty }.Validate())
            .Throws<ArgumentException>();

        // http included, because we hand this address to a reader with no way to check what they
        // are opening.
        await Assert.That(() => new ProbeOptions { InfoUrl = "http://mu-index.com/crawler" }.Validate())
            .Throws<ArgumentException>();

        await Assert.That(() => new ProbeOptions { InfoUrl = "https://mu-index.com/crawler" }.Validate())
            .ThrowsNothing();
    }
}
