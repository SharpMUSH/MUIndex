namespace MUI.Discovery.Tests.Support;

/// <summary>
/// <see cref="IUnitOfWork"/> in memory: buffers what joining writes would do and only applies them on
/// <see cref="CommitAsync"/>, so a test can prove <c>ReviewMergeService.MergeAsync</c> actually rolls
/// both writes back together when the second one fails, rather than merely asserting it "should".
/// </summary>
public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly List<Action> _pending = [];

    /// <summary>
    /// When set, <see cref="CommitAsync"/> throws this instead of applying pending writes, simulating a
    /// violation that only surfaces at commit -- real Postgres does exactly this for
    /// <c>merge_log_no_chains</c>, which is <c>DEFERRABLE INITIALLY DEFERRED</c> (see
    /// <see cref="MergeWouldChainException"/>).
    /// </summary>
    public Exception? ThrowOnCommit { get; set; }

    public void Enqueue(Action apply) => _pending.Add(apply);

    public Task CommitAsync(CancellationToken ct)
    {
        if (ThrowOnCommit is { } error)
        {
            throw error;
        }

        foreach (var apply in _pending)
        {
            apply();
        }

        _pending.Clear();
        return Task.CompletedTask;
    }

    /// <summary>Nothing to dispose -- an uncommitted unit of work just never applied its buffered writes.</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class InMemoryUnitOfWorkFactory : IUnitOfWorkFactory
{
    /// <summary>Carried onto every unit of work this factory begins -- see <see cref="InMemoryUnitOfWork.ThrowOnCommit"/>.</summary>
    public Exception? ThrowOnCommit { get; set; }

    public Task<IUnitOfWork> BeginAsync(CancellationToken ct) =>
        Task.FromResult<IUnitOfWork>(new InMemoryUnitOfWork { ThrowOnCommit = ThrowOnCommit });
}
