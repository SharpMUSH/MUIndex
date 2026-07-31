using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Reference;

namespace MUI.Web.Tests;

/// <summary>
/// The client matrix, read off a rendered frame.
/// </summary>
/// <remarks>
/// The rule this holds is the one this project already spent a week enforcing on its heatmap: an
/// unknown that looks like a no is the same defect wearing different clothes. There it was an
/// unmeasured hour rendered as a dark cell; here it is a capability nobody established rendered as
/// an absence. Both state our own gap as a fact about somebody else's software.
/// </remarks>
public class ClientCapabilityMatrixTests
{
    private static Task<string> RenderAsync(params CapabilityClaim[] claims) =>
        Render.ComponentAsync<ClientCapabilityMatrix>(new()
        {
            [nameof(ClientCapabilityMatrix.Claims)] = (IReadOnlyList<CapabilityClaim>)claims,
        });

    [Test]
    public async Task AnUnestablishedCapabilityRendersAsUnknownAndNeverAsNo()
    {
        var html = Render.Words(await RenderAsync(
            new CapabilityClaim("MSDP", CapabilityState.Unknown, null)));

        await Assert.That(html).Contains("unknown");
        await Assert.That(html).DoesNotContain(">no<");
        await Assert.That(html).Contains("we did not find one");
    }

    [Test]
    public async Task AClaimWrittenWithoutASourceIsDemotedRatherThanPublished()
    {
        // The demotion is in the type, so an author who writes a yes and forgets the citation
        // publishes an unknown rather than an unsourced assertion. The content test is what tells
        // them; the reader is never shown the claim.
        var claim = CapabilityClaim.Read("GMCP", "yes", null);

        await Assert.That(claim.State).IsEqualTo(CapabilityState.Unknown);
        await Assert.That(claim.IsClaimed).IsFalse();

        var html = Render.Words(await RenderAsync(claim));

        await Assert.That(html).Contains("GMCP");
        await Assert.That(html).Contains("unknown");
        await Assert.That(html).DoesNotContain(">yes<");
    }

    [Test]
    public async Task ADocumentedAbsenceIsDistinctFromAnUnknownOnTheSameTable()
    {
        // Three states, three words. A sourced "no" — a project saying its own feature does not
        // work — is a real finding and must not read the same as silence.
        var html = Render.Words(await RenderAsync(
            new CapabilityClaim("scripting", CapabilityState.Absent, "https://example.invalid/history"),
            new CapabilityClaim("MSDP", CapabilityState.Unknown, null)));

        await Assert.That(html).Contains(">no<");
        await Assert.That(html).Contains("unknown");
    }

    [Test]
    public async Task EveryStateIsAWordAndNotOnlyAColour()
    {
        // No glyph carries a state on its own here. In greyscale, through a screen reader and on a
        // printout the cell still says which of the three it is.
        var html = await RenderAsync(
            new CapabilityClaim("TLS", CapabilityState.Present, "https://example.invalid/tls"),
            new CapabilityClaim("MXP", CapabilityState.Unknown, null));

        var words = Render.Words(html);

        await Assert.That(words).Contains("yes");
        await Assert.That(words).Contains("unknown");
    }

    [Test]
    public async Task TheTableSaysHowMuchOfItselfIsEstablished()
    {
        // The count sits in the section head so it survives a collapsed section and a screen reader
        // alike, the same reasoning as the game pages' disagreement count.
        var html = Render.Words(await RenderAsync(
            new CapabilityClaim("TLS", CapabilityState.Present, "https://example.invalid/tls"),
            new CapabilityClaim("MXP", CapabilityState.Unknown, null),
            new CapabilityClaim("MSP", CapabilityState.Unknown, null)));

        await Assert.That(html).Contains("1 of 3 established");
    }

    [Test]
    public async Task ACitationIsShownByHostRatherThanAsAWallOfUrl()
    {
        var html = await RenderAsync(
            new CapabilityClaim("TLS", CapabilityState.Present, "https://wiki.mudlet.org/w/Manual:Supported_Protocols"));

        await Assert.That(html).Contains("wiki.mudlet.org");
        await Assert.That(html).Contains("href=\"https://wiki.mudlet.org/w/Manual:Supported_Protocols\"");
    }

    [Test]
    public async Task TheScreenReaderRowIsAlwaysPresentEvenWhenAPageSaysNothingAboutIt()
    {
        var silent = new ReferenceDocument
        {
            Kind = ReferenceKind.Client,
            Slug = "silent",
            Title = "Silent",
            Summary = "A client whose page mentions no capabilities at all.",
            Body = string.Empty,
        };

        var claims = ClientCapabilities.For(silent);

        await Assert.That(claims.Count).IsEqualTo(ClientCapabilities.Rows.Count);
        await Assert.That(claims[0].Name).IsEqualTo(ClientCapabilities.ScreenReader);
        await Assert.That(claims.All(c => c.State is CapabilityState.Unknown)).IsTrue();
    }
}
