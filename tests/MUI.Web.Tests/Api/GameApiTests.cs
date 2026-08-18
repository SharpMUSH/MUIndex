using System.Net;
using System.Text.Json;

using MUI.Web.Api;
using MUI.Web.Fixtures;

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
        // The GUID is minted once and never reused; the slug is minted from a name games change (spec §5.7).
        await using var host = await ApiHost.StartAsync();

        var bySlug = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}/m-u-s-h"));
        var byId = await Json.ElementAsync(await host.Client.GetAsync($"{ApiRoutes.Games}/{Mush}"));

        await Assert.That(bySlug.GetProperty("id").GetString()).IsEqualTo(Mush);
        await Assert.That(byId.GetProperty("slug").GetString()).IsEqualTo("m-u-s-h");

        // apiUrl is the durable one, not the mutable slug.
        await Assert.That(bySlug.GetProperty("apiUrl").GetString())
            .IsEqualTo($"{ApiRoutes.Games}/{Mush}");
    }

    [Test]
    public async Task AGuidIsResolvedByLookingTheGameUpAndNeverByScanningTheCatalogue()
    {
        // FindByIdAsync answers by GUID in one indexed lookup rather than scanning the catalogue.
        await using var host = await ApiHost.StartAsync(
            queries: new ListingRefusingQueries(new FixtureGameQueries()));

        var response = await host.Client.GetAsync($"{ApiRoutes.Games}/{Mush}");
        var game = await Json.ElementAsync(response);

        await Assert.That((int)response.StatusCode).IsEqualTo(200);
        await Assert.That(game.GetProperty("slug").GetString()).IsEqualTo("m-u-s-h");
        await Assert.That(game.GetProperty("id").GetString()).IsEqualTo(Mush);

        // Nothing is ever deleted, so an archived game answers to its GUID too.
        var archived = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}/aaaaaaaa-0000-0000-0000-000000000005"));
        await Assert.That(archived.GetProperty("slug").GetString()).IsEqualTo("gaslight-row");
        await Assert.That(archived.GetProperty("state").GetString()).IsEqualTo("archived");
    }

    [Test]
    public async Task AGuidNobodyMintedIs404AndTheAnswerIsIdenticalToASlugNobodyMinted()
    {
        // No redirect: an id is minted once and never reused (§5.7), so a miss has nowhere else to look.
        await using var host = await ApiHost.StartAsync(
            queries: new ListingRefusingQueries(new FixtureGameQueries()));

        var missing = await host.Client.GetAsync(
            $"{ApiRoutes.Games}/ffffffff-0000-0000-0000-000000000000");
        var problem = await Json.ElementAsync(missing);

        await Assert.That((int)missing.StatusCode).IsEqualTo(404);
        await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("No such game");
        await Assert.That(problem.GetProperty("detail").GetString()).Contains("archived=true");

        await using var plain = await ApiHost.StartAsync();
        var bySlug = await Json.ElementAsync(
            await plain.Client.GetAsync($"{ApiRoutes.Games}/nothing-here"));
        await Assert.That(problem.GetProperty("status").GetInt32())
            .IsEqualTo(bySlug.GetProperty("status").GetInt32());
        await Assert.That(problem.GetProperty("title").GetString())
            .IsEqualTo(bySlug.GetProperty("title").GetString());
    }

    [Test]
    public async Task AGameFetchedByGuidIsByteForByteTheOneFetchedBySlug()
    {
        await using var host = await ApiHost.StartAsync();

        var byId = await host.Client.GetAsync($"{ApiRoutes.Games}/{Mush}");
        var bySlug = await host.Client.GetAsync($"{ApiRoutes.Games}/m-u-s-h");

        await Assert.That(await byId.Content.ReadAsByteArrayAsync())
            .IsEquivalentTo(await bySlug.Content.ReadAsByteArrayAsync());
        await Assert.That(byId.Headers.ETag!.ToString())
            .IsEqualTo(bySlug.Headers.ETag!.ToString());

        using var conditional = new HttpRequestMessage(HttpMethod.Get, $"{ApiRoutes.Games}/{Mush}");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", byId.Headers.ETag!.ToString());
        var second = await host.Client.SendAsync(conditional);

        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
        await Assert.That((await second.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
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
        // Archiving takes a game out of the default listing and out of nothing else (spec §7.5).
        await using var host = await ApiHost.StartAsync();

        var game = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}/gaslight-row"));

        await Assert.That(game.GetProperty("state").GetString()).IsEqualTo("archived");
        await Assert.That(game.GetProperty("archived").GetBoolean()).IsTrue();
        await Assert.That(game.GetProperty("connectScreen").GetProperty("text").GetString())
            .IsNotNull();
        await Assert.That(game.GetProperty("availability").GetArrayLength()).IsGreaterThan(0);
    }

    /// <summary>A suppressed screen is withheld here as well as on the page (spec §11).</summary>
    [Test]
    public async Task AScreenItsOwnerAskedUsNotToRepublishIsNotRepublishedByTheApiEither()
    {
        await using var host = await ApiHost.StartAsync();

        var suppressed = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}/ashen-court"));
        var screen = suppressed.GetProperty("connectScreen");

        await Assert.That(screen.GetProperty("suppressed").GetBoolean()).IsTrue();
        await Assert.That(screen.GetProperty("text").ValueKind).IsEqualTo(JsonValueKind.Null);

        var ordinary = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}/m-u-s-h"));

        await Assert.That(ordinary.GetProperty("connectScreen").GetProperty("text").GetString())
            .IsNotNull();
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

        // Staleness is the catalogue's answer, carried through rather than re-derived here.
        await Assert.That(fields.GetProperty("created").GetProperty("stale").GetBoolean()).IsTrue();
        await Assert.That(fields.GetProperty("codebase").GetProperty("stale").GetBoolean()).IsFalse();

        // M*U*S*H disagrees with itself — MSSP says 1.8.8p0, banner says 1.8.7 — and the precedence
        // ladder (§5.1) picks MSSP, so the label must say mssp and not banner.
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
        // Measured vs. declared, kept as two columns rather than one merged badge (spec §3.1).
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

        // Nothing said either way is its own state, not "absent".
        var mxp = game.GetProperty("capabilities").EnumerateArray()
            .Single(c => c.GetProperty("protocol").GetString() == "MXP");
        await Assert.That(mxp.GetProperty("measured").GetString()).IsEqualTo("unknown");
        await Assert.That(mxp.GetProperty("lastConfirmedAt").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task AnHourHasThreeStatesAndTheApiKeepsThemThree()
    {
        // Collapsing the middle state is the worst bug this codebase could ship (§5.4).
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
