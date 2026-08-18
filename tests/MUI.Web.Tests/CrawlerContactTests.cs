using System.Net;

namespace MUI.Web.Tests;

/// <summary>
/// <c>/crawler</c> — the address the crawler hands to every server it dials (spec §11) — over real
/// HTTP, because a status line is not something a rendered component can be asked about.
/// </summary>
/// <remarks>The reader has just found an unfamiliar connection in their logs and is retyping a URL out of it — a 404, a GET-only route, or landing at the top of a long page would all fail silently.</remarks>
public class CrawlerContactTests
{
    [Test]
    public async Task TheAddressWePublishLandsOnThePartOfAboutThatAnswersIt()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync(CrawlerContact.Path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Found);
        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo($"/about#{CrawlerContact.Fragment}");
    }

    [Test]
    public async Task ALinkCheckerAskingWhetherItStillWorksIsAnswered()
    {
        // MapGet alone answers HEAD with 405 — reporting itself broken to whoever is monitoring it.
        await using var site = await SiteHost.StartAsync();

        using var head = new HttpRequestMessage(HttpMethod.Head, CrawlerContact.Path);
        var response = await site.Client.SendAsync(head);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Found);
    }

    [Test]
    public async Task AnAdminWhoAskedForThePlainRenderingGetsIt()
    {
        // ?plain=1 is a real second surface (§9); the fragment goes last, after the query.
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync($"{CrawlerContact.Path}?plain=1");

        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo($"/about?plain=1#{CrawlerContact.Fragment}");
    }

    [Test]
    public async Task TheFragmentNamesASectionThePageActuallyHas()
    {
        // Written in different files — About.razor renders the id, this route names it — so a renaming break would otherwise be invisible.
        await using var site = await SiteHost.StartAsync();

        var about = await site.Client.GetStringAsync("/about");

        await Assert.That(about).Contains($"id=\"{CrawlerContact.Fragment}\"");
    }
}
