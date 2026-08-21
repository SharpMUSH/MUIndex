using System.Net;
using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>
/// A hostname and port somebody's MSSP <c>REFERRAL</c> named. <b>A candidate, never a fact</b> (spec
/// §7.2).
/// </summary>
/// <remarks>
/// Carries no game, no name and no identity: a referred host becomes a game only when it
/// independently answers MSSP for itself. <b><see cref="IsCrawlable"/> is the weaker of the two scope
/// checks and must never be mistaken for the gate</b> — it classifies a string, so
/// <c>games.example.com</c> with an A record pointing at <c>169.254.169.254</c> passes here
/// correctly, since nothing can be known about a name until DNS answers.
/// <see cref="HostScopeGuard"/> is the gate that actually holds.
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
/// MSSP describes <c>REFERRAL</c> as a list, and servers express it by repeating the variable, by
/// tab- or newline-separating entries inside one value, and as <c>host port</c>, <c>host:port</c>, or
/// the four-column <c>name host port codebase</c>. This parser accepts all of those and refuses
/// everything else silently and completely — no partial credit, since half an address is not an
/// address. A referral naming a host with no port is dropped rather than guessed: parsers never
/// fabricate (spec §6.4), for addresses as much as player counts.
/// </remarks>
public static class MsspReferrals
{
    /// <summary>The MSSP variable this reads. Matched case-insensitively, as MSSP variables are.</summary>
    public const string Variable = "REFERRAL";

    private static readonly char[] EntrySeparators = ['\n', '\r', '|', ';', ','];
    private static readonly char[] TokenSeparators = [' ', '\t'];

    /// <summary>Every candidate this probe's <c>REFERRAL</c> named, in the order the server listed them.</summary>
    /// <remarks>
    /// Each reported MSSP value is parsed on its own, not joined into one string first — a
    /// comma-joined repeated variable was previously indistinguishable from a comma inside one entry.
    /// The in-value separators stay, since servers also put several entries in one value.
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

    private static bool IsPlausibleHost(string token) => HostText.IsPlausible(token);
}

/// <summary>
/// Whether a string is shaped like a host we could dial at all.
/// </summary>
/// <remarks>
/// A host label has a dot or colon in it, or parses as an address. A bare word does not: accepting a
/// single-label name would let <c>REFERRAL "intranet 80"</c> aim the crawler at whatever the
/// crawler's own search domain resolves that to — the SSRF shape §7.2 exists to prevent, arriving
/// without anybody needing to own a domain. <b>This is a shape check and never the gate</b>: every
/// real DNS name passes it correctly, and <see cref="HostScopeGuard"/> is the check that runs on the
/// resolved address.
/// </remarks>
public static class HostText
{
    public static bool IsPlausible(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        return token.Length > 0
            && !token.StartsWith('-')
            && !token.Any(char.IsWhiteSpace)
            && !token.Any(char.IsControl)
            && (IPAddress.TryParse(token, out _) || token.Contains('.') || token.Contains(':'));
    }
}
