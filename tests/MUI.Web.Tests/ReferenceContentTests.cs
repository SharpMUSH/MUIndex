using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Localization;
using MUI.Web.Reference;

namespace MUI.Web.Tests;

/// <summary>
/// The shipped reference content, checked as content.
/// </summary>
/// <remarks>Reads the real files, not a fixture — these guard against editorial failures (an uncited capability, a dead link, a mistyped protocol name) that look right and aren't bugs in the renderer.</remarks>
public class ReferenceContentTests
{
    private static readonly ReferenceLibrary Library = ReferenceLibrary.Shipped;

    [Test]
    public async Task TheSectionShipsAllFourKinds()
    {
        // Spec §9 names all four kinds; missing one means the section quietly became something else.
        foreach (var kind in Enum.GetValues<ReferenceKind>())
        {
            await Assert.That(Library.OfKind(kind).Count).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task EveryEstablishedCapabilityCitesASource()
    {
        // The type demotes an uncited claim to unknown, so this fails when an author wrote a yes and
        // left the source blank.
        foreach (var document in Library.OfKind(ReferenceKind.Client))
        {
            foreach (var claim in document.Capabilities.Where(c => c.IsClaimed))
            {
                await Assert.That(claim.Source).IsNotNull();
                await Assert.That(claim.Source!).StartsWith("https://");
            }
        }
    }

    [Test]
    public async Task EveryClientPageAnswersTheScreenReaderQuestionOneWayOrTheOther()
    {
        // Present on every client page whether or not established — an omitted row and an unknown
        // row are the same fact and only one is legible.
        foreach (var document in Library.OfKind(ReferenceKind.Client))
        {
            var claims = ClientCapabilities.For(document);
            var accessibility = claims.SingleOrDefault(c => c.Name == ClientCapabilities.ScreenReader);

            await Assert.That(accessibility).IsNotNull();
        }
    }

    [Test]
    public async Task AtLeastOneClientPageEstablishesScreenReaderSupportAndAtLeastOneDoesNot()
    {
        // Both arms have to exist or the row is decorative.
        var states = Library.OfKind(ReferenceKind.Client)
            .Select(d => ClientCapabilities.For(d).Single(c => c.Name == ClientCapabilities.ScreenReader).State)
            .ToList();

        await Assert.That(states).Contains(CapabilityState.Present);
        await Assert.That(states).Contains(CapabilityState.Unknown);
    }

    [Test]
    public async Task EveryProtocolPageNamesACapabilityTheCatalogueMeasures()
    {
        // A mismatched name reports zero adoption forever on a page that otherwise looks correct.
        foreach (var document in Library.OfKind(ReferenceKind.Protocol))
        {
            await Assert.That(document.Protocol).IsNotNull();
            await Assert.That(MUI.Catalog.Persistence.CapabilityFields.Names)
                .Contains(document.Protocol!);
        }
    }

    [Test]
    public async Task EveryCodebasePageNamesAFamilyToMatchOn()
    {
        foreach (var document in Library.OfKind(ReferenceKind.Codebase))
        {
            await Assert.That(document.Codebase).IsNotNull();
            await Assert.That(document.GamesPath).IsNotNull();
        }
    }

    [Test]
    public async Task EverySeeAlsoNamesAPageWeShip()
    {
        // Related() drops an unresolvable reference rather than rendering a dead link, so a typo is
        // invisible on the page; this is where it becomes visible.
        foreach (var document in Library.Documents)
        {
            await Assert.That(Library.Related(document).Count).IsEqualTo(document.SeeAlso.Count);
        }
    }

    [Test]
    public async Task NoTwoPagesClaimTheSameUrl()
    {
        var paths = Library.Documents.Select(d => d.Path).ToList();

        await Assert.That(paths.Distinct(StringComparer.OrdinalIgnoreCase).Count()).IsEqualTo(paths.Count);
    }

    [Test]
    public async Task NoContentFileCarriesACountOfItsOwn()
    {
        // A hand-typed "47 games" is indistinguishable on the page from a measured figure, so it has
        // to be impossible rather than discouraged. Measured figures are rendered by the shell from
        // IGameQueries and never appear in a file.
        foreach (var document in Library.Documents)
        {
            var words = document.Body.Split([' ', '\n', ',', '.', ':', ';'], StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < words.Length - 1; i++)
            {
                if (!int.TryParse(words[i], out _))
                {
                    continue;
                }

                // Plural only — a catalogue count is never written in the singular.
                await Assert.That(words[i + 1].ToLowerInvariant()).IsNotEqualTo("games");
            }
        }
    }

    [Test]
    public async Task ProseCarriesNoRemoteAsset()
    {
        // Asserts the rendered output, not the source — that's where the no-external-fetch guarantee has to hold.
        foreach (var document in Library.Documents)
        {
            var html = ReferenceMarkdown.ToHtml(document.Body);

            await Assert.That(html).DoesNotContain("<img");
            await Assert.That(html).DoesNotContain("<script");
            await Assert.That(html).DoesNotContain("<iframe");
            await Assert.That(html).DoesNotContain("@import");
        }
    }

    [Test]
    public async Task AnImageInProseBecomesALinkRatherThanAFetch()
    {
        var html = ReferenceMarkdown.ToHtml("![a map](https://elsewhere.example/map.png)");

        await Assert.That(html).DoesNotContain("<img");
        await Assert.That(html).Contains("https://elsewhere.example/map.png");
    }

    [Test]
    public async Task RawHtmlInProseIsNeverEmittedAsMarkup()
    {
        var html = ReferenceMarkdown.ToHtml("<script src=\"https://elsewhere.example/x.js\"></script>");

        await Assert.That(html).DoesNotContain("<script");
    }

    [Test]
    public async Task ABodyHeadingNeverCompetesWithThePageTitle()
    {
        // The shell renders the h1; a # in the body would produce a second and break the screen-reader outline.
        var html = ReferenceMarkdown.ToHtml("# A heading\n\ntext");

        await Assert.That(html).DoesNotContain("<h1");
        await Assert.That(html).Contains("<h2");
    }
}

/// <summary>
/// The plain rendering of every reference page, which is the test of whether the section is
/// communicating anything at all.
/// </summary>
/// <remarks>Asserted through <see cref="Messages"/> rather than English literals, so a translator's wording change doesn't break these. <see cref="ReferenceLocalizationTests"/> is where language itself is the subject.</remarks>
public class ReferencePlainTests
{
    private static readonly ReferenceLibrary Library = ReferenceLibrary.Shipped;

    private const string Tag = Locales.SourceTag;

    private static string Word(CapabilityState state) => ClientCapabilities.Word(Tag, state);

    [Test]
    public async Task EveryReferencePageHasAPlainRendering()
    {
        foreach (var document in Library.Documents)
        {
            var text = ReferencePlainText.Render(Tag, document, related: Library.Related(document));

            await Assert.That(text).Contains(document.Title.ToUpperInvariant());
            await Assert.That(text.Length).IsGreaterThan(document.Summary.Length);
        }
    }

    [Test]
    public async Task AClientMatrixSurvivesInWordsWithUnknownSpelledOut()
    {
        var mudlet = Library.Find(ReferenceKind.Client, "mudlet")!;
        var text = ReferencePlainText.Render(Tag, mudlet);

        await Assert.That(text).Contains($"screen reader    {Word(CapabilityState.Present)}");
        await Assert.That(text).Contains($"MCCP             {Word(CapabilityState.Unknown)}");

        // The caveat quotes the same word the cells use.
        await Assert.That(Render.Words(text))
            .Contains(Render.Words(ReferencePlainText.ClientMatrixCaveat(Tag)));
    }

    [Test]
    public async Task ThePlainClientMatrixNeverLeavesAnUnknownCellBlank()
    {
        // A blank reads as a no to a human exactly as it does to a parser.
        foreach (var document in Library.OfKind(ReferenceKind.Client))
        {
            var text = ReferencePlainText.Render(Tag, document);

            foreach (var claim in ClientCapabilities.For(document).Where(c => c.State is CapabilityState.Unknown))
            {
                await Assert.That(text).Contains($"{claim.Name,-16} {Word(CapabilityState.Unknown)}");
            }
        }
    }

    [Test]
    public async Task ThePlainProtocolPageSaysWhatTheRemainderIsNot()
    {
        var gmcp = Library.Find(ReferenceKind.Protocol, "gmcp")!;
        var text = ReferencePlainText.Render(Tag, gmcp, protocol: new ProtocolFigures(3, 10, []));

        await Assert.That(Render.Words(text)).Contains(Render.Words(Messages.Say(
            Tag,
            "reference.plain.protocol.share",
            ("offering", 3),
            ("listed", 10),
            ("percent", Wording.Percent(0.3)))));

        // Rule 5: games not counted are not games without the protocol.
        await Assert.That(Render.Words(text))
            .Contains(Render.Words(ReferencePlainText.ProtocolRemainderCaveat(Tag)));
    }

    [Test]
    public async Task ThePlainIndexListsEverySectionItHas()
    {
        var text = ReferencePlainText.RenderIndex(Tag, Library);

        foreach (var kind in Enum.GetValues<ReferenceKind>())
        {
            await Assert.That(text).Contains(ReferencePlainText.Heading(Tag, kind).ToUpperInvariant());
        }

        await Assert.That(Render.Words(text))
            .Contains(Render.Words(Messages.For(Tag, "reference.plain.lede")));
    }

    [Test]
    public async Task NoPlainReferenceLineIsWiderThanEightyColumnsUnlessItIsOneUnbreakableToken()
    {
        // Narrow exemption: a citation URL can't be wrapped and still be a URL, so it's left whole.
        foreach (var document in Library.Documents)
        {
            var text = ReferencePlainText.Render(Tag, document, related: Library.Related(document));

            foreach (var line in text.Split('\n').Select(l => l.TrimEnd()))
            {
                if (line.Length <= PlainText.Columns || !line.Trim().Contains(' ', StringComparison.Ordinal))
                {
                    continue;
                }

                await Assert.That(line.Length).IsLessThanOrEqualTo(PlainText.Columns);
            }
        }
    }
}
