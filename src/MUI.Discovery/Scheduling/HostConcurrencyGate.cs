using System.Collections.Concurrent;

namespace MUI.Discovery;

/// <summary>
/// One probe at a time per host (spec §7.7's per-host serialisation). Keyed on the host alone,
/// deliberately: a game advertising six ports is one machine and one operator, and the point of the
/// rule is not to arrive six times at once.
/// </summary>
/// <remarks>
/// This is not the concurrency cap. How many probes may be in flight <em>in total</em> is a semaphore
/// in the crawl loop, because that is a fact about connections rather than about hosts, and folding
/// the two together would make neither assertable.
/// </remarks>
public sealed class HostConcurrencyGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public async Task<IDisposable> EnterAsync(string host, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(host);

        var gate = _gates.GetOrAdd(CanonicalHost.Normalize(host), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        return new Holding(gate);
    }

    /// <summary>
    /// Idempotent on purpose: a second release would let two probes into one host, which is precisely
    /// the thing this type exists to prevent. The loop disposes through a <c>using</c> and a retry path
    /// can dispose again.
    /// </summary>
    private sealed class Holding(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
