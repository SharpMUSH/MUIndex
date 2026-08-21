namespace MUI.Web.Components;

/// <summary>
/// The language tag a connect screen is rendered under, derived from the encoding it was read with.
/// </summary>
/// <remarks>
/// <para>
/// <b>Han glyphs are regional, and a browser with no <c>lang</c> guesses.</b> Several hundred
/// characters are drawn differently in Simplified Chinese, Traditional Chinese and Japanese; untagged
/// CJK in an English page commonly renders with whichever face the system reaches first — often the
/// wrong regional form.
/// </para>
/// <para>
/// <b>A narrow inference, which is why it only ever sets an attribute.</b> An encoding isn't a
/// language — GBK means Simplified Chinese, but UTF-8 says nothing about language and gets no tag.
/// Nothing here is stored or shown as a fact about the game; being wrong costs a glyph variant, not
/// a false measurement.
/// </para>
/// </remarks>
public static class ScreenLanguage
{
    /// <summary>
    /// The BCP-47 tag for an encoding, or null when the encoding does not imply one.
    /// </summary>
    /// <remarks>
    /// Null for UTF-8 (carries every script at once), ASCII and Latin-1 (the page's own <c>lang</c>
    /// is already right for them).
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
