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
    public async Task ACountOnTheListingSaysHowItWasObtainedAndHowOldItIs()
    {
        // The gap §10.1 named: the listing shipped bare values while the game route labelled every
        // field. A consumer reading only the listing has to be able to tell a count somebody
        // measured four minutes ago from one a game asserted about itself.
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

        // Aardwolf states its number on the connect screen and nowhere a machine can ask for it.
        // That is a reading of ours off their banner, and it is not the same kind of fact.
        var read = byslug["aardwolf"].GetProperty("playersNowProvenance");
        await Assert.That(read.GetProperty("source").GetString()).IsEqualTo("banner");
        await Assert.That(read.GetProperty("measured").GetBoolean()).IsFalse();

        // The label describes the value beside it, on every row, or it is labelling nothing.
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
        // playersNowState answered "measured" for any count that existed at all, so a game that
        // asserts its own number in MSSP shipped playersNowState: measured next to
        // playersNowProvenance.measured: false — the two halves of one object disagreeing about the
        // one thing this site is for. The state names how the count was obtained, or says it cannot.
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        var byslug = listing.GetProperty("games").EnumerateArray()
            .ToDictionary(g => g.GetProperty("slug").GetString()!, g => g);

        await Assert.That(byslug["m-u-s-h"].GetProperty("playersNowState").GetString())
            .IsEqualTo("measured");

        // Read off a connect screen, and published in MSSP: both are the game's word, not ours.
        await Assert.That(byslug["aardwolf"].GetProperty("playersNowState").GetString())
            .IsEqualTo("declared");
        await Assert.That(byslug["ashen-court"].GetProperty("playersNowState").GetString())
            .IsEqualTo("declared");

        // No count at all is still its own state, and a measured zero is still a measurement.
        await Assert.That(byslug["midnight-sun"].GetProperty("playersNowState").GetString())
            .IsEqualTo("unknown");
        await Assert.That(byslug["eldertale"].GetProperty("playersNowState").GetString())
            .IsEqualTo("measured");

        // The invariant, on every row: the state and the label are two spellings of one fact.
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
        // Gaslight Row stopped answering in 2023 and its codebase has not been confirmed since. The
        // value is still worth publishing; publishing it as though it were current is not.
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

        // And a game confirmed minutes ago is not dressed in the same label.
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
        // A null count and a null codebase are the absence of a measurement, and a provenance chip
        // over nothing would be a label attesting to a value nobody produced.
        await using var host = await ApiHost.StartAsync();

        var listing = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        var byslug = listing.GetProperty("games").EnumerateArray()
            .ToDictionary(g => g.GetProperty("slug").GetString()!, g => g);

        await Assert.That(byslug["midnight-sun"].GetProperty("playersNowProvenance").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
        await Assert.That(byslug["aardwolf"].GetProperty("codebaseProvenance").ValueKind)
            .IsEqualTo(JsonValueKind.Null);

        // Still shipped as a key, because a consumer must not have to tell a missing property from
        // a null one to learn that we did not measure something.
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
