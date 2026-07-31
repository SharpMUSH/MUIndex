using System.Text.Json;

using MUI.Catalog;
using MUI.Web.Api;

namespace MUI.Web.Tests.Api;

/// <summary>
/// <c>/api/games</c>'s facets — the same counts the page shows, over the same querystring.
/// </summary>
/// <remarks>
/// The point of publishing them at all is that a consumer building a filter UI over this endpoint
/// gets the guarantee the site's own panel has: every count is what choosing that value returns.
/// The test that matters here is therefore the round trip — take each count the API advertised, ask
/// the API for that value, and check the answer is the size it promised. A shape assertion would
/// pass on a number computed against any set at all.
/// </remarks>
public class FacetApiTests
{
    [Test]
    public async Task EveryCountTheApiPublishesIsWhatAskingForThatValueReturns()
    {
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));

        foreach (var group in listing.GetProperty("facets").EnumerateArray())
        {
            var key = group.GetProperty("key").GetString()!;

            foreach (var value in group.GetProperty("values").EnumerateArray())
            {
                var token = value.GetProperty("value").GetString()!;
                var promised = value.GetProperty("count").GetInt32();

                var chosen = await Json.ElementAsync(await host.Client.GetAsync(
                    $"{ApiRoutes.Games}?{key}={Uri.EscapeDataString(token)}"));

                await Assert.That(chosen.GetProperty("total").GetInt32())
                    .IsEqualTo(promised)
                    .Because($"{key}={token} advertised {promised}");
            }
        }
    }

    [Test]
    public async Task TheFacetsSayWhichSideOfTheEvidenceEachOfThemReads()
    {
        // Published rather than left to be inferred. A facet reading capability.gmcp.measured and one
        // reading MSSP's GENRE are not the same kind of statement, and a client that presented them
        // identically would be making the claim this site exists to stop making.
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        var byKey = listing.GetProperty("facets").EnumerateArray()
            .ToDictionary(g => g.GetProperty("key").GetString()!);

        await Assert.That(byKey[FacetKeys.Protocol].GetProperty("evidence").GetString())
            .IsEqualTo("measured");
        await Assert.That(byKey[FacetKeys.Genre].GetProperty("evidence").GetString())
            .IsEqualTo("declared");
    }

    [Test]
    public async Task AnUnknownIsPublishedAsAnUnknownAndNeverAsAnAbsentValue()
    {
        // A consumer that read the missing games as "no genre" would be doing exactly what the
        // measured/declared split exists to prevent, one layer out. The flag is on the value so it
        // cannot be inferred from the token's spelling.
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        var genre = listing.GetProperty("facets").EnumerateArray()
            .Single(g => g.GetProperty("key").GetString() == FacetKeys.Genre);

        var unknown = genre.GetProperty("values").EnumerateArray()
            .Single(v => v.GetProperty("unknown").GetBoolean());

        await Assert.That(unknown.GetProperty("value").GetString()).IsEqualTo(FacetChoice.UnknownToken);
        await Assert.That(unknown.GetProperty("count").GetInt32()).IsGreaterThan(0);
    }

    [Test]
    public async Task TheEchoedFilterCarriesEveryFacetTheRequestSet()
    {
        // A cached body has to say which question it is the answer to, and "which question" now has
        // more than two parts.
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(await host.Client.GetAsync(
            $"{ApiRoutes.Games}?band=quiet&seen=week&language=English&codebase=~unknown&tls=true"));

        var echo = listing.GetProperty("filter");

        // The echoed enum and the facet token are the same word, which is the point: what the panel
        // put in the URL is what the API says it answered.
        await Assert.That(echo.GetProperty("band").GetString())
            .IsEqualTo(FacetTokens.Of(ActivityBand.Quiet));
        await Assert.That(echo.GetProperty("seen").GetString())
            .IsEqualTo(FacetTokens.Of(LastSeenBand.Week));
        await Assert.That(echo.GetProperty("language").GetString()).IsEqualTo("English");
        await Assert.That(echo.GetProperty("codebase").GetString()).IsEqualTo(FacetChoice.UnknownToken);
        await Assert.That(echo.GetProperty("tls").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task AnUnreadableLastSeenBandIsRefusedTheSameWayAnActivityBandIs()
    {
        await using var host = await ApiHost.StartAsync();

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}?seen=someday");

        await Assert.That((int)response.StatusCode).IsEqualTo(400);
        await Assert.That((await Json.ElementAsync(response)).GetProperty("detail").GetString())
            .Contains("never");
    }

    [Test]
    public async Task TheListingSaysWhenEachGameWasLastReached()
    {
        // The column the last-seen facet filters on, on the rows it returned. Null is never reached
        // and is not the oldest bucket — a consumer coercing it to a date would publish our own
        // ignorance as somebody's outage.
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        var games = listing.GetProperty("games").EnumerateArray().ToList();

        await Assert.That(games).IsNotEmpty();

        foreach (var game in games)
        {
            var seen = game.GetProperty("lastReachableAt");
            await Assert.That(seen.ValueKind is JsonValueKind.Null or JsonValueKind.String).IsTrue();
        }
    }
}
