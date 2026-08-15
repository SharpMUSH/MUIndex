using System.Net;

namespace MUI.Web.Tests;

/// <summary>
/// <c>/crawler</c> — the address the crawler hands to every server it dials (spec §11) — over real
/// HTTP, because a status line is not something a rendered component can be asked about.
/// </summary>
/// <remarks>
/// The reader these assertions are for has just found an unfamiliar connection in their logs and is
/// retyping a URL out of it. Everything that can go wrong at that moment is silent from our side:
/// the address 404s, or it answers GET and not the HEAD a link checker sent, or it drops the reader
/// at the top of a long page instead of the part that says how to make us stop.
/// </remarks>
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
        // MapGet alone answers HEAD with 405, and the one thing worse than an unreachable contact
        // address is one that reports itself broken to whoever is monitoring it.
        await using var site = await SiteHost.StartAsync();

        using var head = new HttpRequestMessage(HttpMethod.Head, CrawlerContact.Path);
        var response = await site.Client.SendAsync(head);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Found);
    }

    [Test]
    public async Task AnAdminWhoAskedForThePlainRenderingGetsIt()
    {
        // ?plain=1 is a real second surface (§9), and this reader is quite likely to be holding a
        // terminal rather than a browser. The fragment goes last, after the query, because that is
        // where a fragment goes.
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync($"{CrawlerContact.Path}?plain=1");

        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo($"/about?plain=1#{CrawlerContact.Fragment}");
    }

    [Test]
    public async Task TheFragmentNamesASectionThePageActuallyHas()
    {
        // The two halves are written in different files — About.razor renders id="about-@section.Id"
        // and this route names the id — so a renaming that broke the link would otherwise show up
        // only as a reader landing at the top of the page and reading about rankings.
        await using var site = await SiteHost.StartAsync();

        var about = await site.Client.GetStringAsync("/about");

        await Assert.That(about).Contains($"id=\"{CrawlerContact.Fragment}\"");
    }
}
