using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Reference;

namespace MUI.Web.Tests;

/// <summary>
/// The client matrix, read off a rendered frame.
/// </summary>
/// <remarks>The same rule as the presence heatmap: an unknown that looks like a no would state our own gap as a fact about somebody else's software.</remarks>
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
        // The demotion is in the type: a yes without a citation publishes as unknown, never an unsourced claim.
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
        var html = Render.Words(await RenderAsync(
            new CapabilityClaim("scripting", CapabilityState.Absent, "https://example.invalid/history"),
            new CapabilityClaim("MSDP", CapabilityState.Unknown, null)));

        await Assert.That(html).Contains(">no<");
        await Assert.That(html).Contains("unknown");
    }

    [Test]
    public async Task EveryStateIsAWordAndNotOnlyAColour()
    {
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
