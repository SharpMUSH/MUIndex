using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using MUI.Web.Components;
using MUI.Web.Components.Layout;
using MUI.Web.Components.Pages;
using MUI.Web.Data;
using MUI.Web.Fixtures;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// <c>/activity</c>: the two liveness feeds the front page no longer carries.
/// </summary>
public class ActivityPageTests
{
    private static readonly DateTimeOffset Now = FixtureGameQueries.Now;
    private static readonly FixtureGameQueries Queries = new();

    [Test]
    public async Task ThePageDrawsWentDarkAndCameBack()
    {
        var html = await Render.PageAsync<Activity>([]);

        await Assert.That(Render.Words(html)).Contains(Messages.For(Locales.SourceTag, "feed.wentDark"));
        await Assert.That(Render.Words(html)).Contains(Messages.For(Locales.SourceTag, "feed.cameBack"));

        // Verdigris is the fixture's went-dark entry, Aardwolf the came-back one.
        await Assert.That(html).Contains("href=\"/g/verdigris\"");
        await Assert.That(html).Contains("href=\"/g/aardwolf\"");
    }

    [Test]
    public async Task TheAddressAnswersDirectlyAndRoutesUnderALocalePrefix()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync("/activity");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);

        var text = Render.Text(await response.Content.ReadAsStringAsync());
        await Assert.That(text).Contains(Messages.For(Locales.SourceTag, "activity.title"));

        var localized = await site.Client.GetAsync("/de/activity");
        await Assert.That(localized.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);
    }

    [Test]
    public async Task ThePlainMirrorCarriesTheSameTwoFeeds()
    {
        var feeds = await Queries.FeedsAsync();
        var text = PlainText.RenderActivity(Locales.SourceTag, feeds, Now);

        await Assert.That(text).Contains("WENT DARK");
        await Assert.That(text).Contains("CAME BACK");
        await Assert.That(text).DoesNotContain("NEWLY DISCOVERED");
    }

    [Test]
    public async Task TheNavOffersActivityAlongsideTheOtherBrowseDestinations()
    {
        var markup = await Render.ComponentAsync<MainLayout>(new Dictionary<string, object?>(), services =>
        {
            services.AddSingleton(new CatalogueSource(IsMeasured: true));
            services.AddCascadingValue(_ =>
            {
                var context = new DefaultHttpContext();

                context.Request.Path = "/games";

                return (HttpContext)context;
            });
        });

        await Assert.That(markup).Contains("href=\"/activity\"");
        await Assert.That(Render.Words(markup)).Contains(Messages.For(Locales.SourceTag, "nav.activity"));
    }
}
