using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Discovery;

namespace MUI.Import;

/// <summary>
/// The one pass an import makes: resolve identity, seed crawl targets, and — only for a game we
/// already know — write endpoints, fields and whatever history the tier permits.
/// </summary>
/// <remarks>
/// <para>
/// Two structural rules run through this class rather than sitting in comments. Commit versus dry run
/// is <see cref="IImportWriter"/>. Measured versus asserted is <see cref="IHistorySink"/>: the sink
/// this pipeline is handed for an asserted source holds nothing it could write with, so no amount of
/// history in an <see cref="ImportedGame"/> can reach a table.
/// </para>
/// <para>
/// It never calls <see cref="ICrawlTargetRepository.RecordAttemptAsync"/>. An import says an address
/// exists; the scheduler decides when it is visited (spec §7.1), and calling that method from here
/// would be an importer scheduling a probe.
/// </para>
/// </remarks>
public sealed class ImportPipeline(
    ICrawlTargetRepository targets,
    IGameStore games,
    IEndpointStore endpoints,
    IGameFieldStore fields,
    IPresenceStore presence,
    IImportedAvailabilityWriter availability,
    IImportProvenanceStore provenance,
    TimeProvider time)
{
    private readonly ICrawlTargetRepository _targets = targets ?? throw new ArgumentNullException(nameof(targets));
    private readonly IGameStore _games = games ?? throw new ArgumentNullException(nameof(games));
    private readonly IEndpointStore _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
    private readonly IGameFieldStore _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));
    private readonly ImportIdentity _identity = new(endpoints);

    private readonly IImportProvenanceStore _provenance =
        provenance ?? throw new ArgumentNullException(nameof(provenance));

    private readonly IImportWriter _committing =
        new CommittingImportWriter(targets, endpoints, fields, presence, availability, provenance);

    /// <summary>Reads the source and writes what it is entitled to write.</summary>
    public Task<ImportReport> RunAsync(IDirectorySource source, CancellationToken cancellationToken) =>
        ExecuteAsync(source, _committing, cancellationToken);

    /// <summary>Reads the source, reports what it would have written, and writes nothing.</summary>
    public Task<ImportReport> DryRunAsync(IDirectorySource source, CancellationToken cancellationToken) =>
        ExecuteAsync(source, new DryRunImportWriter(), cancellationToken);

    /// <summary>A dry run whose writer the caller keeps, so it can be inspected row by row.</summary>
    public Task<ImportReport> DryRunAsync(
        IDirectorySource source,
        DryRunImportWriter writer,
        CancellationToken cancellationToken) =>
        ExecuteAsync(source, writer, cancellationToken);

    private async Task<ImportReport> ExecuteAsync(
        IDirectorySource source,
        IImportWriter writer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);

        // Refuse before reading a byte, and refuse the same way a dry run does — a run that is not
        // entitled to fetch is not entitled to fetch for a rehearsal either.
        var decision = EtiquettePlanner.Decide(source.Etiquette);
        if (decision.Route is FetchRoute.None)
        {
            throw new EtiquetteViolationException($"{source.SourceName}: {decision.RefusedReason}.");
        }

        var now = _time.GetUtcNow();
        var history = HistorySink.For(source.Tier, writer, _provenance, now);
        var notes = new List<string>();

        var seen = 0;
        var matched = 0;
        var targetsAdded = 0;
        var endpointsWritten = 0;
        var fieldsWritten = 0;
        var presenceRows = 0;
        var availabilityRows = 0;
        var rejected = 0;

        await foreach (var record in source.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            seen++;

            var match = await _identity.ResolveAsync(record, cancellationToken).ConfigureAwait(false);
            targetsAdded += await SeedTargetsAsync(writer, record, match, now, cancellationToken).ConfigureAwait(false);

            if (match.GameId is not { } gameId)
            {
                // Not a failure, and the ordinary case on day one: a host becomes a listed game by
                // answering for itself (spec §7.2). We have put it where the crawler will find it.
                continue;
            }

            matched++;
            endpointsWritten += await WriteEndpointsAsync(writer, gameId, source, record, now, cancellationToken)
                .ConfigureAwait(false);
            fieldsWritten += await WriteFieldsAsync(writer, gameId, source, record, now, cancellationToken)
                .ConfigureAwait(false);

            var written = await history.WriteAsync(gameId, record, cancellationToken).ConfigureAwait(false);
            presenceRows += written.PresenceRows;
            availabilityRows += written.AvailabilityRows;
            rejected += written.Refused;

            if (written.Refused > 0)
            {
                var game = await _games.ByIdAsync(gameId, cancellationToken).ConfigureAwait(false);
                notes.Add(
                    $"{game?.Name ?? record.Name}: refused {written.Refused} history rows — "
                    + $"{source.SourceName} is an asserted source and earns no history, presence or grace (§7.6).");
            }
        }

        if (seen > matched)
        {
            notes.Add(
                $"{seen - matched} of {seen} listings matched no address we already know — "
                + "seeded as crawl targets and not listed (§7.2).");
        }

        return new ImportReport(source.SourceName, source.Tier, seen, targetsAdded, endpointsWritten,
            fieldsWritten, presenceRows, availabilityRows, rejected, matched, notes);
    }

    private async Task<int> SeedTargetsAsync(
        IImportWriter writer,
        ImportedGame record,
        ImportMatch match,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var added = 0;

        foreach (var endpoint in record.Endpoints)
        {
            // Canonicalised here, once, so the crawl target and the endpoint row this import writes
            // carry the same spelling. IEndpointStore normalises for itself; a CrawlTarget is written
            // straight through, and two spellings there are two targets — and two probes — for one
            // machine at somebody else's server.
            var host = HostName.Normalize(endpoint.Host);

            if (await _targets.ByAddressAsync(host, endpoint.Port, cancellationToken).ConfigureAwait(false)
                is not null)
            {
                continue;
            }

            await writer.AddCrawlTargetAsync(
                new CrawlTarget
                {
                    Id = Guid.NewGuid(),
                    GameId = match.GameId,
                    Host = host,
                    Port = endpoint.Port,
                    UseTls = endpoint.Kind is EndpointKind.Tls,

                    // Due now, and nothing else: an import says an address exists and the scheduler
                    // decides when it is actually visited (spec §7.1).
                    NextProbeAt = now,
                    FirstSeenAt = now,

                    // IsOperatorSeed is not mentioned, which is the security-relevant half of this
                    // block: an import is a stranger's list and gets the guarded default (§7.2).
                },
                cancellationToken).ConfigureAwait(false);

            added++;
        }

        return added;
    }

    private async Task<int> WriteEndpointsAsync(
        IImportWriter writer,
        Guid gameId,
        IDirectorySource source,
        ImportedGame record,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var written = 0;

        foreach (var endpoint in record.Endpoints)
        {
            var known = await _endpoints
                .ByAddressAsync(endpoint.Host, endpoint.Port, cancellationToken)
                .ConfigureAwait(false);

            // Stale, not Active, for an address we have not reached ourselves. `last_seen_at` on an
            // imported endpoint is when the third party saw it, and calling that Active would put
            // somebody else's sighting into our own "this answers" vocabulary.
            await writer.UpsertEndpointAsync(
                new GameEndpoint(
                    gameId,
                    HostName.Normalize(endpoint.Host),
                    endpoint.Port,
                    endpoint.Kind,
                    known?.FirstSeenAt ?? now,
                    known?.LastSeenAt ?? now,
                    known?.State ?? EndpointState.Stale),
                cancellationToken).ConfigureAwait(false);

            await writer.RecordProvenanceAsync(
                ImportProvenance.ForEndpoint(gameId, endpoint, record, source.Tier, now),
                cancellationToken).ConfigureAwait(false);

            written++;
        }

        return written;
    }

    private async Task<int> WriteFieldsAsync(
        IImportWriter writer,
        Guid gameId,
        IDirectorySource source,
        ImportedGame record,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var importedSource = ImportTierMap.SourceFor(source.Tier);

        // Rows for THIS source only. §5.1 keys a field by (game, field, source), so an import never
        // overwrites what a probe measured — it writes its own row beside it and loses the precedence
        // ladder at read time. Comparing against the winning row instead would make an import that
        // agrees with MSSP look like a change.
        var existing = (await _fields.ForGameAsync(gameId, cancellationToken).ConfigureAwait(false))
            .Where(field => field.Source == importedSource)
            .ToDictionary(field => field.Field, StringComparer.Ordinal);

        var written = 0;

        foreach (var (name, value) in record.Fields)
        {
            existing.TryGetValue(name, out var incumbent);

            if (incumbent is not null && string.Equals(incumbent.Value, value, StringComparison.Ordinal))
            {
                // Confirm: bump last_confirmed_at and write nothing else (spec §5.1).
                await writer.UpsertFieldAsync(
                    incumbent with { LastConfirmedAt = now },
                    cancellationToken).ConfigureAwait(false);

                continue;
            }

            if (incumbent is not null)
            {
                await writer.AppendChangeAsync(
                    new FieldChange(gameId, name, importedSource, incumbent.Value, value, now),
                    cancellationToken).ConfigureAwait(false);
            }

            await writer.UpsertFieldAsync(
                new GameField(gameId, name, importedSource, value, incumbent?.FirstSeenAt ?? now, now),
                cancellationToken).ConfigureAwait(false);

            await writer.RecordProvenanceAsync(
                ImportProvenance.ForField(gameId, name, record, source.Tier, now),
                cancellationToken).ConfigureAwait(false);

            written++;
        }

        return written;
    }
}
