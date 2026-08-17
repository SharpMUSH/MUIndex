using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// The language tag a connect screen is rendered under.
/// </summary>
/// <remarks>
/// The reason it exists at all: Han glyphs are regional. 直, 骨 and several hundred others are drawn
/// differently in Simplified Chinese, Traditional Chinese and Japanese, and an untagged CJK run in an
/// English page gets whichever regional form the reader's system reaches first. A Chinese game's own
/// name rendered in Japanese forms is a small wrongness we can fix with one attribute.
/// </remarks>
public class ScreenLanguageTests
{
    [Test]
    [Arguments("gbk", "zh-Hans")]
    [Arguments("gb2312", "zh-Hans")]
    [Arguments("GB18030", "zh-Hans")]
    [Arguments("big5", "zh-Hant")]
    [Arguments("euc-kr", "ko")]
    [Arguments("shift_jis", "ja")]
    [Arguments("euc-jp", "ja")]
    [Arguments("windows-1251", "ru")]
    [Arguments("koi8-r", "ru")]
    public async Task AnEncodingThatImpliesAScriptGetsATag(string charset, string expected)
    {
        await Assert.That(ScreenLanguage.For(charset)).IsEqualTo(expected);
    }

    /// <summary>
    /// The spelling an operator typed does not matter, because <c>WireEncoding</c> records the
    /// encoding's own <c>WebName</c> — but every alias maps anyway, so a hand-set value that skipped
    /// that path cannot land here untagged.
    /// </summary>
    [Test]
    public async Task CaseAndSurroundingSpaceDoNotMatter()
    {
        await Assert.That(ScreenLanguage.For("  GBK  ")).IsEqualTo("zh-Hans");
        await Assert.That(ScreenLanguage.For("Big5")).IsEqualTo("zh-Hant");
    }

    /// <summary>
    /// <b>An encoding is not a language, and where it implies nothing this says nothing.</b> UTF-8
    /// carries every script at once, so a tag derived from it would be an invention; ASCII and
    /// Latin-1 are already covered by the page's own lang. Null leaves the attribute off entirely
    /// rather than writing a guess into the markup.
    /// </summary>
    [Test]
    [Arguments("utf-8")]
    [Arguments("us-ascii")]
    [Arguments("iso-8859-1")]
    [Arguments("windows-1252")]
    [Arguments("")]
    [Arguments(null)]
    public async Task AnEncodingThatImpliesNothingGetsNoTag(string? charset)
    {
        await Assert.That(ScreenLanguage.For(charset)).IsNull();
    }
}
