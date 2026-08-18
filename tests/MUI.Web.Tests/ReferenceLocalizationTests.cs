using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Localization;
using MUI.Web.Reference;

namespace MUI.Web.Tests;

/// <summary>
/// The reference section's chrome, answered in the language the reader asked for.
/// </summary>
/// <remarks>
/// <para>
/// <b>The chrome and the articles are localized by two different routes, and this is about the
/// first.</b> An article is a document, translated as a document by the content layer; the furniture
/// round it is strings in a bundle. So the boundary is the subject in both directions: every word
/// the site writes has to come from the bundle, and no word an article owns — nor any machine voice
/// inside one — may pass through it.
/// </para>
/// <para>
/// The pseudo-locale is what proves the first half. A string rendered through the bundle comes back
/// wrapped in <c>⟦ ⟧</c> and with its vowels accented, so a heading that is still bare came from a
/// template rather than from a message — which is a claim no German render can make today, because
/// a locale that has not translated an id yet falls back to the English and is byte-identical to a
/// hard-coded one.
/// </para>
/// </remarks>
public class ReferenceLocalizationTests
{
    private const string Pseudo = "qps-ploc";

    private static readonly ReferenceLibrary Library = ReferenceLibrary.Shipped;

    /// <summary>Every id the section's chrome says, whichever surface says it.</summary>
    private static IReadOnlyList<string> ChromeIds =>
        [.. Messages.Ids.Where(id => id.StartsWith("reference.", StringComparison.Ordinal))];

    [Test]
    public async Task AGermanRequestIsAnsweredWithGermanChrome()
    {
        // "Textfassung" is the German for the plain-text link, and it is on the reference pages
        // because they ask the bundle for it now. Before this change both pages spelled the link
        // "plain text" in every language on the site.
        await using var site = await SiteHost.StartAsync();

        foreach (var path in new[] { "/de/reference", "/de/reference/protocols/mssp" })
        {
            var markup = await site.Client.GetStringAsync(path);

            await Assert.That(markup).Contains(Messages.For("de", "a11y.plainText"));
            await Assert.That(Render.Words(markup))
                .DoesNotContain("plain text")
                .Because($"{path} still writes an English link into a German page");
        }
    }

    [Test]
    public async Task EveryWordTheSectionWritesComesFromTheBundle()
    {
        // The pseudo-locale's brackets are the proof. Each of these is a different kind of chrome —
        // the heading, the lede, a section label, a measured panel's heading, a table's column, the
        // plain-text link — and a bare one is a string that never met a translator.
        await using var site = await SiteHost.StartAsync();

        var index = await site.Client.GetStringAsync($"/{Pseudo}/reference");

        foreach (var id in new[]
        {
            "reference.title", "reference.plain.lede", "reference.section.codebase",
            "reference.section.protocol",
        })
        {
            var expected = id == "reference.plain.lede"
                ? Messages.For(Pseudo, "reference.lede.number")
                : Messages.For(Pseudo, id);

            await Assert.That(Render.Words(index))
                .Contains(Render.Words(expected))
                .Because($"{id} did not reach the page through the bundle");
        }

        var entry = await site.Client.GetStringAsync($"/{Pseudo}/reference/protocols/mssp");

        foreach (var id in new[]
        {
            "reference.kind.protocol", "reference.protocol.heading", "reference.protocol.remainder",
            "reference.seeAlso",
        })
        {
            await Assert.That(Render.Words(entry))
                .Contains(Render.Words(Messages.For(Pseudo, id)))
                .Because($"{id} did not reach the page through the bundle");
        }
    }

    [Test]
    public async Task AnArticlesOwnTextNeverPassesThroughTheBundle()
    {
        // The boundary, from the other side, and asked of the one locale that can answer it. The
        // pseudo-locale accents and brackets everything the message pipeline touches, so an article
        // body coming back plain is the evidence that it is a document rather than a string. What
        // language that document is in is the content layer's business and not asserted here.
        await using var site = await SiteHost.StartAsync();

        var words = Render.Words(
            await site.Client.GetStringAsync($"/{Pseudo}/reference/protocols/mssp"));

        await Assert.That(words).Contains("MSSP is telnet option 70");
    }

    [Test]
    public async Task AProtocolAcronymIsNeverATranslatableString()
    {
        // A locale that translated TTYPE would be destroying evidence rather than localizing a
        // string, so no acronym is in the bundle to be translated: they reach a message as an
        // argument or sit in the markup. Checked against the source text of every id this section
        // owns, in every bundle a reader can be answered from.
        string[] machineVoice =
        [
            "MSSP", "GMCP", "MSDP", "ATCP", "MCCP", "MXP", "MSP", "TTYPE", "CHARSET", "EOR",
            "PennMUSH", "Evennia", "TinyMUX", "Mudlet", "telnet",
        ];

        foreach (var id in ChromeIds)
        {
            foreach (var locale in Locales.All.Where(l => l.IsChoosable))
            {
                var pattern = Messages.Pattern(locale.Tag, id)!;

                foreach (var token in machineVoice)
                {
                    await Assert.That(pattern.Contains(token, StringComparison.OrdinalIgnoreCase))
                        .IsFalse()
                        .Because($"{locale.Tag}/{id} carries {token}, which is machine voice");
                }
            }
        }
    }

    [Test]
    public async Task AnAcronymOnTheRenderedPageIsUntouchedByTheLocale()
    {
        // And the same claim about the frame rather than about the bundle. The pseudo-locale accents
        // every vowel it is given, so TTYPE coming back as TTYPE is the evidence that no locale is
        // between the catalogue and the page. MSSP has no vowel to accent and would prove nothing.
        await using var site = await SiteHost.StartAsync();

        var markup = await site.Client.GetStringAsync($"/{Pseudo}/reference/protocols/ttype");

        await Assert.That(markup).Contains("TTYPE");
        await Assert.That(markup).DoesNotContain("TTYPÉ");
    }

    [Test]
    public async Task ThePlainMirrorIsWrittenInThePagesLanguage()
    {
        // Same rule as the rest of the plain surface: it says what the page says, in the language
        // the page was asked for. Its own wording, because it has no panel round the sentences —
        // but its own wording out of the same bundle.
        var plain = ReferencePlainText.RenderIndex(Pseudo, Library);

        await Assert.That(Render.Words(plain))
            .Contains(Render.Words(Messages.For(Pseudo, "reference.plain.lede")));

        foreach (var kind in Enum.GetValues<ReferenceKind>())
        {
            await Assert.That(plain).Contains(ReferencePlainText.Heading(Pseudo, kind).ToUpperInvariant());
        }
    }

    [Test]
    public async Task TheThreeCapabilityWordsStayThreeWordsInEveryLocale()
    {
        // The finding the whole localization file exists for, in this table's own vocabulary. An
        // unknown is what we looked for and did not establish; a locale that let it collapse into
        // the no beside it would publish our own failure to find a page as the client lacking the
        // feature.
        foreach (var locale in Locales.All.Where(l => l.IsChoosable))
        {
            var words = new[]
            {
                CapabilityState.Present, CapabilityState.Absent, CapabilityState.Unknown,
            }.Select(s => ClientCapabilities.Word(locale.Tag, s)).ToList();

            await Assert.That(words.Distinct().Count())
                .IsEqualTo(3)
                .Because($"{locale.Tag} says the same word for two different states");
        }
    }

    [Test]
    public async Task TheCaveatQuotesTheWordTheCellsUse()
    {
        // One sentence explaining one word, and they cannot drift apart: the word is an argument to
        // the sentence rather than written into it a second time.
        foreach (var locale in Locales.All.Where(l => l.IsChoosable))
        {
            await Assert.That(ReferencePlainText.ClientMatrixCaveat(locale.Tag))
                .Contains(ClientCapabilities.Word(locale.Tag, CapabilityState.Unknown));
        }
    }
}
