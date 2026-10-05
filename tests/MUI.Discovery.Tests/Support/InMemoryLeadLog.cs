namespace MUI.Discovery.Tests.Support;

/// <summary>
/// The lead log in memory, with the same bound <c>crawl_lead</c> enforces and the same lock.
/// </summary>
public sealed class InMemoryLeadLog : ILeadLog
{
    private readonly Lock _gate = new();
    private readonly List<LeadRecord> _rows = [];

    public IReadOnlyList<LeadRecord> Rows
    {
        get
        {
            lock (_gate)
            {
                return _rows.ToList();
            }
        }
    }

    public Task<bool> TryBeginAsync(
        Guid id,
        LeadEvidence evidence,
        DateTimeOffset now,
        int perWindow,
        DateTimeOffset since,
        CancellationToken ct)
    {
        lock (_gate)
        {
            if (_rows.Count(r => r.FoundAt >= since) >= perWindow)
            {
                return Task.FromResult(false);
            }

            _rows.Add(new LeadRecord(id, null, null, evidence, now, null, null));
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
            var index = _rows.FindIndex(r => r.Id == id);

            if (index >= 0)
            {
                _rows[index] = _rows[index] with
                {
                    Host = address?.Host,
                    Port = address?.Port,
                    Outcome = outcome,
                    CrawlTargetId = crawlTargetId,
                };
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LeadRecord>> RecentAsync(int limit, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<LeadRecord>>(
                [.. _rows.OrderByDescending(r => r.FoundAt).ThenByDescending(r => r.Id).Take(limit)]);
        }
    }
}
