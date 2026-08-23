using MUI.Catalog;
using MUI.Discovery;

namespace MUI.Crawler.Tests.Support;

/// <summary>
/// A registry that remembers what was planted and answers for what is already there.
/// </summary>
/// <remarks>
/// Shared by every cycle test — <c>I3CycleTests</c> and <c>AresCycleTests</c> both drive a pass whose
/// whole job is deciding what to seed and what to leave alone, and a second copy of this would let
/// the two drift into asserting different things about the same interface.
/// <para>
/// <see cref="Existing"/> maps an address to the game it has been promoted to, or to null for an
/// address in the registry that has not answered for itself yet — the distinction §7.1 turns on.
/// </para>
/// </remarks>
internal sealed class FakeTargets(DateTimeOffset now) : ICrawlTargetRepository
{
    public Dictionary<(string, int), Guid?> Existing { get; } = [];

    public List<CrawlTarget> Added { get; } = [];

    public Task<CrawlTarget?> ByAddressAsync(string host, int port, CancellationToken ct) =>
        Task.FromResult(Existing.TryGetValue((host, port), out var game)
            ? new CrawlTarget
            {
                Id = Guid.CreateVersion7(),
                GameId = game,
                Host = host,
                Port = port,
                NextProbeAt = now,
                FirstSeenAt = now,
            }
            : null);

    /// <remarks>
    /// Records the address in <see cref="Existing"/> as well as in <see cref="Added"/>, because the
    /// real registry does: <c>ON CONFLICT (host, port)</c> collapses a repeat sighting onto the row
    /// that is already there. A fake that only appended would answer <c>null</c> on the next pass
    /// and let a cycle re-seed for ever while its test stayed green — the kind of fake that is
    /// kinder than production and hides exactly the bug it should catch.
    /// <para>
    /// <c>TryAdd</c>, not an assignment: an address that has already been promoted to a game keeps
    /// its game, since the real <c>AddAsync</c> updates nothing but depth.
    /// </para>
    /// </remarks>
    public Task<Guid> AddAsync(CrawlTarget target, CancellationToken ct)
    {
        Added.Add(target);
        Existing.TryAdd((target.Host, target.Port), target.GameId);

        return Task.FromResult(target.Id);
    }

    public Task<IReadOnlyList<CrawlTarget>> DueAsync(
        DateTimeOffset now, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CrawlTarget>>([]);

    public Task RecordAttemptAsync(
        Guid id, DateTimeOffset at, bool succeeded, TimeSpan? crawlDelay,
        DateTimeOffset nextProbeAt, CancellationToken ct) => Task.CompletedTask;

    public Task AttachGameAsync(Guid id, Guid gameId, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>A field store that records every write, so a test can assert on provenance.</summary>
internal sealed class FakeFields : IGameFieldStore
{
    public List<GameField> Written { get; } = [];

    public Task UpsertAsync(GameField field, CancellationToken ct = default)
    {
        Written.Add(field);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GameField>> ForGameAsync(Guid gameId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GameField>>([]);

    public Task<IReadOnlyList<GameField>> ForGameAsync(
        Guid gameId, FieldSource source, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GameField>>([]);

    public Task RecordChangeAsync(FieldChange change, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<DateTimeOffset?> LastChangedAtAsync(
        Guid gameId, string field, CancellationToken ct = default) =>
        Task.FromResult<DateTimeOffset?>(null);
}
