using System.Security.Cryptography;
using System.Text;

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
    /// Public because <see cref="ClaimTokenBeacon"/> has to search the same flattened text: a beacon
    /// sitting inside an SGR run is still a beacon, and a colourised connect screen is the normal case
    /// rather than the exception. One normaliser with two readers cannot drift; two would.
    /// </remarks>
    public static string Flatten(string banner)
    {
        ArgumentNullException.ThrowIfNull(banner);

        var text = new StringBuilder(banner.Length);
        var pendingSpace = false;

        for (var i = 0; i < banner.Length; i++)
        {
            var ch = banner[i];

            if (ch == '\e')
            {
                i = SkipEscape(banner, i);
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = text.Length > 0;
                continue;
            }

            if (char.IsControl(ch))
            {
                continue;
            }

            if (pendingSpace)
            {
                text.Append(' ');
                pendingSpace = false;
            }

            text.Append(ch);
        }

        return text.ToString();
    }

    /// <summary>The index of the last character of the escape sequence starting at <paramref name="start"/>.</summary>
    private static int SkipEscape(string banner, int start)
    {
        var i = start + 1;
        if (i >= banner.Length)
        {
            return start;
        }

        // CSI: ESC [ … final byte in 0x40–0x7E.
        if (banner[i] == '[')
        {
            for (i++; i < banner.Length; i++)
            {
                if (banner[i] is >= '@' and <= '~')
                {
                    return i;
                }
            }

            return banner.Length - 1;
        }

        // OSC: ESC ] … BEL, or ESC ] … ESC \.
        if (banner[i] == ']')
        {
            for (i++; i < banner.Length; i++)
            {
                if (banner[i] == '\a')
                {
                    return i;
                }

                if (banner[i] == '\e' && i + 1 < banner.Length && banner[i + 1] == '\\')
                {
                    return i + 1;
                }
            }

            return banner.Length - 1;
        }

        // Anything else two-byte: ESC 7, ESC =, ESC ( B and friends.
        return i;
    }
}
