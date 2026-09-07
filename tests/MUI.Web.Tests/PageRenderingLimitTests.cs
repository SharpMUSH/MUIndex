using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MUI.Catalog;
using MUI.Web.Fixtures;

namespace MUI.Web.Tests;

public class PageRenderingLimitTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BusyPagesRejectWithoutQueueingAndLeaveHealthAndRobotsAvailable(bool failFirst)
    {
        var queries = new BlockFirstListing(failFirst);
        await using var site = await SiteHost.StartAsync(
            settings: new() { ["PageRendering:ConcurrencyLimit"] = "1" },
            overrides: services => services.AddSingleton<IGameQueries>(queries));

        var first = site.Client.GetAsync("/games");
        try
        {
            await queries.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var rejected = await site.Client.GetAsync("/de/games?genre=Fantasy")
                .WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(rejected.Headers.RetryAfter?.Delta).IsEqualTo(TimeSpan.FromSeconds(1));
            await Assert.That(rejected.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(queries.Calls).IsEqualTo(1);

            using var missing = await site.Client.GetAsync("/de/this-page-does-not-exist");
            await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);

            using var health = await site.Client.GetAsync("/health");
            await Assert.That(health.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var robots = await site.Client.GetAsync("/robots.txt");
            await Assert.That(robots.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        finally
        {
            queries.Release.TrySetResult();
            using var completed = await first;
        }

        using var recovered = await site.Client.GetAsync("/games?genre=Fantasy");
        await Assert.That(recovered.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(queries.Calls).IsEqualTo(2);
    }

    private sealed class BlockFirstListing(bool failFirst) : IGameQueries
    {
        private readonly FixtureGameQueries _fixture = new();
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GameListing> SearchAsync(GameFilter filter, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                if (failFirst)
                {
                    throw new InvalidOperationException("A failed render must release its permit.");
                }
            }

            return GameListing.Empty;
        }

        public Task<IReadOnlyList<GameSummary>> ListAsync(GameFilter filter, CancellationToken cancellationToken = default) =>
            _fixture.ListAsync(filter, cancellationToken);
        public Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default) =>
            _fixture.FindAsync(slug, cancellationToken);
        public Task<GamePage?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            _fixture.FindAsync(id, cancellationToken);
        public Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            _fixture.FindByIdAsync(id, cancellationToken);
        public Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default) =>
            _fixture.FeedsAsync(cancellationToken);
        public Task<EcosystemDashboard> EcosystemAsync(CancellationToken cancellationToken = default) =>
            _fixture.EcosystemAsync(cancellationToken);
        public Task<Rankings> RankingsAsync(RankingSpan span = RankingSpan.Week, CancellationToken cancellationToken = default) =>
            _fixture.RankingsAsync(span, cancellationToken);
        public Task<IReadOnlyList<RecentGameChange>> RecentFieldChangesAsync(int limit, int perGameLimit = 3, CancellationToken cancellationToken = default) =>
            _fixture.RecentFieldChangesAsync(limit, perGameLimit, cancellationToken);
    }
}
