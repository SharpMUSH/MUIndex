using MUI.Web.Components;
using MUI.Web.Components.Pages;

namespace MUI.Web.Tests;

/// <summary>
/// Game-supplied names, tagged with the script they are actually in.
/// </summary>
/// <remarks>
/// <para>
/// The listing has always contained 北大侠客行 inside a page declared <c>lang="en"</c>. A screen
/// reader picks its voice and its pronunciation rules from that attribute, so it attempts Chinese
/// characters with English phonetics — which is not an accent, it is noise. The same attribute is
/// what selects between the regional Han forms a font draws.
/// </para>
/// <para>
/// The rule these tests exist to hold is the site's own: <b>say what you measured and nothing
/// else.</b> A script settles a language only where it is used for essentially one, and where it is
/// shared the tag is left off rather than filled in with whichever language is commonest.
/// </para>
/// </remarks>
public class ScriptAndDirectionTests
{
    [Test]
    [Arguments("Shangrila", null)]
    [Arguments("Tapestries MUCK", null)]
    [Arguments("3Kingdoms", null)]
    public async Task PlainAsciiGetsNoAttributesAtAll(string name, string? expected)
    {
        // Most of the catalogue, and the page's own lang already describes it. Five hundred rows
        // repeating that is the duplication this whole pass exists to remove.
        await Assert.That(NameScript.IsPlainAscii(name)).IsTrue();
        await Assert.That(NameScript.LanguageOf(name)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("ドラゴンクエスト", "ja")]          // katakana
    [Arguments("剣と魔法の世界", "ja")]            // Han with one hiragana particle
    [Arguments("바람의나라", "ko")]                 // Hangul
    [Arguments("เมืองแห่งเงา", "th")]                    // Thai
    [Arguments("Δίκτυο", "el")]                    // Greek
    [Arguments("Ἑλλάς", "el")]                     // opens in Greek Extended, continues in Greek
    [Arguments("מבוך", "he")]                      // Hebrew
    [Arguments("مدينة الأحلام", "ar")]                // Arabic
    public async Task AScriptUsedForOneLanguageSettlesIt(string name, string expected)
    {
        // Kana settle Japanese wherever they appear, and one is enough: a Japanese title is
        // routinely mostly Han with a single particle in it.
        await Assert.That(NameScript.LanguageOf(name)).IsEqualTo(expected);
    }

    /// <summary>
    /// Greek Extended is Greek, and the widening stops at its edges.
    /// </summary>
    /// <remarks>
    /// Polytonic Greek lives at U+1F00–U+1FFF and nothing else is written there, so the block
    /// settles a language exactly as the basic Greek block does. Unlisted, it fell to
    /// <c>Script.Other</c> — which meant whether a Greek name was tagged at all depended on which of
    /// its letters happened to carry a breathing mark. Asserted from the codepoints rather than from
    /// a word, because no natural Greek word is written entirely out of this block.
    /// </remarks>
    [Test]
    [Arguments(0x1f00, "el")]     // the first letter in the block
    [Arguments(0x1ffe, "el")]     // the last assigned character in it
    [Arguments(0x1efd, null)]     // Latin Extended Additional, one below — shared by many languages
    [Arguments(0x2010, null)]     // general punctuation, above — a script at all only by courtesy
    public async Task GreekExtendedSettlesGreekAndTheBlocksAroundItStillSettleNothing(
        int codepoint, string? expected)
    {
        await Assert.That(NameScript.LanguageOf(char.ConvertFromUtf32(codepoint))).IsEqualTo(expected);
    }

    [Test]
    [Arguments("北大侠客行")]              // Han, no kana — Chinese or Japanese, nothing says which
    [Arguments("Сумеречная Империя")]     // Cyrillic — Russian, Ukrainian, Bulgarian, a dozen more
    [Arguments("मापा गया")]                    // Devanagari — Hindi, Marathi, Nepali
    public async Task AScriptSharedByManyLanguagesSettlesNothing(string name)
    {
        // The important half. Unicode unified Han across Chinese and Japanese, and the correct drawn
        // form differs between them — so guessing "zh" for a Japanese game's name is not a harmless
        // default, it is the site asserting something about somebody's game that no probe measured.
        // An absent lang costs a glyph variant; a wrong one costs the claim the site is built on.
        await Assert.That(NameScript.IsPlainAscii(name)).IsFalse();
        await Assert.That(NameScript.LanguageOf(name)).IsNull();
    }

    [Test]
    [Arguments("مدينة الأحلام", "rtl")]
    [Arguments("מבוך", "rtl")]
    [Arguments("北大侠客行", null)]
    [Arguments("Shangrila", null)]
    public async Task DirectionIsAnsweredFromTheFirstStrongCharacter(string name, string? expected)
    {
        // Which is what the Unicode bidirectional algorithm itself uses to type a paragraph, so a
        // name opening with a Latin bracket around Arabic text is still an Arabic run.
        await Assert.That(NameScript.DirectionOf(name)).IsEqualTo(expected);
    }

    [Test]
    public async Task ANonLatinNameIsIsolatedFromTheRowAroundIt()
    {
        // Without <bdi> the name, the neutral characters beside it and the count in the next cell
        // renegotiate order as one bidirectional run, and the number lands on the wrong side of the
        // row. With it, the name reads right-to-left inside its own box while the row stays
        // left-to-right — which is the whole of "RTL text, LTR layout" and needs no mirrored
        // stylesheet.
        var arabic = await Render.ComponentAsync<GameName>(new() { ["Name"] = "مدينة الأحلام" });
        var chinese = await Render.ComponentAsync<GameName>(new() { ["Name"] = "北大侠客行" });
        var latin = await Render.ComponentAsync<GameName>(new() { ["Name"] = "Shangrila" });

        await Assert.That(arabic).Contains("<bdi");
        await Assert.That(arabic).Contains("dir=\"rtl\"");
        await Assert.That(arabic).Contains("lang=\"ar\"");

        // Isolated, but not tagged: nothing in the codepoints says which language this Han is.
        await Assert.That(chinese).Contains("<bdi");
        await Assert.That(chinese).DoesNotContain("lang=");

        // And no element at all where the page's own lang already describes the name.
        await Assert.That(latin).DoesNotContain("<bdi");
    }

    [Test]
    public async Task TheListingWrapsEveryGameNameAndLeavesTheMachineVoiceAlone()
    {
        // Two halves of tier three. A game's name is isolated so the layout survives it; the
        // machine voice — codebase strings, versions, protocol acronyms, hostnames — carries
        // translate="no", because translating `PennMUSH 1.8.8p0` destroys evidence rather than
        // localizing anything.
        var listing = await Render.PageAsync<Games>([]);
        var game = await Render.PageAsync<Game>(new() { ["Slug"] = "m-u-s-h" });

        await Assert.That(listing).Contains("class=\"meta mono\" translate=\"no\"");
        await Assert.That(game).Contains("translate=\"no\"");
    }
}
