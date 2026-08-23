using MUI.Crawl;
using MUI.Web.Components;

namespace MUI.Web.Tests.Data;

/// <summary>
/// The /about section crediting the sources this site reads on a standing basis.
/// </summary>
/// <remarks>
/// §7.6's etiquette clause says to credit what we read. PR #134 removed the previous Sources
/// section because it duplicated <c>docs/import-sources.md</c> and surfaced an unsettled licence
/// question — and then that doc was deleted too, leaving nothing anywhere. This section is narrower
/// than the one it replaces on purpose: what we are reading now, not a list of directories a
/// one-time backfill once took addresses from and that this tree can no longer even fetch.
/// </remarks>
public class AboutReadingSectionTests
{
    private static IReadOnlyList<AboutFeed> Credited() =>
        [.. AboutPage.Build(new ProbeOptions()).Sections.SelectMany(s => s.Feeds)];

    [Test]
    public async Task TheAboutPageCreditsEverySourceWeStandinglyRead()
    {
        var names = Credited().Select(f => f.Name).ToList();

        await Assert.That(names).Contains("AresCentral");
        await Assert.That(names).Contains("Intermud-3");
    }

    /// <summary>
    /// The backfill directories are not read and are not credited as though they were. A credit for
    /// something we stopped doing is a claim about the present that is not true, on a page whose
    /// whole subject is not doing that.
    /// </summary>
    [Test]
    public async Task TheBackfillDirectoriesAreNotListedAsSourcesWeRead()
    {
        var names = Credited().Select(f => f.Name).ToList();

        await Assert.That(names).DoesNotContain("MudStats");
        await Assert.That(names).DoesNotContain("The Mud Connector");
        await Assert.That(names).DoesNotContain("MudVerse");
    }

    /// <summary>Every credit is a name, a reachable address, and a sentence saying what it gives us.</summary>
    [Test]
    public async Task EveryCreditNamesItsSourceAndSaysWhatItGivesUs()
    {
        var feeds = Credited();

        await Assert.That(feeds).IsNotEmpty();

        foreach (var feed in feeds)
        {
            await Assert.That(string.IsNullOrWhiteSpace(feed.Name)).IsFalse();
            await Assert.That(string.IsNullOrWhiteSpace(feed.Note)).IsFalse();
            await Assert.That(Uri.TryCreate(feed.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                .IsTrue()
                .Because($"{feed.Name} is credited with a link that is not an https address");
        }
    }

    /// <summary>
    /// The section is in the page rather than only reachable through a helper, so a refactor that
    /// stops rendering it fails here.
    /// </summary>
    [Test]
    public async Task TheSectionIsPartOfThePage()
    {
        var ids = AboutPage.Build(new ProbeOptions()).Sections.Select(s => s.Id).ToList();

        await Assert.That(ids).Contains("reading");
    }
}
