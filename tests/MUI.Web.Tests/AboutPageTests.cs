using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using MUI.Crawl;
using MUI.Web.Api;
using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// The about page, which is an obligation this project incurred by crawling rather than a feature.
/// </summary>
/// <remarks>
/// <para>
/// The load-bearing test here is not that the page renders: it is that the attribution list names
/// every directory <c>docs/import-sources.md</c> says was read. That file is the record §7.6 leaves
/// behind on <c>main</c> when the importer goes, and a credit that drifts from it is a credit that
/// quietly drops whoever was added last.
/// </para>
/// <para>
/// The rest assert sentences rather than markup, for the reason the whole plain surface exists: a
/// limitation that only survives in the graphical page is a limitation stated to the layout.
/// </para>
/// </remarks>
public class AboutPageTests
{
    private static AboutPage Page => AboutPage.Build(new ProbeOptions(), new DatasetLicenceOptions());

    private static string Plain => PlainText.RenderAbout(Page);

    [Test]
    public async Task EveryDirectoryTheBackfillReadIsCreditedByNameAndByAddress()
    {
        // Parsed from the record rather than pasted from it. A source added to the document and not
        // to the page is the failure this exists to catch, and a copied list cannot catch it.
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
        // MudVerse is written, tested and deliberately never run. Leaving it off would read as
        // completeness; crediting it as read would be the same lie the other way round.
        var mudverse = Page.Sources.Single(s => s.Name == "MudVerse");

        await Assert.That(mudverse.State).IsEqualTo(ImportSourceState.Withheld);
        await Assert.That(Plain).Contains("not read — waiting on permission");
    }

    [Test]
    public async Task TheAttributionSaysAddressesOnlyAndSaysWhy()
    {
        await Assert.That(Render.Words(Plain)).Contains("We take addresses. Nothing else.");
        await Assert.That(Plain).Contains("no player counts");
        await Assert.That(Plain).Contains("no reachability history");
    }

    [Test]
    public async Task TheCrawlOfMudStatsThatWentOutUnaskedIsOnThePage()
    {
        // The record matters more than the tidy version, and it matters most on the page that asks
        // to be trusted about everything else.
        var text = Render.Words(Plain);

        await Assert.That(text).Contains("143 of their pages");
        await Assert.That(text).Contains("before anyone had written to them");
    }

    [Test]
    public async Task TheArchiveGraceLimitationIsStatedInTheSpecsOwnTerms()
    {
        var text = Render.Words(Plain);

        await Assert.That(text).Contains("60-day floor on the day we discover it");
        await Assert.That(text).Contains("reachable time we ourselves probed");
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
        // A name typed into the page is a description of the source tree. This one has to be the
        // object the probe is constructed from, or a deployment that configures it gets a page that
        // confidently names somebody else.
        var probe = new ProbeOptions { TerminalTypes = ["EXAMPLE-CRAWLER"], InfoUrl = "https://example.test/bot" };
        var text = PlainText.RenderAbout(AboutPage.Build(probe, new DatasetLicenceOptions()));

        await Assert.That(text).Contains("EXAMPLE-CRAWLER");
        await Assert.That(text).Contains("https://example.test/bot");
        await Assert.That(text).DoesNotContain("MUINDEX-CRAWLER");
    }

    [Test]
    public async Task TheCrawlerIsAnnouncedNowThatTncSupportsClientIdentity()
    {
        // TelnetNegotiationCore 2.8.1 added WithClientIdentity and WithTerminalTypes, which wire
        // the configured name into TTYPE responses and MNES CLIENT_NAME. The probe sets both, so
        // what an administrator reads in their logs is the name in ProbeOptions.TerminalTypes.
        var identity = Page.Sections.Single(s => s.Id == "crawler").Identity;

        await Assert.That(identity).IsNotNull();
        await Assert.That(identity!.Announced).IsTrue();
        await Assert.That(Render.Words(Plain)).Contains("names itself");
    }

    [Test]
    public async Task AnUnconfiguredContactAddressIsMarkedAsThePlaceholderItIs()
    {
        // The built-in URL is on a domain nobody has chosen. Printed unmarked it would read as the
        // way to reach us, which is the one thing this section exists to provide.
        await Assert.That(Plain).Contains("built-in placeholder");

        var configured = PlainText.RenderAbout(AboutPage.Build(
            new ProbeOptions { InfoUrl = "https://example.test/crawler" }, new DatasetLicenceOptions()));

        await Assert.That(configured).DoesNotContain("built-in placeholder");
    }

    [Test]
    public async Task TheOptOutIsNotAdvertisedAsAutomatedWhileItIsNot()
    {
        // Neither the MSSP field nor the DNS TXT record the design describes exists in code. A page
        // offering a switch that is wired to nothing is worse than a page admitting there is none.
        var text = Render.Words(Plain);

        await Assert.That(text).Contains("no automated opt-out yet");
        await Assert.That(text).Contains("Ask, and we stop.");
    }

    [Test]
    public async Task ThePoliteAndSecurityFactsAboutAProbeSurvive()
    {
        var text = Render.Words(Plain);

        await Assert.That(text).Contains("CRAWL DELAY wins.");
        await Assert.That(text).Contains("resolved before anything is dialled");
        await Assert.That(text).Contains("globally routable");
    }

    [Test]
    public async Task ThePermittedCommandIsReadOffTheProbeAndNotDescribedFromMemory()
    {
        // "It sends one WHO" is a claim about the probe. The probe publishes the list, so the page
        // cannot understate it when a second command is ever added.
        foreach (var command in TelnetProbe.PermittedCommands)
        {
            await Assert.That(Plain).Contains(command);
        }
    }

    [Test]
    public async Task TheMeasuredSpineSurvivesInWords()
    {
        var text = Render.Words(Plain);

        await Assert.That(text).Contains("Measured beats declared, and both are shown.");
        await Assert.That(text).Contains("MSSP PLAYERS field");
        await Assert.That(text).Contains("WHO or DOING read at the connect screen");
        await Assert.That(text).Contains("unknown, never zero");
    }

    [Test]
    public async Task ReachableIsExplainedAndNeverCalledUptimeExceptToRefuseTheWord()
    {
        var text = Render.Words(Plain);

        await Assert.That(text).Contains("Reachable, never uptime.");

        // The word appears twice and both are refusals. Anything else would be the site's own
        // vocabulary rule broken on the page that states it.
        var uses = Regex.Matches(text, "uptime", RegexOptions.IgnoreCase).Count;
        await Assert.That(uses).IsEqualTo(2);
        await Assert.That(text).Contains("does not measure whether the game was up");
        await Assert.That(text).Contains("nothing here measured it");
    }

    [Test]
    public async Task TheThingsThisSiteWillNotDoAreStatedRatherThanImplied()
    {
        // The one page where these words are allowed to appear, because it is the page that says
        // they are absent. PlainParityTests asserts the opposite about every other surface.
        var text = Render.Words(Plain);

        await Assert.That(text).Contains("no votes, stars, ratings or recommendations");
        await Assert.That(text).Contains("no forums, reviews, wikis, comments or player profiles");
        await Assert.That(text).Contains("Player names are never persisted.");
        await Assert.That(text).Contains("No absolute population figure is published.");
    }

    [Test]
    public async Task TheDataLicenceIsPresentedAsUndecidedRatherThanAsSettled()
    {
        var text = Render.Words(Plain);

        await Assert.That(text).Contains("The code is MIT.");
        await Assert.That(text).Contains("licence for the data is an open question");
        await Assert.That(text).Contains("has not been taken");

        // What the deployment serves is still shown — a consumer needs the terms — but framed as
        // this deployment's answer rather than as the project's.
        await Assert.That(text).Contains("this deployment serves");
        await Assert.That(text).Contains(new DatasetLicenceOptions().LicenceName);
    }

    [Test]
    public async Task NoPlainLineIsWiderThanEightyColumns()
    {
        // The over-long lines rather than the first one, because a width failure is usually a whole
        // block that was built without the wrapper and the count is the diagnosis.
        var wide = Plain.Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > PlainText.Columns)
            .ToArray();

        await Assert.That(wide).IsEmpty();
    }

    [Test]
    public async Task TheGraphicalPageCarriesEverySentenceThePlainOneDoes()
    {
        // The parity that matters: both surfaces render one view model, so nothing can exist on one
        // and not the other. Read off the rendered frame, because that is what a browser receives.
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

                // The address is the link here and a printed line there. Asserted against the
                // markup rather than the reading text, because a graphical page that spelled its
                // URLs out as well would be the plain page with worse typography.
                await Assert.That(markup).Contains(source.Url);
            }
        }
    }

    [Test]
    public async Task TheGraphicalPageIsReachableWithoutScriptingAndSaysHowToReadItPlainly()
    {
        var html = await RenderAboutAsync();

        await Assert.That(html).DoesNotContain("<script");
        await Assert.That(html).Contains("?plain=1");
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
