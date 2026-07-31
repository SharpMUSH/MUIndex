using System.Security.Cryptography;
using System.Text;
using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>
/// A stable fingerprint of a connect screen, for the identity matcher's banner signal (spec §7.3).
/// </summary>
/// <remarks>
/// ANSI-stripped and whitespace-collapsed on purpose. A game that recolours its login screen or whose
/// server switched CRLF for LF has not become a different game; a game that rewrote its welcome text
/// has changed something worth noticing. Everything the escape sequences carry is presentation, and
/// the whole point of the signal is that it "survives host moves; changes on redesign".
/// </remarks>
public static class BannerFingerprint
{
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
    /// <remarks>
    /// Forwards to <see cref="BannerText.Flatten"/>, which the probe reads as well. Kept as a name
    /// here because <see cref="ClaimTokenBeacon"/> searches "the same text the fingerprint hashes",
    /// and that sentence should stay true by construction rather than by two implementations
    /// agreeing.
    /// </remarks>
    public static string Flatten(string banner) => BannerText.Flatten(banner);
}
