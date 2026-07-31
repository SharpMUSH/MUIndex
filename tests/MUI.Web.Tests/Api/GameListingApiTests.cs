using System.Net;
using System.Text.Json;

using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests.Api;

/// <summary>
/// <c>/api/games</c> — the listing, and the querystring it shares with the site's facet panel.
/// </summary>
public class GameListingApiTests
{
    [Test]
    public async Task TheDefaultListingLeavesArchivedGamesOutAndNothingElse()
    {
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        var slugs = listing.GetProperty("games").EnumerateArray()
            .Select(g => g.GetProperty("slug").GetString())
            .ToList();

        await Assert.That(slugs).DoesNotContain("gaslight-row");

        // Out of the default listing and of nothing else (spec §7.5): its own route still answers.
        var page = await host.Client.GetAsync($"{ApiRoutes.Games}/gaslight-row");
        await Assert.That((int)page.StatusCode).IsEqualTo(200);
    }

    [Test]
    public async Task TheListingTakesTheFacetPanelsOwnQuerystring()
    {
        // q and archived are the names the GET form on /games already publishes. An API that
        // invented a second spelling for the same question would let the two drift.
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}?q=gaslight&archived=true"));

        var expected = await new FixtureGameQueries().ListAsync(
            new GameFilter { Text = "gaslight", IncludeArchived = true });

        await Assert.That(listing.GetProperty("total").GetInt32()).IsEqualTo(expected.Count);
        await Assert.That(listing.GetProperty("games")[0].GetProperty("slug").GetString())
            .IsEqualTo("gaslight-row");

        // The filter is echoed, so a cached body says what question it is the answer to.
        var echo = listing.GetProperty("filter");
        await Assert.That(echo.GetProperty("q").GetString()).IsEqualTo("gaslight");
        await Assert.That(echo.GetProperty("includeArchived").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task AProtocolFacetMatchesWhatWasMeasuredAndNotWhatWasClaimed()
    {
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}?protocol=GMCP,MSSP"));

        var games = listing.GetProperty("games").EnumerateArray().ToList();
        await Assert.That(games.Count).IsGreaterThan(0);

        foreach (var game in games)
        {
            var measured = game.GetProperty("measuredProtocols").EnumerateArray()
                .Select(p => p.GetString())
                .ToList();
            await Assert.That(measured).Contains("GMCP");
            await Assert.That(measured).Contains("MSSP");
        }

        // Every game in the fixture declares GMCP; only some were observed offering it. A facet that
        // read the claim would return the whole catalogue.
        await Assert.That(games.Count).IsLessThan(
            (await new FixtureGameQueries().ListAsync(new GameFilter())).Count);
    }

    [Test]
    public async Task AnUnrecognisedBandIsRefusedRatherThanQuietlyIgnored()
    {
        await using var host = await ApiHost.StartAsync();

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}?band=wat");

        await Assert.That((int)response.StatusCode).IsEqualTo(400);
        var problem = await Json.ElementAsync(response);
        await Assert.That(problem.GetProperty("detail").GetString()).Contains("activeThisWeek");
    }

    [Test]
    public async Task TheETagIsAHashOfTheExactBytesThatWereSent()
    {
        await using var host = await ApiHost.StartAsync();

        var response = await host.Client.GetAsync(ApiRoutes.Games);
        var (body, document) = await Json.ReadAsync(response);
        document.Dispose();

        await Assert.That(response.Headers.ETag).IsNotNull();
        await Assert.That(response.Headers.ETag!.IsWeak).IsFalse();
        await Assert.That(response.Headers.ETag.Tag).IsEqualTo(ETag.Of(body));
    }

    [Test]
    public async Task AConditionalRequestGetsA304WithNoBodyAndTheSameValidator()
    {
        await using var host = await ApiHost.StartAsync();

        var first = await host.Client.GetAsync(ApiRoutes.Games);
        var tag = first.Headers.ETag!.ToString();

        using var conditional = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.Games);
        conditional.Headers.TryAddWithoutValidation("If-None-Match", tag);
        var second = await host.Client.SendAsync(conditional);

        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
        await Assert.That(second.Headers.ETag!.ToString()).IsEqualTo(tag);
        await Assert.That((await second.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
    }

    [Test]
    public async Task AWeakValidatorAndAStarBothMatch()
    {
        // If-None-Match is defined to compare weakly, so W/"x" and "x" are the same entity here.
        var tag = ETag.Of("body"u8);

        await Assert.That(ETag.Matches($"W/{tag}", tag)).IsTrue();
        await Assert.That(ETag.Matches("*", tag)).IsTrue();
        await Assert.That(ETag.Matches("\"something-else\", " + tag, tag)).IsTrue();
        await Assert.That(ETag.Matches("\"something-else\"", tag)).IsFalse();
        await Assert.That(ETag.Matches(null, tag)).IsFalse();
    }

    [Test]
    public async Task ACountWeDidNotMeasureIsNullAndSaysSo()
    {
        // The worst bug this codebase could ship is a missing count rendered as a zero. The API
        // ships the null and names the state beside it, so a consumer that coerces cannot silently
        // publish a claim we never made.
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        var byslug = listing.GetProperty("games").EnumerateArray()
            .ToDictionary(g => g.GetProperty("slug").GetString()!, g => g);

        var uncountable = byslug["midnight-sun"];
        await Assert.That(uncountable.GetProperty("playersNow").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(uncountable.GetProperty("playersNowState").GetString()).IsEqualTo("unknown");

        // A measured zero is a measurement and must not wear the same state.
        var measuredZero = byslug["eldertale"];
        await Assert.That(measuredZero.GetProperty("playersNow").GetInt32()).IsEqualTo(0);
        await Assert.That(measuredZero.GetProperty("playersNowState").GetString()).IsEqualTo("measured");
    }

    [Test]
    public async Task PagingIsBoundedAndTheTotalIsTheWholeMatch()
    {
        await using var host = await ApiHost.StartAsync();

        var page = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}?limit=2&offset=1"));

        await Assert.That(page.GetProperty("count").GetInt32()).IsEqualTo(2);
        await Assert.That(page.GetProperty("total").GetInt32()).IsGreaterThan(2);
        await Assert.That(page.GetProperty("games").GetArrayLength()).IsEqualTo(2);

        var absurd = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}?limit=100000"));
        await Assert.That(absurd.GetProperty("limit").GetInt32())
            .IsEqualTo(GameFilterBinding.MaxLimit);
    }
}
