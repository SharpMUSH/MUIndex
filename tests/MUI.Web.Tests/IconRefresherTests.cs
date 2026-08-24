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

    /// <summary>
    /// A failure is written down, at a distance that grows with how many came before it.
    /// </summary>
    /// <remarks>
    /// The marker is what lets the queue advance past a URL that cannot be fetched — see migration
    /// 0035. The growth is what stops us asking a dead web server forty-eight times a day for ever.
    /// </remarks>
    [Test]
    public async Task AFailedFetchIsWrittenDownWithAGrowingBackOff()
    {
        var store = new RecordingStore(
            new IconCandidate(Guid.NewGuid(), "https://corvid.example/logo.png", null, null, null, 3));

        var refresher = new IconRefresher(
            store,
            // No response scripted, so the handler 404s and every candidate fails.
            new IconFetcher(new HttpClient(new NotFound()), new HostScopeGuard(new Routable()), new Frozen(Now)),
            new Frozen(Now),
            new Spy());

        await refresher.PassAsync(CancellationToken.None);

        var attempt = store.Failures.Single();

        await Assert.That(attempt.Failures).IsEqualTo(4);
        await Assert.That(attempt.AttemptedAt).IsEqualTo(Now);
        await Assert.That(attempt.NextAttemptAt).IsEqualTo(Now + IconRefresher.Backoff(4));
        await Assert.That(store.Stored).IsEmpty();
    }

    /// <summary>
    /// The back-off doubles from one pass and stops at the staleness window, rather than running off.
    /// </summary>
    [Test]
    public async Task TheBackOffDoublesAndThenStopsAtTheStalenessWindow()
    {
        await Assert.That(IconRefresher.Backoff(1)).IsEqualTo(IconRefresher.Interval);
        await Assert.That(IconRefresher.Backoff(2)).IsEqualTo(IconRefresher.Interval * 2);
        await Assert.That(IconRefresher.Backoff(3)).IsEqualTo(IconRefresher.Interval * 4);

        // Far past where the doubling would overflow a long if it were not clamped.
        await Assert.That(IconRefresher.Backoff(64)).IsEqualTo(IconRefresher.Stale);
        await Assert.That(IconRefresher.Backoff(int.MaxValue)).IsEqualTo(IconRefresher.Stale);
    }

    /// <summary>
    /// A far end honouring our ETag is not a failure, and is not backed off for saying so.
    /// </summary>
    /// <remarks>
    /// Counted as one, a game whose icon has not changed would have its next check pushed out to a
    /// week — and, because a 304 writes no row, be permanently stale and permanently punished for it.
    /// </remarks>
    [Test]
    public async Task AnUnchangedIconIsNotAFailure()
    {
        var store = new RecordingStore(
            new IconCandidate(
                Guid.NewGuid(),
                "https://corvid.example/logo.png",
                "https://corvid.example/logo.png",
                "\"abc\"",
                Now.AddDays(-30)));

        var refresher = new IconRefresher(
            store,
            new IconFetcher(
                new HttpClient(new Answering(System.Net.HttpStatusCode.NotModified)),
                new HostScopeGuard(new Routable()),
                new Frozen(Now)),
            new Frozen(Now),
            new Spy());

        await refresher.PassAsync(CancellationToken.None);

        await Assert.That(store.Failures).IsEmpty();
        await Assert.That(store.Stored).IsEmpty();
    }

    /// <summary>A store whose one interesting method throws whatever it was handed.</summary>
    private sealed class ThrowingStore(Exception error) : IIconStore
    {
        public Task<GameIcon?> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default) =>
            Task.FromResult<GameIcon?>(null);

        public Task UpsertAsync(GameIcon icon, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<IconCandidate>> DueAsync(
            int limit,
            DateTimeOffset staleBefore,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<IconCandidate>>(error);

        public Task RecordFailureAsync(IconAttempt attempt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>A store that offers a fixed candidate list and keeps whatever the pass wrote.</summary>
    private sealed class RecordingStore(params IconCandidate[] due) : IIconStore
    {
        public List<IconAttempt> Failures { get; } = [];

        public List<GameIcon> Stored { get; } = [];

        public Task<GameIcon?> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default) =>
            Task.FromResult<GameIcon?>(null);

        public Task UpsertAsync(GameIcon icon, CancellationToken cancellationToken = default)
        {
            Stored.Add(icon);

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IconCandidate>> DueAsync(
            int limit,
            DateTimeOffset staleBefore,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IconCandidate>>(due);

        public Task RecordFailureAsync(IconAttempt attempt, CancellationToken cancellationToken = default)
        {
            Failures.Add(attempt);

            return Task.CompletedTask;
        }
    }

    /// <summary>A far end with one fixed answer and no body.</summary>
    private sealed class Answering(System.Net.HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    /// <summary>The commonest far end in this file: one that has nothing at that address.</summary>
    private sealed class NotFound : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    /// <summary>A resolver whose one answer is an address §7.2 permits, so the gate is not the test.</summary>
    private sealed class Routable : IHostResolver
    {
        public Task<IReadOnlyList<System.Net.IPAddress>> ResolveAsync(
            string host, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<System.Net.IPAddress>>(
                [System.Net.IPAddress.Parse("203.0.113.10")]);
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
