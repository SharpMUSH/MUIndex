using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// The language tag a connect screen is rendered under.
/// </summary>
/// <remarks>Han glyphs are regional — drawn differently in Simplified Chinese, Traditional Chinese and Japanese — so an untagged CJK run gets whichever form the reader's system reaches first.</remarks>
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

    /// <summary><c>WireEncoding</c> records the encoding's own <c>WebName</c>, but every alias maps anyway, so a hand-set value can't land here untagged.</summary>
    [Test]
    public async Task CaseAndSurroundingSpaceDoNotMatter()
    {
        await Assert.That(ScreenLanguage.For("  GBK  ")).IsEqualTo("zh-Hans");
        await Assert.That(ScreenLanguage.For("Big5")).IsEqualTo("zh-Hant");
    }

    /// <summary><b>An encoding is not a language.</b> UTF-8 carries every script at once, so a derived tag would be invented; null leaves the attribute off rather than guessing.</summary>
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
