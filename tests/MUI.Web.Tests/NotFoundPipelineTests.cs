using System.Net;

using MUI.Web.Api;

namespace MUI.Web.Tests;

/// <summary>
/// The not-found page answers for pages, and for nothing else.
/// </summary>
/// <remarks>
/// One process serves three surfaces — the site, the read API, and the account endpoints — so a rule
/// about how a <em>page</em> says "nothing here" must not reach the two that answer for themselves. A
/// status-code page applied to the whole pipeline would rewrite every bodiless error in the process:
/// <c>Results.Unauthorized()</c> from a failed passkey sign-in would come back as HTML, and
/// <c>passkey.js</c> would render <c>&lt;!DOCTYPE html&gt;</c> into the sign-in status line.
/// </remarks>
public class NotFoundPipelineTests
{
    [Test]
    public async Task ABodilessErrorFromAnAccountEndpointStaysBodiless()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.PostAsync(SiteHost.SignInPath, content: null);
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(body).DoesNotContain("<!DOCTYPE html>");
        await Assert.That(body).DoesNotContain("No game here");
    }

    [Test]
    public async Task AnUnmatchedApiRouteIsNotAnsweredWithAPage()
    {
        // A consumer reading /api gets JSON or nothing; a markup page there is a parse error at the far end.
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync($"{ApiRoutes.Base}/nothing-here");
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(body).DoesNotContain("<!DOCTYPE html>");
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsNotEqualTo("text/html");
    }

    [Test]
    public async Task TheApiKeepsItsOwnProblemDocumentForAGameNobodyHas()
    {
        // Nothing in front of the API may replace its own §10 problem document with the site's copy.
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync($"{ApiRoutes.Games}/never-existed");
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(body).Contains("No such game");
        await Assert.That(body).DoesNotContain("<!DOCTYPE html>");
    }

    [Test]
    public async Task APageThatIsNotThereStillGetsTheSitesOwnAnswer()
    {
        // The other half of the rule: scoping it must not quietly undo the page behaviour.
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync("/nothing-here");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(Render.Words(await response.Content.ReadAsStringAsync()))
            .Contains("No game at this address");
    }
}
