namespace MUI.Discovery;

/// <summary>
/// The two time floors on a crawl: a gap between any two connections, and a longer gap between two
/// connections to the same host.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a timer, a queue, or anything that sleeps on your behalf. It answers one question —
/// <see cref="DelayBefore"/>, how long from now until this host may be dialled — and records one fact —
/// <see cref="RecordStart"/>, it just was. That separation is what makes the limit assertable: a test
/// drives an injected <see cref="TimeProvider"/> and reads the answers back, instead of sleeping for
/// the interval and hoping the machine agreed.
/// </para>
/// <para>
/// The third limit, how many connections may be open at once, is not here. It is a semaphore in the
/// crawl loop, because it is a fact about connections in flight rather than about time.
/// </para>
/// </remarks>
public sealed class CrawlRateLimiter(DiscoveryOptions options, TimeProvider time)
{
    private readonly Dictionary<string, DateTimeOffset> _lastPerHost = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    private DateTimeOffset? _lastAny;

    /// <summary>Zero when <paramref name="host"/> may be dialled now, otherwise the longer of the two waits owed.</summary>
    public TimeSpan DelayBefore(string host)
    {
        ArgumentNullException.ThrowIfNull(host);

        lock (_gate)
        {
            return DelayLocked(CanonicalHost.Normalize(host), time.GetUtcNow());
        }
    }

    /// <summary>
    /// Stamps a connection as starting now, against both limits. Called when the connection is
    /// <em>started</em>, never when it finishes: stamping on completion would let a burst of slow
    /// connections all start together and would make the effective rate depend on how fast the servers
    /// answered, which is the opposite of a rate limit.
    /// </summary>
    public void RecordStart(string host)
    {
        ArgumentNullException.ThrowIfNull(host);

        lock (_gate)
        {
            RecordStartLocked(CanonicalHost.Normalize(host), time.GetUtcNow());
        }
    }

    /// <summary>
    /// Waits out <see cref="DelayBefore"/> and then stamps the start, re-checking after each wait — two
    /// workers told to wait one second would otherwise both start at the end of it and halve the global
    /// interval.
    /// </summary>
    public async Task WaitForTurnAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);

        var canonical = CanonicalHost.Normalize(host);

        while (true)
        {
            TimeSpan wait;
            lock (_gate)
            {
                var now = time.GetUtcNow();
                wait = DelayLocked(canonical, now);
                if (wait <= TimeSpan.Zero)
                {
                    RecordStartLocked(canonical, now);
                    return;
                }
            }

            await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan DelayLocked(string host, DateTimeOffset now)
    {
        var globalReady = _lastAny is { } lastAny ? lastAny + options.GlobalInterval : now;
        var hostReady = _lastPerHost.TryGetValue(host, out var lastHost)
            ? lastHost + options.PerHostInterval
            : now;

        var ready = globalReady > hostReady ? globalReady : hostReady;
        var wait = ready - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    private void RecordStartLocked(string host, DateTimeOffset now)
    {
        _lastAny = now;
        _lastPerHost[host] = now;
    }
}
