using MUI.Catalog.Persistence;

namespace MUI.Catalog.Tests.Persistence.Support;

/// <summary>
/// In-memory stands-in for the two stores the archive sweep reads.
/// </summary>
/// <remarks>
/// A fake must never be more lenient than the real thing. The same behaviour is also asserted against
/// Postgres in <c>ArchiveSweeperPostgresTests</c> — a fake agreeing with the code proves only that
/// they were written by the same person.
/// </remarks>
internal sealed class InMemoryGameStore : IGameStore
{
    private readonly Dictionary<Guid, GameRecord> _games = [];

    /// <summary>
    /// The slugs this store has retired. <see cref="RenameAsync"/> writes both in one act, as the real
    /// statement does — a rename that didn't retire its URL would violate §5.7.
    /// </summary>
    public InMemorySlugHistory Slugs { get; }

    public InMemoryGameStore() =>
        Slugs = new InMemorySlugHistory(id => _games.GetValueOrDefault(id)?.Slug);

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

    public Task ExcludeAsync(Guid id, string reason, DateTimeOffset at, CancellationToken ct = default) =>
        SetStateAsync(id, LifecycleState.Excluded, at, ct);

    public Task IncludeAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
        Move(id, LifecycleState.Active, at, from: LifecycleState.Excluded);

    public Task UnlistAsync(Guid id, Guid byUserId, DateTimeOffset at, CancellationToken ct = default)
    {
        if (_games.TryGetValue(id, out var game))
        {
            _games[id] = game with { State = LifecycleState.Unlisted, ArchivedAt = null };
        }

        return Task.CompletedTask;
    }

    public Task RelistAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
        Move(id, LifecycleState.Active, at, from: LifecycleState.Unlisted);

    public Task SetStateAsync(
        Guid id,
        LifecycleState state,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        // Mirrors the store's `state NOT IN ('excluded', 'unlisted')` clause.
        if (_games.TryGetValue(id, out var game)
            && game.State is not (LifecycleState.Excluded or LifecycleState.Unlisted))
        {
            _games[id] = game with
            {
                State = state,
                ArchivedAt = state is LifecycleState.Archived ? at : null,
            };
        }

        return Task.CompletedTask;
    }

    private Task Move(Guid id, LifecycleState to, DateTimeOffset at, LifecycleState from)
    {
        if (_games.TryGetValue(id, out var game) && game.State == from)
        {
            _games[id] = game with { State = to, ArchivedAt = null };
        }

        return Task.CompletedTask;
    }

    public Task CorroborateAsync(
        Guid id,
        DateTimeOffset at,
        IReadOnlyList<string> signals,
        CancellationToken cancellationToken = default)
    {
        // Write-once, mirroring the SQL's `WHERE corroborated_at IS NULL`.
        if (signals.Count > 0
            && _games.TryGetValue(id, out var game)
            && game.CorroboratedAt is null)
        {
            _games[id] = game with { CorroboratedAt = at, CorroboratedBy = [.. signals] };
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
        if (_games.TryGetValue(id, out var game)
            && (game.LastReachableAt is null || game.LastReachableAt < at))
        {
            _games[id] = game with { LastReachableAt = at };
        }

        return Task.CompletedTask;
    }

    public Task<string?> RenameAsync(
        Guid id,
        string name,
        string slug,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        if (!_games.TryGetValue(id, out var game))
        {
            return Task.FromResult<string?>(null);
        }

        var retired = string.Equals(game.Slug, slug, StringComparison.Ordinal) ? null : game.Slug;

        if (retired is not null)
        {
            Slugs.Retire(retired, id, at);
        }

        _games[id] = game with { Name = name, Slug = slug };

        return Task.FromResult(retired);
    }

    public Task<IReadOnlyList<GameRecord>> UnarchivedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameRecord>>(
            _games.Values.Where(g => g.State is not LifecycleState.Archived).ToList());
}

/// <summary>
/// The former-slug table in memory (spec §5.7), keyed the way the real one is: on the slug, because
/// one URL can only ever have belonged to one game.
/// </summary>
internal sealed class InMemorySlugHistory(Func<Guid, string?> currentSlug) : ISlugHistoryStore
{
    private readonly Dictionary<string, SlugRetirement> _retired = new(StringComparer.Ordinal);

    /// <summary>Records a retirement, keeping the earliest as <c>ON CONFLICT DO NOTHING</c> does.</summary>
    public void Retire(string slug, Guid gameId, DateTimeOffset at) =>
        _retired.TryAdd(slug, new SlugRetirement(slug, gameId, at));

    public Task<string?> CurrentSlugAsync(
        string formerSlug, CancellationToken cancellationToken = default)
    {
        if (!_retired.TryGetValue(formerSlug, out var row)
            || currentSlug(row.GameId) is not { } current
            || string.Equals(current, formerSlug, StringComparison.Ordinal))
        {
            // Last arm mirrors `g.slug <> h.slug`: a game that took back an old name leaves a row
            // pointing at a slug that's current again, which would otherwise loop a redirect.
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(current);
    }

    public Task<Guid?> RetiredByAsync(string slug, CancellationToken cancellationToken = default) =>
        Task.FromResult<Guid?>(_retired.TryGetValue(slug, out var row) ? row.GameId : null);

    public Task<IReadOnlyList<SlugRetirement>> ForGameAsync(
        Guid gameId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SlugRetirement>>(_retired.Values
            .Where(row => row.GameId == gameId)
            .OrderByDescending(row => row.RetiredAt)
            .ToList());
}

internal sealed class InMemoryReachableHistory : IReachableHistory
{
    public Dictionary<Guid, TimeSpan> FirstParty { get; } = [];

    public Task<TimeSpan> CumulativeReachableAsync(
        Guid gameId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(FirstParty.GetValueOrDefault(gameId));
}
