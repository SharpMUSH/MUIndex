using MUI.Catalog;

namespace MUI.Import;

/// <summary>
/// The measured tier's sink: a third party that ran its own probe produced a measurement, and this
/// writes it as one (spec §7.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Two stamps, doing two different jobs.</b> The <c>import_provenance</c> row records which site
/// the value came from and when we took it — §7.6's provenance chip. The availability row's own
/// <c>origin = 'imported_measured'</c> is what §7.5's half weight is computed from, and it is written
/// through <see cref="IImportedAvailabilityWriter"/> rather than
/// <c>IAvailabilityStore.OpenAsync</c>, which would default the column to <c>first_party</c> and
/// credit somebody else's history at full weight. Neither stamp can do the other's job, and grace is
/// never computed from the sidecar.
/// </para>
/// <para>
/// A presence row imported here carries no <c>aggregates</c>: §11's idle histogram and unique-player
/// estimate need a per-player <c>WHO</c> read, which an import never had. Writing an empty object
/// there would say we looked and found nothing.
/// </para>
/// </remarks>
public sealed class MeasuredHistorySink(
    IImportWriter writer,
    IImportProvenanceStore provenance,
    DateTimeOffset importedAt) : IHistorySink
{
    private readonly IImportWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    private readonly IImportProvenanceStore _provenance =
        provenance ?? throw new ArgumentNullException(nameof(provenance));

    public async Task<HistoryWrite> WriteAsync(
        Guid gameId,
        ImportedGame game,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var presenceRows = 0;

        foreach (var sample in game.Presence)
        {
            if (await Already(gameId, game, ImportSubjectKind.Presence, sample.At, cancellationToken)
                    .ConfigureAwait(false))
            {
                continue;
            }

            await _writer.AppendPresenceAsync(
                new PresenceSample
                {
                    GameId = gameId,
                    At = sample.At,
                    Count = sample.Count,
                    Source = FieldSource.ImportedMeasured,
                },
                cancellationToken).ConfigureAwait(false);

            await _writer.RecordProvenanceAsync(
                ImportProvenance.ForHistory(gameId, ImportSubjectKind.Presence, sample.At, game, importedAt),
                cancellationToken).ConfigureAwait(false);

            presenceRows++;
        }

        var availabilityRows = 0;

        foreach (var span in game.Availability)
        {
            if (await Already(gameId, game, ImportSubjectKind.Availability, span.From, cancellationToken)
                    .ConfigureAwait(false))
            {
                continue;
            }

            await _writer.WriteClosedAvailabilityAsync(
                gameId,
                span.Reachable ? AvailabilityState.Reachable : AvailabilityState.Unreachable,
                // No cause, either way. We did not measure why somebody else's probe failed and must
                // not guess: `timeout` or `dns` here would be our invention about their socket, in a
                // game's public reachability history, which is the same class of lie as recording an
                // unparseable WHO as zero players. FailureCause.None reads as "none recorded", and
                // an imported span is told apart from ours by its origin column rather than by this.
                FailureCause.None,
                span.From,
                span.To ?? importedAt,
                cancellationToken).ConfigureAwait(false);

            await _writer.RecordProvenanceAsync(
                ImportProvenance.ForHistory(gameId, ImportSubjectKind.Availability, span.From, game, importedAt),
                cancellationToken).ConfigureAwait(false);

            availabilityRows++;
        }

        return new HistoryWrite(presenceRows, availabilityRows, 0);
    }

    private Task<bool> Already(
        Guid gameId,
        ImportedGame game,
        ImportSubjectKind subject,
        DateTimeOffset at,
        CancellationToken cancellationToken) =>
        _provenance.ExistsAsync(gameId, game.SourceName, subject, null, at, cancellationToken);
}
