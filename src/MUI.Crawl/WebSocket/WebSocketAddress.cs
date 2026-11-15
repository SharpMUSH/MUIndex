namespace MUI.Crawl;

/// <summary>
/// One WebSocket address a game declared in its MSSP <c>WEBSOCKET</c> variable.
/// </summary>
/// <remarks>
/// A declaration, not a measurement: nothing here says the address answers. It becomes a fact about
/// the game only once <see cref="IWebSocketProbe"/> has completed a handshake there (rule 1).
/// </remarks>
/// <param name="Host">The host, lower-cased, IPv6 without brackets.</param>
/// <param name="Port">The port, the scheme's default where the URL named none.</param>
/// <param name="Tls">True for <c>wss</c>.</param>
/// <param name="Path">The path and query, always starting with <c>/</c>.</param>
public sealed record WebSocketAddress(string Host, int Port, bool Tls, string Path)
{
    /// <summary>The address as a URL, the port always written.</summary>
    public Uri Uri => new(
        $"{(Tls ? "wss" : "ws")}://{(Host.Contains(':', StringComparison.Ordinal) ? $"[{Host}]" : Host)}:{Port}{Path}");
}

/// <summary>
/// Reads the MSSP <c>WEBSOCKET</c> variable: one or more <c>ws://</c> or <c>wss://</c> URLs.
/// </summary>
/// <remarks>
/// <para>
/// The MSSP proposal lets the value be a comma-separated list, and MSSP lets the variable repeat; both
/// are read. A URL that is not one this site would show a reader is refused rather than tidied:
/// another scheme, user information (a login the game did not mean to publish), a fragment, a path
/// that is not plain printable ASCII, or one longer than <see cref="MaxPathLength"/>. The same path
/// rule is the database's (migration 0045).
/// </para>
/// <para>
/// The value is server text, and so is every host it names: none of it is dialled before the opt-out
/// and scope gates have ruled on it.
/// </para>
/// </remarks>
public static class WebSocketAddresses
{
    /// <summary>The variable's name.</summary>
    public const string Variable = "WEBSOCKET";

    /// <summary>The longest path kept, the database's own limit.</summary>
    public const int MaxPathLength = 256;

    /// <summary>How many addresses one report may name. More than this is noise, not a game.</summary>
    public const int MaxAddresses = 4;

    /// <summary>Every address the report declares that this site would show, in order, once each.</summary>
    public static IReadOnlyList<WebSocketAddress> From(IReadOnlyDictionary<string, IReadOnlyList<string>>? report)
    {
        if (report is null || !report.TryGetValue(Variable, out var values))
        {
            return [];
        }

        var found = new List<WebSocketAddress>();

        foreach (var value in values)
        {
            foreach (var piece in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (Parse(piece) is { } address && !found.Contains(address))
                {
                    found.Add(address);

                    if (found.Count == MaxAddresses)
                    {
                        return found;
                    }
                }
            }
        }

        return found;
    }

    /// <summary>One URL, or null when it is not one this site would show.</summary>
    public static WebSocketAddress? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxPathLength + 300)
        {
            return null;
        }

        text = text.Trim();

        // Anything outside printable ASCII is refused before Uri sees it, which would otherwise
        // percent-encode it into something that looks like a path we chose.
        foreach (var c in text)
        {
            if (c is < '!' or > '~')
            {
                return null;
            }
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var tls = uri.Scheme switch
        {
            "wss" => true,
            "ws" => false,
            _ => (bool?)null,
        };

        if (tls is null
            || uri.UserInfo.Length > 0
            || text.Contains('#', StringComparison.Ordinal)
            || uri.HostNameType is UriHostNameType.Unknown or UriHostNameType.Basic
            || uri.Port is < 1 or > 65535)
        {
            return null;
        }

        var path = uri.PathAndQuery;

        if (path.Length == 0 || path[0] != '/' || path.Length > MaxPathLength)
        {
            return null;
        }

        var host = uri.HostNameType is UriHostNameType.IPv6 ? uri.Host.Trim('[', ']') : uri.Host;

        return new WebSocketAddress(host.ToLowerInvariant(), uri.Port, tls.Value, path);
    }
}
