using System.Net;

using MUI.Catalog.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace MUI.Web.Tests;

/// <summary>
/// A merged game's URL, over real HTTP (spec §7.3, §7.5).
/// </summary>
/// <remarks>
/// <b>The redirect is not a nicety on top of the merge; it is the half that keeps §7.5 true.</b> The
/// public reads stop offering an absorbed game as a game of its own, and without this its page would
/// answer 404 — which is "nothing is ever deleted" broken by the feature that was supposed to be a
/// pointer. A reader holding the old URL is sent to the game it turned out to be, permanently,
/// exactly as a renamed game's former slug sends them.
/// </remarks>
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
        // §9's plain mode is a real second surface, and sending a reader who asked for ?plain=1 to the
        // graphical page would be answering a different question from the one they asked.
        await using var site = await SiteHost.StartAsync(services => services
            .AddSingleton<IMergeRedirects>(new FakeMergeRedirects { ["tidewater-nights"] = "m-u-s-h" }));

        var response = await site.Client.GetAsync("/g/tidewater-nights?plain=1");

        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/g/m-u-s-h?plain=1");
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
