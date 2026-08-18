using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using MUI.Crawl;
using MUI.Discovery;
using MUI.Web.Api;
using MUI.Web.Components;
using MUI.Web.Localization;

namespace MUI.Web.Tests;

/// <summary>
/// The about page, which is an obligation this project incurred by crawling rather than a feature.
/// </summary>
/// <remarks>
/// The load-bearing test is that the attribution list names every directory
/// <c>docs/import-sources.md</c> records as read (spec §7.6) — drift there silently drops a credit.
/// The rest assert sentences rather than markup, since a claim only present in the graphical layout
/// isn't really stated.
/// </remarks>
public class AboutPageTests
{
    private static AboutPage Page => AboutPage.Build(new ProbeOptions(), new DatasetLicenceOptions());

    private static string Plain => PlainText.RenderAbout(Page);

    /// <summary>
    /// The English a message id carries, which is what this page is asserted against.
    /// </summary>
    /// <remarks>
    /// Reads the message bundle rather than a pasted sentence, so this still fails if the id stops
    /// reaching the page. Where a claim is a rule rather than a sentence (e.g. "no automated
    /// opt-out"), the literal wording stays instead — see
    /// <see cref="NoMessageOutsideTheseTwoRefusalsUsesTheWordUptime"/> for the one vocabulary rule
    /// checked across the whole bundle instead.
    /// </remarks>
    private static string Says(string id) => Messages.For(Locales.SourceTag, id);

    [Test]
    public async Task EveryDirectoryTheBackfillReadIsCreditedByNameAndByAddress()
    {
        // Parsed from the record, not pasted from it, so a source added to the doc and not the page
        // fails here.
        var documented = DocumentedSources();

        await Assert.That(documented).IsNotEmpty();
        await Assert.That(Page.Sources).IsNotEmpty();

        foreach (var (name, url) in documented)
        {
            await Assert.That(Plain).Contains(name);
            await Assert.That(Plain).Contains(url);
        }
    }

    [Test]
    public async Task ASourceWeChoseNotToFetchIsCreditedAndSaidToBeUnread()
    {
        // MudVerse is deliberately never fetched; crediting it as read would misrepresent that.
        var mudverse = Page.Sources.Single(s => s.Name == "MudVerse");

        await Assert.That(mudverse.State).IsEqualTo(ImportSourceState.Withheld);
        await Assert.That(Plain).Contains(Says("about.source.withheld"));

        // A distinct string, not a negation — "we chose not to" vs. "we could not".
        await Assert.That(Says("about.source.read")).IsNotEqualTo(Says("about.source.withheld"));
    }

    [Test]
    public async Task TheAttributionSaysAddressesOnlyAndSaysWhy()
    {
        await Assert.That(Render.Words(Plain)).Contains(Says("about.sources.addresses.lead"));
        await Assert.That(Render.Words(Plain)).Contains("No player counts");
        await Assert.That(Render.Words(Plain)).Contains("no reachability history");
    }

    [Test]
    public async Task TheCrawlOfMudStatsThatWentOutUnaskedIsOnThePage()
    {
        var text = Render.Words(Plain);

        await Assert.That(text).Contains("143 of their pages");
        await Assert.That(text).Contains("before anyone had written to them");
    }

    [Test]
    public async Task TheArchiveGraceLimitationIsStatedInTheSpecsOwnTerms()
    {
        var text = Render.Words(Plain);

        await Assert.That(text).Contains("starts at the floor on the day we discover it");
        await Assert.That(text).Contains("quarter of the reachable time we probed");
    }

    [Test]
    public async Task TheReasonMsspCreatedEarnsNoGraceIsGiven()
    {
        var text = Render.Words(Plain);

        await Assert.That(text).Contains("do not credit MSSP CREATED");
        await Assert.That(text).Contains("gameable");
    }

    [Test]
    public async Task TheCrawlerIdentityIsReadOffTheProbeRatherThanWrittenOut()
    {
        // Must read off the actual probe object, or a deployment that configures it gets a page
        // confidently naming somebody else.
        var probe = new ProbeOptions { TerminalTypes = ["EXAMPLE-CRAWLER"], InfoUrl = "https://example.test/bot" };
        var text = PlainText.RenderAbout(AboutPage.Build(probe, new DatasetLicenceOptions()));

        await Assert.That(text).Contains("EXAMPLE-CRAWLER");
        await Assert.That(text).Contains("https://example.test/bot");
        await Assert.That(text).DoesNotContain("MUINDEX-CRAWLER");
    }

    [Test]
    public async Task TheCrawlerIsAnnouncedNowThatTncSupportsClientIdentity()
    {
        // TelnetNegotiationCore wires the configured name into TTYPE and MNES CLIENT_NAME, so what an
        // administrator sees in their logs is ProbeOptions.TerminalTypes.
        var identity = Page.Sections.Single(s => s.Id == "crawler").Identity;

        await Assert.That(identity).IsNotNull();
        await Assert.That(identity!.Announced).IsTrue();
        await Assert.That(Render.Words(Plain)).Contains("names itself");
    }

    [Test]
    public async Task AnUnconfiguredContactAddressIsMarkedAsThePlaceholderItIs()
    {
        // Printed unmarked, the built-in URL would read as our real contact address.
        await Assert.That(Render.Words(Plain)).Contains(Says("about.identity.placeholder.plain"));

        var configured = PlainText.RenderAbout(AboutPage.Build(
            new ProbeOptions { InfoUrl = "https://example.test/crawler" }, new DatasetLicenceOptions()));

        await Assert.That(Render.Words(configured))
            .DoesNotContain(Says("about.identity.placeholder.plain"));
    }

    [Test]
    public async Task TheOptOutIsPublishedWithTheExactWordsThatWork()
    {
        // Names both spellings an operator would type, read off the same vocabulary the reader
        // consumes, so the two can't drift apart.
        var text = Render.Words(Plain);

        await Assert.That(text).DoesNotContain("no automated opt-out");
        await Assert.That(text).Contains(OptOutVocabulary.MsspVariable);
        await Assert.That(text).Contains(OptOutVocabulary.DnsLabel);
        await Assert.That(text).Contains(OptOutVocabulary.DnsValue);

        // All three of §11's routes, named as routes rather than implied.
        await Assert.That(text).Contains("MSSP report");
        await Assert.That(text).Contains("TXT record");
        await Assert.That(text).Contains("write to a person");

        // An opt-out with no documented exit is a trap.
        await Assert.That(text).Contains("within one crawl cycle");
        await Assert.That(text).Contains("undo without asking us");
    }

    [Test]
    public async Task ThePageSaysWhatAnOptedOutGamesPageStillShows()
    {
        // Rule 3: an opted-out game keeps its history, only new measurement stops.
        var text = Render.Words(Plain);

        await Assert.That(text).Contains("Stopping is not deleting");
        await Assert.That(text).Contains("everything we measured before");
        await Assert.That(text).Contains("names no cause");
    }

    [Test]
    public async Task ThePoliteAndSecurityFactsAboutAProbeSurvive()
    {
        var text = Render.Words(Plain);

        await Assert.That(text).Contains(Says("about.crawler.delay.lead"));
        await Assert.That(text).Contains("resolved before anything is dialled");
        await Assert.That(text).Contains("globally routable");
    }

    [Test]
    public async Task ThePermittedCommandIsReadOffTheProbeAndNotDescribedFromMemory()
    {
        // Read off the probe's own published list, so the page can't understate it if a second
        // command is added.
        foreach (var command in TelnetProbe.PermittedCommands)
        {
            await Assert.That(Plain).Contains(command);
        }
    }

    [Test]
    public async Task TheMeasuredSpineSurvivesInWords()
    {
        var text = Render.Words(Plain);

        await Assert.That(text).Contains(Says("about.measures.declared.lead"));
        await Assert.That(text).Contains("MSSP PLAYERS field");
        await Assert.That(text).Contains("WHO or DOING read at the connect screen");
        await Assert.That(text).Contains("unknown, never zero");
    }

    /// <summary>
    /// The page explains what reachability is, in whatever language it is being read in.
    /// </summary>
    /// <remarks>
    /// Asserts the message ids rather than counting the English word "uptime" — a literal word count
    /// breaks under translation. <see cref="NoMessageOutsideTheseTwoRefusalsUsesTheWordUptime"/>
    /// checks the vocabulary rule ("reachable, never uptime") itself, across every locale.
    /// </remarks>
    [Test]
    public async Task ReachableIsExplainedInWhateverLanguageThePageIsRead()
    {
        var text = Render.Words(Plain);

        await Assert.That(text).Contains(Says("about.measures.reachable.lead"));
        await Assert.That(text).Contains(Says("about.measures.reachable.body"));

        await Assert.That(text).Contains("unreachable and perfectly alive");
        await Assert.That(text).Contains("nothing here measured it");
    }

    /// <summary>
    /// "Reachable, never uptime" — over the bundle, in every locale, rather than over one page.
    /// </summary>
    /// <remarks>
    /// The two about-page ids are the only place "uptime" is allowed to appear, since they exist to
    /// refuse it. Every other id, in every locale, is checked for the word.
    /// </remarks>
    [Test]
    public async Task NoMessageOutsideTheseTwoRefusalsUsesTheWordUptime()
    {
        string[] refusals = ["about.measures.reachable.lead", "about.measures.reachable.body"];

        var tags = Locales.All
            .Select(locale => locale.Tag)
            .Append(Locales.SourceTag)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await Assert.That(tags).IsNotEmpty();

        foreach (var tag in tags)
        {
            foreach (var id in Messages.Ids.Except(refusals, StringComparer.Ordinal))
            {
                var pattern = Messages.Pattern(tag, id) ?? string.Empty;

                await Assert.That(pattern.Contains("uptime", StringComparison.OrdinalIgnoreCase))
                    .IsFalse()
                    .Because($"{id} says \"uptime\" in {tag}; the word here is reachable");
            }
        }

        // The exemption is real, not vacuous: the English refusals do use the word.
        await Assert.That(Says("about.measures.reachable.lead").ToLowerInvariant()).Contains("uptime");
    }

    [Test]
    public async Task TheThingsThisSiteWillNotDoAreStatedRatherThanImplied()
    {
        // The one page where these words appear, because it's the page saying they're absent
        // elsewhere; PlainParityTests asserts the opposite about every other surface.
        var text = Render.Words(Plain);

        await Assert.That(text).Contains(Says("about.never.votes.lead"));
        await Assert.That(text).Contains(Says("about.never.forums.lead"));
        await Assert.That(text).Contains(Says("about.never.names.lead"));
        await Assert.That(text).Contains(Says("about.never.population.lead"));
    }

    [Test]
    public async Task TheDataLicenceIsPresentedAsUndecidedRatherThanAsSettled()
    {
        var text = Render.Words(Plain);

        await Assert.That(text).Contains(Says("about.licence.code.lead"));
        await Assert.That(text).Contains("licence for the data is an open question");
        await Assert.That(text).Contains("not yet taken");

        // Framed as this deployment's answer, not the project's.
        await Assert.That(text).Contains("this deployment serves");
        await Assert.That(text).Contains(new DatasetLicenceOptions().LicenceName);
    }

    [Test]
    public async Task NoPlainLineIsWiderThanEightyColumns()
    {
        // All the over-long lines, not just the first — the count is the diagnosis.
        var wide = Plain.Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > PlainText.Columns)
            .ToArray();

        await Assert.That(wide).IsEmpty();
    }

    [Test]
    public async Task TheGraphicalPageCarriesEverySentenceThePlainOneDoes()
    {
        // Both surfaces render one view model; read off the rendered frame, what a browser receives.
        var markup = await RenderAboutAsync();
        var text = Text(markup);

        foreach (var section in Page.Sections)
        {
            await Assert.That(text).Contains(section.Heading);

            foreach (var point in section.Points)
            {
                await Assert.That(text).Contains(Render.Words(point.Sentence));
            }

            foreach (var source in section.Sources)
            {
                await Assert.That(text).Contains(source.Name);
                await Assert.That(text).Contains(Render.Words(source.Note));

                // The URL is a link here, not printed text — asserted against markup, not text.
                await Assert.That(markup).Contains(source.Url);
            }
        }
    }

    /// <summary>
    /// Every sentence on this page comes out of the bundle, and the machine voice does not.
    /// </summary>
    /// <remarks>
    /// The pseudolocale marks every string that passed through <see cref="Messages"/>; anything still
    /// legible as English here was hard-coded. Directory names and URLs are asserted
    /// <em>unchanged</em> instead — translating someone else's name would misattribute the credit
    /// §7.6 owes.
    /// </remarks>
    [Test]
    public async Task EverySentenceComesFromTheBundleAndTheDirectoriesOwnNamesDoNot()
    {
        var page = AboutPage.Build(new ProbeOptions(), new DatasetLicenceOptions(), "qps-ploc");
        var pseudo = PlainText.RenderAbout(page, "qps-ploc");

        await Assert.That(pseudo).Contains("⟦");

        foreach (var section in Page.Sections)
        {
            await Assert.That(pseudo)
                .DoesNotContain(section.Heading)
                .Because($"the {section.Id} heading never went through the message pipeline");

            foreach (var point in section.Points)
            {
                await Assert.That(pseudo)
                    .DoesNotContain(point.Lead)
                    .Because($"a point in {section.Id} is hard-coded English");
            }
        }

        // Machine voice too; read off the objects that own them rather than out of the bundle.
        await Assert.That(pseudo).Contains(page.Sections.Single(s => s.Id == "crawler").Identity!.Name);
        await Assert.That(pseudo).Contains(new DatasetLicenceOptions().LicenceName);

        foreach (var source in Page.Sources)
        {
            await Assert.That(pseudo).Contains(source.Name);
            await Assert.That(pseudo).Contains(source.Url);
        }
    }

    /// <summary>
    /// The graphical page needs no scripting to be read.
    /// </summary>
    /// <remarks>
    /// The text-mirror-offer assertion lives in <see cref="ReadingControlsTests"/> now, checked once
    /// across every route rather than duplicated per page.
    /// </remarks>
    [Test]
    public async Task TheGraphicalPageIsReachableWithoutScripting()
    {
        var html = await RenderAboutAsync();

        await Assert.That(html).DoesNotContain("<script");
    }

    /// <summary>
    /// A rendered page as its reader hears it: tags gone, entities decoded, whitespace collapsed.
    /// </summary>
    /// <remarks>
    /// A sentence whose lead-in is bold is one sentence with a tag inside it, so an assertion that
    /// read the markup would be asserting on the emphasis rather than on the claim.
    /// </remarks>
    private static string Text(string html) => Render.Words(Regex.Replace(html, "<[^>]+>", " "));

    /// <summary>
    /// Renders the page with the services it injects, which <see cref="Render"/> cannot supply.
    /// </summary>
    private static async Task<string> RenderAboutAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IOptions<DatasetLicenceOptions>>(
            Options.Create(new DatasetLicenceOptions()));

        // Fixture mode, matching the rest of this harness.
        services.AddSingleton(new MUI.Web.Data.CatalogueSource(IsMeasured: false));

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<MUI.Web.Components.Pages.About>();
            return output.ToHtmlString();
        });
    }

    /// <summary>
    /// The sources <c>docs/import-sources.md</c> records under <c>## Read</c>, as name and address.
    /// </summary>
    /// <remarks>
    /// Rows are <c>| [Name](url) | … |</c>; the second table in that stretch is a yield summary with
    /// no links in its first cell, so requiring the link is what separates them.
    /// </remarks>
    private static IReadOnlyList<(string Name, string Url)> DocumentedSources()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "import-sources.md"));
        var found = new List<(string, string)>();
        var reading = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                reading = line.StartsWith("## Read", StringComparison.Ordinal);
                continue;
            }

            if (!reading)
            {
                continue;
            }

            var match = Regex.Match(line, @"^\|\s*\[([^\]]+)\]\(([^)]+)\)");
            if (match.Success)
            {
                found.Add((match.Groups[1].Value, match.Groups[2].Value));
            }
        }

        return found;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MUIndex.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("No MUIndex.slnx above " + AppContext.BaseDirectory);
    }
}
