namespace MUI.Discovery.Tests.Support;

/// <summary>
/// The submission log in memory, with the same bound the table enforces.
/// </summary>
/// <remarks>
/// <para>
/// <c>submitted_at &gt;= since</c> and ordinal equality on the source, because a fake that counted
/// more loosely would let the rate limit's tests pass against a bound the database never enforced.
/// </para>
/// <para>
/// <b>The lock is not decoration.</b> The real implementation takes a Postgres advisory lock on the
/// source so that counting and inserting cannot interleave, and a fake without one would let the
/// concurrency test pass here and fail against the database — which is the wrong way round for a
/// bound whose entire failure mode is concurrent.
/// </para>
/// </remarks>
public sealed class InMemorySubmissionLog : ISubmissionLog
{
    private readonly Lock _gate = new();
    private readonly List<Row> _rows = [];

    public IReadOnlyList<Row> Rows
    {
        get
        {
            lock (_gate)
            {
                return _rows.ToList();
            }
        }
    }

    /// <summary>What was written, in the shape the table holds it.</summary>
    public sealed record Row(Guid Id, DateTimeOffset SubmittedAt, string Source)
    {
        public string? Host { get; set; }

        public int? Port { get; set; }

        /// <summary>Null while the row is the reservation and nothing more.</summary>
        public SubmissionOutcome? Outcome { get; set; }

        public Guid? CrawlTargetId { get; set; }
    }

    public Task<bool> TryBeginAsync(
        Guid id,
        string source,
        DateTimeOffset now,
        int perSource,
        DateTimeOffset since,
        CancellationToken ct)
    {
        lock (_gate)
        {
            var taken = _rows.Count(r =>
                string.Equals(r.Source, source, StringComparison.Ordinal) && r.SubmittedAt >= since);

            if (taken >= perSource)
            {
                return Task.FromResult(false);
            }

            _rows.Add(new Row(id, now, source));
            return Task.FromResult(true);
        }
    }

    public Task CompleteAsync(
        Guid id,
        SubmittedAddress? address,
        SubmissionOutcome outcome,
        Guid? crawlTargetId,
        CancellationToken ct)
    {
        lock (_gate)
        {
            if (_rows.FirstOrDefault(r => r.Id == id) is { } row)
            {
                row.Host = address?.Host;
                row.Port = address?.Port;
                row.Outcome = outcome;
                row.CrawlTargetId = crawlTargetId;
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>§11's salt, fixed, so a digest is reproducible inside one test.</summary>
/// <remarks>Two instances with different values stand in for two epochs.</remarks>
public sealed class FixedSubmissionSalt(byte value = 7) : ISubmissionSalt
{
    private readonly byte[] _salt = Enumerable.Repeat(value, 32).ToArray();

    public Task<byte[]> CurrentAsync(CancellationToken ct) => Task.FromResult(_salt);
}
