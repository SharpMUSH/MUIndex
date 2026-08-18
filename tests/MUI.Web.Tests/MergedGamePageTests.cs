using System.Net;

using MUI.Catalog;
using MUI.Catalog.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace MUI.Web.Tests;

/// <summary>
/// A merged game's URL, over real HTTP (spec §7.3, §7.5).
/// </summary>
/// <remarks>The redirect is the half that keeps §7.5 ("nothing is ever deleted") true — without it, an absorbed game's page would 404 rather than point onward, exactly as a renamed game's former slug does.</remarks>
public class MergedGamePageTests
{
    [Test]
    public async Task AnAbsorbedGamesPageRedirectsToTheGameThatAbsorbedItPermanently()
    {
        await using var site = await SiteHost.StartAsync(services => services
            .AddSingleton<IMergeRedirects>(new FakeMergeRedirects { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await site.Client.GetAsync("/g/tidewater-nights");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/g/m-u-s-h");
    }

    [Test]
    public async Task AGameNoMergeTouchesIsServedAsItAlwaysWas()
    {
        await using var site = await SiteHost.StartAsync(services => services
            .AddSingleton<IMergeRedirects>(new FakeMergeRedirects { ["tidewater-nights"] = "m-u-s-h" }));

        await Assert.That((await site.Client.GetAsync("/g/m-u-s-h")).StatusCode)
            .IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task ThePlainSurfaceRedirectsWithItsQueryIntact()
    {
        // §9's plain mode is a real second surface; sending ?plain=1 to the graphical page answers a different question.
        await using var site = await SiteHost.StartAsync(services => services
            .AddSingleton<IMergeRedirects>(new FakeMergeRedirects { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await site.Client.GetAsync("/g/tidewater-nights?plain=1");

        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/g/m-u-s-h?plain=1");
    }

    [Test]
    public async Task AFormerSlugOfAnAbsorbedGameFollowsThroughToTheSurvivor()
    {
        // §5.7 promises every former slug redirects FOR EVER; a rename followed by a merge is two
        // hops through two different tables, and the promise is about the URL, not the hop count.
        await using var site = await SiteHost.StartAsync(services => services
            .AddSingleton<ISlugHistoryStore>(new FakeSlugHistory { ["aardmud-org-4000"] = "tidewater-nights" })
            .AddSingleton<IMergeRedirects>(new FakeMergeRedirects { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await site.Client.GetAsync("/g/aardmud-org-4000");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/g/m-u-s-h");
    }

    private sealed class FakeSlugHistory : ISlugHistoryStore
    {
        private readonly Dictionary<string, string> _rows = new(StringComparer.Ordinal);

        public string this[string formerSlug]
        {
            set => _rows[formerSlug] = value;
        }

        public Task<string?> CurrentSlugAsync(string formerSlug, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rows.GetValueOrDefault(formerSlug));

        public Task<Guid?> RetiredByAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(_rows.ContainsKey(slug) ? Guid.Empty : null);

        public Task<IReadOnlyList<SlugRetirement>> ForGameAsync(
            Guid gameId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SlugRetirement>>([]);
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
}
