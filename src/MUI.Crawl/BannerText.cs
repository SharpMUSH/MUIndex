using System.Text;

namespace MUI.Crawl;

/// <summary>
/// The connect screen reduced to the text it actually carries.
/// </summary>
/// <remarks>
/// It lives in this project rather than beside its first caller because the probe needs it too: the
/// connect-screen phase decides whether what it has is a screen or a placeholder, and "a kilobyte of
/// colour and one word" has to measure as one word. One normaliser with two readers cannot drift;
/// two would, and then a banner would be judged slight by one rule and fingerprinted under another.
/// </remarks>
public static class BannerText
{
    /// <summary>
    /// The banner with escape sequences removed and whitespace collapsed.
    /// </summary>
    /// <remarks>
    /// A beacon sitting inside an SGR run is still a beacon, and a colourised connect screen is the
    /// normal case rather than the exception, so every reader of a banner's *content* comes through
    /// here.
    /// </remarks>
    public static string Flatten(string banner)
    {
        ArgumentNullException.ThrowIfNull(banner);

        // MXP's own markup goes first, and it has to happen here rather than in a caller so that the
        // probe's "is this a screen or a placeholder" judgement and the duplicate fingerprint agree.
        // The escape sequences below already fall to SkipEscape — ESC[1z is a CSI like any other —
        // but the tags between them are ordinary characters and survived: tirradyn.com opens with
        // nothing but a version request, which was stored and hashed as the literal "<VERSION>" and
        // put up as a duplicate of another game that answers the same way.
        banner = MxpSignal.Strip(banner);

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
