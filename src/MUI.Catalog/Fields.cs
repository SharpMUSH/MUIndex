namespace MUI.Catalog;

/// <summary>
/// One source's answer for one field of one game (spec §5.1). Keyed by source as well as field,
/// because the capability matrix shows measured beside declared and each needs its own age.
/// </summary>
public sealed record GameField(
    Guid GameId,
    string Field,
    FieldSource Source,
    string Value,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastConfirmedAt);

/// <summary>A field that actually moved. Only transitions are recorded (spec §5.1).</summary>
public sealed record FieldChange(
    Guid GameId,
    string Field,
    FieldSource Source,
    string? OldValue,
    string NewValue,
    DateTimeOffset At);

/// <summary>
/// What a probe observed about one field, before anything is known about what is already stored.
/// </summary>
public sealed record FieldObservation(string Field, FieldSource Source, string Value);

/// <summary>
/// How long a field may go unconfirmed before it is stale (spec §5.6).
/// </summary>
/// <remarks>
/// "Old" is not one duration: a player count is stale in hours, a hand-typed MSSP <c>GENRE</c> is
/// unremarkable at six months. The window lives beside the field definition because the API, the
/// plain-text surface and the rendered page must all agree on it, and only one of them is a front
/// end.
/// </remarks>
public sealed record FieldDefinition(
    string Name,
    TimeSpan ExpectedRefresh,
    bool OwnerEnrichable = false);

/// <summary>The catalogue's field vocabulary and their staleness windows.</summary>
public interface IFieldRegistry
{
    /// <summary>The definition for <paramref name="field"/>, or null if it is not a known field.</summary>
    FieldDefinition? Find(string field);

    /// <summary>
    /// Whether a value last confirmed at <paramref name="lastConfirmedAt"/> has aged past its own
    /// window. An unknown field is never stale — we cannot judge a field we do not define.
    /// </summary>
    bool IsStale(string field, DateTimeOffset lastConfirmedAt, DateTimeOffset now);
}

/// <summary>Reads and writes <see cref="GameField"/> rows. Storage-agnostic by construction.</summary>
public interface IGameFieldStore
{
    Task<IReadOnlyList<GameField>> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the row for <c>(game, field, source)</c>, or inserts it.</summary>
    Task UpsertAsync(GameField field, CancellationToken cancellationToken = default);

    /// <summary>Appends a transition. Never called when a value merely repeats.</summary>
    Task RecordChangeAsync(FieldChange change, CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns observations into stored fields, doing exactly one of two things to each (spec §5.1):
/// <b>confirm</b> — bump <c>last_confirmed_at</c> and write nothing else — or <b>change</b> — update
/// the row and append a <see cref="FieldChange"/>.
/// </summary>
public interface IFieldReconciler
{
    Task<FieldReconciliation> ApplyAsync(
        Guid gameId,
        IReadOnlyList<FieldObservation> observed,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);
}

/// <summary>What a reconciliation did, so a caller can assert on it rather than on the store.</summary>
public sealed record FieldReconciliation(int Confirmed, int Changed, int Added)
{
    public static readonly FieldReconciliation Nothing = new(0, 0, 0);
}

/// <summary>
/// Which source wins when several answer the same field (spec §5.1). The winner is derived on read
/// and never stored, so it cannot go stale against the rows it summarises.
/// </summary>
public static class FieldPrecedence
{
    /// <summary>
    /// Lower is stronger. Mirrors the declaration order of <see cref="FieldSource"/>, which is the
    /// precedence order — <see cref="FieldSource.Handshake"/> beats <see cref="FieldSource.Mssp"/>
    /// for capability fields because offering an option is an observation and claiming one is not.
    /// </summary>
    public static int RankOf(FieldSource source) => (int)source;

    /// <summary>
    /// The winning row among rows for one field, or null if there are none. Ties — which the
    /// <c>(game, field, source)</c> key makes impossible — resolve to the most recently confirmed.
    /// </summary>
    public static GameField? Winner(IEnumerable<GameField> rowsForOneField) =>
        rowsForOneField
            .OrderBy(r => RankOf(r.Source))
            .ThenByDescending(r => r.LastConfirmedAt)
            .FirstOrDefault();
}
