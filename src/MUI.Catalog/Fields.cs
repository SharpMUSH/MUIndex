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
    OwnerWritable OwnerWritable = OwnerWritable.No,
    FieldShape Shape = FieldShape.Text);

/// <summary>
/// What kind of value a field holds, where that decides what may be done with it.
/// </summary>
/// <remarks>
/// Shared by the enrichment form (rejects a wrong-shape value), the game page (links only a
/// <see cref="Url"/> or <see cref="Email"/>) and the MSSP scorecard — one rule instead of three
/// private "is this a URL field" lists. <see cref="Text"/> is the default and deliberately not
/// "unknown": prose is never a destination.
/// </remarks>
public enum FieldShape
{
    /// <summary>Prose, an enumerated word, a number. Never rendered as a link.</summary>
    Text,

    /// <summary>An http or https address. See <see cref="ExternalUrl"/> for what that means here.</summary>
    Url,

    /// <summary>An email address — or, in practice, a contact page. See <see cref="ExternalUrl"/>.</summary>
    Email,
}

/// <summary>
/// Whether a verified owner may write a field, and on what grounds (spec §8.5).
/// </summary>
/// <remarks>
/// Three states rather than a flag because the two writable ones are different offers:
/// <see cref="Enrichment"/> is a field MSSP has no variable for (an open question); <see cref="Override"/>
/// is one MSSP does have (the dashboard shows the game's own report and asks whether the owner wants
/// to supersede it). "Is this writable at all" lives in <c>OwnerEnrichment</c> alone — do not
/// re-derive it here.
/// </remarks>
public enum OwnerWritable
{
    /// <summary>A measurement, or machinery. Refused out loud, and the whole submission with it.</summary>
    No,

    /// <summary>MSSP has no such variable, so the owner is the only source there could be.</summary>
    Enrichment,

    /// <summary>
    /// MSSP has this variable and the owner's answer outranks their game's report of it.
    /// </summary>
    /// <remarks>
    /// An MSSP report is <em>not</em> a measurement. §5.1: <c>mssp</c> is a game filling in a
    /// structured self-description it maintains, and <c>owner</c> is a person typing — the same kind
    /// of fact, from the same person, arriving by a different road. Both rows go on existing, both
    /// carry their age, and the page shows the owner's with the report beside it.
    /// </remarks>
    Override,
}

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

    /// <summary>One source's rows for one game.</summary>
    /// <remarks>
    /// Narrow on purpose: reading every row just to keep the handful of
    /// <see cref="FieldSource.Owner"/> ones would drag the connect screen — thousands of characters —
    /// across the wire on every page load, only to discard it.
    /// </remarks>
    Task<IReadOnlyList<GameField>> ForGameAsync(
        Guid gameId,
        FieldSource source,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the row for <c>(game, field, source)</c>, or inserts it.</summary>
    Task UpsertAsync(GameField field, CancellationToken cancellationToken = default);

    /// <summary>Appends a transition. Never called when a value merely repeats.</summary>
    Task RecordChangeAsync(FieldChange change, CancellationToken cancellationToken = default);

    /// <summary>
    /// When this field last moved, across every source, or null if it never has.
    /// </summary>
    /// <remarks>
    /// <see cref="GameField.FirstSeenAt"/> survives a value change, so it cannot answer "how long has
    /// this value held" — only the change feed can. Matched case-insensitively on field name, as MSSP
    /// variables are everywhere else.
    /// </remarks>
    Task<DateTimeOffset?> LastChangedAtAsync(
        Guid gameId, string field, CancellationToken cancellationToken = default);
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
