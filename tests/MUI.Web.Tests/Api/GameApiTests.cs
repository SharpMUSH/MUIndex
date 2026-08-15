using System.Net;
using System.Text.Json;

using MUI.Web.Api;

namespace MUI.Web.Tests.Api;

/// <summary>
/// <c>/api/games/{id-or-slug}</c> — the identifiers, the provenance, and the three states.
/// </summary>
public class GameApiTests
{
    private const string Mush = "aaaaaaaa-0000-0000-0000-000000000001";

    [Test]
    public async Task TheIdIsTheGuidAndTheGuidAddressesTheGame()
    {
        // The GUID is minted once and never reused; the slug is minted from a name games change
        // (spec §5.7). So the id in the payload is the GUID, and the GUID is a route.
        await using var host = await ApiHost.StartAsync();

        var bySlug = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}/m-u-s-h"));
        var byId = await Json.ElementAsync(await host.Client.GetAsync($"{ApiRoutes.Games}/{Mush}"));

        await Assert.That(bySlug.GetProperty("id").GetString()).IsEqualTo(Mush);
        await Assert.That(byId.GetProperty("slug").GetString()).IsEqualTo("m-u-s-h");

        // apiUrl is the durable one. A payload that pointed a mirror at the mutable key would be
        // handing it a link that stops working the next time the game renames itself.
        await Assert.That(bySlug.GetProperty("apiUrl").GetString())
            .IsEqualTo($"{ApiRoutes.Games}/{Mush}");
    }

    [Test]
    public async Task ASlugTheGameUsedToHaveRedirectsPermanently()
    {
        await using var host = await ApiHost.StartAsync(new Dictionary<string, string?>
        {
            ["SlugAliases:tidewater-nights"] = "m-u-s-h",
        });

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}/tidewater-nights");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString())
            .IsEqualTo($"{ApiRoutes.Games}/m-u-s-h");
    }

    [Test]
    public async Task AnArchivedGameKeepsItsUrlAndItsEvidence()
    {
        // Archiving takes a game out of the default listing and out of nothing else. Its page, its
        // history and its last connect screen survive, and state says what it is.
        await using var host = await ApiHost.StartAsync();

        var game = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}/gaslight-row"));

        await Assert.That(game.GetProperty("state").GetString()).IsEqualTo("archived");
        await Assert.That(game.GetProperty("archived").GetBoolean()).IsTrue();
        await Assert.That(game.GetProperty("connectScreen").GetProperty("text").GetString())
            .IsNotNull();
        await Assert.That(game.GetProperty("availability").GetArrayLength()).IsGreaterThan(0);
    }

    [Test]
    public async Task EveryDeclaredValueCarriesWhereItCameFromAndHowOldItIs()
    {
        await using var host = await ApiHost.StartAsync();

        var game = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}/m-u-s-h"));

        var fields = game.GetProperty("fields");
        await Assert.That(fields.EnumerateObject().Count()).IsGreaterThan(0);

        foreach (var field in fields.EnumerateObject())
        {
            await Assert.That(field.Value.GetProperty("source").ValueKind)
                .IsEqualTo(JsonValueKind.String);
            await Assert.That(field.Value.GetProperty("lastConfirmedAt").ValueKind)
                .IsEqualTo(JsonValueKind.String);
            await Assert.That(field.Value.GetProperty("ageSeconds").GetDouble())
                .IsGreaterThanOrEqualTo(0d);
            await Assert.That(field.Value.TryGetProperty("measured", out _)).IsTrue();
            await Assert.That(field.Value.TryGetProperty("stale", out _)).IsTrue();
        }

        // Staleness is the catalogue's answer against the field's own refresh window, carried
        // through rather than re-derived here — a six-year-old CREATED is stale and a four-minute
        // banner is not, and nothing on this surface gets to hold a second opinion.
        await Assert.That(fields.GetProperty("created").GetProperty("stale").GetBoolean()).IsTrue();
        await Assert.That(fields.GetProperty("codebase").GetProperty("stale").GetBoolean()).IsFalse();

        // MSSP and not the banner. M*U*S*H disagrees with itself — its MSSP says 1.8.8p0 and its
        // banner says 1.8.7 — and 1.8.8p0 is the value on the page, which is what the precedence
        // ladder returns (§5.1). Labelling it `banner` credited one source with another's value,
        // which is the single thing a provenance chip exists not to do.
        await Assert.That(fields.GetProperty("codebase").GetProperty("source").GetString())
            .IsEqualTo("mssp");
        await Assert.That(game.GetProperty("codebaseProvenance").GetProperty("source").GetString())
            .IsEqualTo("mssp");
        await Assert.That(game.GetProperty("codebaseProvenance").GetProperty("value").GetString())
            .IsEqualTo(game.GetProperty("codebase").GetString());
    }

    [Test]
    public async Task MeasuredAndDeclaredArriveAsTwoColumnsAndTheDisagreementIsNamed()
    {
        // "Declared GMCP, never offered in a handshake" is the most useful thing a capability matrix
        // can say, and one merged badge cannot hold it (spec §3.1).
        await using var host = await ApiHost.StartAsync();

        var game = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}/m-u-s-h"));

        var gmcp = game.GetProperty("capabilities").EnumerateArray()
            .Single(c => c.GetProperty("protocol").GetString() == "GMCP");

        await Assert.That(gmcp.GetProperty("measured").GetString()).IsEqualTo("absent");
        await Assert.That(gmcp.GetProperty("declared").GetString()).IsEqualTo("present");
        await Assert.That(gmcp.GetProperty("disagrees").GetBoolean()).IsTrue();
        await Assert.That(gmcp.GetProperty("ageSeconds").GetDouble()).IsGreaterThan(0d);
        await Assert.That(game.GetProperty("disagreementCount").GetInt32()).IsGreaterThan(0);

        // Nothing said either way is its own state, and it is not "absent".
        var mxp = game.GetProperty("capabilities").EnumerateArray()
            .Single(c => c.GetProperty("protocol").GetString() == "MXP");
        await Assert.That(mxp.GetProperty("measured").GetString()).IsEqualTo("unknown");
        await Assert.That(mxp.GetProperty("lastConfirmedAt").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task AnHourHasThreeStatesAndTheApiKeepsThemThree()
    {
        // The worst bug this codebase could ship is collapsing the middle one. A cell we probed and
        // could not count is not a zero, and a cell we could not reach is not a zero either.
        await using var host = await ApiHost.StartAsync();

        async Task<List<JsonElement>> CellsAsync(string slug)
        {
            var game = await Json.ElementAsync(
                await host.Client.GetAsync($"{ApiRoutes.Games}/{slug}"));
            return [.. game.GetProperty("presence").GetProperty("cells").EnumerateArray()];
        }

        var mush = await CellsAsync("m-u-s-h");
        var uncountable = await CellsAsync("midnight-sun");
        var dark = await CellsAsync("gaslight-row");

        States(mush);
        await Assert.That(mush.Count).IsEqualTo(168);
        await Assert.That(States(mush)).Contains("counted");
        await Assert.That(States(mush)).Contains("gap");
        await Assert.That(States(uncountable)).Contains("unmeasurable");
        await Assert.That(States(dark)).Contains("gap");
        await Assert.That(States(dark)).DoesNotContain("counted");

        // The count is present exactly when the state is "counted", and never otherwise.
        foreach (var cell in mush.Concat(uncountable).Concat(dark))
        {
            var counted = cell.GetProperty("state").GetString() == "counted";
            var hasNumber = cell.GetProperty("count").ValueKind is not JsonValueKind.Null;
            await Assert.That(hasNumber).IsEqualTo(counted);
        }

        static List<string> States(List<JsonElement> cells) =>
            [.. cells.Select(c => c.GetProperty("state").GetString()!).Distinct()];
    }

    [Test]
    public async Task ReachabilityIsAWindowAFractionAndTheWorstOutageInIt()
    {
        await using var host = await ApiHost.StartAsync();

        var game = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}/m-u-s-h"));

        var reachable = game.GetProperty("reachable");
        await Assert.That(reachable.GetProperty("windowDays").GetInt32()).IsEqualTo(90);
        await Assert.That(reachable.GetProperty("fraction").GetDouble()).IsGreaterThan(0.9);
        await Assert.That(reachable.GetProperty("longestOutageSeconds").GetDouble())
            .IsEqualTo(TimeSpan.FromDays(2).TotalSeconds);
    }

    [Test]
    public async Task AGameNobodyHasIsA404ThatSaysWhereElseToLook()
    {
        await using var host = await ApiHost.StartAsync();

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}/no-such-game");

        await Assert.That((int)response.StatusCode).IsEqualTo(404);
        var problem = await Json.ElementAsync(response);
        await Assert.That(problem.GetProperty("detail").GetString()).Contains("archived=true");
    }
}
