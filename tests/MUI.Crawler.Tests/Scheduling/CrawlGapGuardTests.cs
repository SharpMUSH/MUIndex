using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawler.Tests.Support;

namespace MUI.Crawler.Tests;

/// <summary>
/// The guard that lets the catalogue say "we stopped watching".
/// </summary>
/// <remarks>
/// <see cref="MUI.Discovery.CrawlGap"/> decides whether a silence was an outage; this is what the
/// decision is worth. Without it, an interval spanning a crawl outage counts the whole span as
/// observed, rendering a hole in our crawl as a measurement of somebody's game.
/// </remarks>
public class CrawlGapGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static (CrawlGapGuard Guard, FakeAvailabilityStore Store) Build(DateTimeOffset? lastCycle)
    {
        var store = new FakeAvailabilityStore();

        return (new CrawlGapGuard(store, new FakeCrawlCycles(lastCycle), new SettableClock(Now)), store);
    }

    private static void Watching(FakeAvailabilityStore store, params Guid[] games)
    {
        foreach (var game in games)
        {
            store.Intervals.Add(new AvailabilityInterval
            {
                GameId = game,
                State = AvailabilityState.Reachable,
                FromAt = Now.AddDays(-30),
                Cause = FailureCause.None,
            });
        }
    }

    [Test]
    public async Task AnOutageEndsEveryOpenIntervalWhereTheCrawlStopped()
    {
        var stopped = Now.AddDays(-15);
        var (guard, store) = Build(stopped);
        Watching(store, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var closed = await guard.CloseAnyGapAsync();

        await Assert.That(closed).IsEqualTo(stopped);
        await Assert.That(store.Intervals.All(i => i.ToAt == stopped)).IsTrue();
    }

    [Test]
    public async Task ARestartLeavesTheCatalogueAlone()
    {
        // The common case by far, and the one that must cost nothing: a routine restart, not an outage.
        var (guard, store) = Build(Now.AddMinutes(-2));
        Watching(store, Guid.NewGuid(), Guid.NewGuid());

        var closed = await guard.CloseAnyGapAsync();

        await Assert.That(closed).IsNull();
        await Assert.That(store.Intervals.All(i => i.IsOpen)).IsTrue();
    }

    [Test]
    public async Task ADeploymentThatHasNeverCrawledIsNotAnOutage()
    {
        var (guard, store) = Build(null);
        Watching(store, Guid.NewGuid());

        await Assert.That(await guard.CloseAnyGapAsync()).IsNull();
        await Assert.That(store.Intervals.Single().IsOpen).IsTrue();
    }

    [Test]
    public async Task WithoutTheCycleTableThereIsNothingToCompareAgainst()
    {
        // Not knowing when we stopped is a reason to write nothing, never a reason to guess.
        var store = new FakeAvailabilityStore();
        var guard = new CrawlGapGuard(store, cycles: null, new SettableClock(Now));
        Watching(store, Guid.NewGuid());

        await Assert.That(await guard.CloseAnyGapAsync()).IsNull();
        await Assert.That(store.Intervals.Single().IsOpen).IsTrue();
    }
}

/// <summary>The crawl-cycle table reduced to the one fact the guard asks it for.</summary>
public sealed class FakeCrawlCycles(DateTimeOffset? lastFinishedAt) : ICrawlCycles
{
    public Task RecordAsync(CrawlCycleRecord cycle, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<CrawlerPulse> PulseAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        Task.FromResult(lastFinishedAt is { } at
            ? new CrawlerPulse(at, null, 0, 0, new CrawlCycleRecord(at, at, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0))
            : CrawlerPulse.Unknown);

    public Task<int> SweepAsync(DateTimeOffset before, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task<IReadOnlyList<CrawlCycleRecord>> RecentAsync(
        int count,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CrawlCycleRecord>>([]);

    public Task<CrawlWindow> WindowAsync(
        DateTimeOffset now,
        TimeSpan span,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(CrawlWindow.Empty(span));

    public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
