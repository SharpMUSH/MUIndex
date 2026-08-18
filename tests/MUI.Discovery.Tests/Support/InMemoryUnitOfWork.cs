namespace MUI.Discovery.Tests.Support;

/// <summary>
/// <see cref="IUnitOfWork"/> in memory: buffers what joining writes would do and only applies them on
/// <see cref="CommitAsync"/>, so a test can prove <c>ReviewMergeService.MergeAsync</c> actually rolls
/// both writes back together when the second one fails, rather than merely asserting it "should".
/// </summary>
public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly List<Action> _pending = [];

    public void Enqueue(Action apply) => _pending.Add(apply);

    public Task CommitAsync(CancellationToken ct)
    {
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
    public Task<IUnitOfWork> BeginAsync(CancellationToken ct) =>
        Task.FromResult<IUnitOfWork>(new InMemoryUnitOfWork());
}
