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

    /// <summary>
    /// The English a message id carries, which is what this page is asserted against.
    /// </summary>
    /// <remarks>
    /// The copy lives in <see cref="Messages"/> now, so a sentence pasted into a test would be a
    /// third copy of it — and the one nothing checks. Asking the bundle asserts the fact this page
    /// makes a claim about ("the archive-grace limitation is stated") rather than the spelling of
    /// it, and still fails if the id stops reaching the page. Where a claim is a <em>rule</em>
    /// rather than a sentence — the refusal to say "no automated opt-out" — the literal stays,
    /// because there the wording is the rule. A rule about the site's <em>vocabulary</em> is the one
    /// case where a literal on this page is wrong: it belongs over the whole bundle in every locale,
    /// which is where <see cref="NoMessageOutsideTheseTwoRefusalsUsesTheWordUptime"/> puts it.
    /// </remarks>
    private static string Says(string id) => Messages.For(Locales.SourceTag, id);

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
        await Assert.That(Plain).Contains(Says("about.source.withheld"));

        // And the badge for one we did read is a different string, not a negation of this one: a
        // reader meets the difference between "we chose not to" and "we could not" only here.
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
        await Assert.That(Render.Words(Plain)).Contains(Says("about.identity.placeholder.plain"));

        var configured = PlainText.RenderAbout(AboutPage.Build(
            new ProbeOptions { InfoUrl = "https://example.test/crawler" }, new DatasetLicenceOptions()));

        await Assert.That(Render.Words(configured))
            .DoesNotContain(Says("about.identity.placeholder.plain"));
    }

    [Test]
    public async Task TheOptOutIsPublishedWithTheExactWordsThatWork()
    {
        // The page used to say there was no automated opt-out, which was true and was the honest
        // thing to print. Now that one exists, the page has to name the two spellings an operator
        // would type — and name them off the reader that consumes them, so an edit on either side
        // cannot leave this page advertising a switch wired to something else.
        var text = Render.Words(Plain);

        await Assert.That(text).DoesNotContain("no automated opt-out");
        await Assert.That(text).Contains(OptOutVocabulary.MsspVariable);
        await Assert.That(text).Contains(OptOutVocabulary.DnsLabel);
        await Assert.That(text).Contains(OptOutVocabulary.DnsValue);

        // All three of §11's routes, named as routes rather than implied.
        await Assert.That(text).Contains("MSSP report");
        await Assert.That(text).Contains("TXT record");
        await Assert.That(text).Contains("write to a person");

        // Honoured within one cycle, and the way back out is on the page — an opt-out whose exit is
        // undocumented is a trap.
        await Assert.That(text).Contains("within one crawl cycle");
        await Assert.That(text).Contains("undo without asking us");
    }

    [Test]
    public async Task ThePageSaysWhatAnOptedOutGamesPageStillShows()
    {
        // Nothing is ever deleted, and the third state of an hour names no cause. A reader who opted
        // out has to be able to find out what happens to what we already measured, and the answer is
        // "it stays, and nothing new arrives".
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

        await Assert.That(text).Contains(Says("about.measures.declared.lead"));
        await Assert.That(text).Contains("MSSP PLAYERS field");
        await Assert.That(text).Contains("WHO or DOING read at the connect screen");
        await Assert.That(text).Contains("unknown, never zero");
    }

    /// <summary>
    /// The page explains what reachability is, in whatever language it is being read in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This used to count the English word "uptime" and require exactly two of it.</b> That was a
    /// reasonable guard while the page was a C# string and an unreasonable one the moment it became
    /// a translation: a Japanese or Chinese rendering of the same two refusals contains the token
    /// zero times and would fail, and a German one contains it twice by a coincidence of loanwords
    /// rather than because the rule held. Worse, the assertion made the *presence* of the forbidden
    /// word the thing under test, so the page's vocabulary rule was guarded by requiring the
    /// vocabulary to be broken.
    /// </para>
    /// <para>
    /// The rule survives, split from the spelling. Here: the two refusal ids reach the page, asked
    /// of the bundle, so this holds in every locale. And in
    /// <see cref="NoMessageOutsideTheseTwoRefusalsUsesTheWordUptime"/>: the word appears in no other
    /// message, in no locale — which is the rule itself ("reachable, never uptime", in copy),
    /// checked over the whole bundle rather than inferred from one page's word count.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ReachableIsExplainedInWhateverLanguageThePageIsRead()
    {
        var text = Render.Words(Plain);

        await Assert.That(text).Contains(Says("about.measures.reachable.lead"));
        await Assert.That(text).Contains(Says("about.measures.reachable.body"));

        // The substance of the refusal, which is what the rule is for: the measurement is of our
        // socket from our host, and an unreachable game may be perfectly alive.
        await Assert.That(text).Contains("unreachable and perfectly alive");
        await Assert.That(text).Contains("nothing here measured it");
    }

    /// <summary>
    /// "Reachable, never uptime" — over the bundle, in every locale, rather than over one page.
    /// </summary>
    /// <remarks>
    /// The two about-page ids are the whole exemption: they are the sentences that name the word in
    /// order to refuse it, and they are the only place on the site allowed to. Every other id is
    /// checked in every bundle a reader can be served, so a translator who reaches for the loanword
    /// in a reachability string fails this rather than shipping it — which a rendered-English word
    /// count could never have caught.
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

        // And the exemption is real rather than vacuous: the English refusals do use the word, which
        // is what makes them refusals and not silence.
        await Assert.That(Says("about.measures.reachable.lead").ToLowerInvariant()).Contains("uptime");
    }

    [Test]
    public async Task TheThingsThisSiteWillNotDoAreStatedRatherThanImplied()
    {
        // The one page where these words are allowed to appear, because it is the page that says
        // they are absent. PlainParityTests asserts the opposite about every other surface.
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

    /// <summary>
    /// Every sentence on this page comes out of the bundle, and the machine voice does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pseudolocale accents and brackets every string that reached a reader through
    /// <see cref="Messages"/>, so anything still legible as English here is a sentence typed into a
    /// page — which is what the whole of this page was until it was moved. This was the largest
    /// untranslated surface on the site, and the one it would be worst to leave: it is where the
    /// site explains what a measurement here proves, to a reader who by definition does not yet
    /// trust it.
    /// </para>
    /// <para>
    /// The directories' names and addresses go the other way. They are somebody else's name and
    /// somebody else's URL, they are the credit §7.6 owes, and translating either would destroy the
    /// acknowledgement rather than localize it — so they are asserted to be <em>unchanged</em>.
    /// </para>
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

        // The crawler's own name and the licence a deployment configured are machine voice too, and
        // are read off the objects that own them rather than out of the bundle.
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
    /// It used to assert the offer of the text mirror here too. That offer is in the bar now — it
    /// was the one thing in each page's footer worth keeping, and eleven pages each carried their
    /// own copy of it. <see cref="ReadingControlsTests"/> holds the claim, over every route rather
    /// than over this one.
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

        // Whether a catalogue is configured, which the page's preview metadata switches on: over a
        // fixture the description says so, because no unfurler renders the banner that says it in
        // the body. Not measured, matching the rest of this harness.
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
