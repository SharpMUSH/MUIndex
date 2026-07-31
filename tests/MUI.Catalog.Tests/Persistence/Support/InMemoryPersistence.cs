using MUI.Catalog.Persistence;

namespace MUI.Catalog.Tests.Persistence.Support;

/// <summary>
/// In-memory stands-in for the two stores the archive sweep reads.
/// </summary>
/// <remarks>
/// The rule these obey, and the reason they exist: <b>a fake must never be more lenient than the real
/// thing</b>. The sweep's own arithmetic is what these tests are about, so the database is not in the
/// way of them — but the same behaviour is asserted against Postgres in
/// <c>ArchiveSweeperPostgresTests</c>, because a fake agreeing with the code proves only that they
/// were written by the same person.
/// </remarks>
internal sealed class InMemoryGameStore : IGameStore
{
    private readonly Dictionary<Guid, GameRecord> _games = [];

    public void Seed(GameRecord game) => _games[game.Id] = game;

    public Task<GameRecord?> ByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_games.GetValueOrDefault(id));

    public Task<GameRecord?> BySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        Task.FromResult(_games.Values.FirstOrDefault(g => g.Slug == slug));

    public Task InsertAsync(GameRecord game, CancellationToken cancellationToken = default)
    {
        if (!_games.TryAdd(game.Id, game))
        {
            throw new InvalidOperationException("A game with that id is already stored.");
        }

        return Task.CompletedTask;
    }

    public Task SetStateAsync(
        Guid id,
        LifecycleState state,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        if (_games.TryGetValue(id, out var game))
        {
            _games[id] = game with
            {
                State = state,
                ArchivedAt = state is LifecycleState.Archived ? at : null,
            };
        }

        return Task.CompletedTask;
    }

    public Task MarkReachableAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (_games.TryGetValue(id, out var game)
            && (game.LastReachableAt is null || game.LastReachableAt < at))
        {
            _games[id] = game with { LastReachableAt = at };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GameRecord>> UnarchivedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameRecord>>(
            _games.Values.Where(g => g.State is not LifecycleState.Archived).ToList());
}

internal sealed class InMemoryReachableHistory : IReachableHistory
{
    public Dictionary<Guid, TimeSpan> FirstParty { get; } = [];

    public Dictionary<Guid, TimeSpan> ImportedMeasured { get; } = [];

    public Task<TimeSpan> CumulativeReachableAsync(
        Guid gameId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(FirstParty.GetValueOrDefault(gameId));

    public Task<TimeSpan> CumulativeImportedMeasuredReachableAsync(
        Guid gameId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ImportedMeasured.GetValueOrDefault(gameId));
}
