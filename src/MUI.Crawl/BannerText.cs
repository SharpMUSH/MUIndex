using System.Text;

namespace MUI.Crawl;

/// <summary>
/// The connect screen reduced to the text it actually carries.
/// </summary>
/// <remarks>
/// One normaliser shared by every reader of banner content, so a screen judged slight by one rule
/// and fingerprinted by another can't disagree about what the text actually is.
/// </remarks>
public static class BannerText
{
    /// <summary>
    /// The banner with escape sequences removed and whitespace collapsed.
    /// </summary>
    /// <remarks>
    /// A colourised connect screen is the normal case, not the exception, so every reader of a
    /// banner's content comes through here.
    /// </remarks>
    public static string Flatten(string banner)
    {
        ArgumentNullException.ThrowIfNull(banner);

        // MXP tags are ordinary characters, not escape sequences, and survive SkipEscape below — strip
        // them here so a banner that is nothing but MXP markup doesn't get stored and fingerprinted as
        // its literal tag text.
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
