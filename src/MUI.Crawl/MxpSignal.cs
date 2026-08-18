using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// Whether a server speaks MXP, read off what it sends rather than off a negotiation.
/// </summary>
/// <remarks>
/// Many servers never negotiate MXP (telnet option 91) and simply start emitting it, since its
/// line-mode sequences are ANSI-legal and pass through a client that never heard of them — so the
/// handshake alone would miss them. Two independent tells, either sufficient:
/// <list type="bullet">
/// <item><b>Line-mode sequences</b> — <c>ESC[0z</c> through <c>ESC[7z</c>. The <c>z</c> final byte
/// is private to MXP, so one occurrence is conclusive.</item>
/// <item><b>Protocol tags</b> — <c>&lt;VERSION&gt;</c>, <c>&lt;SUPPORT&gt;</c>, <c>&lt;SEND&gt;</c>
/// and friends. Weaker alone (a game may print a literal <c>&lt;send&gt;</c> in prose), so the list
/// holds only tags a server emits <em>at</em> a client, never ones a builder might type.</item>
/// </list>
/// Absence is never recorded as absence — only that none was seen during one connection (§6.1).
/// </remarks>
public static partial class MxpSignal
{
    /// <summary>Whether anything in this text is MXP.</summary>
    public static bool IsPresent(string? text) =>
        !string.IsNullOrEmpty(text) && (LineModePattern().IsMatch(text) || TagPattern().IsMatch(text));

    /// <summary>
    /// The text with MXP's own markup removed, so a fingerprint is taken over what a player would
    /// see.
    /// </summary>
    /// <remarks>
    /// <c>BannerText.Flatten</c> strips escape sequences but not the MXP tags between them, so two
    /// unrelated servers answering with a bare <c>&lt;VERSION&gt;</c> request would hash to the same
    /// banner and surface as duplicates of each other.
    /// </remarks>
    public static string Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TagPattern().Replace(text, string.Empty);
    }

    // ESC [ <digits> z — MXP's line-mode tags. The final byte 'z' is private to MXP; ANSI proper
    // does not use it, which is what makes one occurrence conclusive.
    [GeneratedRegex(@"\e\[\d*z")]
    private static partial Regex LineModePattern();

    // Only tags a server sends. <B>, <I> and <FONT> are deliberately absent: they collide with
    // Pueblo, with HTML a game might quote, and with prose.
    [GeneratedRegex(
        @"<\s*/?\s*(?:VERSION|SUPPORT|SEND|EXPIRE|RESET|DEST|A\s+HREF|IMAGE|IMG\s|SOUND|MUSIC"
        + @"|!\s*ELEMENT|!\s*ENTITY|!\s*ATTLIST|!\s*TAG)[^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex TagPattern();
}
