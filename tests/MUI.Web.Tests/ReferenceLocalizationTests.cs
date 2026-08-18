using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Localization;
using MUI.Web.Reference;

namespace MUI.Web.Tests;

/// <summary>
/// The reference section's chrome, answered in the language the reader asked for.
/// </summary>
/// <remarks>
/// The chrome and the articles are localized by two different routes; this is about the first — the
/// bundle, not the content layer's translation. The pseudo-locale proves the boundary: a string
/// rendered through the bundle comes back wrapped in <c>⟦ ⟧</c>.
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
        await using var site = await SiteHost.StartAsync();

        var words = Render.Words(
            await site.Client.GetStringAsync($"/{Pseudo}/reference/protocols/mssp"));

        await Assert.That(words).Contains("MSSP is telnet option 70");
    }

    [Test]
    public async Task AProtocolAcronymIsNeverATranslatableString()
    {
        // Translating an acronym like TTYPE would be destroying evidence, not localizing a string.
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
        await using var site = await SiteHost.StartAsync();

        var markup = await site.Client.GetStringAsync($"/{Pseudo}/reference/protocols/ttype");

        await Assert.That(markup).Contains("TTYPE");
        await Assert.That(markup).DoesNotContain("TTYPÉ");
    }

    [Test]
    public async Task ThePlainMirrorIsWrittenInThePagesLanguage()
    {
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
        // An unknown is what we looked for and didn't establish; collapsing it into "no" would
        // publish our own failure to find as the client lacking the feature.
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
        // The word is an argument to the sentence, not written into it a second time.
        foreach (var locale in Locales.All.Where(l => l.IsChoosable))
        {
            await Assert.That(ReferencePlainText.ClientMatrixCaveat(locale.Tag))
                .Contains(ClientCapabilities.Word(locale.Tag, CapabilityState.Unknown));
        }
    }

    /// <summary>
    /// A translated article is the same article: same slug, same kind, same related pages.
    /// </summary>
    /// <remarks>Enforced by the loader and asserted here by walking files on disk, since a front matter disagreeing with the English is a trap for whoever edits it next. Only <c>title</c> and <c>summary</c> are prose.</remarks>
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
    /// <remarks>Walked up from the test binary rather than assumed at a fixed depth, since output layout moves with configuration and framework.</remarks>
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
    /// <remarks>Chinese previously silently didn't work: MSBuild embeds <c>content/reference/zh-Hans/</c> as <c>…reference.zh_Hans.…</c>, and a dash-spelled lookup matched nothing.</remarks>
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

        await Assert.That(translated.Slug).IsEqualTo(english.Slug);
        await Assert.That(translated.Kind).IsEqualTo(english.Kind);
        await Assert.That(translated.SeeAlso).IsEquivalentTo(english.SeeAlso);
        await Assert.That(translated.Home).IsEqualTo(english.Home);
    }

    /// <summary>Every article is translated in every offered locale, not merely most — the loader falls back per article, so a missing file would look like one never meant to exist.</summary>
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
