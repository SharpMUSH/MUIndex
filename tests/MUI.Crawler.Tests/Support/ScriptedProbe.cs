using System.Collections.Concurrent;
using System.Net;

using MUI.Crawl;

namespace MUI.Crawler.Tests.Support;

/// <summary>
/// A probe that answers from a script rather than from a socket.
/// </summary>
/// <remarks>
/// The whole point of §6.5's seam: the crawl loop, the ingestor and the writers can be run end to end
/// against a real database with no network anywhere. It records what it was asked, so per-host
/// serialisation and the concurrency bound can be asserted rather than assumed.
/// </remarks>
public sealed class ScriptedProbe(Func<ProbeTarget, ProbeResult> answer) : IProbe
{
    private readonly ConcurrentQueue<ProbeTarget> _asked = new();

    private int _inFlight;

    public IReadOnlyCollection<ProbeTarget> Asked => _asked.ToList();

    /// <summary>The most probes that were ever in flight at once.</summary>
    public int Peak { get; private set; }

    /// <summary>How long each probe pretends to take, so concurrency is observable.</summary>
    public TimeSpan Latency { get; init; } = TimeSpan.Zero;

    public async Task<ProbeResult> ProbeAsync(ProbeTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        _asked.Enqueue(target);

        var now = Interlocked.Increment(ref _inFlight);
        lock (_asked)
        {
            if (now > Peak)
            {
                Peak = now;
            }
        }

        try
        {
            if (Latency > TimeSpan.Zero)
            {
                await Task.Delay(Latency, cancellationToken);
            }

            return answer(target);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}

/// <summary>A resolver that answers from a table, so no test here performs a live lookup.</summary>
public sealed class FakeHostResolver : IHostResolver
{
    private readonly Dictionary<string, IReadOnlyList<IPAddress>> _answers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What an unlisted name resolves to. A public address, so the ordinary case is the default.</summary>
    public IReadOnlyList<IPAddress>? Fallback { get; init; } = [IPAddress.Parse("198.51.100.7")];

    public FakeHostResolver Resolving(string host, params string[] addresses)
    {
        _answers[host] = addresses.Select(IPAddress.Parse).ToList();
        return this;
    }

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken = default) =>
        _answers.TryGetValue(host, out var found)
            ? Task.FromResult(found)
            : Fallback is { } fallback
                ? Task.FromResult(fallback)
                : throw new InvalidOperationException($"No scripted answer for {host}.");
}
