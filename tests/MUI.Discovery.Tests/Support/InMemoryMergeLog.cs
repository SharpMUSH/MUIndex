namespace MUI.Discovery.Tests.Support;

/// <summary>
/// The merge trail in memory. A merge is a redirect, so this holds the log and the pointer and nothing
/// is ever removed from either.
/// </summary>
public sealed class InMemoryMergeLog : IMergeLog
{
    private readonly List<MergeRecord> _records = [];

    public IReadOnlyList<MergeRecord> All => _records;

    /// <summary>Where each absorbed game now points, which is the whole of what a merge does.</summary>
    public Dictionary<Guid, Guid> RedirectedTo { get; } = [];

    public Task<Guid> RecordAsync(MergeRecord record, CancellationToken ct)
    {
        _records.Add(record);
        RedirectedTo[record.FromGameId] = record.IntoGameId;
        return Task.FromResult(record.Id);
    }

    public Task RevertAsync(Guid mergeId, DateTimeOffset at, CancellationToken ct)
    {
        var index = _records.FindIndex(r => r.Id == mergeId && r.RevertedAt is null);
        if (index >= 0)
        {
            // Clearing the pointer is the whole revert: nothing was moved, so nothing has to be moved
            // back, and both games are complete again the moment it goes.
            RedirectedTo.Remove(_records[index].FromGameId);
            _records[index] = _records[index] with { RevertedAt = at };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MergeRecord>> ForGameAsync(Guid gameId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MergeRecord>>(
            _records.Where(r => r.IntoGameId == gameId || r.FromGameId == gameId).ToList());
}
