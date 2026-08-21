namespace MUI.Discovery;

/// <summary>
/// The two time floors on a crawl: a gap between any two connections, and a longer gap between two
/// connections to the same host.
/// </summary>
/// <remarks>
/// Deliberately not a timer or queue: <see cref="DelayBefore"/> answers how long until a host may be
/// dialled, and <see cref="RecordStart"/> records that it was — a test can drive an injected
/// <see cref="TimeProvider"/> and read the answers back instead of sleeping for real.
/// <para>
/// The third limit — how many connections may be open at once — lives in the crawl loop as a
/// semaphore; it's a fact about connections in flight, not about time.
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
    /// Stamps a connection as starting now, against both limits. Must be called on start, not on
    /// completion — stamping on completion would let a burst of slow connections all start together,
    /// making the effective rate depend on server response time instead of bounding it.
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
    /// Waits out <see cref="DelayBefore"/> then stamps the start, re-checking after each wait so two
    /// workers told to wait the same duration don't both start at the end of it and halve the interval.
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
