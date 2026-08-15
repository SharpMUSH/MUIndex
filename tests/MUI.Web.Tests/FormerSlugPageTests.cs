using System.Net;

using MUI.Catalog.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace MUI.Web.Tests;

/// <summary>
/// Spec §5.7 at the URL people actually hold — <c>/g/{slug}</c>, over real HTTP.
/// </summary>
/// <remarks>
/// A bookmark, a link in a channel topic and a search-engine result all point at the page rather
/// than at <c>/api/games/{key}</c>, so these are the assertions the promise is made of: the status
/// line is 301 and not 302, the <c>Location</c> is the page the game has now, an archived game's
/// former URL behaves exactly as a live one's, and a slug nobody ever held is still an honest 404.
/// </remarks>
public class FormerSlugPageTests
{
    [Test]
    public async Task AFormerSlugRedirectsToTheGamesPagePermanently()
    {
        await using var site = await SiteHost.StartAsync(services => services
            .AddSingleton<ISlugHistoryStore>(new FakeSlugHistory { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await site.Client.GetAsync("/g/tidewater-nights");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/g/m-u-s-h");
    }

    [Test]
    public async Task AnArchivedGamesFormerSlugRedirectsExactlyAsALiveOneDoes()
    {
        // §7.5: archiving takes a game out of the default listing and out of nothing else. Its page
        // still renders and its former URLs still point at it.
        await using var site = await SiteHost.StartAsync(services => services
            .AddSingleton<ISlugHistoryStore>(new FakeSlugHistory { ["gaslight-lane"] = "gaslight-row" }));

        var moved = await site.Client.GetAsync("/g/gaslight-lane");
        var page = await site.Client.GetAsync("/g/gaslight-row");

        await Assert.That(moved.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(moved.Headers.Location!.ToString()).IsEqualTo("/g/gaslight-row");
        await Assert.That(page.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(Render.Words(await page.Content.ReadAsStringAsync())).Contains("archived");
    }

    [Test]
    public async Task AGameThatTookItsOwnFormerSlugBackResolvesToItselfRatherThanBouncing()
    {
        // The store answers nothing for a slug that is current again, so the page renders and there
        // is no redirect to follow — which is the difference between a URL that works and a browser
        // stuck in a loop. Asserted here as well as at the store, because this is where a reader
        // would have met it.
        await using var site = await SiteHost.StartAsync(services => services
            .AddSingleton<ISlugHistoryStore>(new FakeSlugHistory()));

        var response = await site.Client.GetAsync("/g/m-u-s-h");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Location).IsNull();
    }

    [Test]
    public async Task ASlugNobodyEverHeldIsStillNotFound()
    {
        await using var site = await SiteHost.StartAsync(services => services
            .AddSingleton<ISlugHistoryStore>(new FakeSlugHistory { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await site.Client.GetAsync("/g/never-existed");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(Render.Words(await response.Content.ReadAsStringAsync()))
            .Contains("No game here");
    }

    [Test]
    public async Task AMistypedUrlGetsTheSitesOwnNotFoundPage()
    {
        // Not about slugs at all, and it is how the one above came to be true: the <NotFound>
        // fragment inside <Router> renders nothing under static server rendering, so every unknown
        // URL was answered with a 404 and an empty body while this paragraph sat in the source
        // looking like the site's answer.
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync("/nothing-here");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(Render.Words(await response.Content.ReadAsStringAsync()))
            .Contains("No game here");
    }

    [Test]
    public async Task AnAliasForASlugAGameStillWearsNeverTakesAReaderOffItsPage()
    {
        // A stale or mistyped SlugAliases entry naming a *live* slug. The game is resolved first, so
        // the alias never fires — a permanent redirect off a working page is the one mistake in this
        // area a reader cannot recover from and a browser will cache for ever.
        await using var site = await SiteHost.StartAsync(new Dictionary<string, string?>
        {
            ["SlugAliases:m-u-s-h"] = "gaslight-row",
        });

        var response = await site.Client.GetAsync("/g/m-u-s-h");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Location).IsNull();
    }

    [Test]
    public async Task TheQueryStringTravelsWithTheRedirect()
    {
        // Plain mode is a real second surface (§9). A reader who asked for it must not be answered
        // with the graphical page because their bookmark was old.
        await using var site = await SiteHost.StartAsync(services => services
            .AddSingleton<ISlugHistoryStore>(new FakeSlugHistory { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await site.Client.GetAsync("/g/tidewater-nights?plain=1");

        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/g/m-u-s-h?plain=1");
    }

    [Test]
    public async Task AConfiguredAliasKeepsAUrlWorkingWhereThereIsNoDatabase()
    {
        // The site starts on the demo fixture with no Postgres at all, and an operator carrying a
        // rename by hand is still doing something legitimate. No store registered here at all.
        await using var site = await SiteHost.StartAsync(new Dictionary<string, string?>
        {
            ["SlugAliases:tidewater-nights"] = "m-u-s-h",
        });

        var response = await site.Client.GetAsync("/g/tidewater-nights");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/g/m-u-s-h");
    }

    [Test]
    public async Task AnAliasPointingAtAGameNobodyHasIsNotAPermanentRedirect()
    {
        // A typo in an operator's configuration. A 301 is cached by the browser for ever, so sending
        // a reader permanently to a page that says "no game here" is worse than saying it directly.
        await using var site = await SiteHost.StartAsync(new Dictionary<string, string?>
        {
            ["SlugAliases:tidewater-nights"] = "no-such-game",
        });

        var response = await site.Client.GetAsync("/g/tidewater-nights");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ARouteBelowTheGamePageIsNobodyElsesToRedirect()
    {
        // /g/{slug} is one segment. A deeper path is somebody else's route, present or future, and a
        // redirect that swallowed it would be this middleware deciding what the site's URLs mean.
        await using var site = await SiteHost.StartAsync(services => services
            .AddSingleton<ISlugHistoryStore>(new FakeSlugHistory { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await site.Client.GetAsync("/g/tidewater-nights/history");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Headers.Location).IsNull();
    }

    [Test]
    public async Task ALinkCheckerAskingWithHeadIsToldTheUrlMoved()
    {
        await using var site = await SiteHost.StartAsync(services => services
            .AddSingleton<ISlugHistoryStore>(new FakeSlugHistory { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await site.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, "/g/tidewater-nights"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/g/m-u-s-h");
    }

    /// <summary>
    /// The former-slug table, already resolved: what the real query returns after its join, which is
    /// the current slug of the game that used to wear the one asked for. It answers nothing for a
    /// slug that is current, which is the query's own <c>g.slug &lt;&gt; h.slug</c>.
    /// </summary>
    private sealed class FakeSlugHistory : ISlugHistoryStore
    {
        private readonly Dictionary<string, string> _rows = new(StringComparer.Ordinal);

        public string this[string formerSlug]
        {
            set => _rows[formerSlug] = value;
        }

        public Task<string?> CurrentSlugAsync(
            string formerSlug, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rows.GetValueOrDefault(formerSlug));

        public Task<Guid?> RetiredByAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(_rows.ContainsKey(slug) ? Guid.Empty : null);

        public Task<IReadOnlyList<SlugRetirement>> ForGameAsync(
            Guid gameId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SlugRetirement>>([]);
    }
}
