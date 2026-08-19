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

        // Out of the default listing and nothing else (spec §7.5).
        var page = await host.Client.GetAsync($"{ApiRoutes.Games}/gaslight-row");
        await Assert.That((int)page.StatusCode).IsEqualTo(200);
    }

    [Test]
    public async Task TheListingTakesTheFacetPanelsOwnQuerystring()
    {
        // q and archived are the same names the GET form on /games already publishes.
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}?q=gaslight&archived=true"));

        var expected = await new FixtureGameQueries().ListAsync(
            new GameFilter { Text = "gaslight", IncludeArchived = true });

        await Assert.That(listing.GetProperty("total").GetInt32()).IsEqualTo(expected.Count);
        await Assert.That(listing.GetProperty("games")[0].GetProperty("slug").GetString())
            .IsEqualTo("gaslight-row");

        var echo = listing.GetProperty("filter");
        await Assert.That(echo.GetProperty("q").GetString()).IsEqualTo("gaslight");
        await Assert.That(echo.GetProperty("includeArchived").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task TheAdultDefaultIsTheSameOnTheApiAsOnThePage()
    {
        // One parser, two callers — /games and /api/games must agree on the default.
        await using var host = await ApiHost.StartAsync();

        var hidden = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        var shown = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}?{FacetKeys.Adult}=true"));

        static IEnumerable<string?> Slugs(JsonElement listing) =>
            listing.GetProperty("games").EnumerateArray().Select(g => g.GetProperty("slug").GetString());

        await Assert.That(Slugs(hidden)).DoesNotContain("cinder");
        await Assert.That(Slugs(shown)).Contains("cinder");

        await Assert.That(hidden.GetProperty("filter").GetProperty("includeAdult").GetBoolean()).IsFalse();
        await Assert.That(shown.GetProperty("filter").GetProperty("includeAdult").GetBoolean()).IsTrue();

        await Assert.That((int)(await host.Client.GetAsync($"{ApiRoutes.Games}/cinder")).StatusCode)
            .IsEqualTo(200);
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

        // Every game in the fixture declares GMCP; only some were observed offering it.
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
        // If-None-Match compares weakly, so W/"x" and "x" match here.
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
        // A missing count rendered as a zero is the worst bug this codebase could ship.
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        var byslug = listing.GetProperty("games").EnumerateArray()
            .ToDictionary(g => g.GetProperty("slug").GetString()!, g => g);

        var uncountable = byslug["midnight-sun"];
        await Assert.That(uncountable.GetProperty("playersNow").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(uncountable.GetProperty("playersNowState").GetString()).IsEqualTo("unknown");

        var measuredZero = byslug["eldertale"];
        await Assert.That(measuredZero.GetProperty("playersNow").GetInt32()).IsEqualTo(0);
        await Assert.That(measuredZero.GetProperty("playersNowState").GetString()).IsEqualTo("measured");
    }

    [Test]
    public async Task ACountOnTheListingSaysHowItWasObtainedAndHowOldItIs()
    {
        // §10.1: the listing must label its bare values the same way the game route does.
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        var byslug = listing.GetProperty("games").EnumerateArray()
            .ToDictionary(g => g.GetProperty("slug").GetString()!, g => g);

        var measured = byslug["m-u-s-h"].GetProperty("playersNowProvenance");
        await Assert.That(measured.GetProperty("value").GetString()).IsEqualTo("15");
        await Assert.That(measured.GetProperty("source").GetString()).IsEqualTo("who");
        await Assert.That(measured.GetProperty("measured").GetBoolean()).IsTrue();
        await Assert.That(measured.GetProperty("ageSeconds").GetDouble()).IsEqualTo(240d).Within(1d);
        await Assert.That(measured.GetProperty("stale").GetBoolean()).IsFalse();

        // Aardwolf's count is parsed off its connect screen on every probe — measured, since the
        // freshness is ours even though the arithmetic is theirs.
        var read = byslug["aardwolf"].GetProperty("playersNowProvenance");
        await Assert.That(read.GetProperty("source").GetString()).IsEqualTo("banner");
        await Assert.That(read.GetProperty("measured").GetBoolean()).IsTrue();

        var asserted = byslug["ashen-court"].GetProperty("playersNowProvenance");
        await Assert.That(asserted.GetProperty("source").GetString()).IsEqualTo("mssp");
        await Assert.That(asserted.GetProperty("measured").GetBoolean()).IsFalse();

        foreach (var game in listing.GetProperty("games").EnumerateArray())
        {
            if (game.GetProperty("playersNow").ValueKind is JsonValueKind.Null)
            {
                continue;
            }

            await Assert.That(game.GetProperty("playersNowProvenance").GetProperty("value").GetString())
                .IsEqualTo(game.GetProperty("playersNow").GetInt32().ToString());
        }
    }

    [Test]
    public async Task TheNamedStateOfACountNeverContradictsTheLabelBesideIt()
    {
        // Regression guard: playersNowState once said "measured" for any count that existed at all,
        // disagreeing with playersNowProvenance.measured on the same object.
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        var byslug = listing.GetProperty("games").EnumerateArray()
            .ToDictionary(g => g.GetProperty("slug").GetString()!, g => g);

        // Three sources, two verdicts: the verdict turns on who read the number, not who authored it.
        await Assert.That(byslug["m-u-s-h"].GetProperty("playersNowState").GetString())
            .IsEqualTo("measured");
        await Assert.That(byslug["aardwolf"].GetProperty("playersNowState").GetString())
            .IsEqualTo("measured");
        await Assert.That(byslug["ashen-court"].GetProperty("playersNowState").GetString())
            .IsEqualTo("declared");

        await Assert.That(byslug["midnight-sun"].GetProperty("playersNowState").GetString())
            .IsEqualTo("unknown");
        await Assert.That(byslug["eldertale"].GetProperty("playersNowState").GetString())
            .IsEqualTo("measured");

        foreach (var game in listing.GetProperty("games").EnumerateArray())
        {
            var state = game.GetProperty("playersNowState").GetString();
            var label = game.GetProperty("playersNowProvenance");

            if (label.ValueKind is JsonValueKind.Null)
            {
                await Assert.That(state).IsEqualTo("unknown");
                continue;
            }

            await Assert.That(state)
                .IsEqualTo(label.GetProperty("measured").GetBoolean() ? "measured" : "declared");
        }
    }

    [Test]
    public async Task ACodebaseNobodyHasConfirmedInYearsSaysSoOnTheListingItself()
    {
        // Gaslight Row stopped answering in 2023; the value is still worth publishing, but not as current.
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}?q=gaslight&archived=true"));
        var game = listing.GetProperty("games")[0];
        var codebase = game.GetProperty("codebaseProvenance");

        await Assert.That(codebase.GetProperty("value").GetString())
            .IsEqualTo(game.GetProperty("codebase").GetString());
        await Assert.That(codebase.GetProperty("source").GetString()).IsEqualTo("mssp");
        await Assert.That(codebase.GetProperty("measured").GetBoolean()).IsFalse();
        await Assert.That(codebase.GetProperty("stale").GetBoolean()).IsTrue();
        await Assert.That(codebase.GetProperty("ageSeconds").GetDouble())
            .IsGreaterThan(365d * 24 * 60 * 60);

        var fresh = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        var current = fresh.GetProperty("games").EnumerateArray()
            .Single(g => g.GetProperty("slug").GetString() == "m-u-s-h")
            .GetProperty("codebaseProvenance");
        await Assert.That(current.GetProperty("stale").GetBoolean()).IsFalse();
        await Assert.That(current.GetProperty("age").GetString()).IsNotNull();
    }

    [Test]
    public async Task AFactWeDoNotHaveCarriesNoLabelRatherThanAnInventedOne()
    {
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        var byslug = listing.GetProperty("games").EnumerateArray()
            .ToDictionary(g => g.GetProperty("slug").GetString()!, g => g);

        await Assert.That(byslug["midnight-sun"].GetProperty("playersNowProvenance").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
        await Assert.That(byslug["aardwolf"].GetProperty("codebaseProvenance").ValueKind)
            .IsEqualTo(JsonValueKind.Null);

        // Still shipped as a key: missing property vs. null must not both mean "unmeasured".
        await Assert.That(byslug["midnight-sun"].TryGetProperty("playersNowProvenance", out _)).IsTrue();
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
