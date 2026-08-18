using System.Text.RegularExpressions;

namespace MUI.Web.Tests;

/// <summary>
/// Every in-page link lands on something the page rendered.
/// </summary>
/// <remarks>
/// <para>
/// A fragment that names no id is not a broken link a browser reports: it scrolls nowhere and says
/// nothing, and to a screen reader following it the page simply does not move. Nothing in a build
/// log or an HTTP status ever mentions it.
/// </para>
/// <para>
/// This began as a test of the game page's footer, where a link to <c>#changed</c> was rendered
/// unconditionally beside a section that was not. That footer is gone — its two jump links pointed
/// a screen and a half back up a page they sat at the bottom of, and the rest of it was the site's
/// own navigation restated. What is left is the rule the old test happened to prove, asked of every
/// page rather than one, and it now covers the link a keyboard reader meets first: the skip link
/// in the bar, which nothing had ever followed.
/// </para>
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

        // A sweep over "every in-page link" asserts nothing where a page renders none and passes
        // anyway. Every page on this site carries the skip link at least, so an empty list here is
        // the layout having lost it rather than this test having nothing to do.
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
