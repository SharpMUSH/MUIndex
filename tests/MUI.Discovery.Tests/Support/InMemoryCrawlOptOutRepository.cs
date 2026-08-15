namespace MUI.Discovery.Tests.Support;

/// <summary>
/// The opt-out register in memory, obeying the same four rules the table does: one row per address
/// per route, the first ask keeps its date, nothing is ever removed, and <b>a timestamp is rounded to
/// the microsecond on the way in</b>.
/// </summary>
/// <remarks>
/// That last one is not pedantry. <c>timestamptz</c> stores microseconds and a
/// <see cref="DateTimeOffset"/> counts 100ns ticks, so a date written here and read back in
/// production is a <em>different value</em> — which is exactly what broke the first version of the
/// gate's "have we heard this before" test, by comparing the stored date against the clock. A fake
/// that round-tripped the value exactly would be a fake kinder than the database, and every test
/// would pass while the log line never fired.
/// </remarks>
public sealed class InMemoryCrawlOptOutRepository : ICrawlOptOutRepository
{
    private readonly Dictionary<(string Host, int? Port, OptOutSource Source), CrawlOptOut> _rows = [];

    public Task<CrawlOptOut?> StandingAsync(string host, int port, CancellationToken ct) =>
        Task.FromResult(_rows.Values
            .Where(row => row.Standing && row.Covers(host, port))
            .OrderBy(row => row.RecordedAt)
            .FirstOrDefault());

    public Task<OptOutRecording> RecordAsync(CrawlOptOut optOut, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(optOut);

        var canonical = optOut with
        {
            Host = CanonicalHost.Normalize(optOut.Host),
            RecordedAt = ToMicroseconds(optOut.RecordedAt),
            LastConfirmedAt = ToMicroseconds(optOut.LastConfirmedAt),
        };

        var key = (canonical.Host, canonical.Port, canonical.Source);

        // When they first asked and when we last heard it are two facts, and a confirmation may only
        // move the second. Un-withdrawing is the same event read the other way: they are asking again.
        var existed = _rows.TryGetValue(key, out var existing);

        var stored = existed
            ? existing! with
            {
                LastConfirmedAt = canonical.LastConfirmedAt,
                Detail = canonical.Detail,
                WithdrawnAt = null,
            }
            : canonical;

        _rows[key] = stored;

        return Task.FromResult(new OptOutRecording(stored, IsFirstAsk: !existed));
    }

    public Task WithdrawAsync(string host, int? port, OptOutSource route, DateTimeOffset at, CancellationToken ct)
    {
        var key = (CanonicalHost.Normalize(host), port, route);

        if (_rows.TryGetValue(key, out var existing))
        {
            _rows[key] = existing with { WithdrawnAt = ToMicroseconds(at) };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CrawlOptOut>> AllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CrawlOptOut>>(
            _rows.Values.OrderBy(row => row.RecordedAt).ThenBy(row => row.Host).ToList());

    private static DateTimeOffset ToMicroseconds(DateTimeOffset at) =>
        at.AddTicks(-(at.Ticks % TimeSpan.TicksPerMicrosecond));
}
