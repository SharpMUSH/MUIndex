using System.Net;

using MUI.Catalog.Persistence;
using MUI.Web.Api;

using Microsoft.Extensions.DependencyInjection;

namespace MUI.Web.Tests.Api;

/// <summary>
/// Spec §5.7 — a slug a game used to have redirects to it, from wherever we keep the record.
/// </summary>
/// <remarks>Two stores, one promise: with a database the answer comes from <c>game_slug_history</c>; without one (the demo fixture) a configured alias answers behind the table rather than instead of it.</remarks>
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
        // §7.3's merge previously reached the page middleware and stopped there, leaving /api/games
        // to 404 on an absorbed game the page happily redirected.
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
        // A badge is pasted somewhere and left for years, so it's the surface a stale URL survives
        // longest on — and it had the same merge gap, which would break every embedded badge.
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
        // The table was written by the rename itself; where it disagrees with a configured alias, it wins.
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
        // A 301 to the URL that was asked for is a loop a reader can't escape.
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
        // §7.5: archiving takes a game out of the default listing and nothing else — URLs included.
        await using var host = await ApiHost.StartAsync(services => services
            .AddSingleton<ISlugHistoryStore>(new FakeSlugHistory { ["gaslight-lane"] = "gaslight-row" }));

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}/gaslight-lane");
        var page = await host.Client.GetAsync($"{ApiRoutes.Games}/gaslight-row");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(page.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    /// <summary>The former-slug table, already resolved to the current slug (as the real query returns after its join).</summary>
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
