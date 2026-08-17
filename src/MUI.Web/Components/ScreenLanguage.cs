namespace MUI.Web.Components;

/// <summary>
/// The language tag a connect screen is rendered under, derived from the encoding it was read with.
/// </summary>
/// <remarks>
/// <para>
/// <b>Han glyphs are regional, and a browser with no <c>lang</c> guesses.</b> 直, 骨, 令 and several
/// hundred others are drawn differently in Simplified Chinese, Traditional Chinese and Japanese, and
/// the shape a font serves depends on what the document says the text is. Untagged CJK in an English
/// page is commonly rendered with whichever CJK face the system reaches first, which for a Chinese
/// game on a Japanese-configured machine is the wrong regional form of its own name.
/// </para>
/// <para>
/// <b>This is an inference, and a narrow one, which is why it only ever sets an attribute.</b> An
/// encoding is not a language: GBK is used for Simplified Chinese and essentially nothing else, so
/// the mapping holds where it is defined, but UTF-8 says nothing whatever about language and gets no
/// tag at all rather than a guess. Nothing here is stored, published as a field, or shown as a fact
/// about the game — it is a typographic hint on one element, and being wrong costs a glyph variant
/// rather than a false measurement.
/// </para>
/// </remarks>
public static class ScreenLanguage
{
    /// <summary>
    /// The BCP-47 tag for an encoding, or null when the encoding does not imply one.
    /// </summary>
    /// <remarks>
    /// Null for UTF-8, ASCII and Latin-1 — the first because it carries every script at once, the
    /// other two because the page's own <c>lang</c> is already right for them.
    /// </remarks>
    public static string? For(string? charset) => charset?.Trim().ToLowerInvariant() switch
    {
        "gbk" or "gb2312" or "gb18030" or "hz-gb-2312" => "zh-Hans",
        "big5" or "big5-hkscs" or "cp950" => "zh-Hant",
        "shift_jis" or "sjis" or "cp932" or "euc-jp" or "iso-2022-jp" => "ja",
        "euc-kr" or "ks_c_5601-1987" or "cp949" or "iso-2022-kr" => "ko",
        "koi8-r" or "windows-1251" or "cp1251" or "ibm866" => "ru",
        "windows-1256" or "iso-8859-6" => "ar",
        "windows-1255" or "iso-8859-8" => "he",
        "windows-1253" or "iso-8859-7" => "el",
        "tis-620" or "windows-874" => "th",
        _ => null,
    };
}
