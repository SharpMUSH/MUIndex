using MUI.Web.Components;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

/// <summary>
/// What the plain-text page tells a reader to type at an endpoint behind a handshake.
/// </summary>
/// <remarks>
/// The line was written when nothing could produce a TLS endpoint, so nobody had to read it against
/// one: it prints <c>telnet host port</c> for every address and appends a note. Followed literally
/// at a TLS port that is a connection that opens and then says nothing for ever — which is the exact
/// symptom that made these ports invisible to the crawler in the first place, handed to a reader as
/// instructions.
/// </remarks>
public class TlsConnectLineTests
{
    private static readonly DateTimeOffset Now = FixtureGameQueries.Now;

    [Test]
    public async Task ATlsEndpointIsNotOfferedAsATelnetCommand()
    {
        var page = await new FixtureGameQueries().FindAsync("ashen-court");

        await Assert.That(page!.Endpoints.Any(e => e.TlsMeasured)).IsTrue();

        var line = PlainText.Render(page, Now)
            .ReplaceLineEndings("\n")
            .Split('\n')
            .First(line => line.Contains("ashen.example", StringComparison.Ordinal));

        await Assert.That(line).DoesNotStartWith("telnet ");
        await Assert.That(line).Contains("ashen.example 4000");
    }

    /// <summary>An ordinary port keeps the command that has always worked there.</summary>
    [Test]
    public async Task AnOrdinaryEndpointIsStillOfferedAsATelnetCommand()
    {
        var page = await new FixtureGameQueries().FindAsync("aardwolf");

        var line = PlainText.Render(page!, Now)
            .ReplaceLineEndings("\n")
            .Split('\n')
            .First(line => line.Contains("aardmud.org", StringComparison.Ordinal));

        await Assert.That(line).IsEqualTo("telnet aardmud.org 4000");
    }
}
