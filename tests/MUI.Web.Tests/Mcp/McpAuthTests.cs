using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using MUI.Web.Data;
using MUI.Web.Mcp;
using MUI.Web.Tests.Support;

namespace MUI.Web.Tests.Mcp;

/// <summary>
/// <c>/mcp</c>'s authentication: fail closed with no token configured, refuse a wrong one, and let a
/// correct one through to a real tool call — over real HTTP and a real Postgres, the way
/// <see cref="HealthEndpointTests"/> does for <c>/health</c>.
/// </summary>
public class McpAuthTests
{
    private const string Token = "test-mcp-token-0123456789abcdef";

    /// <summary>A bare JSON-RPC envelope. The auth middleware runs before any of it is read, so its
    /// content never matters to these tests — only that a request reached the route at all.</summary>
    private static object InitializeBody() => new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "initialize",
        @params = new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "mui-web-tests", version = "1.0.0" },
        },
    };

    private static Dictionary<string, string?> SettingsWithToken(string? token) => new()
    {
        [CrawlerSettings.EnabledConfigurationKey] = "false",
        [MuiMcp.TokenConfigurationKey] = token,
    };

    [Test]
    public async Task NoBearerHeaderIsRefused()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var site = await SiteHost.StartAsync(
            settings: SettingsWithToken(Token), connectionString: database.ConnectionString);

        using var request = new HttpRequestMessage(HttpMethod.Post, MuiMcp.Route)
        {
            Content = JsonContent.Create(InitializeBody()),
        };

        var response = await site.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task TheWrongTokenIsRefused()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var site = await SiteHost.StartAsync(
            settings: SettingsWithToken(Token), connectionString: database.ConnectionString);

        using var request = new HttpRequestMessage(HttpMethod.Post, MuiMcp.Route)
        {
            Content = JsonContent.Create(InitializeBody()),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "the-wrong-token");

        var response = await site.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    /// <summary>Fail CLOSED: an unset MUI_MCP_TOKEN must refuse everything, never accept anything.</summary>
    [Test]
    public async Task WithNoTokenConfiguredEveryRequestIsRefused()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var site = await SiteHost.StartAsync(
            settings: SettingsWithToken(null), connectionString: database.ConnectionString);

        using var request = new HttpRequestMessage(HttpMethod.Post, MuiMcp.Route)
        {
            Content = JsonContent.Create(InitializeBody()),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "anything-at-all");

        var response = await site.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    /// <summary>The correct token reaches the MCP server and lists the nine tools.</summary>
    [Test]
    public async Task TheCorrectTokenSucceeds()
    {
        await using var database = await PostgresFixture.MigratedAsync();
        await using var site = await SiteHost.StartAsync(
            settings: SettingsWithToken(Token), connectionString: database.ConnectionString);

        await using var client = await McpTestClient.ConnectAsync(site, Token);

        var tools = await client.ListToolsAsync();

        await Assert.That(tools.Select(t => t.Name)).IsEquivalentTo(
            [
                "crawl_seed_add", "crawl_opt_out_record", "crawl_opt_out_check", "crawl_due_targets",
                "crawl_run_cycle", "crawl_summary", "game_field_set", "game_rename", "game_merge",
            ]);
    }
}
