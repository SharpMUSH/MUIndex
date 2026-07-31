using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Discovery;

namespace MUI.Import;

/// <summary>
/// Everything an import may write, behind one seam.
/// </summary>
/// <remarks>
/// Commit versus dry run is this type rather than an <c>if (dryRun)</c> at each of eight call sites.
/// A dry run that forgot one of them would write to a production database while reporting that it had
/// not, which is the worst failure this component has available to it.
/// </remarks>
public interface IImportWriter
{
    Task AddCrawlTargetAsync(CrawlTarget target, CancellationToken cancellationToken);

    Task UpsertEndpointAsync(GameEndpoint endpoint, CancellationToken cancellationToken);

    Task UpsertFieldAsync(GameField field, CancellationToken cancellationToken);

    Task AppendChangeAsync(FieldChange change, CancellationToken cancellationToken);

    Task AppendPresenceAsync(PresenceSample sample, CancellationToken cancellationToken);

    Task WriteClosedAvailabilityAsync(
        Guid gameId,
        AvailabilityState state,
        FailureCause cause,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);

    Task RecordProvenanceAsync(ImportProvenance provenance, CancellationToken cancellationToken);
}

/// <summary>The writer that actually writes.</summary>
public sealed class CommittingImportWriter(
    ICrawlTargetRepository targets,
    IEndpointStore endpoints,
    IGameFieldStore fields,
    IPresenceStore presence,
    IImportedAvailabilityWriter availability,
    IImportProvenanceStore provenance) : IImportWriter
{
    private readonly ICrawlTargetRepository _targets = targets ?? throw new ArgumentNullException(nameof(targets));
    private readonly IEndpointStore _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
    private readonly IGameFieldStore _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IPresenceStore _presence = presence ?? throw new ArgumentNullException(nameof(presence));

    private readonly IImportedAvailabilityWriter _availability =
        availability ?? throw new ArgumentNullException(nameof(availability));

    private readonly IImportProvenanceStore _provenance =
        provenance ?? throw new ArgumentNullException(nameof(provenance));

    public Task AddCrawlTargetAsync(CrawlTarget target, CancellationToken cancellationToken) =>
        _targets.AddAsync(target, cancellationToken);

    public Task UpsertEndpointAsync(GameEndpoint endpoint, CancellationToken cancellationToken) =>
        _endpoints.UpsertAsync(endpoint, cancellationToken);

    public Task UpsertFieldAsync(GameField field, CancellationToken cancellationToken) =>
        _fields.UpsertAsync(field, cancellationToken);

    public Task AppendChangeAsync(FieldChange change, CancellationToken cancellationToken) =>
        _fields.RecordChangeAsync(change, cancellationToken);

    public Task AppendPresenceAsync(PresenceSample sample, CancellationToken cancellationToken) =>
        _presence.AppendAsync(sample, cancellationToken);

    public Task WriteClosedAvailabilityAsync(
        Guid gameId,
        AvailabilityState state,
        FailureCause cause,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        _availability.WriteClosedAsync(gameId, state, cause, from, to, cancellationToken);

    public Task RecordProvenanceAsync(ImportProvenance provenance, CancellationToken cancellationToken) =>
        _provenance.RecordAsync(provenance, cancellationToken);
}

/// <summary>
/// The writer that writes nothing and remembers everything, so <c>--dry-run</c> can print the same
/// report the real run would produce.
/// </summary>
public sealed class DryRunImportWriter : IImportWriter
{
    public List<CrawlTarget> Targets { get; } = [];

    public List<GameEndpoint> Endpoints { get; } = [];

    public List<GameField> Fields { get; } = [];

    public List<FieldChange> Changes { get; } = [];

    public List<PresenceSample> Presence { get; } = [];

    public List<(Guid GameId, AvailabilityState State, DateTimeOffset From, DateTimeOffset To)> Availability { get; }
        = [];

    public List<ImportProvenance> Provenance { get; } = [];

    public Task AddCrawlTargetAsync(CrawlTarget target, CancellationToken cancellationToken)
    {
        Targets.Add(target);

        return Task.CompletedTask;
    }

    public Task UpsertEndpointAsync(GameEndpoint endpoint, CancellationToken cancellationToken)
    {
        Endpoints.Add(endpoint);

        return Task.CompletedTask;
    }

    public Task UpsertFieldAsync(GameField field, CancellationToken cancellationToken)
    {
        Fields.Add(field);

        return Task.CompletedTask;
    }

    public Task AppendChangeAsync(FieldChange change, CancellationToken cancellationToken)
    {
        Changes.Add(change);

        return Task.CompletedTask;
    }

    public Task AppendPresenceAsync(PresenceSample sample, CancellationToken cancellationToken)
    {
        Presence.Add(sample);

        return Task.CompletedTask;
    }

    public Task WriteClosedAvailabilityAsync(
        Guid gameId,
        AvailabilityState state,
        FailureCause cause,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        Availability.Add((gameId, state, from, to));

        return Task.CompletedTask;
    }

    public Task RecordProvenanceAsync(ImportProvenance provenance, CancellationToken cancellationToken)
    {
        Provenance.Add(provenance);

        return Task.CompletedTask;
    }
}
