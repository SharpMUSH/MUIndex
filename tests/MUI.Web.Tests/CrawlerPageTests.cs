using MUI.Web.Localization;

using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Data;
using MUI.Web.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace MUI.Web.Tests;

/// <summary>
/// The status page's own copy: the fuller breakdown a front-page strip has no room for.
/// </summary>
public class CrawlerPageTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static CrawlCycleRecord Cycle(int considered, int probed, int answered, int failed, int errored, int optedOut) =>
        new(Now.AddSeconds(-11), Now, considered, probed, answered, failed, 0, optedOut, errored, 0, 0, answered, 0, 0, 0);

    private static CrawlerPulse Pulse(CrawlCycleRecord? cycle) => new(Now, Now.AddMinutes(3), 4, 710, cycle);

    /// <summary>Six of the thirteen counters, all named rather than a subset the strip already prints.</summary>
    [Test]
    public async Task TheFullBreakdownNamesAllSixCounters()
    {
        var pulse = Pulse(Cycle(considered: 40, probed: 39, answered: 35, failed: 4, errored: 2, optedOut: 1));

        var line = CrawlerCopy.FullCycle(Locales.SourceTag, pulse);

        await Assert.That(line).IsEqualTo("40 due · 39 probed · 35 answered · 4 failed · 2 errored · 1 opted out");
    }

    /// <summary>An empty cycle reads as the healthiest state, same rule as the strip's three-figure line.</summary>
    [Test]
    public async Task AnEmptyCycleSaysNothingDueRatherThanSixZeroes()
    {
        var pulse = Pulse(Cycle(0, 0, 0, 0, 0, 0));

        await Assert.That(CrawlerCopy.FullCycle(Locales.SourceTag, pulse))
            .IsEqualTo("nothing due this cycle");
    }

    /// <summary>Before any cycle, there is nothing to break down.</summary>
    [Test]
    public async Task NoCycleYetMeansNoBreakdownLine()
    {
        await Assert.That(CrawlerCopy.FullCycle(Locales.SourceTag, Pulse(null))).IsNull();
        await Assert.That(CrawlerCopy.CycleFinishedAt(Locales.SourceTag, Pulse(null))).IsNull();
    }

    /// <summary>The finish time is the site's one absolute format, and the duration reads like a strip's would.</summary>
    [Test]
    public async Task TheFinishLineNamesTheZoneAndTheDuration()
    {
        var cycle = Cycle(1, 1, 1, 0, 0, 0) with
        {
            StartedAt = new DateTimeOffset(2026, 8, 18, 11, 58, 0, TimeSpan.Zero),
            FinishedAt = new DateTimeOffset(2026, 8, 18, 11, 58, 42, TimeSpan.Zero),
        };

        var line = CrawlerCopy.CycleFinishedAt(Locales.SourceTag, Pulse(cycle));

        await Assert.That(line).IsEqualTo("last cycle finished 18 Aug 2026 11:58 UTC · took 0m");
    }

    /// <summary>No fabricated data before the first cycle — same rule as the front-page strip, stated
    /// this time rather than rendering nothing at all.</summary>
    [Test]
    public async Task BeforeAnyCrawlThePageNamesNoCauseAndInventsNothing()
    {
        var html = await RenderAsync(new NoCrawlerPulse());

        await Assert.That(html).Contains(Messages.For(Locales.SourceTag, "crawler.page.empty"));
        await Assert.That(html).DoesNotContain("crawler live");
        await Assert.That(html).DoesNotContain("crawler quiet");
    }

    /// <summary>A working crawler's status page carries the pulse, the full breakdown and the history.</summary>
    [Test]
    public async Task AWorkingCrawlerShowsThePulseTheBreakdownAndTheHistory()
    {
        var pulse = Pulse(Cycle(considered: 40, probed: 39, answered: 35, failed: 4, errored: 2, optedOut: 1));
        var recent = new[]
        {
            Cycle(10, 10, 9, 1, 0, 0),
            Cycle(8, 8, 8, 0, 0, 0),
        };

        var text = Render.Text(await RenderAsync(new StubCrawlerPulse(pulse, recent)));

        await Assert.That(text).Contains("crawler live");
        await Assert.That(text).Contains("710")
            .Because("the registry line carries the same targets-known figure the strip does");
        await Assert.That(text).Contains("40 due · 39 probed · 35 answered · 4 failed · 2 errored · 1 opted out");
        await Assert.That(text).Contains("10 due · 10 probed · 9 answered · 1 failed");
        await Assert.That(text).Contains("8 due · 8 probed · 8 answered");
    }

    /// <summary>The page never claims uptime or describes a game — same vocabulary discipline as the strip.</summary>
    [Test]
    public async Task ThePageNeverSaysUptimeOrDown()
    {
        var pulse = Pulse(Cycle(1, 1, 1, 0, 0, 0));
        var html = await RenderAsync(new StubCrawlerPulse(pulse, [Cycle(1, 1, 1, 0, 0, 0)]));

        foreach (var word in new[] { "uptime", "down", "stopped", "crashed" })
        {
            await Assert.That(html.ToLowerInvariant()).DoesNotContain(word);
        }
    }

    /// <summary>
    /// <c>/crawler</c> answers directly now, rather than 302ing to About.
    /// </summary>
    /// <remarks>
    /// Replaces the old <c>CrawlerContactTests</c>: this is the same address the crawler still
    /// publishes over the wire (spec §11), now a real page instead of a redirect.
    /// </remarks>
    [Test]
    public async Task TheAddressTheCrawlerPublishesAnswersDirectly()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync("/crawler");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);

        var text = Render.Text(await response.Content.ReadAsStringAsync());
        await Assert.That(text).Contains(Messages.For(Locales.SourceTag, "crawler.page.title"));
    }

    /// <summary>The page routes under a locale prefix too, same as every other page.</summary>
    [Test]
    public async Task TheAddressRoutesUnderALocalePrefix()
    {
        await using var site = await SiteHost.StartAsync();

        var response = await site.Client.GetAsync("/de/crawler");

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);
    }

    /// <summary>The status page draws what changed recently and what is queued next, each with its own heading.</summary>
    [Test]
    public async Task ThePageDrawsRecentChangesAndDueTargets()
    {
        var pulse = Pulse(Cycle(1, 1, 1, 0, 0, 0));
        var stub = new StubCrawlerPulse(pulse, [Cycle(1, 1, 1, 0, 0, 0)])
        {
            Due = [new DueTarget("soonest.example", 4201, Now.AddMinutes(-2))],
        };

        var html = await Render.ComponentAsync<Components.Pages.Crawler>(
            new Dictionary<string, object?>(),
            services =>
            {
                services.AddSingleton<ICrawlerPulse>(stub);
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
                services.AddSingleton<IGameQueries>(new StubChangeQueries(
                    new RecentGameChange(
                        Guid.NewGuid(), "m-u-s-h", "M*U*S*H", "CODEBASE", FieldSource.Mssp,
                        "PennMUSH 1.8.7p0", "PennMUSH 1.8.8p0", Now.AddMinutes(-4))));
            });

        var text = Render.Text(html);

        await Assert.That(text).Contains("M*U*S*H");
        await Assert.That(text).Contains("PennMUSH 1.8.7p0");
        await Assert.That(text).Contains("PennMUSH 1.8.8p0");
        await Assert.That(text).Contains("soonest.example:4201");
    }

    /// <summary>Absent, not a fabricated "nothing changed" claim with an implied cause.</summary>
    [Test]
    public async Task WithNothingRecentOrDueThePageSaysSoRatherThanShowingEmptyLists()
    {
        var pulse = Pulse(Cycle(1, 1, 1, 0, 0, 0));

        var html = await Render.ComponentAsync<Components.Pages.Crawler>(
            new Dictionary<string, object?>(),
            services =>
            {
                services.AddSingleton<ICrawlerPulse>(new StubCrawlerPulse(pulse, [Cycle(1, 1, 1, 0, 0, 0)]));
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
                services.AddSingleton<IGameQueries>(new StubChangeQueries());
            });

        await Assert.That(html).Contains(Messages.For(Locales.SourceTag, "crawler.recent.empty"));
        await Assert.That(html).Contains(Messages.For(Locales.SourceTag, "crawler.due.empty"));
    }

    /// <summary>The plain mirror carries the same facts as the rendered page.</summary>
    [Test]
    public async Task ThePlainRenderingCarriesTheSameFacts()
    {
        await using var site = await SiteHost.StartAsync();

        var plain = await site.Client.GetStringAsync("/crawler?plain=1");

        await Assert.That(plain).Contains(Messages.For(Locales.SourceTag, "crawler.page.title"));
        await Assert.That(plain).Contains(Messages.For(Locales.SourceTag, "crawler.page.empty"))
            .Because("the demo site has no crawler, same as the rendered page's empty state");
    }

    private static Task<string> RenderAsync(ICrawlerPulse pulse) =>
        Render.ComponentAsync<Components.Pages.Crawler>(
            new Dictionary<string, object?>(),
            services =>
            {
                services.AddSingleton(pulse);
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
                services.AddSingleton<IGameQueries>(new FixtureGameQueries());
            });

    /// <summary>A fixed pulse and a fixed history, so a test controls both without a database.</summary>
    private sealed class StubCrawlerPulse(CrawlerPulse pulse, IReadOnlyList<CrawlCycleRecord> recent) : ICrawlerPulse
    {
        public IReadOnlyList<DueTarget> Due { get; init; } = [];

        public Task<CrawlerPulse> ReadAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult(pulse);

        public Task<IReadOnlyList<CrawlCycleRecord>> RecentAsync(
            int count,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(recent);

        public Task<IReadOnlyList<DueTarget>> DueSoonAsync(
            DateTimeOffset now,
            int count,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Due);
    }

    /// <summary>Answers only <see cref="RecentFieldChangesAsync"/>, which is all this page asks of it.</summary>
    private sealed class StubChangeQueries(params RecentGameChange[] changes) : IGameQueries
    {
        public Task<IReadOnlyList<RecentGameChange>> RecentFieldChangesAsync(
            int limit, int perGameLimit = 3, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RecentGameChange>>(changes);

        public Task<GameListing> SearchAsync(GameFilter filter, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GameSummary>> ListAsync(GameFilter filter, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GamePage?> FindAsync(string slug, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GamePage?> FindAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LivenessFeeds> FeedsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EcosystemDashboard> EcosystemAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Rankings> RankingsAsync(RankingSpan span = RankingSpan.Week, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
