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

    /// <summary>
    /// A translated article is the same article: same slug, same kind, same related pages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Enforced by the loader and asserted here anyway.</b> <c>ReferenceLibrary.For</c> hands out
    /// the English record with only the prose replaced, so a translator cannot move a slug even by
    /// writing a different one — but the file on disk is what a person edits next, and a front
    /// matter that disagrees with the English is a trap set for whoever reads it later. This walks
    /// the files rather than the loaded documents, which is the only place the difference shows.
    /// </para>
    /// <para>
    /// The structural keys are the routing and the see-also graph: <c>kind</c> and <c>slug</c> make
    /// the URL, <c>see-also</c> makes the related-pages links, and <c>codebase</c>, <c>protocol</c>,
    /// <c>home</c>, <c>platform</c> and <c>capability</c> are read as machine voice. Only
    /// <c>title</c> and <c>summary</c> are prose.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ATranslatedArticleKeepsTheEnglishStructure()
    {
        var root = ArticleRoot();
        var english = root.GetFiles("*.md").ToDictionary(f => f.Name, StringComparer.Ordinal);

        await Assert.That(english).IsNotEmpty().Because("the English articles are the source");

        var checkedFiles = 0;

        foreach (var directory in root.GetDirectories())
        {
            foreach (var translated in directory.GetFiles("*.md"))
            {
                await Assert.That(english.ContainsKey(translated.Name))
                    .IsTrue()
                    .Because($"{directory.Name}/{translated.Name} translates an article that does not exist");

                var source = Structure(File.ReadAllText(english[translated.Name].FullName));
                var target = Structure(File.ReadAllText(translated.FullName));

                await Assert.That(target)
                    .IsEquivalentTo(source)
                    .Because($"{directory.Name}/{translated.Name} changed a structural front-matter key");

                checkedFiles++;
            }
        }

        await Assert.That(checkedFiles)
            .IsGreaterThan(0)
            .Because("a sweep over no translations proves nothing about translations");
    }

    /// <summary>Every front-matter line that is not prose, in the order the file gives them.</summary>
    private static List<string> Structure(string document)
    {
        var parts = document.Split("---", 3);
        var front = parts.Length > 1 ? parts[1] : string.Empty;

        return
        [
            .. front.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Length > 0 && !l.StartsWith("title:", StringComparison.Ordinal)
                    && !l.StartsWith("summary:", StringComparison.Ordinal)),
        ];
    }

    /// <summary>
    /// The articles on disk, which is where a front matter can disagree with the English.
    /// </summary>
    /// <remarks>
    /// Walked up from the test binary to the repository root rather than assumed at a fixed depth:
    /// the output layout moves with the configuration, the framework and any <c>--output</c>, and a
    /// content test that fails as a missing directory says nothing about content.
    /// </remarks>
    private static DirectoryInfo ArticleRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var articles = new DirectoryInfo(Path.Combine(directory.FullName, "content", "reference"));

            if (articles.Exists)
            {
                return articles;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("content/reference was not found above the test binary.");
    }

    /// <summary>
    /// Every offered locale gets its own articles, including the one whose tag carries a dash.
    /// </summary>
    /// <remarks>
    /// <b>Chinese was the locale that silently did not work.</b> MSBuild builds a manifest resource
    /// name out of the file's path and replaces what cannot appear in an identifier, so
    /// <c>content/reference/zh-Hans/</c> is embedded as <c>…reference.zh_Hans.…</c> and a lookup
    /// spelled with the dash matched nothing. German, Dutch and Japanese all worked, which is
    /// exactly the evidence that hides it — so this names <c>zh-Hans</c> rather than iterating and
    /// hoping the interesting case is in the list.
    /// </remarks>
    [Test]
    [Arguments("de")]
    [Arguments("nl")]
    [Arguments("ja")]
    [Arguments("zh-Hans")]
    public async Task ALocaleWithArticlesServesItsOwnProse(string tag)
    {
        var english = ReferenceLibrary.Shipped.Find("/reference/protocols/mssp");
        var translated = ReferenceLibrary.For(tag).Find("/reference/protocols/mssp");

        await Assert.That(english).IsNotNull();
        await Assert.That(translated).IsNotNull();

        await Assert.That(translated!.Body)
            .IsNotEqualTo(english!.Body)
            .Because($"{tag} has a translation of this article and should be reading it");

        // The prose changed and nothing else did: a translation supplies words, not structure.
        await Assert.That(translated.Slug).IsEqualTo(english.Slug);
        await Assert.That(translated.Kind).IsEqualTo(english.Kind);
        await Assert.That(translated.SeeAlso).IsEquivalentTo(english.SeeAlso);
        await Assert.That(translated.Home).IsEqualTo(english.Home);
    }

    /// <summary>And every article is translated in every offered locale, not merely most of them.</summary>
    /// <remarks>
    /// The loader falls back per article so a gap is invisible on the page, which is right for a
    /// reader and wrong for us: without this, one file failing to be written would look exactly like
    /// a file that was never meant to exist.
    /// </remarks>
    [Test]
    [Arguments("de")]
    [Arguments("nl")]
    [Arguments("ja")]
    [Arguments("zh-Hans")]
    public async Task NoArticleQuietlyFallsBackToEnglish(string tag)
    {
        var english = ReferenceLibrary.Shipped;
        var localized = ReferenceLibrary.For(tag);

        foreach (var article in english.Documents)
        {
            var translated = localized.Find(article.Path);

            await Assert.That(translated).IsNotNull();
            await Assert.That(translated!.Body)
                .IsNotEqualTo(article.Body)
                .Because($"{tag}{article.Path} is being served in English");
        }
    }
}
