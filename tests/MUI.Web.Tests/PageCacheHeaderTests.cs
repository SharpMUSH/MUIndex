using MUI.Web.Api;

namespace MUI.Web.Tests;

/// <summary>
/// No page of this site may be stored by the browser that read it (spec §8.2).
/// </summary>
/// <remarks>
/// Every page names its reader: the header carries either "Sign in" or the account link, and the
/// claim page renders the whole ceremony or "you need an account first" depending on who is asking.
/// A document like that, answered with no cache directive at all, is eligible for the browser's
/// back/forward cache — so an operator who lands on a claim page signed out, signs in, and presses
/// Back is shown the signed-out render again, with its sign-in link, for ever. That loop was
/// reported from the live site and reproduces in both Chromium and Firefox.
/// <b>The rule is scoped to documents</b>: the read API, the badges, the icons and the fingerprinted
/// assets all say for themselves how long they may be held, and nothing here may overrule them.
/// </remarks>
public class PageCacheHeaderTests
{
    [Test]
    [Arguments("/")]
    [Arguments("/games")]
    [Arguments("/g/aardwolf/claim")]
    [Arguments("/account/sign-in")]
    [Arguments("/about")]
    public async Task APageIsNeverStored(string path)
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync(path);

        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/html");
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    /// <summary>The page a reader reaches through a mistyped URL is a page too.</summary>
    [Test]
    public async Task TheNotFoundPageIsNeverStoredEither()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync("/nothing-here");

        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    /// <summary>
    /// The read API answers a consumer, not a reader, and already says how long its answer keeps.
    /// </summary>
    [Test]
    public async Task TheReadApiKeepsItsOwnCacheDirective()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync(ApiRoutes.Games);

        await Assert.That(response.Headers.CacheControl!.ToString()).IsEqualTo("public, max-age=60");
    }

    /// <summary>
    /// A fingerprinted asset is immutable and must stay so — the whole point of the fingerprint.
    /// </summary>
    [Test]
    public async Task AStaticAssetKeepsItsOwnCacheDirective()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync("/site.webmanifest");

        await Assert.That(response.Headers.CacheControl?.NoStore).IsNotEqualTo(true);
    }
}
