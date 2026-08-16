using System.Net;

using MUI.Web.Api;

namespace MUI.Web.Tests;

/// <summary>
/// The not-found page answers for pages, and for nothing else.
/// </summary>
/// <remarks>
/// <para>
/// One process serves three surfaces — the site, the read API, and the account endpoints
/// (spec §4, §8, §10) — so a rule about how a <em>page</em> says "there is nothing here" must not
/// reach the two that answer for themselves. A status-code page applied to the whole pipeline
/// rewrites every bodiless error in the process: <c>Results.Unauthorized()</c> from a failed passkey
/// sign-in comes back as a page of HTML, and <c>passkey.js</c> renders
/// <c>throw new Error(await response.text())</c> into the sign-in status line — so a reader who
/// mistyped their passkey is shown <c>&lt;!DOCTYPE html&gt;</c>.
/// </para>
/// <para>
/// These are the tests that were missing when it shipped: the harness knew only about pages, so
/// nothing in the suite ever asked what the API or an account endpoint answered.
/// </para>
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
        // A consumer reading /api gets JSON or nothing. A page of markup under an API route is a
        // parse error at the far end and tells them nothing about what went wrong.
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
        // The API already says this well, in the vocabulary §10 publishes. Nothing in front of it
        // may replace that with the site's copy.
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
        // The other half of the rule, so scoping it does not quietly undo the page behaviour.
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync("/nothing-here");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(Render.Words(await response.Content.ReadAsStringAsync()))
            .Contains("No game at this address");
    }
}
