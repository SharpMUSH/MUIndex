using System.Net;
using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>
/// A hostname and port somebody's MSSP <c>REFERRAL</c> named. <b>A candidate, never a fact</b> (spec
/// §7.2).
/// </summary>
/// <remarks>
/// <para>
/// This type carries no game, no name and no identity, and that is the whole point: a referred host is
/// a hostname somebody claimed is a game, and it becomes one only when it independently answers MSSP
/// for itself. Everything a referrer says about it beyond the address is discarded here.
/// </para>
/// <para>
/// <b><see cref="IsCrawlable"/> is the weaker of the two scope checks and must never be mistaken for
/// the gate.</b> It classifies a <em>string</em>, so it catches a referral naming <c>10.0.0.5</c>
/// outright and is worth nothing against anybody who owns a domain: <c>games.example.com</c> with an A
/// record pointing at <c>169.254.169.254</c> passes here, correctly, because nothing can be known
/// about a name until DNS answers. <see cref="HostScopeGuard"/> is the gate that actually holds.
/// </para>
/// </remarks>
public sealed record ReferralCandidate(string Host, int Port)
{
    /// <summary>
    /// Whether a <em>literal</em> address in this referral is somewhere we may dial. True for a name,
    /// because a name says nothing until it is resolved — see the type's remarks.
    /// </summary>
    public bool IsCrawlable =>
        !IPAddress.TryParse(Host, out var literal) || AddressScope.IsGloballyRoutable(literal);

    public override string ToString() => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
}

/// <summary>
/// Reads MSSP <c>REFERRAL</c> into candidates.
/// </summary>
/// <remarks>
/// <para>
/// <b>The field is not written the same way twice in the wild.</b> MSSP describes <c>REFERRAL</c> as a
/// list, and servers express the list by repeating the variable, by tab-separating entries inside one
/// value, and by newline-separating them; entries themselves appear as <c>host port</c>,
/// <c>host:port</c>, and as the four-column <c>name host port codebase</c> some listing sites ask for.
/// This parser accepts all of those and refuses everything else <em>silently and completely</em> —
/// there is no partial credit, because half an address is not an address.
/// </para>
/// <para>
/// Nothing here fabricates a port. A referral naming a host and no port is dropped: guessing 4201
/// would send somebody else's crawler at a socket nobody advertised, and spec §6.4's rule that parsers
/// never fabricate applies to addresses exactly as it applies to player counts.
/// </para>
/// </remarks>
public static class MsspReferrals
{
    /// <summary>The MSSP variable this reads. Matched case-insensitively, as MSSP variables are.</summary>
    public const string Variable = "REFERRAL";

    private static readonly char[] EntrySeparators = ['\n', '\r', '|', ';', ','];
    private static readonly char[] TokenSeparators = [' ', '\t'];

    /// <summary>Every candidate this probe's <c>REFERRAL</c> named, in the order the server listed them.</summary>
    /// <remarks>
    /// <b>The repeated-variable form is now read from the list, not recovered from a joined string.</b>
    /// MSSP's own way of expressing a list is to send the variable more than once, and that used to
    /// reach here as one value with the entries glued together by a comma — so this parser had to
    /// split them apart again, and could not distinguish the glue from a comma inside an entry. Each
    /// reported value is parsed on its own now. The in-value separators stay, because servers really
    /// do put several entries in one value as well, and both shapes turn up in the wild.
    /// </remarks>
    public static IReadOnlyList<ReferralCandidate> From(IReadOnlyDictionary<string, IReadOnlyList<string>> mssp)
    {
        ArgumentNullException.ThrowIfNull(mssp);

        var candidates = new List<ReferralCandidate>();

        foreach (var (variable, values) in mssp)
        {
            if (!string.Equals(variable.Trim(), Variable, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var value in values)
            {
                candidates.AddRange(Parse(value));
            }
        }

        return candidates;
    }

    /// <summary>Parses one <c>REFERRAL</c> value, which may hold any number of entries.</summary>
    public static IReadOnlyList<ReferralCandidate> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var candidates = new List<ReferralCandidate>();
        foreach (var entry in value.Split(EntrySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseEntry(entry, out var candidate))
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    /// <summary>One entry, in any of the shapes the remarks list, or nothing.</summary>
    public static bool TryParseEntry(string entry, out ReferralCandidate candidate)
    {
        candidate = null!;
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        var tokens = entry.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // `host port`, and the four-column `name host port codebase`: take the first adjacent pair
        // that reads as one. A game called "Port 5" cannot fool this, because a bare number is not a
        // plausible host.
        for (var i = 0; i + 1 < tokens.Length; i++)
        {
            if (IsPlausibleHost(tokens[i]) && IsPort(tokens[i + 1], out var port))
            {
                candidate = new ReferralCandidate(CanonicalHost.Normalize(tokens[i]), port);
                return true;
            }
        }

        // `host:port`, including the bracketed IPv6 form.
        foreach (var token in tokens)
        {
            if (TrySplitHostPort(token, out var host, out var inlinePort))
            {
                candidate = new ReferralCandidate(CanonicalHost.Normalize(host), inlinePort);
                return true;
            }
        }

        return false;
    }

    private static bool TrySplitHostPort(string token, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (token.StartsWith('[') && token.Contains("]:", StringComparison.Ordinal))
        {
            var close = token.IndexOf("]:", StringComparison.Ordinal);
            host = token[1..close];
            return IsPort(token[(close + 2)..], out port) && IsPlausibleHost(host);
        }

        // A bare IPv6 literal is full of colons, so only a single-colon token can be host:port.
        var colon = token.IndexOf(':');
        if (colon <= 0 || colon != token.LastIndexOf(':'))
        {
            return false;
        }

        host = token[..colon];
        return IsPort(token[(colon + 1)..], out port) && IsPlausibleHost(host);
    }

    private static bool IsPort(string token, out int port) =>
        int.TryParse(token, out port) && port is >= 1 and <= 65535;

    /// <summary>
    /// A host label has a dot or a colon in it, or parses as an address. A bare word does not: a
    /// single-label name is not routable on the public internet, and accepting one would let
    /// <c>REFERRAL "intranet 80"</c> aim the crawler at whatever the crawler's own search domain
    /// resolves that to.
    /// </summary>
    private static bool IsPlausibleHost(string token) =>
        token.Length > 0
        && !token.StartsWith('-')
        && (IPAddress.TryParse(token, out _) || token.Contains('.') || token.Contains(':'));
}
