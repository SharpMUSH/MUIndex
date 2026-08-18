namespace MUI.Discovery.Tests.Support;

/// <summary>
/// The registry's contract in memory: monotonic, no delete, depth only ever shrinks.
/// </summary>
/// <remarks>
/// <b>Hosts are canonicalised with <see cref="CanonicalHost.Normalize"/> and compared ordinally</b> —
/// the same rule a database-backed registry has to enforce. An <c>OrdinalIgnoreCase</c> dictionary
/// here would be a fake kinder than the real thing: tests would pass while the registry minted a
/// second target for the same machine.
/// </remarks>
public sealed class InMemoryCrawlTargetRepository : ICrawlTargetRepository
{
    private readonly Dictionary<(string Host, int Port), CrawlTarget> _targets = new();

    public IReadOnlyCollection<CrawlTarget> All => _targets.Values.ToList();

    public Task<CrawlTarget?> ByAddressAsync(string host, int port, CancellationToken ct) =>
        Task.FromResult(_targets.GetValueOrDefault((CanonicalHost.Normalize(host), port)));

    public Task<Guid> AddAsync(CrawlTarget target, CancellationToken ct)
    {
        var canonical = target with { Host = CanonicalHost.Normalize(target.Host) };
        var key = (canonical.Host, canonical.Port);

        if (_targets.TryGetValue(key, out var existing))
        {
            // A repeat sighting may lower the recorded depth and may change nothing else. Resetting
            // the schedule here would let a referral drag a target forward, which is exactly the
            // coupling §7.1 removes.
            _targets[key] = existing with { Depth = Math.Min(existing.Depth, canonical.Depth) };
            return Task.FromResult(existing.Id);
        }

        _targets[key] = canonical;
        return Task.FromResult(canonical.Id);
    }

    public Task<IReadOnlyList<CrawlTarget>> DueAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CrawlTarget>>(_targets.Values
            .Where(t => t.NextProbeAt <= now)
            .OrderBy(t => t.NextProbeAt)
            .ThenBy(t => t.Depth)
            .ThenBy(t => t.FirstSeenAt)
            .Take(limit)
            .ToList());

    public Task RecordAttemptAsync(
        Guid id,
        DateTimeOffset at,
        bool succeeded,
        TimeSpan? crawlDelay,
        DateTimeOffset nextProbeAt,
        CancellationToken ct)
    {
        Replace(id, target => target with
        {
            LastProbedAt = at,
            ConsecutiveFailures = succeeded ? 0 : target.ConsecutiveFailures + 1,
            CrawlDelay = crawlDelay ?? target.CrawlDelay,
            NextProbeAt = nextProbeAt,
        });

        return Task.CompletedTask;
    }

    public Task AttachGameAsync(Guid id, Guid gameId, CancellationToken ct)
    {
        Replace(id, target => target.GameId is null ? target with { GameId = gameId } : target);
        return Task.CompletedTask;
    }

    private void Replace(Guid id, Func<CrawlTarget, CrawlTarget> change)
    {
        foreach (var (key, target) in _targets.ToList())
        {
            if (target.Id == id)
            {
                _targets[key] = change(target);
            }
        }
    }
}
