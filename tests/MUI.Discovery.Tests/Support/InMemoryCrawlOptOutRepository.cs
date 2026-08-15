namespace MUI.Discovery.Tests.Support;

/// <summary>
/// The opt-out register in memory, obeying the same three rules the table does: one row per address
/// per route, the first ask keeps its date, and nothing is ever removed.
/// </summary>
public sealed class InMemoryCrawlOptOutRepository : ICrawlOptOutRepository
{
    private readonly Dictionary<(string Host, int? Port, OptOutSource Source), CrawlOptOut> _rows = [];

    public Task<CrawlOptOut?> StandingAsync(string host, int port, CancellationToken ct) =>
        Task.FromResult(_rows.Values
            .Where(row => row.Standing && row.Covers(host, port))
            .OrderBy(row => row.RecordedAt)
            .FirstOrDefault());

    public Task<CrawlOptOut> RecordAsync(CrawlOptOut optOut, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(optOut);

        var canonical = optOut with { Host = CanonicalHost.Normalize(optOut.Host) };
        var key = (canonical.Host, canonical.Port, canonical.Source);

        // When they first asked and when we last heard it are two facts, and a confirmation may only
        // move the second. Un-withdrawing is the same event read the other way: they are asking again.
        var stored = _rows.TryGetValue(key, out var existing)
            ? existing with
            {
                LastConfirmedAt = canonical.LastConfirmedAt,
                Detail = canonical.Detail,
                WithdrawnAt = null,
            }
            : canonical;

        _rows[key] = stored;

        return Task.FromResult(stored);
    }

    public Task WithdrawAsync(string host, int? port, OptOutSource route, DateTimeOffset at, CancellationToken ct)
    {
        var key = (CanonicalHost.Normalize(host), port, route);

        if (_rows.TryGetValue(key, out var existing))
        {
            _rows[key] = existing with { WithdrawnAt = at };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CrawlOptOut>> AllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CrawlOptOut>>(
            _rows.Values.OrderBy(row => row.RecordedAt).ThenBy(row => row.Host).ToList());
}
