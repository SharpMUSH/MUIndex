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

    public FakeGameStore() => Slugs = new FakeSlugHistory(id => _games.GetValueOrDefault(id)?.Slug);

    /// <summary>
    /// Makes every rename raise, standing in for the race the mint cannot close: the check asks
    /// whether a slug is taken and the write happens a moment later, so two games settling on one
    /// name in the same cycle collide at the unique index.
    /// </summary>
    public bool RenamesFail { get; set; }

    public IReadOnlyCollection<GameRecord> All => _games.Values.ToList();

    /// <summary>
    /// The slugs this store has retired (spec §5.7). Held here because the real
    /// <see cref="RenameAsync"/> writes the retirement and the re-mint in one statement, and a fake
    /// that renamed a game without retiring its URL would be more lenient than the real thing.
    /// </summary>
    public FakeSlugHistory Slugs { get; }

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

    public Task CorroborateAsync(
        Guid id,
        DateTimeOffset at,
        IReadOnlyList<string> signals,
        CancellationToken cancellationToken = default)
    {
        // Write-once, as the SQL is: the UPDATE carries `WHERE corroborated_at IS NULL`, and a fake
        // that overwrote would let a test pass against behaviour the database refuses.
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
        if (_games.TryGetValue(id, out var game) && (game.LastReachableAt is null || game.LastReachableAt < at))
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

        if (RenamesFail
            || _games.Values.Any(g => g.Id != id && string.Equals(g.Slug, slug, StringComparison.Ordinal)))
        {
            // The real table's unique index on game.slug, which is what stops two games answering to
            // one URL. A fake without it would let a caller mint a collision and never hear about it.
            throw new InvalidOperationException($"Another game already answers to '{slug}'.");
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
/// The former-slug table in memory (spec §5.7), keyed on the slug as the real one is: one URL can
/// only ever have belonged to one game.
/// </summary>
public sealed class FakeSlugHistory(Func<Guid, string?> currentSlug) : ISlugHistoryStore
{
    private readonly Dictionary<string, SlugRetirement> _retired = new(StringComparer.Ordinal);

    public IReadOnlyCollection<SlugRetirement> All => _retired.Values.ToList();

    /// <summary>Records a retirement, keeping the earliest as <c>ON CONFLICT DO NOTHING</c> does.</summary>
    public void Retire(string slug, Guid gameId, DateTimeOffset at) =>
        _retired.TryAdd(slug, new SlugRetirement(slug, gameId, at));

    public Task<string?> CurrentSlugAsync(
        string formerSlug, CancellationToken cancellationToken = default)
    {
        // The last arm is the real query's `g.slug <> h.slug`: a game that took back a name it used
        // to have leaves a row pointing at a slug that is current again, and answering with it would
        // send a reader round a redirect loop.
        if (!_retired.TryGetValue(formerSlug, out var row)
            || currentSlug(row.GameId) is not { } current
            || string.Equals(current, formerSlug, StringComparison.Ordinal))
        {
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

    /// <summary>Across every source, and folded on the field name, as the real query is.</summary>
    public Task<DateTimeOffset?> LastChangedAtAsync(
        Guid gameId, string field, CancellationToken cancellationToken = default) =>
        Task.FromResult(Changes
            .Where(c => c.GameId == gameId
                        && string.Equals(c.Field, field, StringComparison.OrdinalIgnoreCase))
            .Select(c => (DateTimeOffset?)c.At)
            .DefaultIfEmpty(null)
            .Max());

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

    /// <summary>The former-slug table these stores share (spec §5.7).</summary>
    public FakeSlugHistory Slugs => Games.Slugs;

    /// <summary>§5.7's re-mint, at whatever grace the test wants to reason about.</summary>
    public SlugMinter Minter(TimeSpan? grace = null, bool renamesFail = false)
    {
        Games.RenamesFail = renamesFail;
        return new SlugMinter(Games, Fields, Slugs, grace);
    }

    public ProbeIngestor Ingestor(TimeSpan? grace = null, bool renamesFail = false) => new(
        new PresenceWriter(Presence),
        new AvailabilityWriter(Availability),
        new FieldReconciler(Fields),
        Games,
        new ArchiveSweeper(Games, Availability, Availability),
        Minter(grace, renamesFail));

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
