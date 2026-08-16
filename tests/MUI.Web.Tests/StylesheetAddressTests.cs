using System.Text.RegularExpressions;

namespace MUI.Web.Tests;

/// <summary>
/// The address the stylesheet is linked at.
/// </summary>
/// <remarks>
/// It was <c>app.css</c>, flat, and the proxy in front of the deployment caches <c>wwwroot</c> for
/// four hours. So a deploy that changed the CSS served new markup against the old sheet until every
/// edge copy expired — the trend chart went out styled by a stylesheet written before it existed,
/// which reads as a broken page rather than as a stale one. A URL that changes when the bytes change
/// cannot be stale, and that is a property of the address rather than a promise about somebody's
/// cache configuration, which is why it is asserted here.
/// </remarks>
public class StylesheetAddressTests
{
    [Test]
    public async Task TheStylesheetIsLinkedAtAnAddressThatChangesWithItsBytes()
    {
        await using var site = await SiteHost.StartAsync();

        var head = await site.Client.GetStringAsync("/");
        var href = Regex.Match(head, """<link rel="stylesheet" href="([^"]+)""").Groups[1].Value;

        await Assert.That(href).IsNotEqualTo("app.css")
            .Because("a flat name is a name a cache cannot tell two builds apart by");
        await Assert.That(Regex.IsMatch(href, @"^app\.[a-z0-9]+\.css$")).IsTrue()
            .Because($"the link should carry a fingerprint, and it is '{href}'");
    }

    [Test]
    public async Task TheAddressTheHeadNamesIsTheStylesheetWeShip()
    {
        // A fingerprint nothing serves would be worse than the flat name it replaced: the page would
        // come back styled by nothing at all rather than by something old.
        await using var site = await SiteHost.StartAsync();

        var head = await site.Client.GetStringAsync("/");
        var href = Regex.Match(head, """<link rel="stylesheet" href="([^"]+)""").Groups[1].Value;

        using var response = await site.Client.GetAsync("/" + href);
        var css = await response.Content.ReadAsStringAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(css).Contains("svg.trend");

        // And the flat address still answers, because the manifest and the icons name assets that
        // way and a reader with a bookmarked stylesheet is nobody's problem to break.
        using var flat = await site.Client.GetAsync("/app.css");

        await Assert.That(flat.IsSuccessStatusCode).IsTrue();
    }
}
