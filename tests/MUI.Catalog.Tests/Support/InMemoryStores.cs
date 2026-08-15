using MUI.Catalog;

namespace MUI.Catalog.Tests.Support;

/// <summary>
/// In-memory implementations of the catalogue's stores.
/// </summary>
/// <remarks>
/// The rule these obey, and the reason they exist: <b>a fake must never be more lenient than the
/// real thing</b>. A test that passes against a permissive fake and fails against Postgres has
/// taught us nothing and cost us a debugging session. Where the real store will have a key or a
/// constraint, these have the same one.
/// </remarks>
internal sealed class InMemoryGameFieldStore : IGameFieldStore
{
    private readonly Dictionary<(Guid, string, FieldSource), GameField> _fields = [];

    public List<FieldChange> Changes { get; } = [];

    public Task<IReadOnlyList<GameField>> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameField>>(
            _fields.Values.Where(f => f.GameId == gameId).ToList());

    public Task<IReadOnlyList<GameField>> ForGameAsync(
        Guid gameId,
        FieldSource only,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameField>>(
            [.. _fields.Values.Where(f => f.GameId == gameId && f.Source == only)]);

    public Task UpsertAsync(GameField field, CancellationToken cancellationToken = default)
    {
        // Same key as the real table: (game, field, source). A fake keyed only on (game, field)
        // would silently collapse measured and declared into one row and make the capability
        // matrix untestable.
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
}

internal sealed class InMemoryPresenceStore : IPresenceStore
{
    public List<PresenceSample> Samples { get; } = [];

    public Task AppendAsync(PresenceSample sample, CancellationToken cancellationToken = default)
    {
        Samples.Add(sample);
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

internal sealed class InMemoryAvailabilityStore : IAvailabilityStore
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
}
