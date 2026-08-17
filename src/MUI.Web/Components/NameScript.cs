using System.Globalization;

namespace MUI.Web.Components;

/// <summary>
/// What script a game-supplied string is written in, and the two attributes that follow from it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The listing already contains 北大侠客行 inside a page declared <c>lang="en"</c>.</b> A screen
/// reader picks its voice and its pronunciation rules from that attribute, so it attempts Chinese
/// characters with English phonetics — which is not an accent, it is noise. The same attribute is
/// what selects between the regional Han forms a font serves, so an untagged Japanese game name on a
/// page whose CJK fallback resolves to a Simplified face is drawn wrong in a way a Japanese reader
/// notices immediately.
/// </para>
/// <para>
/// <b>This only ever answers where the answer is not a guess.</b> A script maps to a language when
/// it is used for essentially one — Thai, Hangul, Greek, Hebrew, Arabic — and kana settle Japanese
/// whenever any appear. It does <em>not</em> map where the script is shared: Cyrillic is Russian and
/// Ukrainian and Bulgarian and a dozen more, Devanagari is Hindi and Marathi and Nepali, and Han
/// without kana is Chinese or Japanese with no way to tell from the codepoints. In those cases the
/// tag is left off entirely rather than filled in with whichever is commonest. An absent
/// <c>lang</c> costs a glyph variant; a wrong one asserts something about somebody's game that we
/// did not measure, which is the thing this site exists not to do.
/// </para>
/// <para>
/// <b>Direction is a separate question and is always answered.</b> <c>dir</c> on a <c>&lt;bdi&gt;</c>
/// makes those characters flow right-to-left <em>inside the box they occupy</em> and moves nothing
/// else — the row, the grid and the column order stay left-to-right. That is the whole of the RTL
/// requirement here (i18n S6): text runs have a direction, layouts have a direction, and only the
/// first is in scope. Without it an Arabic name and the neutral characters beside it renegotiate
/// order with the count in the next cell, and the number lands on the wrong side of the row.
/// </para>
/// <para>
/// Derived at render time from the characters themselves. It is not stored, not published as a
/// field and not shown to a reader as a fact about the game — the handoff's preferred design detects
/// it once at crawl time and stores it beside the name, which is the right shape and needs a
/// migration this does not.
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

        // Han with no kana anywhere. Chinese and Japanese share these codepoints and disagree about
        // how to draw them, and nothing in the string says which this is. Unicode unified them; we
        // are not entitled to un-unify them by guessing.
        _ = han;

        return null;
    }

    /// <summary>
    /// <c>"rtl"</c> for a right-to-left script, or null to let the surrounding direction stand.
    /// </summary>
    /// <remarks>
    /// Answered from the first strongly-directional character, which is what the Unicode
    /// bidirectional algorithm itself uses to type a paragraph — so a name opening with a Latin
    /// bracket around Arabic text is still an Arabic run.
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
    /// Pure ASCII is the overwhelming majority of the catalogue and is exactly what the page's own
    /// <c>lang</c> already describes, so it gets no attributes — five hundred rows carrying
    /// <c>lang="en"</c> on a page that says so once is the duplication this pass removes.
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
    /// Range checks rather than <see cref="CharUnicodeInfo"/>, because .NET exposes general category
    /// and not script, and the ranges that matter here are few and stable. Anything unlisted is
    /// <see cref="Script.Other"/> and settles nothing, which is the safe answer.
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
