using System.Security.Cryptography;
using System.Text;
using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>
/// A stable fingerprint of a connect screen, for the identity matcher's banner signal (spec §7.3).
/// </summary>
/// <remarks>
/// ANSI-stripped and whitespace-collapsed on purpose: a recoloured login screen or a CRLF/LF change
/// is not a different game, but rewritten welcome text is.
/// </remarks>
public static class BannerFingerprint
{
    /// <summary>
    /// How much flattened text a connect screen must carry before its fingerprint identifies a
    /// <em>game</em> rather than a codebase (spec §7.3).
    /// </summary>
    /// <remarks>
    /// Below 40 flattened characters, connect screens are capability-negotiation prompts shared by
    /// unrelated codebases (e.g. "Do you want ANSI? (Y/n)"), not identifying text — treating them as
    /// a banner signal previously caused false duplicate-review matches between unrelated games. Not
    /// a blocklist, and not a claim that a longer banner is always distinctive.
    /// </remarks>
    public const int MinimumIdentifyingLength = 40;

    /// <summary>Lower-case hex SHA-256 over the normalised text. Never throws on content.</summary>
    public static string Of(string banner)
    {
        ArgumentNullException.ThrowIfNull(banner);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Flatten(banner))));
    }

    /// <summary>
    /// The banner with escape sequences removed and whitespace collapsed — exactly the text
    /// <see cref="Of"/> hashes.
    /// </summary>
    /// <remarks>Forwards to <see cref="BannerText.Flatten"/>, which the probe also uses, so the two stay in sync by construction.</remarks>
    public static string Flatten(string banner) => BannerText.Flatten(banner);
}
