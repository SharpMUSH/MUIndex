using MUI.Catalog;
using MUI.Catalog.Persistence;

namespace MUI.Import;

/// <summary>
/// The one availability write an import is allowed to make: a <b>closed</b> span stamped
/// <c>origin = 'imported_measured'</c>.
/// </summary>
/// <remarks>
/// <para>
/// This interface exists because <see cref="IAvailabilityStore.OpenAsync(AvailabilityInterval,
/// CancellationToken)"/> defaults the column to <c>first_party</c>, and calling it from here would
/// credit a third party's history at <em>full</em> weight — the exact opposite of §7.5, and silently.
/// Making the imported write a different method on a different interface means the wrong one cannot
/// be reached by habit.
/// </para>
/// <para>
/// The span is always closed. An open interval means "and it is still like this, because we are
/// watching"; we read a file, and the partial unique index would refuse a second open row anyway.
/// </para>
/// </remarks>
public interface IImportedAvailabilityWriter
{
    Task WriteClosedAsync(
        Guid gameId,
        AvailabilityState state,
        FailureCause cause,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Adapts <see cref="NpgsqlAvailabilityStore"/>'s origin-bearing overload, which the interface in
/// <c>MUI.Catalog</c> does not expose.
/// </summary>
public sealed class ImportedAvailabilityWriter(NpgsqlAvailabilityStore store) : IImportedAvailabilityWriter
{
    private readonly NpgsqlAvailabilityStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public Task WriteClosedAsync(
        Guid gameId,
        AvailabilityState state,
        FailureCause cause,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default) =>
        _store.OpenAsync(
            new AvailabilityInterval
            {
                GameId = gameId,
                State = state,
                FromAt = from,
                ToAt = to,
                Cause = cause,
            },
            IntervalOrigin.ImportedMeasured,
            cancellationToken);
}
