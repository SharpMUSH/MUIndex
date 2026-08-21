namespace MUI.Crawler;

/// <summary>One address, with the port optional — "every port on this host" is a thing to be able to say.</summary>
/// <remarks>
/// The counterpart to <see cref="CrawlSeed"/>, whose port is never optional because a crawl target
/// dials exactly one: this is for §11's opt-out and opt-out-check, where a bare host is a real answer
/// ("stop dialling every port on this machine") and not a missing argument.
/// </remarks>
public sealed record CrawlAddress(string Host, int? Port)
{
    public override string ToString() => Port is { } port ? $"{Host}:{port}" : $"{Host} (every port)";

    /// <summary>
    /// <c>host</c> or <c>host:port</c>, where leaving the port off means every port on that host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One implementation for every caller that reads this shape — <c>mui-crawl</c>'s <c>--opt-out</c>
    /// and <c>--opt-out-check</c>, and the MCP tools of the same names
    /// (<c>MUI.Web.Mcp.MuiMcpTools</c>) — because two parsers would eventually disagree about a
    /// bracketed IPv6 address and only one of them would be tested.
    /// </para>
    /// <para>
    /// A bare IPv6 literal is read as a host rather than as an address with a port, because
    /// <c>2001:db8::1</c> ends in something that parses as a number and guessing wrong here would file
    /// an opt-out under an address nobody dials.
    /// </para>
    /// </remarks>
    public static CrawlAddress Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var text = value.Trim();

        if (text.Length == 0)
        {
            throw new ArgumentException("An address is needed.");
        }

        if (text.StartsWith('[') && text.Contains("]:", StringComparison.Ordinal))
        {
            var bracket = text.IndexOf("]:", StringComparison.Ordinal);
            return new CrawlAddress(text[1..bracket], Port(text[(bracket + 2)..]));
        }

        if (System.Net.IPAddress.TryParse(text.Trim('[', ']'), out var literal))
        {
            return new CrawlAddress(literal.ToString(), null);
        }

        var colon = text.LastIndexOf(':');

        return colon > 0
            ? new CrawlAddress(text[..colon], Port(text[(colon + 1)..]))
            : new CrawlAddress(text, null);

        static int Port(string text) =>
            int.TryParse(text, out var port) && port is >= 1 and <= 65535
                ? port
                : throw new ArgumentException($"'{text}' is not a port.");
    }
}
