namespace MUI.Discovery.Tests.Support;

/// <summary>
/// The submission log in memory, counting exactly the way the table's index does.
/// </summary>
/// <remarks>
/// <c>submitted_at &gt;= since</c> and equality on the source, because a fake that counted more
/// loosely would let the rate limit's tests pass against a bound the database never enforced.
/// </remarks>
public sealed class InMemorySubmissionLog : ISubmissionLog
{
    private readonly List<SubmissionRecord> _records = [];

    public IReadOnlyList<SubmissionRecord> Records => _records;

    public Task RecordAsync(SubmissionRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);

        _records.Add(record);
        return Task.CompletedTask;
    }

    public Task<int> CountSinceAsync(string source, DateTimeOffset since, CancellationToken ct) =>
        Task.FromResult(_records.Count(r =>
            string.Equals(r.Source, source, StringComparison.Ordinal) && r.SubmittedAt >= since));
}
