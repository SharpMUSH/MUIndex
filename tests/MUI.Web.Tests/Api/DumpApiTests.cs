using System.Text.Json;

using MUI.Web.Api;

namespace MUI.Web.Tests.Api;

/// <summary>
/// The bulk dump: streamed, licensed, and complete — including the games that went dark.
/// </summary>
/// <remarks>Assertions about usability as much as correctness: it arrives without being assembled, says what may be done with it, and NDJSON is one whole game per line.</remarks>
public class DumpApiTests
{
    [Test]
    public async Task TheDumpIsStreamedRatherThanBuffered()
    {
        await using var host = await ApiHost.StartAsync();

        var response = await host.Client.GetAsync(
            ApiRoutes.Dump, HttpCompletionOption.ResponseHeadersRead);

        // No Content-Length: the body is never assembled in one place before being streamed.
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

        // Configuration, not a literal: the code's MIT licence and the dataset's terms are separate (spec §15.2).
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

        var listing = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Games));
        await Assert.That(slugs.Count).IsGreaterThan(listing.GetProperty("total").GetInt32());
    }

    [Test]
    public async Task TheDumpsETagIsAHashOfTheBytesItActuallySent()
    {
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
        await using var host = await ApiHost.StartAsync();

        var dump = await Json.ElementAsync(await host.Client.GetAsync(ApiRoutes.Dump));
        var fromDump = dump.GetProperty("games").EnumerateArray()
            .Single(g => g.GetProperty("slug").GetString() == "m-u-s-h");
        var fromRoute = await Json.ElementAsync(
            await host.Client.GetAsync($"{ApiRoutes.Games}/m-u-s-h"));

        await Assert.That(fromDump.GetRawText()).IsEqualTo(fromRoute.GetRawText());
    }
}
