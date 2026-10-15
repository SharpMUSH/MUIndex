using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Fixtures;

using Microsoft.Extensions.DependencyInjection;

namespace MUI.Web.Tests;

/// <summary>
/// The six variables SharpMUSH reports beyond the generic set, as the game page and its plain
/// surface say them: the game's own day and week count beside our figure and apart from it, its
/// flags as yes or no, and its portal pages as links only where a line is an address and a name.
/// </summary>
public class SelfReportSurfaceTests
{
    private static readonly DateTimeOffset Now = FixtureGameQueries.Now;

    private const string Slug = "m-u-s-h";

    private static ProvenanceChip Mssp(string value, double ageHours = 1) =>
        new(value, FieldSource.Mssp, Now.AddHours(-ageHours), IsStale: false);

    private static readonly Dictionary<string, ProvenanceChip> Reported = new(StringComparer.Ordinal)
    {
        ["players day"] = Mssp("12"),
        ["players week"] = Mssp("40", ageHours: 3),
        ["logins"] = Mssp("1"),
        ["guests"] = Mssp("0"),
        ["player creation"] = Mssp("wizards only"),
        ["portal application"] = Mssp(
            "https://mush.example.com/apps/chargen Character Application\n"
            + "javascript:alert(1) Evil\n"
            + "https://mush.example.com/apps/bare"),
    };

    private static async Task<GamePage> PageAsync(Dictionary<string, ProvenanceChip>? extra = null)
    {
        var page = (await new FixtureGameQueries().FindAsync(Slug))!;
        var declared = new Dictionary<string, ProvenanceChip>(page.Declared, StringComparer.Ordinal);

        foreach (var (name, chip) in extra ?? Reported)
        {
            declared[name] = chip;
        }

        return page with { Declared = declared };
    }

    private static async Task<string> RenderAsync(GamePage page)
    {
        // After the site's own graph, which registers the fixture as IGameQueries itself.
        await using var site = await SiteHost.StartAsync(overrides: services =>
            services.AddSingleton<IGameQueries>(new WithPage(new FixtureGameQueries(), page)));

        return await site.Client.GetStringAsync($"/g/{Slug}");
    }

    [Test]
    public async Task TheGamesOwnCountIsSaidAsItsOwnAndApartFromTheFigure()
    {
        var html = await RenderAsync(await PageAsync());

        await Assert.That(html).Contains(
            "By the game&#x27;s own count: 12 different players in the last day, 40 in the last week.");

        // In its own paragraph with the declared chip, never in the measured figure.
        var at = html.IndexOf("recent-players", StringComparison.Ordinal);

        await Assert.That(at).IsGreaterThan(0);
        await Assert.That(html.IndexOf("chip declared", at, StringComparison.Ordinal)).IsGreaterThan(at);

        // Said once: a count the sentence carries is not listed again under "Declared by the game".
        await Assert.That(html).DoesNotContain(">players day<");
        await Assert.That(html).DoesNotContain(">players week<");
    }

    [Test]
    public async Task ACountThatIsNotANumberStaysInTheGamesOwnWords()
    {
        var html = await RenderAsync(await PageAsync(new()
        {
            ["players day"] = Mssp("lots"),
            ["players week"] = Mssp("40"),
        }));

        await Assert.That(html).Contains("By the game&#x27;s own count: 40 different players in the last week.");
        await Assert.That(html).Contains(">players day<");
        await Assert.That(html).Contains(">lots<");
    }

    [Test]
    public async Task AFlagReadsYesOrNoAndAnythingElseAsSent()
    {
        var html = await RenderAsync(await PageAsync());

        await Assert.That(html).Contains("<span class=\"value\">yes</span>");
        await Assert.That(html).Contains("<span class=\"value\">no</span>");
        await Assert.That(html).Contains("<span class=\"value\">wizards only</span>");
    }

    [Test]
    public async Task APortalPageIsALinkOnlyWhereItIsAnAddressAndAName()
    {
        var html = await RenderAsync(await PageAsync());

        await Assert.That(html).Contains(
            "<a href=\"https://mush.example.com/apps/chargen\" rel=\"noopener nofollow ugc\">Character Application</a>");

        // Refused as a link, never hidden.
        await Assert.That(html).DoesNotContain("href=\"javascript:");
        await Assert.That(html).Contains("javascript:alert(1) Evil");
        await Assert.That(html).DoesNotContain("href=\"https://mush.example.com/apps/bare\"");
        await Assert.That(html).Contains("https://mush.example.com/apps/bare");
    }

    [Test]
    public async Task ThePlainSurfaceSaysTheSameThings()
    {
        var text = PlainText.Render(await PageAsync(), Now);

        await Assert.That(text).Contains("By the game's own count: 12 different players in the last day,");
        await Assert.That(text).Contains("    https://mush.example.com/apps/chargen Character Application");
        await Assert.That(text).DoesNotContain("players day");

        foreach (var line in text.Split('\n').Where(l => !l.Contains("://", StringComparison.Ordinal)))
        {
            await Assert.That(line.TrimEnd().Length).IsLessThanOrEqualTo(PlainText.Columns);
        }
    }

    /// <summary>The fixture, answering one slug with a page a test built.</summary>
    private sealed class WithPage(FixtureGameQueries inner, GamePage page) : IGameQueries
    {
        public Task<GameListing> SearchAsync(GameFilter filter, CancellationToken cancellationToken = default) =>
            inner.SearchAsync(filter, cancellationToken);

        public Task<IReadOnlyList<GameSummary>> ListAsync(GameFilter filter, CancellationToken cancellationToken = default) =>
            inner.ListAsync(filter, cancellationToken);

        public Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default) =>
            slug == page.Summary.Slug ? Task.FromResult<GamePage?>(page) : inner.FindAsync(slug, cancellationToken);

        public Task<GamePage?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            id == page.Summary.Id ? Task.FromResult<GamePage?>(page) : inner.FindAsync(id, cancellationToken);

        public Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.FindByIdAsync(id, cancellationToken);

        public Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default) =>
            inner.FeedsAsync(cancellationToken);

        public Task<EcosystemDashboard> EcosystemAsync(CancellationToken cancellationToken = default) =>
            inner.EcosystemAsync(cancellationToken);

        public Task<Rankings> RankingsAsync(
            RankingSpan span = RankingSpan.Week,
            CancellationToken cancellationToken = default) =>
            inner.RankingsAsync(span, cancellationToken);

        public Task<IReadOnlyList<RecentGameChange>> RecentFieldChangesAsync(
            int limit,
            int perGameLimit = 3,
            CancellationToken cancellationToken = default) =>
            inner.RecentFieldChangesAsync(limit, perGameLimit, cancellationToken);
    }
}
