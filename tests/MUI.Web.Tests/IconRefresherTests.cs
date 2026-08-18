using Microsoft.Extensions.Logging;

using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Discovery;
using MUI.Web.Icons;

namespace MUI.Web.Tests;

/// <summary>
/// The loop behind the icon cache, and the one property that matters about it: it outlives its
/// failures.
/// </summary>
/// <remarks>
/// .NET's default <c>BackgroundServiceExceptionBehavior</c> is <c>StopHost</c>, so an exception
/// escaping <c>ExecuteAsync</c> here stops the whole site, not just an icon fetch. It happened: a
/// stalled web server made <c>HttpClient</c> raise its own timeout as a
/// <see cref="TaskCanceledException"/>, and a catch filter reading "everything except a cancellation"
/// let it through, assuming a cancellation meant the host was stopping. Each test here races the
/// loop's own warning against the service's task, so the bug fails the assertion rather than a slow
/// timeout.
/// </remarks>
public class IconRefresherTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A pass that fails with a cancellation nobody asked for is a pass skipped, not a host stopped.
    /// </summary>
    [Test]
    public async Task APassThatFailsWithATimeoutLeavesTheServiceRunning() =>
        // The exact exception HttpClient raises when its own Timeout elapses.
        await AssertSurvives(new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing.",
            new TimeoutException()));

    /// <summary>An ordinary failure is still handled the same way — the broad catch isn't narrowed by the fix.</summary>
    [Test]
    public async Task APassThatFailsAtAllLeavesTheServiceRunning() =>
        await AssertSurvives(new InvalidOperationException("the database said no"));

    private static async Task AssertSurvives(Exception failure)
    {
        var logger = new Spy();
        var service = new IconRefresher(
            new ThrowingStore(failure),
            new IconFetcher(new HttpClient(), new HostScopeGuard(new NoResolver()), new Frozen(Now)),
            new Frozen(Now),
            logger);

        await service.StartAsync(CancellationToken.None);

        try
        {
            // Races both outcomes: waiting on the log alone would turn the bug into a slow timeout
            // rather than a failing assertion.
            await Task.WhenAny(logger.Warned.Task, service.ExecuteTask!).WaitAsync(TimeSpan.FromSeconds(30));

            await Assert.That(service.ExecuteTask!.IsCompleted).IsFalse();
            await Assert.That(logger.Warned.Task.IsCompleted).IsTrue();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>A store whose one interesting method throws whatever it was handed.</summary>
    private sealed class ThrowingStore(Exception error) : IIconStore
    {
        public Task<GameIcon?> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default) =>
            Task.FromResult<GameIcon?>(null);

        public Task UpsertAsync(GameIcon icon, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<IconCandidate>> DueAsync(
            int limit, DateTimeOffset staleBefore, CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<IconCandidate>>(error);
    }

    /// <summary>Completes when the loop says it skipped a pass.</summary>
    private sealed class Spy : ILogger<IconRefresher>
    {
        public TaskCompletionSource Warned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel is LogLevel.Warning)
            {
                Warned.TrySetResult();
            }
        }
    }

    /// <summary>No test here reaches a socket, so no test here performs a lookup.</summary>
    private sealed class NoResolver : IHostResolver
    {
        public Task<IReadOnlyList<System.Net.IPAddress>> ResolveAsync(
            string host, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<System.Net.IPAddress>>([]);
    }

    private sealed class Frozen(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}
