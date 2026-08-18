namespace MUI.Catalog.Persistence;

/// <summary>
/// Extends the open interval, or closes it and opens a new one (spec §5.3).
/// </summary>
/// <remarks>
/// <b>Only a change of state or cause writes a transition.</b> A hundred consecutive timeouts are one
/// interval, not a hundred: a game reachable for three years is one open row, which is what makes
/// "reachable over 90 days" and "longest outage" arithmetic over a handful of rows rather than a scan
/// of twenty-six thousand samples.
/// </remarks>
public sealed class AvailabilityWriter(IAvailabilityStore store) : IAvailabilityWriter
{
    public async Task<AvailabilityOutcome> ObserveAsync(
        Guid gameId,
        AvailabilityState state,
        FailureCause cause,
        DateTimeOffset at,
        string? detail = null,
        CancellationToken cancellationToken = default)
    {
        // 'none' is what a reachable interval carries; a failure that reported no cause would be a
        // decision of ours recorded as a measurement of theirs, which rule 5 forbids.
        if (state is AvailabilityState.Reachable && cause is not FailureCause.None)
        {
            throw new ArgumentException(
                "A reachable interval carries no failure cause.", nameof(cause));
        }

        // Same rule for the evidence as for the cause: `detail` is what a failed dial said, and a
        // reachable interval has no dial it could be about (rule 5).
        if (state is AvailabilityState.Reachable && detail is not null)
        {
            throw new ArgumentException(
                "A reachable interval carries no failure detail.", nameof(detail));
        }

        if (state is not AvailabilityState.Reachable && cause is FailureCause.None)
        {
            throw new ArgumentException(
                "An interval that is not reachable must say what stopped us.", nameof(cause));
        }

        var open = await store.OpenIntervalAsync(gameId, cancellationToken);

        if (open is null)
        {
            await store.OpenAsync(
                new AvailabilityInterval { GameId = gameId, State = state, FromAt = at, Cause = cause, Detail = detail },
                cancellationToken);

            return AvailabilityOutcome.Opened;
        }

        if (open.State == state && open.Cause == cause)
        {
            // Nothing is written: the open interval already says this.
            return AvailabilityOutcome.Extended;
        }

        await store.CloseAsync(gameId, at, cancellationToken);
        await store.OpenAsync(
            new AvailabilityInterval { GameId = gameId, State = state, FromAt = at, Cause = cause, Detail = detail },
            cancellationToken);

        return AvailabilityOutcome.Transitioned;
    }
}
