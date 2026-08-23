using MUI.Web.Localization;

using Microsoft.AspNetCore.Http;

using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Components.Layout;
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

    /// <summary>The page draws went-dark and came-back too, the two liveness feeds Home no longer carries.</summary>
    [Test]
    public async Task ThePageDrawsWentDarkAndCameBack()
    {
        var pulse = Pulse(Cycle(1, 1, 1, 0, 0, 0));
        var dark = new FeedEntry(Guid.NewGuid(), "verdigris", "Verdigris", Now.AddHours(-3), "connection refused");
        var back = new FeedEntry(Guid.NewGuid(), "aardwolf", "Aardwolf MUD", Now.AddMinutes(-40), string.Empty);

        var html = await Render.ComponentAsync<Components.Pages.Crawler>(
            new Dictionary<string, object?>(),
            services =>
            {
                services.AddSingleton<ICrawlerPulse>(new StubCrawlerPulse(pulse, [Cycle(1, 1, 1, 0, 0, 0)]));
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
                services.AddSingleton<IGameQueries>(new StubChangeQueries
                {
                    Feeds = new LivenessFeeds([], [dark], [back]),
                });
            });

        var text = Render.Text(html);

        await Assert.That(text).Contains(Messages.For(Locales.SourceTag, "feed.wentDark"));
        await Assert.That(text).Contains(Messages.For(Locales.SourceTag, "feed.cameBack"));
        await Assert.That(text).Contains("Verdigris");
        await Assert.That(text).Contains("connection refused");
        await Assert.That(text).Contains("Aardwolf MUD");
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
        await Assert.That(html).Contains(Messages.For(Locales.SourceTag, "feed.nothingDark"));
        await Assert.That(html).Contains(Messages.For(Locales.SourceTag, "feed.nothingBack"));
    }

    /// <summary>A failed catalogue query costs the page one section, never the whole render.</summary>
    [Test]
    public async Task AFailedQueryCostsThePageOneSectionRatherThanFailingTheWholeRender()
    {
        var pulse = Pulse(Cycle(1, 1, 1, 0, 0, 0));

        var html = await Render.ComponentAsync<Components.Pages.Crawler>(
            new Dictionary<string, object?>(),
            services =>
            {
                services.AddSingleton<ICrawlerPulse>(new StubCrawlerPulse(pulse, [Cycle(1, 1, 1, 0, 0, 0)])
                {
                    Due = [new DueTarget("soonest.example", 4201, Now.AddMinutes(-2))],
                });
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
                services.AddSingleton<IGameQueries>(new StubChangeQueries { ThrowOnRecentFieldChanges = true, ThrowOnFeeds = true });
            });

        // The two sections a failed IGameQueries costs the page read as "nothing found", the same
        // words an empty-but-successful answer uses — not an error, since our own query failing is not
        // a fact about the games (rule 5).
        await Assert.That(html).Contains(Messages.For(Locales.SourceTag, "crawler.recent.empty"));
        await Assert.That(html).Contains(Messages.For(Locales.SourceTag, "feed.nothingDark"));

        // What the crawler pulse itself answered survives a catalogue query failing beside it.
        await Assert.That(html).Contains("soonest.example:4201");
    }

    /// <summary>The nav offers the crawler page directly, where went dark/came back moved to.</summary>
    [Test]
    public async Task TheNavOffersTheCrawlerPageAlongsideTheOtherBrowseDestinations()
    {
        var markup = await Render.ComponentAsync<MainLayout>(new Dictionary<string, object?>(), services =>
        {
            services.AddSingleton(new CatalogueSource(IsMeasured: true));
            services.AddCascadingValue(_ =>
            {
                var context = new DefaultHttpContext();

                context.Request.Path = "/games";

                return (HttpContext)context;
            });
        });

        await Assert.That(markup).Contains("href=\"/crawler\"");
        await Assert.That(Render.Words(markup)).Contains(Messages.For(Locales.SourceTag, "nav.crawler"));
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

    /// <summary>
    /// The day's throughput is on the page, and the ten-row history is labelled as a sample of it.
    /// </summary>
    /// <remarks>
    /// The history is ten rows whatever the loop did. Without the window figure beside it, a crawler
    /// that ran fourteen hundred cycles today and one that ran ten and stopped render identically —
    /// which is the whole reason the tallest block on this page was also the least informative.
    /// </remarks>
    [Test]
    public async Task TheDayFigureSaysWhatTheTenRowHistoryCannot()
    {
        var pulse = Pulse(Cycle(1, 1, 1, 0, 0, 0));

        var html = await Render.ComponentAsync<Components.Pages.Crawler>(
            new Dictionary<string, object?>(),
            services =>
            {
                services.AddSingleton<ICrawlerPulse>(new StubCrawlerPulse(pulse, [Cycle(1, 1, 1, 0, 0, 0)])
                {
                    Window = new CrawlWindow(
                        TimeSpan.FromHours(24), Cycles: 1412, Considered: 1500, Probed: 1490,
                        Answered: 1361, Failed: 129, Errored: 0, OptedOut: 0),
                });
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
                services.AddSingleton<IGameQueries>(new StubChangeQueries());
            });

        var text = Render.Text(html);

        await Assert.That(text).Contains("1,490").Because("the figure is what the loop probed in the window");
        await Assert.That(text).Contains("1,361 answered");
        await Assert.That(text).Contains("129 failed");
        await Assert.That(text).Contains("1,412 cycles");
    }

    /// <summary>
    /// A day with no cycle in it says so, and does not say why.
    /// </summary>
    /// <remarks>
    /// Rule 5 pointed inward, the same discipline the pulse's "quiet" already keeps: an empty window
    /// is consistent with a crashed host, a lease held by a replica, and a registry with nothing due,
    /// and this page may not pick one.
    /// </remarks>
    [Test]
    public async Task AnEmptyWindowNamesNoCause()
    {
        var pulse = Pulse(Cycle(1, 1, 1, 0, 0, 0));

        var html = await Render.ComponentAsync<Components.Pages.Crawler>(
            new Dictionary<string, object?>(),
            services =>
            {
                services.AddSingleton<ICrawlerPulse>(new StubCrawlerPulse(pulse, []));
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
                services.AddSingleton<IGameQueries>(new StubChangeQueries());
            });

        var words = Render.Words(html).ToLowerInvariant();

        await Assert.That(words).Contains("no cycle finished in the last 24h");

        foreach (var forbidden in new[] { "down", "stopped", "stalled", "crashed", "uptime", "offline" })
        {
            await Assert.That(words).DoesNotContain(forbidden)
                .Because($"an empty window may not be given the cause '{forbidden}'");
        }
    }

    /// <summary>
    /// A window that could not be read is left off the page, not printed as a day of zeroes.
    /// </summary>
    /// <remarks>
    /// The failure mode this guards is specific and loud: <c>PROBED IN 24H / 0</c> at figure size,
    /// under a pulse that on an unreachable database already reads "quiet", with "no cycle finished
    /// in the last 24h" under the history — three statements the page never measured, agreeing with
    /// each other that the crawler is dead. The sibling sections can fall back to "nothing found"
    /// because an empty list renders as no rows; a window of zeroes renders as a claim.
    /// </remarks>
    [Test]
    public async Task AWindowThatCouldNotBeReadIsOmittedRatherThanPrintedAsZero()
    {
        var pulse = Pulse(Cycle(1, 1, 1, 0, 0, 0));

        var html = await Render.ComponentAsync<Components.Pages.Crawler>(
            new Dictionary<string, object?>(),
            services =>
            {
                services.AddSingleton<ICrawlerPulse>(new StubCrawlerPulse(pulse, [Cycle(1, 1, 1, 0, 0, 0)])
                {
                    WindowUnavailable = true,
                });
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
                services.AddSingleton<IGameQueries>(new StubChangeQueries());
            });

        var words = Render.Words(html).ToLowerInvariant();

        await Assert.That(words).DoesNotContain("probed in 24h")
            .Because("the tile is the claim, and there is nothing to claim");
        await Assert.That(words).DoesNotContain("no cycle finished in the last 24h")
            .Because("that sentence is a measurement, and no measurement was read");

        // What the pulse itself answered is unaffected — one unavailable figure costs one figure.
        await Assert.That(words).Contains("addresses in the registry");
        await Assert.That(html).Contains(Messages.For(Locales.SourceTag, "crawler.history.title"));
    }

    /// <summary>
    /// All three liveness registers, on the page about the thing that writes them.
    /// </summary>
    /// <remarks>
    /// Newly discovered used to be left on the front page and this page's lede apologised for its
    /// absence. Found, lost and returned are one question asked three ways.
    /// </remarks>
    [Test]
    public async Task ThePageCarriesAllThreeLivenessRegisters()
    {
        var pulse = Pulse(Cycle(1, 1, 1, 0, 0, 0));
        var found = new FeedEntry(Guid.NewGuid(), "brand-new", "Brand New MUSH", Now.AddHours(-1), string.Empty);
        var dark = new FeedEntry(Guid.NewGuid(), "verdigris", "Verdigris", Now.AddHours(-3), "connection refused");
        var back = new FeedEntry(Guid.NewGuid(), "aardwolf", "Aardwolf MUD", Now.AddMinutes(-40), string.Empty);

        var html = await Render.ComponentAsync<Components.Pages.Crawler>(
            new Dictionary<string, object?>(),
            services =>
            {
                services.AddSingleton<ICrawlerPulse>(new StubCrawlerPulse(pulse, [Cycle(1, 1, 1, 0, 0, 0)]));
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
                services.AddSingleton<IGameQueries>(new StubChangeQueries
                {
                    Feeds = new LivenessFeeds([found], [dark], [back]),
                });
            });

        var text = Render.Text(html);

        await Assert.That(text).Contains(Messages.For(Locales.SourceTag, "feed.newlyDiscovered"));
        await Assert.That(text).Contains("Brand New MUSH");
        await Assert.That(text).Contains("Verdigris");
        await Assert.That(text).Contains("Aardwolf MUD");
    }

    /// <summary>
    /// The bands are grids, and only the intro keeps the reading measure.
    /// </summary>
    /// <remarks>
    /// The whole page was inside <c>.prose</c>, whose 62ch is a measure for paragraphs — which put
    /// three feed columns, a four-column table and two dated lists into a 500px ribbon with two
    /// thirds of the window empty beside it, and collapsed <c>.feeds</c>' own auto-fit to one column.
    /// </remarks>
    [Test]
    public async Task OnlyTheIntroKeepsTheReadingMeasure()
    {
        var pulse = Pulse(Cycle(1, 1, 1, 0, 0, 0));

        var html = await Render.ComponentAsync<Components.Pages.Crawler>(
            new Dictionary<string, object?>(),
            services =>
            {
                services.AddSingleton<ICrawlerPulse>(new StubCrawlerPulse(pulse, [Cycle(1, 1, 1, 0, 0, 0)]));
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
                services.AddSingleton<IGameQueries>(new StubChangeQueries(
                    new RecentGameChange(
                        Guid.NewGuid(), "m-u-s-h", "M*U*S*H", "CODEBASE", FieldSource.Mssp,
                        "PennMUSH 1.8.7p0", "PennMUSH 1.8.8p0", Now.AddMinutes(-4))));
            });

        await Assert.That(html).Contains("class=\"status-page\"");
        await Assert.That(html).Contains("class=\"feeds\"").Because("the three registers sit side by side");
        await Assert.That(html).Contains("<table class=\"transitions\"")
            .Because("four facts per row, the same four every row, is a table");

        var prose = html.IndexOf("class=\"prose\"", StringComparison.Ordinal);
        var feeds = html.IndexOf("class=\"feeds\"", StringComparison.Ordinal);

        await Assert.That(prose).IsGreaterThan(-1);
        await Assert.That(feeds).IsGreaterThan(prose)
            .Because("the measure wraps the intro only, and the bands come after it");
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

        public CrawlWindow? Window { get; init; }

        /// <summary>The read failed, which is not the same answer as a window holding nothing.</summary>
        public bool WindowUnavailable { get; init; }

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

        public Task<CrawlWindow?> WindowAsync(
            DateTimeOffset now,
            TimeSpan span,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(WindowUnavailable ? null : Window ?? CrawlWindow.Empty(span));
    }

    /// <summary>Answers only <see cref="RecentFieldChangesAsync"/> and <see cref="FeedsAsync"/>, which is all this page asks of it.</summary>
    private sealed class StubChangeQueries(params RecentGameChange[] changes) : IGameQueries
    {
        public LivenessFeeds Feeds { get; init; } = new([], [], []);

        public bool ThrowOnRecentFieldChanges { get; init; }

        public bool ThrowOnFeeds { get; init; }

        public Task<IReadOnlyList<RecentGameChange>> RecentFieldChangesAsync(
            int limit, int perGameLimit = 3, CancellationToken cancellationToken = default) =>
            ThrowOnRecentFieldChanges
                ? throw new InvalidOperationException("simulated database failure")
                : Task.FromResult<IReadOnlyList<RecentGameChange>>(changes);

        public Task<LivenessFeeds> FeedsAsync(CancellationToken ct = default) =>
            ThrowOnFeeds
                ? throw new InvalidOperationException("simulated database failure")
                : Task.FromResult(Feeds);

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

        public Task<EcosystemDashboard> EcosystemAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Rankings> RankingsAsync(RankingSpan span = RankingSpan.Week, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
