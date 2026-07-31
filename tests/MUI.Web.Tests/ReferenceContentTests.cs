using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Reference;

namespace MUI.Web.Tests;

/// <summary>
/// The shipped reference content, checked as content.
/// </summary>
/// <remarks>
/// These read the real files rather than a fixture, because the failures they guard against are
/// editorial rather than structural: a capability asserted with nothing behind it, a page linking to
/// one that does not exist, a protocol page naming an option the catalogue has never heard of and so
/// reporting zero for ever. None of those is a bug in the renderer and all of them look right.
/// </remarks>
public class ReferenceContentTests
{
    private static readonly ReferenceLibrary Library = ReferenceLibrary.Shipped;

    [Test]
    public async Task TheSectionShipsAllFourKinds()
    {
        // Spec §9 names clients, codebases and protocols, and orientation content beside them. A
        // section missing one of the four is a section that has quietly become something else.
        foreach (var kind in Enum.GetValues<ReferenceKind>())
        {
            await Assert.That(Library.OfKind(kind).Count).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task EveryEstablishedCapabilityCitesASource()
    {
        // The type demotes an uncited claim to unknown, so this cannot fail as a rendering bug — it
        // fails when an author wrote a yes and left the source blank, and the demotion is what would
        // otherwise hide that from them.
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
        // The row spec §9 calls out by name, and the one no incumbent publishes. It is present on
        // every client page whether or not anybody established it, because an omitted row and an
        // unknown row are the same fact and only one of them is legible.
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
        // Both arms have to exist or the row is decorative. A table where every cell is unknown
        // teaches a reader to ignore the column; one where every cell is yes is not a table anybody
        // checked.
        var states = Library.OfKind(ReferenceKind.Client)
            .Select(d => ClientCapabilities.For(d).Single(c => c.Name == ClientCapabilities.ScreenReader).State)
            .ToList();

        await Assert.That(states).Contains(CapabilityState.Present);
        await Assert.That(states).Contains(CapabilityState.Unknown);
    }

    [Test]
    public async Task EveryProtocolPageNamesACapabilityTheCatalogueMeasures()
    {
        // A protocol page whose name does not match the field vocabulary reports zero adoption for
        // ever, on a page that otherwise looks entirely correct — the worst shape a bug can have
        // here, because the number is plausible.
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
        // Related() drops a reference it cannot resolve rather than rendering a dead link, so a typo
        // is invisible on the page. This is where it becomes visible.
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
        // The failure this whole section is shaped to prevent: a codebase page whose "47 games" is a
        // number somebody typed. It is indistinguishable on the page from the measured version,
        // which is why it has to be impossible rather than discouraged.
        //
        // Read as: no sentence in the prose asserts a number of games. The measured figures are
        // rendered by the shell from IGameQueries and never appear in a file.
        foreach (var document in Library.Documents)
        {
            var words = document.Body.Split([' ', '\n', ',', '.', ':', ';'], StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < words.Length - 1; i++)
            {
                if (!int.TryParse(words[i], out _))
                {
                    continue;
                }

                // Plural only. "Bartle and Trubshaw's 1978 game" is a date and a noun, and a
                // catalogue count is never written in the singular.
                await Assert.That(words[i + 1].ToLowerInvariant()).IsNotEqualTo("games");
            }
        }
    }

    [Test]
    public async Task ProseCarriesNoRemoteAsset()
    {
        // The rendered page fetches from no external host. Raw HTML is off in the pipeline, so the
        // only syntax that could make a browser reach out is an image — and the renderer turns those
        // into ordinary links. This asserts the rendered output rather than the source, because that
        // is where the guarantee has to hold.
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
        // The shell renders the h1. A # in the body would produce a second first-level heading and
        // break the outline a screen reader navigates by.
        var html = ReferenceMarkdown.ToHtml("# A heading\n\ntext");

        await Assert.That(html).DoesNotContain("<h1");
        await Assert.That(html).Contains("<h2");
    }
}

/// <summary>
/// The plain rendering of every reference page, which is the test of whether the section is
/// communicating anything at all.
/// </summary>
public class ReferencePlainTests
{
    private static readonly ReferenceLibrary Library = ReferenceLibrary.Shipped;

    [Test]
    public async Task EveryReferencePageHasAPlainRendering()
    {
        foreach (var document in Library.Documents)
        {
            var text = ReferencePlainText.Render(document, related: Library.Related(document));

            await Assert.That(text).Contains(document.Title.ToUpperInvariant());
            await Assert.That(text.Length).IsGreaterThan(document.Summary.Length);
        }
    }

    [Test]
    public async Task AClientMatrixSurvivesInWordsWithUnknownSpelledOut()
    {
        var mudlet = Library.Find(ReferenceKind.Client, "mudlet")!;
        var text = ReferencePlainText.Render(mudlet);

        await Assert.That(text).Contains("screen reader    yes");
        await Assert.That(text).Contains("MCCP             unknown");
        await Assert.That(text).Contains("It never means no.");
    }

    [Test]
    public async Task ThePlainClientMatrixNeverLeavesAnUnknownCellBlank()
    {
        // A blank reads as a no to a human exactly as it does to a parser, which is the entire
        // reason this surface exists.
        foreach (var document in Library.OfKind(ReferenceKind.Client))
        {
            var text = ReferencePlainText.Render(document);

            foreach (var claim in ClientCapabilities.For(document).Where(c => c.State is CapabilityState.Unknown))
            {
                await Assert.That(text).Contains($"{claim.Name,-16} unknown");
            }
        }
    }

    [Test]
    public async Task ThePlainProtocolPageSaysWhatTheRemainderIsNot()
    {
        var gmcp = Library.Find(ReferenceKind.Protocol, "gmcp")!;
        var text = ReferencePlainText.Render(gmcp, protocol: new ProtocolFigures(3, 10, []));

        await Assert.That(text).Contains("3 of 10 listed games were observed offering it");
        await Assert.That(Render.Words(text)).Contains("not games without the protocol");
    }

    [Test]
    public async Task ThePlainIndexListsEverySectionItHas()
    {
        var text = ReferencePlainText.RenderIndex(Library);

        foreach (var heading in new[] { "START HERE", "CODEBASES", "CLIENTS", "PROTOCOLS" })
        {
            await Assert.That(text).Contains(heading);
        }

        await Assert.That(Render.Words(text)).Contains("This is not a wiki");
    }

    [Test]
    public async Task NoPlainReferenceLineIsWiderThanEightyColumnsUnlessItIsOneUnbreakableToken()
    {
        // The exemption is deliberate and narrow: a citation URL longer than eighty characters
        // cannot be wrapped and still be a URL, so it is left whole. Everything with a space in it
        // has to fit.
        foreach (var document in Library.Documents)
        {
            var text = ReferencePlainText.Render(document, related: Library.Related(document));

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
