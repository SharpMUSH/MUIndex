using System.Text.RegularExpressions;

namespace MUI.Web.Tests;

/// <summary>
/// The address the stylesheet is linked at.
/// </summary>
/// <remarks>
/// A flat <c>app.css</c> behind a 4-hour edge cache meant a deploy served new markup against the old
/// sheet until every edge copy expired. A URL that changes when the bytes change can't be stale — a
/// property of the address, not a promise about cache configuration.
/// </remarks>
public class StylesheetAddressTests
{
    [Test]
    public async Task TheStylesheetIsLinkedAtAnAddressThatChangesWithItsBytes()
    {
        await using var site = await SiteHost.StartAsync();

        var head = await site.Client.GetStringAsync("/");
        var href = Regex.Match(head, """<link rel="stylesheet" href="([^"]+)""").Groups[1].Value;

        await Assert.That(href).IsNotEqualTo("/app.css")
            .Because("a flat name is a name a cache cannot tell two builds apart by");
        await Assert.That(Regex.IsMatch(href, @"^/app\.[a-z0-9]+\.css$")).IsTrue()
            .Because($"the link should be absolute and fingerprinted, and it is '{href}'");
    }

    [Test]
    public async Task TheAddressTheHeadNamesIsTheStylesheetWeShip()
    {
        await using var site = await SiteHost.StartAsync();

        var head = await site.Client.GetStringAsync("/");
        var href = Regex.Match(head, """<link rel="stylesheet" href="([^"]+)""").Groups[1].Value;

        using var response = await site.Client.GetAsync(href);
        var css = await response.Content.ReadAsStringAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(css).Contains("svg.trend");

        // The flat address still answers — the manifest and icons name assets that way.
        using var flat = await site.Client.GetAsync("/app.css");

        await Assert.That(flat.IsSuccessStatusCode).IsTrue();

        await Assert.That(await flat.Content.ReadAsStringAsync()).IsEqualTo(css);
    }
}
