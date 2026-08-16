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
/// <para>
/// This is a <see cref="BackgroundService"/> in the same process as the crawler, and .NET's default
/// <c>BackgroundServiceExceptionBehavior</c> is <c>StopHost</c> — so an exception that escapes
/// <c>ExecuteAsync</c> here does not skip an icon, it stops the site. The crawl loop, the web tier
/// and the lease all go with it, over a decoration.
/// </para>
/// <para>
/// It happened. A stalled web server made <c>HttpClient</c> raise its own timeout as a
/// <see cref="TaskCanceledException"/>; both catch filters on the path read "everything except a
/// cancellation" and let it through, because a cancellation was assumed to mean the host was
/// stopping. Three hundred restarts, and a crawler that never finished a cycle.
/// </para>
/// <para>
/// Each test here waits on the loop's own warning rather than on a delay, and races it against the
/// service's task, so the bug fails the assertion outright instead of failing a timeout slowly.
/// </para>
/// </remarks>
public class IconRefresherTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A pass that fails with a cancellation nobody asked for is a pass skipped, not a host stopped.
    /// </summary>
    [Test]
    public async Task APassThatFailsWithATimeoutLeavesTheServiceRunning() =>
        // The exception HttpClient raises when its own Timeout elapses, which is what reached this
        // loop in production: a cancellation by type, and a far end that went quiet by cause.
        await AssertSurvives(new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing.",
            new TimeoutException()));

    /// <summary>
    /// And an ordinary failure is still handled the same way — the broad catch is not narrowed by
    /// the fix.
    /// </summary>
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
            // Whichever happens first: the loop logs the failed pass and carries on, or it does not
            // survive the pass at all and its task completes. Waiting on the log alone would turn
            // the bug into a timeout rather than an assertion.
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
