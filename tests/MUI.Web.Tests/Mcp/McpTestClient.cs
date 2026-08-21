using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using MUI.Web.Mcp;

namespace MUI.Web.Tests.Mcp;

/// <summary>
/// Wires a real MCP client at a running <see cref="SiteHost"/>, so these tests exercise the same
/// Streamable HTTP transport and bearer-token authentication pipeline a real caller (Claude Code)
/// would — rather than resolving <see cref="CrawlAdminTools"/>/<see cref="GameAdminTools"/> out of the container and skipping the
/// protocol and the auth handler entirely.
/// </summary>
internal static class McpTestClient
{
    /// <summary>Connects with the given bearer token. Throws if the server refuses the handshake.</summary>
    public static async Task<McpClient> ConnectAsync(
        SiteHost site, string token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(site);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(site.Client.BaseAddress!, MuiMcp.Route),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            },
            // The site's own HttpClient, so the request goes through the exact loopback connection
            // SiteHost already stood up — ownsHttpClient: false because SiteHost.DisposeAsync owns it.
            site.Client,
            NullLoggerFactory.Instance,
            ownsHttpClient: false);

        return await McpClient.CreateAsync(transport, null, NullLoggerFactory.Instance, cancellationToken);
    }

    /// <summary>Calls a tool and deserializes its structured content, or throws with the tool's own error text.</summary>
    public static async Task<T> CallAsync<T>(
        this McpClient client,
        string tool,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        var result = await client.TryCallAsync(tool, arguments, cancellationToken);

        if (result.IsError is true)
        {
            throw new InvalidOperationException($"{tool} failed: {result.ErrorText()}");
        }

        // StructuredContent is only populated when the tool advertises a JSON output schema; the
        // ordinary path — and the one every one of the nine tools takes — is a single
        // TextContentBlock carrying the same JSON the tool method returned, serialized.
        if (result.StructuredContent is { } structured)
        {
            return structured.Deserialize<T>(JsonOptions)!;
        }

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
            ?? throw new InvalidOperationException($"{tool} returned no content at all.");

        return JsonSerializer.Deserialize<T>(text, JsonOptions)!;
    }

    /// <summary>Calls a tool and returns the raw result, error or not, for the refusal tests.</summary>
    public static Task<CallToolResult> TryCallAsync(
        this McpClient client,
        string tool,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default) =>
        client.CallToolAsync(
            tool, arguments ?? new Dictionary<string, object?>(), null, null, cancellationToken)
            .AsTask();

    public static string ErrorText(this CallToolResult result) =>
        string.Join(" ", result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    /// <summary>
    /// A <see cref="JsonStringEnumConverter"/> is required here: the MCP SDK's own JSON options
    /// serialize an enum like <c>OptOutSource</c> as its string name (schema-friendly for a model
    /// caller), and the default numeric enum converter cannot read that back.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
