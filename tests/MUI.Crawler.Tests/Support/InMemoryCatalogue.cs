using MUI.Catalog;
using MUI.Catalog.Persistence;

namespace MUI.Crawler.Tests.Support;

/// <summary>
/// The catalogue's stores in memory, so the ingestion rules can be asserted without a database.
/// </summary>
/// <remarks>
/// <b>A fake must never be more lenient than the real thing.</b> Where the real table has a key, these
/// have the same one — <c>game_field</c> is keyed <c>(game, field, source)</c> here as there, because
/// a fake keyed only on <c>(game, field)</c> would silently collapse measured and declared into one
/// row and make the capability matrix untestable. The same behaviours are asserted against a real
/// PostgreSQL in <c>CrawlCyclePostgresTests</c>, because a fake agreeing with the code proves only
/// that they were written by the same person.
/// </remarks>
public sealed class FakeGameStore : IGameStore
{
    private readonly Dictionary<Guid, GameRecord> _games = [];

    public IReadOnlyCollection<GameRecord> All => _games.Values.ToList();

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

    public Task SetClaimedAsync(Guid id, bool isClaimed, CancellationToken cancellationToken = default)
    {
        if (_games.TryGetValue(id, out var game))
        {
            _games[id] = game with { IsClaimed = isClaimed };
        }

        return Task.CompletedTask;
    }

    public Task MarkReachableAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (_games.TryGetValue(id, out var game) && (game.LastReachableAt is null || game.LastReachableAt < at))
        {
            _games[id] = game with { LastReachableAt = at };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GameRecord>> UnarchivedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameRecord>>(
            _games.Values.Where(g => g.State is not LifecycleState.Archived).ToList());
}

public sealed class FakePresenceStore : IPresenceStore
{
    public List<PresenceSample> Samples { get; } = [];

    public Task AppendAsync(PresenceSample sample, CancellationToken cancellationToken = default)
    {
        // The real table is keyed (game_id, at) and takes ON CONFLICT DO NOTHING, so a second write at
        // one instant is discarded rather than allowed to overwrite the better source.
        if (!Samples.Any(s => s.GameId == sample.GameId && s.At == sample.At))
        {
            Samples.Add(sample);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PresenceSample>> ForGameAsync(
        Guid gameId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PresenceSample>>(
            Samples.Where(s => s.GameId == gameId && s.At >= from && s.At <= to).ToList());
}

public sealed class FakeAvailabilityStore : IAvailabilityStore, IReachableHistory
{
    public List<AvailabilityInterval> Intervals { get; } = [];

    public Task<AvailabilityInterval?> OpenIntervalAsync(Guid gameId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Intervals.LastOrDefault(i => i.GameId == gameId && i.IsOpen));

    public Task OpenAsync(AvailabilityInterval interval, CancellationToken cancellationToken = default)
    {
        Intervals.Add(interval);
        return Task.CompletedTask;
    }

    public Task CloseAsync(Guid gameId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var index = Intervals.FindLastIndex(i => i.GameId == gameId && i.IsOpen);
        if (index >= 0)
        {
            Intervals[index] = Intervals[index] with { ToAt = at };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AvailabilityInterval>> ForGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AvailabilityInterval>>(
            Intervals.Where(i => i.GameId == gameId).ToList());

    public Task<TimeSpan> CumulativeReachableAsync(
        Guid gameId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Reachability.CumulativeReachable(
            Intervals.Where(i => i.GameId == gameId).ToList(), now));
}

public sealed class FakeGameFieldStore : IGameFieldStore
{
    private readonly Dictionary<(Guid, string, FieldSource), GameField> _fields = [];

    public List<FieldChange> Changes { get; } = [];

    public IReadOnlyCollection<GameField> All => _fields.Values.ToList();

    public Task<IReadOnlyList<GameField>> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameField>>(_fields.Values.Where(f => f.GameId == gameId).ToList());

    public Task<IReadOnlyList<GameField>> ForGameAsync(
        Guid gameId,
        FieldSource only,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameField>>(
            [.. _fields.Values.Where(f => f.GameId == gameId && f.Source == only)]);

    public Task UpsertAsync(GameField field, CancellationToken cancellationToken = default)
    {
        _fields[(field.GameId, field.Field, field.Source)] = field;
        return Task.CompletedTask;
    }

    public Task RecordChangeAsync(FieldChange change, CancellationToken cancellationToken = default)
    {
        Changes.Add(change);
        return Task.CompletedTask;
    }

    public string? Value(Guid gameId, string field, FieldSource source) =>
        _fields.GetValueOrDefault((gameId, field, source))?.Value;
}

/// <summary>The whole ingestion pipeline, in memory, ready to assert on.</summary>
public sealed class Catalogue
{
    public FakeGameStore Games { get; } = new();

    public FakePresenceStore Presence { get; } = new();

    public FakeAvailabilityStore Availability { get; } = new();

    public FakeGameFieldStore Fields { get; } = new();

    public ProbeIngestor Ingestor() => new(
        new PresenceWriter(Presence),
        new AvailabilityWriter(Availability),
        new FieldReconciler(Fields),
        Games,
        new ArchiveSweeper(Games, Availability, Availability));

    /// <summary>A game that already exists, so a probe has something to be attributed to.</summary>
    public Guid Listed(LifecycleState state = LifecycleState.Active)
    {
        var id = Guid.CreateVersion7();

        Games.Seed(new GameRecord(
            id,
            "corvid",
            "Corvid",
            Tagline: null,
            state,
            IsClaimed: false,
            FirstSeenAt: Probes.Observed.AddYears(-1),
            ArchivedAt: state is LifecycleState.Archived ? Probes.Observed.AddDays(-1) : null));

        return id;
    }
}
