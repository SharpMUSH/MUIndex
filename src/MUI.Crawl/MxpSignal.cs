using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// Whether a server speaks MXP, read off what it sends rather than off a negotiation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not a telnet observation.</b> MXP is telnet option 91 and a server that
/// negotiates it is recorded by the handshake like any other option — but a great many servers
/// never negotiate it and simply start emitting MXP anyway, because the protocol's own line-mode
/// sequences are ANSI-legal and pass through a client that has never heard of them. Those servers
/// support MXP by any useful definition and the handshake sees nothing.
/// </para>
/// <para>
/// Two independent tells, and either is enough:
/// </para>
/// <list type="bullet">
/// <item><b>Line-mode sequences</b> — <c>ESC[0z</c> through <c>ESC[7z</c>. The <c>z</c> final byte
/// is MXP's and nothing else in the CSI repertoire uses it, so this is the strong signal. Observed
/// on tirradyn.com, which opens with nothing but <c>ESC[1z&lt;VERSION&gt;ESC[6z</c>.</item>
/// <item><b>Protocol tags</b> — <c>&lt;VERSION&gt;</c>, <c>&lt;SUPPORT&gt;</c>, <c>&lt;SEND&gt;</c>,
/// <c>&lt;!ELEMENT&gt;</c> and friends. Weaker on their own, because a game may print a literal
/// <c>&lt;send&gt;</c> in prose, so this list is deliberately short and holds only tags a server
/// emits <em>at</em> a client rather than words a builder might type.</item>
/// </list>
/// <para>
/// <b>Absence is not recorded as absence.</b> Nothing here can say a server does not do MXP — it can
/// say we saw none during one connection, which is a different claim and the reason a capability is
/// written true or not written at all (§6.1).
/// </para>
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
    /// <b>This is a real defect being fixed, not tidiness.</b> <c>BannerText.Flatten</c> strips the
    /// escape sequences but not the tags between them, so tirradyn's connect screen was stored and
    /// hashed as the literal string <c>&lt;VERSION&gt;</c> — a protocol request recorded as a game's
    /// banner. Two unrelated games that both answer with a bare version request therefore shared a
    /// banner hash and were put up as candidate duplicates of each other.
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
