using MUI.Catalog;
using MUI.Catalog.Persistence;
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
        var pulse = new StoredCrawlerPulse(store);

        await Assert.That(await pulse.RecentAsync(10)).IsEquivalentTo(store.Recent);
    }

    /// <summary>A missing table or a failed query is "we cannot say", never a 500 on the status page.</summary>
    [Test]
    public async Task RecentAsyncFallsBackToEmptyRatherThanThrowing()
    {
        var store = new FakeCrawlCycles { Throws = true };
        var pulse = new StoredCrawlerPulse(store);

        await Assert.That(await pulse.RecentAsync(10)).IsEmpty();
    }

    /// <summary>The demo path has no crawler and no invented history.</summary>
    [Test]
    public async Task NoCrawlerPulseHasNoHistoryEither()
    {
        await Assert.That(await new NoCrawlerPulse().RecentAsync(10)).IsEmpty();
    }

    private sealed class FakeCrawlCycles : ICrawlCycles
    {
        public IReadOnlyList<CrawlCycleRecord> Recent { get; set; } = [];

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

        public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
