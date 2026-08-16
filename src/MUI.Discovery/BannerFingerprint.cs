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
    /// <summary>
    /// How much flattened text a connect screen must carry before its fingerprint identifies a
    /// <em>game</em> rather than a codebase (spec §7.3).
    /// </summary>
    /// <remarks>
    /// <b>Measured against the live catalogue, not chosen.</b> Of 418 games, every connect screen under
    /// 40 flattened characters is a capability-negotiation prompt the engine emits before the game has
    /// said anything — "Do you want ANSI? (Y/n)", "Detecting client, please wait...", "&lt;VERSION&gt;" —
    /// and the shortest that names its game is 42 ("Welcome to Minstrel Hall! Enter your name:"). So the
    /// floor sits between the two, and it is the *only* place in that distribution where a floor can sit
    /// without either admitting a stock prompt or discarding a real screen.
    /// <para>
    /// The failure it prevents had already happened: Adventures Unlimited, Pendulum's Calm and StockMUD
    /// send nothing but "Do you want ANSI? (Y/n)", hashed identically, and two duplicate reviews were
    /// open between three unrelated games at 0.50 — the same score a genuine second port of one game
    /// scores. This is <see cref="MsspDefaults.IsPlaceholder"/>'s rule reaching the one signal that is
    /// not a field: a value every unedited install shares is the absence of a signal, not a weak one.
    /// </para>
    /// <para>
    /// It claims only that a shorter screen is never distinctive. It does not claim a longer one always
    /// is, and it is deliberately not a blocklist of known prompts — that list has no end, and the next
    /// codebase would arrive with a phrasing nobody had typed into it.
    /// </para>
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
    /// <remarks>
    /// Forwards to <see cref="BannerText.Flatten"/>, which the probe reads as well. Kept as a name
    /// here because <see cref="ClaimTokenBeacon"/> searches "the same text the fingerprint hashes",
    /// and that sentence should stay true by construction rather than by two implementations
    /// agreeing.
    /// </remarks>
    public static string Flatten(string banner) => BannerText.Flatten(banner);
}
