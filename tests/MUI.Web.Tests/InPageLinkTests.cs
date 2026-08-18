using System.Text.RegularExpressions;

namespace MUI.Web.Tests;

/// <summary>
/// Every in-page link lands on something the page rendered.
/// </summary>
/// <remarks>
/// A fragment naming no id is not a broken link a browser reports — it scrolls nowhere and says
/// nothing, silently, to a screen reader too. Nothing in a build log or HTTP status ever mentions it.
/// </remarks>
public class InPageLinkTests
{
    [Test]
    [Arguments("/")]
    [Arguments("/games")]
    [Arguments("/find")]
    [Arguments("/archive")]
    [Arguments("/ecosystem")]
    [Arguments("/rankings")]
    [Arguments("/reference")]
    [Arguments("/reference/protocols/mssp")]
    [Arguments("/about")]
    [Arguments("/submit")]
    [Arguments("/g/eldertale")]
    [Arguments("/g/eldertale/mssp")]
    [Arguments("/account/sign-in")]
    public async Task EveryInPageLinkLandsOnSomething(string path)
    {
        await using var site = await SiteHost.StartAsync();

        var html = await site.Client.GetStringAsync(path);

        var targets = Regex.Matches(html, "href=\"#([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Every page carries at least the skip link, so an empty list means the layout lost it, not
        // that this test has nothing to check.
        await Assert.That(targets).IsNotEmpty()
            .Because($"{path} renders no in-page link at all, not even the skip link");

        foreach (var target in targets)
        {
            await Assert.That(html)
                .Contains($"id=\"{target}\"")
                .Because($"{path} links to #{target} and renders no such id");
        }
    }
}
