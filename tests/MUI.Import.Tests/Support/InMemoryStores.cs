using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Discovery;

namespace MUI.Import.Tests.Support;

/// <summary>
/// Stands-in for everything an import writes through.
/// </summary>
/// <remarks>
/// The rule these obey: <b>a fake must never be more lenient than the real thing</b>. Hosts are
/// canonicalised on both ends of the endpoint store because <see cref="IEndpointStore"/> says its
/// implementations do, and a fake that compared more leniently would pass every test while production
/// minted a duplicate endpoint for a host spelled in capitals. The behaviour that finally rests on a
/// column — <c>availability_interval.origin</c> — is asserted here <em>and</em> against a real
/// Postgres, because a fake agreeing with the code proves only that they were written by one person.
/// </remarks>
internal sealed class InMemoryCrawlTargetRepository : ICrawlTargetRepository
{
    public List<CrawlTarget> Targets { get; } = [];

    public Task<CrawlTarget?> ByAddressAsync(string host, int port, CancellationToken ct) =>
        Task.FromResult(Targets.FirstOrDefault(
            target => CanonicalHost.Same(target.Host, host) && target.Port == port));

    public Task<Guid> AddAsync(CrawlTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        var existing = Targets.FirstOrDefault(
            candidate => CanonicalHost.Same(candidate.Host, target.Host) && candidate.Port == target.Port);

        if (existing is not null)
        {
            return Task.FromResult(existing.Id);
        }

        Targets.Add(target);

        return Task.FromResult(target.Id);
    }

    public Task<IReadOnlyList<CrawlTarget>> DueAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CrawlTarget>>(
            Targets.Where(target => target.NextProbeAt <= now).Take(limit).ToList());

    public Task RecordAttemptAsync(
        Guid id,
        DateTimeOffset at,
        bool succeeded,
        TimeSpan? crawlDelay,
        DateTimeOffset nextProbeAt,
        CancellationToken ct) =>
        throw new InvalidOperationException(
            "An import may never record a probe attempt: that is scheduling, and it belongs to the crawler (§7.1).");

    public Task AttachGameAsync(Guid id, Guid gameId, CancellationToken ct)
    {
        var index = Targets.FindIndex(target => target.Id == id);
        if (index >= 0)
        {
            Targets[index] = Targets[index] with { GameId = gameId };
        }

        return Task.CompletedTask;
    }
}

internal sealed class InMemoryGameStore : IGameStore
{
    private readonly Dictionary<Guid, GameRecord> _games = [];

    public void Seed(GameRecord game) => _games[game.Id] = game;

    public Task<GameRecord?> ByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_games.GetValueOrDefault(id));

    public Task<GameRecord?> BySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        Task.FromResult(_games.Values.FirstOrDefault(game => game.Slug == slug));

    public Task InsertAsync(GameRecord game, CancellationToken cancellationToken = default)
    {
        _games[game.Id] = game;

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
            _games[id] = game with { State = state };
        }

        return Task.CompletedTask;
    }

    public Task MarkReachableAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<GameRecord>> UnarchivedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameRecord>>(_games.Values.ToList());
}

internal sealed class InMemoryEndpointStore : IEndpointStore
{
    public List<GameEndpoint> Endpoints { get; } = [];

    public Task<IReadOnlyList<GameEndpoint>> ForGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameEndpoint>>(
            Endpoints.Where(endpoint => endpoint.GameId == gameId).ToList());

    public Task<GameEndpoint?> ByAddressAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var normalised = HostName.Normalize(host);

        return Task.FromResult(Endpoints.FirstOrDefault(
            endpoint => endpoint.Host == normalised && endpoint.Port == port));
    }

    public Task UpsertAsync(GameEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var stored = endpoint with { Host = HostName.Normalize(endpoint.Host) };
        var index = Endpoints.FindIndex(
            candidate => candidate.Host == stored.Host && candidate.Port == stored.Port);

        if (index >= 0)
        {
            Endpoints[index] = stored;
        }
        else
        {
            Endpoints.Add(stored);
        }

        return Task.CompletedTask;
    }
}

internal sealed class InMemoryGameFieldStore : IGameFieldStore
{
    public List<GameField> Fields { get; } = [];

    public List<FieldChange> Changes { get; } = [];

    public Task<IReadOnlyList<GameField>> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GameField>>(Fields.Where(field => field.GameId == gameId).ToList());

    public Task UpsertAsync(GameField field, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);

        // Keyed (game, field, source), the same as the table. A fake keyed on (game, field) alone
        // would let an import overwrite what a probe measured and no test would notice.
        var index = Fields.FindIndex(candidate =>
            candidate.GameId == field.GameId
            && candidate.Field == field.Field
            && candidate.Source == field.Source);

        if (index >= 0)
        {
            Fields[index] = field;
        }
        else
        {
            Fields.Add(field);
        }

        return Task.CompletedTask;
    }

    public Task RecordChangeAsync(FieldChange change, CancellationToken cancellationToken = default)
    {
        Changes.Add(change);

        return Task.CompletedTask;
    }
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
            Samples.Where(sample => sample.GameId == gameId && sample.At >= from && sample.At <= to).ToList());
}

/// <summary>
/// The imported availability writer, recording the origin it was asked for.
/// </summary>
/// <remarks>
/// There is only one origin it can record, which is the point: this interface exists so that the
/// first-party write path cannot be reached from an importer at all.
/// </remarks>
internal sealed class InMemoryImportedAvailabilityWriter : IImportedAvailabilityWriter
{
    public List<(Guid GameId, AvailabilityState State, FailureCause Cause, DateTimeOffset From, DateTimeOffset To)>
        Intervals
    { get; } = [];

    public Task WriteClosedAsync(
        Guid gameId,
        AvailabilityState state,
        FailureCause cause,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        Intervals.Add((gameId, state, cause, from, to));

        return Task.CompletedTask;
    }

    /// <summary>The sum §7.5 weights at half — computed here the way the database computes it.</summary>
    public TimeSpan CumulativeImportedMeasuredReachable(Guid gameId) =>
        Intervals
            .Where(interval => interval.GameId == gameId && interval.State is AvailabilityState.Reachable)
            .Aggregate(TimeSpan.Zero, (total, interval) => total + (interval.To - interval.From));
}

internal sealed class InMemoryImportProvenanceStore : IImportProvenanceStore
{
    public List<ImportProvenance> Rows { get; } = [];

    public Task<bool> ExistsAsync(
        Guid gameId,
        string sourceName,
        ImportSubjectKind subject,
        string? subjectKey,
        DateTimeOffset? subjectAt,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.Any(row =>
            row.GameId == gameId
            && row.SourceName == sourceName
            && row.Subject == subject
            && row.SubjectKey == subjectKey
            && row.SubjectAt == subjectAt));

    public Task RecordAsync(ImportProvenance provenance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        Rows.Add(provenance);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SourceContribution>> ContributionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SourceContribution>>(
            Rows
                .GroupBy(row => (row.SourceName, row.Tier))
                .Select(group => new SourceContribution(
                    group.Key.SourceName,
                    group.Key.Tier,
                    group.Count(),
                    group.Min(row => row.ImportedAt),
                    group.Max(row => row.ImportedAt)))
                .ToList());
}
