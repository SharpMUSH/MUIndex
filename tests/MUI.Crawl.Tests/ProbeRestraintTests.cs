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
    public async Task TheOnlyThingsItAsksForAreWhoAndAnMsspReport()
    {
        // A short list is the point. Every addition here is a new way to affect a stranger's server,
        // so it should be hard to grow and obvious when it does.
        await Assert.That(TelnetProbe.PermittedCommands).Count().IsEqualTo(2);
        await Assert.That(TelnetProbe.PermittedCommands[0]).IsEqualTo("WHO");
        await Assert.That(TelnetProbe.PermittedCommands[1]).IsEqualTo("MSSP-REQUEST");
    }

    [Test]
    public async Task TheOneThatPutsTextOnTheWireIsOffUntilSomebodyAsksForIt()
    {
        // IAC DO 70 is negotiation: a server that does not implement MSSP ignores it and nothing it
        // does is affected. MSSP-REQUEST is *text*, sent at a login screen, and a server that does
        // not implement it reads the word as a character name — realms.reichel.net:4000 and
        // tsosmud.org:7070 both answer "Illegal name, try another.", eternitymud.com:23 answers
        // "'MSSP-REQUEST' does not exist.". So it spends one of a stranger's login attempts, and a
        // crawler does not get to do that to every game it has never met by default.
        await Assert.That(new ProbeOptions().RequestPlaintextMssp).IsFalse();
    }

    [Test]
    public async Task AskingForMsspByNegotiationIsStillTheDefaultRoute()
    {
        // The polite route stays on: it is the client half of an option handshake, and asking is
        // what makes a "no" a measurement rather than an assumption never tested.
        var options = new ProbeOptions();

        await Assert.That(options.RequestOptions).Contains(ProbeOptions.MsspOption);
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
