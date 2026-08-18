using System.Globalization;

namespace MUI.Web.Components;

/// <summary>
/// What script a game-supplied string is written in, and the two attributes that follow from it.
/// </summary>
/// <remarks>
/// <para>
/// A page declared <c>lang="en"</c> still contains names like 北大侠客行. A screen reader picks
/// pronunciation from that attribute, so untagged CJK gets attempted with English phonetics — noise,
/// not an accent — and the same attribute selects between regional Han font forms a page's CJK
/// fallback might otherwise get wrong.
/// </para>
/// <para>
/// <b>This only answers where the answer isn't a guess.</b> A script maps to a language only when
/// it's used for essentially one (Thai, Hangul, Greek, Hebrew, Arabic; kana settle Japanese). It does
/// <em>not</em> map where the script is shared — Cyrillic, Devanagari, Han without kana — and the tag
/// is left off entirely rather than guessed. An absent <c>lang</c> costs a glyph variant; a wrong
/// one asserts something about a game we did not measure.
/// </para>
/// <para>
/// <b>Direction is answered separately, always.</b> <c>dir</c> on a <c>&lt;bdi&gt;</c> flows those
/// characters right-to-left <em>inside their own box</em> and moves nothing else — row, grid and
/// column order stay left-to-right (i18n S6). Without it an Arabic name renegotiates order with the
/// count in the next cell, and the number lands on the wrong side of the row.
/// </para>
/// <para>
/// Derived at render time from the characters themselves — not stored, not published as a field.
/// </para>
/// </remarks>
public static class NameScript
{
    /// <summary>The BCP-47 tag for a string, or null where the script does not settle one.</summary>
    public static string? LanguageOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var han = false;

        foreach (var rune in text.EnumerateRunes())
        {
            switch (Block(rune.Value))
            {
                // Kana are Japanese and nothing else, and one is enough: a Japanese title is
                // routinely mostly Han with a single hiragana particle in it.
                case Script.Kana:
                    return "ja";

                case Script.Hangul:
                    return "ko";

                case Script.Thai:
                    return "th";

                case Script.Greek:
                    return "el";

                case Script.Hebrew:
                    return "he";

                case Script.Arabic:
                    return "ar";

                // Seen, but not decisive on its own — a later kana would settle it as Japanese.
                case Script.Han:
                    han = true;
                    break;
            }
        }

        // Han with no kana: Chinese and Japanese share these codepoints and disagree how to draw
        // them, and nothing in the string says which this is.
        _ = han;

        return null;
    }

    /// <summary>
    /// <c>"rtl"</c> for a right-to-left script, or null to let the surrounding direction stand.
    /// </summary>
    /// <remarks>
    /// Answered from the first strongly-directional character, as the Unicode bidi algorithm itself
    /// does — a name opening with a Latin bracket around Arabic text is still an Arabic run.
    /// </remarks>
    public static string? DirectionOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var rune in text.EnumerateRunes())
        {
            var block = Block(rune.Value);

            if (block is Script.Arabic or Script.Hebrew)
            {
                return "rtl";
            }

            // A strongly left-to-right character first means the run is left-to-right, and `auto`
            // on the <bdi> would have reached the same answer.
            if (block is not Script.Neutral)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>Whether a string needs any of this at all.</summary>
    /// <remarks>
    /// Pure ASCII is most of the catalogue and is what the page's own <c>lang</c> already describes,
    /// so it gets no attributes.
    /// </remarks>
    public static bool IsPlainAscii(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        foreach (var c in text)
        {
            if (c > 0x7f)
            {
                return false;
            }
        }

        return true;
    }

    private enum Script
    {
        Neutral,
        Latin,
        Han,
        Kana,
        Hangul,
        Thai,
        Greek,
        Hebrew,
        Arabic,
        Cyrillic,
        Devanagari,
        Other,
    }

    /// <summary>
    /// Which script a codepoint belongs to, at the coarseness this needs.
    /// </summary>
    /// <remarks>
    /// Range checks rather than <see cref="CharUnicodeInfo"/>, since .NET exposes general category
    /// and not script. Anything unlisted is <see cref="Script.Other"/>, the safe "settles nothing"
    /// answer.
    /// </remarks>
    private static Script Block(int cp) => cp switch
    {
        < 0x0041 => Script.Neutral,                                   // digits, space, punctuation
        <= 0x005a or (>= 0x0061 and <= 0x007a) => Script.Latin,
        < 0x00c0 => Script.Neutral,
        <= 0x024f => Script.Latin,                                    // Latin-1 supplement, extended
        >= 0x0370 and <= 0x03ff => Script.Greek,
        >= 0x0400 and <= 0x04ff => Script.Cyrillic,
        >= 0x0590 and <= 0x05ff => Script.Hebrew,
        >= 0x0600 and <= 0x06ff => Script.Arabic,
        >= 0x0750 and <= 0x077f => Script.Arabic,
        >= 0x0900 and <= 0x097f => Script.Devanagari,
        >= 0x0e00 and <= 0x0e7f => Script.Thai,
        >= 0x1100 and <= 0x11ff => Script.Hangul,

        // Greek Extended (polytonic Greek): unlisted, it fell to Script.Other and a name spelled
        // with breathings — Ἑλλάς — got no lang at all.
        >= 0x1f00 and <= 0x1fff => Script.Greek,

        >= 0x3040 and <= 0x30ff => Script.Kana,                       // hiragana and katakana
        >= 0x3400 and <= 0x4dbf => Script.Han,                        // extension A
        >= 0x4e00 and <= 0x9fff => Script.Han,                        // unified ideographs
        >= 0xa960 and <= 0xa97f => Script.Hangul,
        >= 0xac00 and <= 0xd7ff => Script.Hangul,
        >= 0xf900 and <= 0xfaff => Script.Han,                        // compatibility ideographs
        >= 0xfb50 and <= 0xfdff => Script.Arabic,
        >= 0xfe70 and <= 0xfeff => Script.Arabic,
        >= 0xff66 and <= 0xff9f => Script.Kana,                       // halfwidth katakana
        >= 0x20000 and <= 0x2ffff => Script.Han,                      // extensions B and beyond
        _ => Script.Other,
    };
}
