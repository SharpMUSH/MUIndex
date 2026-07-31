using System.Text.Json;

using MUI.Web.Api;

namespace MUI.Web.Tests.Api;

/// <summary>
/// The bulk dump: streamed, licensed, and complete — including the games that went dark.
/// </summary>
/// <remarks>
/// A directory that publishes an unusable dump has published nothing, so these are assertions about
/// usability as much as about correctness: it arrives without ever being assembled, it says what may
/// be done with it, and the line-delimited form is one whole game per line.
/// </remarks>
public class DumpApiTests
{
    [Test]
    public async Task TheDumpIsStreamedRatherThanBuffered()
    {
        await using var host = await ApiHost.StartAsync();

        var response = await host.Client.GetAsync(
            ApiRoutes.Dump, HttpCompletionOption.ResponseHeadersRead);

        // No Content-Length, because the body was never in one place to be measured. Kestrel chunks
        // it, and the first bytes are on the wire before the last game has been read.
        await Assert.That(response.Content.Headers.ContentLength).IsNull();
        await Assert.That(response.Headers.TransferEncodingChunked).IsTrue();
    }

    [Test]
    public async Task TheDumpStatesItsLicenceInThePayloadAndInAHeader()
    {
        await using var host = await ApiHost.StartAsync(new Dictionary<string, string?>
        {
            ["Dataset:LicenceId"] = "ODbL-1.0",
            ["Dataset:LicenceName"] = "Open Database License v1.0",
            ["Dataset:LicenceUrl"] = "https://opendatacommons.org/licenses/odbl/1-0/",
        });

        var response = await host.Client.GetAsync(ApiRoutes.Dump);
        var dump = await Json.ElementAsync(response);

        // Configuration and not a literal: the code is MIT and the dataset's terms are a separate,
        // still-open decision (spec §15.2).
        await Assert.That(dump.GetProperty("licence").GetProperty("id").GetString())
            .IsEqualTo("ODbL-1.0");
        await Assert.That(response.Headers.GetValues("X-MUIndex-Licence").Single())
            .IsEqualTo("ODbL-1.0");
        await Assert.That(response.Headers.GetValues("Link").Single()).Contains("rel=\"license\"");
        await Assert.That(dump.GetProperty("notice").GetString()).IsNotNull();
        await Assert.That(dump.TryGetProperty("attribution", out _)).IsTrue();
    }

    [Test]
    public async Task AnyResponseCarriesTheLicenceAndNotOnlyTheDump()
    {
        // Somebody republishing three fields off the listing is under the same terms as somebody
        // taking the whole catalogue, and should not have to fetch a different route to learn it.
        await using var host = await ApiHost.StartAsync();

        var listing = await host.Client.GetAsync(ApiRoutes.Games);

        await Assert.That(listing.Headers.Contains("X-MUIndex-Licence")).IsTrue();
    }

    [Test]
    public async Task ArchivedGamesAreInTheDumpBecauseTheDumpIsTheRecord()
    {
        await using var host = await ApiHost.StartAsync();

        var dump = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Dump));
        var slugs = dump.GetProperty("games").EnumerateArray()
            .Select(g => g.GetProperty("slug").GetString())
            .ToList();

        await Assert.That(slugs).Contains("gaslight-row");
        await Assert.That(slugs).Contains("verdigris");

        // The default listing excludes them; the dump is not the default listing.
        var listing = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        await Assert.That(slugs.Count).IsGreaterThan(listing.GetProperty("total").GetInt32());
    }

    [Test]
    public async Task TheDumpsETagIsAHashOfTheBytesItActuallySent()
    {
        // The validator is computed by running the same writer into a hashing sink first, so a 304
        // on a dump is a promise about the body rather than a guess from a version stamp.
        await using var host = await ApiHost.StartAsync();

        var response = await host.Client.GetAsync(ApiRoutes.Dump);
        var body = await response.Content.ReadAsByteArrayAsync();

        await Assert.That(response.Headers.ETag!.Tag).IsEqualTo(ETag.Of(body));
        await Assert.That(response.Headers.ETag.IsWeak).IsFalse();

        using var conditional = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.Dump);
        conditional.Headers.TryAddWithoutValidation(
            "If-None-Match", response.Headers.ETag.ToString());
        var second = await host.Client.SendAsync(conditional);

        await Assert.That((int)second.StatusCode).IsEqualTo(304);
    }

    [Test]
    public async Task EveryLineOfTheNdjsonIsOneWholeGame()
    {
        await using var host = await ApiHost.StartAsync();

        var response = await host.Client.GetAsync(ApiRoutes.DumpLines);
        var text = await response.Content.ReadAsStringAsync();

        await Assert.That(response.Content.Headers.ContentType!.MediaType)
            .IsEqualTo("application/x-ndjson");

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var dump = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Dump));
        await Assert.That(lines.Length).IsEqualTo(dump.GetProperty("games").GetArrayLength());

        foreach (var line in lines)
        {
            using var game = JsonDocument.Parse(line);
            await Assert.That(game.RootElement.GetProperty("id").ValueKind)
                .IsEqualTo(JsonValueKind.String);
            await Assert.That(game.RootElement.GetProperty("presence")
                .GetProperty("cells").GetArrayLength()).IsEqualTo(168);
        }
    }

    [Test]
    public async Task TheDumpCarriesTheSameFactsTheGameRouteDoes()
    {
        // One mapper, so a consumer who takes the dump and a consumer who walks the routes have the
        // same catalogue rather than two that quietly differ.
        await using var host = await ApiHost.StartAsync();

        var dump = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Dump));
        var fromDump = dump.GetProperty("games").EnumerateArray()
            .Single(g => g.GetProperty("slug").GetString() == "m-u-s-h");
        var fromRoute = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}/m-u-s-h"));

        await Assert.That(fromDump.GetRawText()).IsEqualTo(fromRoute.GetRawText());
    }
}
