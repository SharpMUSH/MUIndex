using System.Net;

namespace MUI.Discovery;

/// <summary>
/// One spelling per machine. The canonical form the crawl registry keys on and the referral writer
/// compares with.
/// </summary>
/// <remarks>
/// One host must have one spelling, or <c>MUD.Example.ORG.</c> and <c>mud.example.org</c> become two
/// crawl targets for one machine — double probing, and the duplicate-listing failure spec §7.3
/// exists to prevent. The same argument applies to <c>[2001:db8::1]</c> against
/// <c>2001:0db8:0000::1</c>.
/// </remarks>
public static class CanonicalHost
{
    /// <summary>
    /// The canonical spelling: trimmed, unbracketed, lower-cased, with the root label's trailing dot
    /// removed, and with an IP literal reduced to its own canonical text.
    /// </summary>
    public static string Normalize(string host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var trimmed = host.Trim();

        // A bracketed IPv6 literal is an authority-component spelling, not a host name.
        if (trimmed.Length > 1 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            trimmed = trimmed[1..^1];
        }

        if (IPAddress.TryParse(trimmed, out var literal))
        {
            // ToString() is the address's own canonical text: it collapses IPv6 zero runs and drops
            // leading zeroes, so two spellings of one address become one key.
            return literal.ToString();
        }

        // The root label is implied. "example.org." and "example.org" are the same name and the
        // registry must not hold both.
        while (trimmed.EndsWith('.'))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed.ToLowerInvariant();
    }

    /// <summary>Whether two host spellings name the same machine.</summary>
    public static bool Same(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
}
