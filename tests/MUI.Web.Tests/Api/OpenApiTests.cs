using System.Text.Json;

namespace MUI.Web.Tests.Api;

public class OpenApiTests
{
    [Test]
    public async Task EveryDataRouteAndEmbeddedSchemaReferenceCanBeResolved()
    {
        await using var host = await ApiHost.StartAsync();
        var index = await Json.ElementAsync(await host.Client.GetAsync("/api"));
        var document = await Json.ElementAsync(await host.Client.GetAsync("/api/openapi.json"));
        var paths = document.GetProperty("paths");
        foreach (var route in index.GetProperty("routes").EnumerateArray())
        {
            var path = route.GetProperty("path").GetString()!.Replace("{id-or-slug}", "{key}", StringComparison.Ordinal);
            if (path.StartsWith("/api/", StringComparison.Ordinal) && path != "/api/openapi.json")
            {
                await Assert.That(paths.TryGetProperty(path, out _)).IsTrue();
            }
        }
        foreach (var pointer in References(document))
        {
            await Assert.That(pointer).StartsWith("#/components/schemas/");
            var resolved = document;
            foreach (var part in pointer[2..].Split('/'))
            {
                resolved = resolved.GetProperty(part.Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal));
            }
            await Assert.That(resolved.ValueKind == JsonValueKind.Object || resolved.ValueKind == JsonValueKind.True).IsTrue();
        }
    }

    private static IEnumerable<string> References(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                if (property.Name == "$ref")
                {
                    yield return property.Value.GetString()!;
                }
                foreach (var reference in References(property.Value))
                {
                    yield return reference;
                }
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray())
            {
                foreach (var reference in References(child))
                {
                    yield return reference;
                }
            }
        }
    }

    [Test]
    public async Task BrowserClientsCanRevalidateAndReadErrorsWithoutOpeningAccountRoutes()
    {
        await using var site = await SiteHost.StartAsync();
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/games");
        preflight.Headers.Add("Origin", "https://client.example");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "if-none-match");
        var permission = await site.Client.SendAsync(preflight);
        await Assert.That((int)permission.StatusCode).IsEqualTo(204);
        await Assert.That(permission.Headers.GetValues("Access-Control-Allow-Headers")
            .Any(v => v.Contains("If-None-Match", StringComparison.OrdinalIgnoreCase))).IsTrue();

        foreach (var path in new[] { "/api/games", "/api/games?band=invalid", "/api/games/not-a-real-game" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("Origin", "https://client.example");
            var response = await site.Client.SendAsync(request);
            await Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").Single()).IsEqualTo("*");
            var exposed = string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers"));
            await Assert.That(exposed).Contains("ETag", StringComparison.OrdinalIgnoreCase);
            await Assert.That(exposed).Contains("Link", StringComparison.OrdinalIgnoreCase);
            await Assert.That(exposed).Contains("X-MUIndex-Licence", StringComparison.OrdinalIgnoreCase);
        }
        using var account = new HttpRequestMessage(HttpMethod.Get, "/account/sign-in");
        account.Headers.Add("Origin", "https://client.example");
        await Assert.That((await site.Client.SendAsync(account)).Headers.Contains("Access-Control-Allow-Origin")).IsFalse();
    }

    [Test]
    public async Task TheGuideExposesWorkingExamplesAndMachineDiscoveryWithoutScript()
    {
        await using var site = await SiteHost.StartAsync();
        var html = await site.Client.GetStringAsync("/about/api");
        await Assert.That(html).Contains("lang=\"en\"");
        await Assert.That(html).Contains("href=\"#interpret\"");
        await Assert.That(html).Contains("id=\"interpret\"");
        await Assert.That(html).Contains("rel=\"service-desc\"");
        await Assert.That(html).Contains("/api/games?limit=10&amp;sort=name");
        var listing = await Json.ElementAsync(await site.Client.GetAsync("/api/games?limit=10&sort=name"));
        await Assert.That(listing.GetProperty("limit").GetInt32()).IsEqualTo(10);
        await Assert.That(listing.GetProperty("filter").GetProperty("sort").GetString()).IsEqualTo("name");
    }

    [Test]
    public async Task AClientCanDiscoverTheContractAndItsWireTypes()
    {
        await using var host = await ApiHost.StartAsync();
        var index = await host.Client.GetStringAsync("/api");
        await Assert.That(index).Contains("/api/openapi.json");
        var response = await host.Client.GetAsync("/api/openapi.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        await Assert.That(root.GetProperty("openapi").GetString()).IsEqualTo("3.1.0");
        var paths = root.GetProperty("paths");
        await Assert.That(paths.TryGetProperty("/mcp", out _)).IsFalse();
        var operation = paths.GetProperty("/api/games").GetProperty("get");
        var parameters = operation.GetProperty("parameters").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()).ToArray();
        await Assert.That(parameters).Contains("genre");
        await Assert.That(parameters).Contains("language");
        await Assert.That(parameters).Contains("offset");
        var schema = root.GetProperty("components").GetProperty("schemas").GetProperty("GameView");
        await Assert.That(schema.GetProperty("properties").TryGetProperty("playersNowProvenance", out _)).IsTrue();
        await Assert.That(schema.GetProperty("properties").GetProperty("playersNow").GetProperty("type")
            .EnumerateArray().Select(t => t.GetString()).ToArray()).Contains("null");
        await Assert.That(response.Headers.ETag).IsNotNull();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/openapi.json");
        request.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        await Assert.That((int)(await host.Client.SendAsync(request)).StatusCode).IsEqualTo(304);
    }
}
