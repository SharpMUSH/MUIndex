using System.Net;
using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// Whether a server speaks Pueblo, and the text with Pueblo's markup removed.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="MxpSignal"/>, and separate from it on purpose. Pueblo is a different
/// protocol that happens to look similar: where MXP defines its own tag vocabulary, Pueblo switches
/// the client into an HTML mode and then sends actual HTML, marked by <c>xch_</c> attributes.
/// <see cref="MxpSignal"/>'s tag list deliberately omits <c>&lt;B&gt;</c>, <c>&lt;I&gt;</c> and
/// <c>&lt;FONT&gt;</c> precisely because they "collide with Pueblo, with HTML a game might quote, and
/// with prose" — so widening it was the wrong place to fix this.
/// </para>
/// <para>
/// Measured, not supposed: four connect screens in the catalogue reach the site as raw markup
/// (<c>elendor</c>, <c>legends-of-terris</c>, <c>twisted-muck-2</c>, <c>cosrin</c>). Elendor's is the
/// worked example and cost more than looks: <c>&lt;a xch_cmd="WHO" xch_hint="See who is
/// online"&gt;</c> survived <c>BannerText.Flatten</c> intact, so the *attribute* text read as visible
/// prose — <c>LoginPromptGate</c> found "see who is online" beside a stray digit from the same soup
/// and answered a who's-online menu that does not exist on that screen. The screen also rendered as
/// tag salad on the game's own page.
/// </para>
/// </remarks>
public static partial class PuebloSignal
{
    /// <summary>Whether anything in this text is unambiguously Pueblo.</summary>
    /// <remarks>
    /// Only the tells no prose produces: an <c>xch_</c> attribute, or an SGML-ish declaration.
    /// A bare <c>&lt;br&gt;</c> is <em>not</em> one — a game is free to print that literally.
    /// </remarks>
    public static bool IsPresent(string? text) =>
        !string.IsNullOrEmpty(text) && MarkerPattern().IsMatch(text);

    /// <summary>
    /// The text with Pueblo's markup removed, so what is stored, fingerprinted and shown is what a
    /// player would see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Gated on <see cref="IsPresent"/>, and that gate is the whole design.</b> Connect screens are
    /// full of angle brackets that are not markup — <c>&lt;&lt;&lt; WELCOME &gt;&gt;&gt;</c>, arrows,
    /// box-drawing, a prompt written <c>&lt;name&gt;</c> — and a stripper that ran unconditionally
    /// would eat the artwork off 898 screens to tidy four. So nothing is touched at all until the text
    /// has already proven itself Pueblo by a marker no ASCII art produces; only then is it safe to
    /// treat the surrounding <c>&lt;br&gt;</c>/<c>&lt;samp&gt;</c>/<c>&lt;a&gt;</c> as tags rather than
    /// as decoration. Positive evidence first, the same way the residue flush decides.
    /// </para>
    /// <para>
    /// Tags go before entities, so a <c>&amp;lt;</c> that decodes to <c>&lt;</c> cannot be read as the
    /// start of a tag that was never sent.
    /// </para>
    /// </remarks>
    public static string Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return IsPresent(text) ? StripKnown(text) : text;
    }

    /// <summary>
    /// The same removal, for a caller that has already established this session is Pueblo.
    /// </summary>
    /// <remarks>
    /// Pueblo is a fact about the <em>session</em>, not about each fragment of it, and the difference
    /// is load-bearing. A server marks up everything it sends, but only some of it carries a marker:
    /// Elendor's connect screen is unmistakable, while its <c>INFO</c> reply is merely
    /// "### Begin INFO 1&lt;br&gt;Name: ElendorMUSH&lt;br&gt;…" — no <c>xch_</c>, no declaration. Asked
    /// on its own, <see cref="Strip"/> rightly declines to touch it. So the decision is taken once
    /// over everything the session said, exactly as <c>MxpObserved</c> is, and applied here.
    /// </remarks>
    public static string StripKnown(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Breaks first, and as a newline rather than as nothing. A <br> is a line ending: dropping it
        // silently welds the lines either side of it into one, which is not merely ugly in the stored
        // screen — every reader that works a line at a time then sees one run-on line instead of
        // several. Measured on elendor: with <br> dropped, "…Elendor Mods" and "Pueblo enhanced mode!"
        // and "Your Wizards are:" became one line, and LoginPromptGate read the 7 out of the version
        // string "PennMUSH 1.7.1" as a menu token whose label then ran on far enough to reach the words
        // "who is online" from what had been a different line entirely.
        var text2 = BreakPattern().Replace(text, "\n");

        return WebUtility.HtmlDecode(MarkupPattern().Replace(text2, string.Empty));
    }

    // The tells that say "this is a markup screen": Pueblo's own xch_ attributes, and the SGML-ish
    // declarations a Pueblo/MXP server sends to define its elements. <!EL is the abbreviation MXP's
    // own list spells only in full (!ELEMENT), which is why Elendor's survived that stripper.
    [GeneratedRegex(@"xch_[a-z]+|<\s*!\s*(?:EL|ELEMENT|ENTITY|ATTLIST)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MarkerPattern();

    // The tags that end a line, replaced by one rather than removed. Applied before MarkupPattern, so
    // a <br> carrying an xch_ attribute is still read as a break rather than swallowed as markup.
    [GeneratedRegex(
        @"<\s*/?\s*(?:br|p|div|tr|li|hr|h[1-6]|center|pre|table|ul|ol|xch_page|xch_mudtext)\b"
        + Tail,
        RegexOptions.IgnoreCase)]
    private static partial Regex BreakPattern();

    // Applied only once MarkerPattern has already fired, and only to what BreakPattern left. Two
    // shapes: any tag carrying an xch_ attribute or an SGML declaration (unambiguous), and the inline
    // HTML a Pueblo server wraps its screen in (safe here, because we are past the gate).
    //
    // The allowlist is the safety, and it stays even past the gate: Elendor's own screen tells a
    // player to type `connect <name> <password>`, so a stripper that took every angle-bracketed run
    // would delete the instructions along with the markup.
    [GeneratedRegex(
        @"<\s*!\s*(?:EL|ELEMENT|ENTITY|ATTLIST)\b" + Tail
        + @"|<(?:[^>""']|""[^""]*""|'[^']*')*?xch_[a-z]+" + Tail
        + @"|<\s*/?\s*(?:html|head|body|samp|tt|code|b|i|u|em|strong|font|img|a|td|th|span|nobr)\b"
        + Tail,
        RegexOptions.IgnoreCase)]
    private static partial Regex MarkupPattern();

    /// <summary>
    /// The rest of a tag, up to the <c>&gt;</c> that actually closes it.
    /// </summary>
    /// <remarks>
    /// Quote-aware rather than <c>[^&gt;]*&gt;</c>, because an SGML declaration may carry a <c>&gt;</c>
    /// inside a quoted attribute and a naive tail stops on it. Measured on <c>elendor</c>:
    /// <c>&lt;!EL img "&lt;image URL=&amp;src; … ISMAP=&amp;ismap;&gt;" ATT="…" EMPTY&gt;</c> was cut at
    /// the <c>&gt;</c> inside the quotes, leaving <c>" ATT="…" EMPTY&gt;</c> behind in the screen.
    /// </remarks>
    private const string Tail = @"(?:[^>""']|""[^""]*""|'[^']*')*>";
}
