using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Discovery;
using MUI.Web.Data;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

/// <summary>
/// The crawler status page's history query, and the same unavailable-means-empty rule
/// <see cref="StoredCrawlerPulse.ReadAsync"/> already keeps for the pulse.
/// </summary>
/// <remarks>
/// Against a fake <see cref="ICrawlCycles"/> rather than Postgres — the row mapping is
/// <see cref="CrawlCyclePostgresTests"/>'s job; this is only the fallback behaviour a page must be
/// able to trust without a database in reach.
/// </remarks>
public class StoredCrawlerPulseTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static CrawlCycleRecord Cycle(int considered) =>
        new(Now.AddMinutes(-1), Now, considered, considered, considered, 0, 0, 0, 0, 0, 0, considered, 0, 0, 0);

    [Test]
    public async Task RecentAsyncPassesThroughWhatTheStoreHolds()
    {
        var store = new FakeCrawlCycles { Recent = [Cycle(4), Cycle(3)] };
        var pulse = new StoredCrawlerPulse(store, new FakeCrawlTargets());

        await Assert.That(await pulse.RecentAsync(10)).IsEquivalentTo(store.Recent);
    }

    /// <summary>A missing table or a failed query is "we cannot say", never a 500 on the status page.</summary>
    [Test]
    public async Task RecentAsyncFallsBackToEmptyRatherThanThrowing()
    {
        var store = new FakeCrawlCycles { Throws = true };
        var pulse = new StoredCrawlerPulse(store, new FakeCrawlTargets());

        await Assert.That(await pulse.RecentAsync(10)).IsEmpty();
    }

    /// <summary>The demo path has no crawler and no invented history.</summary>
    [Test]
    public async Task NoCrawlerPulseHasNoHistoryEither()
    {
        await Assert.That(await new NoCrawlerPulse().RecentAsync(10)).IsEmpty();
    }

    [Test]
    public async Task DueSoonAsyncPassesThroughTheRegistrysOwnAddresses()
    {
        var due = new[]
        {
            new CrawlTarget
            {
                Id = Guid.NewGuid(), Host = "soonest.example", Port = 4201,
                NextProbeAt = Now.AddMinutes(-2), FirstSeenAt = Now.AddDays(-1),
            },
        };
        var targets = new FakeCrawlTargets { Due = due };
        var pulse = new StoredCrawlerPulse(new FakeCrawlCycles(), targets);

        var soon = await pulse.DueSoonAsync(Now, 10);

        await Assert.That(soon).IsEquivalentTo(new[] { new DueTarget("soonest.example", 4201, Now.AddMinutes(-2)) });
    }

    /// <summary>Same fallback rule as the cycle history: a failed read is "we cannot say", not a 500.</summary>
    [Test]
    public async Task DueSoonAsyncFallsBackToEmptyRatherThanThrowing()
    {
        var pulse = new StoredCrawlerPulse(new FakeCrawlCycles(), new FakeCrawlTargets { Throws = true });

        await Assert.That(await pulse.DueSoonAsync(Now, 10)).IsEmpty();
    }

    /// <summary>
    /// A failed window read is an empty window, on the same terms as the history and the queue.
    /// </summary>
    /// <remarks>
    /// The figure is the page's headline, and a headline that throws is a 500 on the page whose whole
    /// job is saying whether the instrument is working. Empty is also honest: no cycle was read.
    /// </remarks>
    [Test]
    public async Task WindowAsyncFallsBackToAnEmptyWindowRatherThanThrowing()
    {
        var pulse = new StoredCrawlerPulse(new FakeCrawlCycles { Throws = true }, new FakeCrawlTargets());

        var window = await pulse.WindowAsync(Now, TimeSpan.FromHours(24));

        await Assert.That(window.IsEmpty).IsTrue();
        await Assert.That(window.Span).IsEqualTo(TimeSpan.FromHours(24));
    }

    /// <summary>The demo path has no crawler and no invented window either.</summary>
    [Test]
    public async Task NoCrawlerPulseHasNoWindowEither()
    {
        var window = await new NoCrawlerPulse().WindowAsync(Now, TimeSpan.FromHours(24));

        await Assert.That(window.IsEmpty).IsTrue();
    }

    /// <summary>The demo path has no crawler and no invented queue either.</summary>
    [Test]
    public async Task NoCrawlerPulseHasNoDueTargetsEither()
    {
        await Assert.That(await new NoCrawlerPulse().DueSoonAsync(Now, 10)).IsEmpty();
    }

    private sealed class FakeCrawlCycles : ICrawlCycles
    {
        public IReadOnlyList<CrawlCycleRecord> Recent { get; set; } = [];

        public CrawlWindow? Window { get; set; }

        public bool Throws { get; set; }

        public Task RecordAsync(CrawlCycleRecord cycle, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CrawlerPulse> PulseAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> SweepAsync(DateTimeOffset before, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CrawlCycleRecord>> RecentAsync(
            int count,
            CancellationToken cancellationToken = default) =>
            Throws ? throw new InvalidOperationException("no database in this test") : Task.FromResult(Recent);

        public Task<CrawlWindow> WindowAsync(
            DateTimeOffset now,
            TimeSpan span,
            CancellationToken cancellationToken = default) =>
            Throws
                ? throw new InvalidOperationException("no database in this test")
                : Task.FromResult(Window ?? CrawlWindow.Empty(span));

        public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class FakeCrawlTargets : ICrawlTargetRepository
    {
        public IReadOnlyList<CrawlTarget> Due { get; set; } = [];

        public bool Throws { get; set; }

        public Task<CrawlTarget?> ByAddressAsync(string host, int port, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Guid> AddAsync(CrawlTarget target, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CrawlTarget>> DueAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
            Throws ? throw new InvalidOperationException("no database in this test") : Task.FromResult(Due);

        public Task RecordAttemptAsync(
            Guid id, DateTimeOffset at, bool succeeded, TimeSpan? crawlDelay,
            DateTimeOffset nextProbeAt, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task AttachGameAsync(Guid id, Guid gameId, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
