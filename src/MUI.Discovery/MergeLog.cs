namespace MUI.Discovery;

/// <summary>
/// One merge, and the evidence for it (spec §7.3).
/// </summary>
/// <remarks>
/// <b>A merge is a redirect, not a move.</b> It does not carry endpoint rows, field rows or history
/// from one game to another: it records this row and points the absorbed game at the surviving one. Its
/// page redirects, listings skip it, and its own history stays exactly where it was. Reverting is
/// therefore clearing one pointer, which is why "merges are reversible and logged" costs no bookkeeping
/// and cannot half-fail — and it is the only representation consistent with nothing ever being deleted.
/// </remarks>
public sealed record MergeRecord(
    Guid Id,
    Guid IntoGameId,
    Guid FromGameId,
    double Score,
    string SignalsJson,
    DateTimeOffset At,
    DateTimeOffset? RevertedAt,
    string? Reason = null)
{
    /// <summary>Whether this merge is still in force.</summary>
    public bool IsInForce => RevertedAt is null;
}

/// <summary>
/// The merge audit trail. Recording a merge <em>is</em> performing it: an implementation writes the
/// row and the redirect together, so a merge cannot exist unlogged and a log entry cannot describe a
/// merge that did not happen.
/// </summary>
public interface IMergeLog
{
    Task<Guid> RecordAsync(MergeRecord record, CancellationToken ct);

    /// <summary>
    /// Undoes the redirect and stamps the row. Reverting twice must not rewrite when the first revert
    /// happened.
    /// </summary>
    Task RevertAsync(Guid mergeId, DateTimeOffset at, CancellationToken ct);

    /// <summary>Every merge this game was on either side of, reverted or not. Nothing is deleted.</summary>
    Task<IReadOnlyList<MergeRecord>> ForGameAsync(Guid gameId, CancellationToken ct);
}
