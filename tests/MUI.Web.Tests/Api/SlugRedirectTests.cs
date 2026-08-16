using System.Net;

using MUI.Catalog.Persistence;
using MUI.Web.Api;

using Microsoft.Extensions.DependencyInjection;

namespace MUI.Web.Tests.Api;

/// <summary>
/// Spec §5.7 — a slug a game used to have redirects to it, from wherever we keep the record.
/// </summary>
/// <remarks>
/// Two stores and one promise. Where there is a database the answer comes from
/// <c>game_slug_history</c>, written by the rename itself; where there is not — the demo fixture,
/// which is how this site starts with no Postgres at all — an operator's configured alias is still a
/// legitimate way to keep a URL working, so it answers behind the table rather than instead of it.
/// </remarks>
public class SlugRedirectTests
{
    [Test]
    public async Task AFormerSlugInTheTableRedirectsPermanently()
    {
        await using var host = await ApiHost.StartAsync(services => services
            .AddSingleton<ISlugHistoryStore>(new FakeSlugHistory { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}/tidewater-nights");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo($"{ApiRoutes.Games}/m-u-s-h");
    }

    [Test]
    public async Task AnAbsorbedGameRedirectsHereJustAsItDoesOnThePage()
    {
        // §7.3's merge reached the page middleware and stopped there, so /g/{slug} sent a reader on
        // and /api/games/{key} answered 404 for the same game. One promise, two surfaces, and a
        // consumer following the API would have concluded the game was gone.
        await using var host = await ApiHost.StartAsync(services => services
            .AddSingleton<IMergeRedirects>(new FakeMergeRedirects { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}/tidewater-nights");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo($"{ApiRoutes.Games}/m-u-s-h");
    }

    [Test]
    public async Task AFormerSlugOfAnAbsorbedGameFollowsThroughHereToo()
    {
        await using var host = await ApiHost.StartAsync(services => services
            .AddSingleton<ISlugHistoryStore>(new FakeSlugHistory { ["aardmud-org-4000"] = "tidewater-nights" })
            .AddSingleton<IMergeRedirects>(new FakeMergeRedirects { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}/aardmud-org-4000");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo($"{ApiRoutes.Games}/m-u-s-h");
    }

    [Test]
    public async Task ABadgeOfAnAbsorbedGameRedirectsRatherThanBreaking()
    {
        // A badge is pasted into somebody's README or channel topic once and left for years, so it is
        // the surface a stale URL survives longest on — and it had the merge gap too, which would have
        // turned every embedded badge of an absorbed game into a broken image.
        await using var host = await ApiHost.StartAsync(services => services
            .AddSingleton<IMergeRedirects>(new FakeMergeRedirects { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await host.Client.GetAsync("/g/tidewater-nights/badge.svg");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/g/m-u-s-h/badge.svg");
    }

    [Test]
    public async Task ASeriesOfAnAbsorbedGameRedirectsRatherThanBreaking()
    {
        await using var host = await ApiHost.StartAsync(services => services
            .AddSingleton<IMergeRedirects>(new FakeMergeRedirects { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}/tidewater-nights/presence");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo($"{ApiRoutes.Games}/m-u-s-h/presence");
    }

    private sealed class FakeMergeRedirects : IMergeRedirects
    {
        private readonly Dictionary<string, string> _rows = new(StringComparer.Ordinal);

        public string this[string absorbedSlug]
        {
            set => _rows[absorbedSlug] = value;
        }

        public Task<string?> AbsorbedIntoAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rows.GetValueOrDefault(slug));
    }

    [Test]
    public async Task AConfiguredAliasStillAnswersWhenTheTableHasNothingToSay()
    {
        await using var host = await ApiHost.StartAsync(
            new Dictionary<string, string?> { ["SlugAliases:gaslight-lane"] = "gaslight-row" },
            services: services => services.AddSingleton<ISlugHistoryStore>(new FakeSlugHistory()));

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}/gaslight-lane");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo($"{ApiRoutes.Games}/gaslight-row");
    }

    [Test]
    public async Task TheTableAnswersBeforeAnOperatorsRecollectionOfTheSameRename()
    {
        // The table was written by the rename; a configured alias for the same URL is somebody's
        // memory of it. Where they disagree, the measured record wins.
        await using var host = await ApiHost.StartAsync(
            new Dictionary<string, string?> { ["SlugAliases:tidewater-nights"] = "gaslight-row" },
            services: services => services
                .AddSingleton<ISlugHistoryStore>(new FakeSlugHistory { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}/tidewater-nights");

        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo($"{ApiRoutes.Games}/m-u-s-h");
    }

    [Test]
    public async Task AnAliasThatPointsAtItselfIsNotARedirect()
    {
        // Hand-written configuration is the one place this can happen, and a 301 to the URL that was
        // asked for is a loop a reader cannot escape. It reads as "no such game", which is true.
        await using var host = await ApiHost.StartAsync(new Dictionary<string, string?>
        {
            ["SlugAliases:tidewater-nights"] = "tidewater-nights",
        });

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}/tidewater-nights");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AnArchivedGamesFormerSlugRedirectsToItsPageLikeAnyOther()
    {
        // §7.5: archiving takes a game out of the default listing and out of nothing else. Its URLs
        // are part of "nothing else", former ones included.
        await using var host = await ApiHost.StartAsync(services => services
            .AddSingleton<ISlugHistoryStore>(new FakeSlugHistory { ["gaslight-lane"] = "gaslight-row" }));

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}/gaslight-lane");
        var page = await host.Client.GetAsync($"{ApiRoutes.Games}/gaslight-row");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(page.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    /// <summary>
    /// The former-slug table, already resolved: what the real query returns after its join, which is
    /// the current slug of the game that used to wear the one asked for.
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
