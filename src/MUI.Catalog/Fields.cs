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
/// <para>
/// <b>Three shapes, because exactly three things ask.</b> The enrichment form refuses a value that is
/// not the shape its field holds, the game page renders a link only for a value that is one, and the
/// MSSP scorecard names the mismatch to the operator whose config produced it. All three were about
/// to grow a private list of "the URL fields", and a rule spelled three times is a rule that drifts
/// twice.
/// </para>
/// <para>
/// <see cref="Text"/> is the default and is deliberately not "unknown": a <c>GENRE</c> holds prose,
/// and prose is never a destination. It is the answer for every field that has not made a case for
/// something narrower.
/// </para>
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
/// <para>
/// <b>Three states rather than a flag, because the two writable ones are different offers.</b>
/// <see cref="Enrichment"/> is a field MSSP has no variable for, so the dashboard asks an open
/// question and the owner is the only possible source. <see cref="Override"/> is one MSSP does have,
/// so the dashboard shows what the game already reports and asks whether the owner would rather we
/// showed something else. Collapsed into one state the form offers an empty box beside a field we
/// already have an answer for, which reads as an invitation to retype it.
/// </para>
/// <para>
/// <b>The distinction is presentational; the authorisation is not.</b> Everything that is not
/// <see cref="No"/> is writable, and that predicate lives in <c>OwnerEnrichment</c> alone — a second
/// spelling of it is a second thing to keep in step with this enum.
/// </para>
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
    /// Narrow on purpose. The owner dashboard wants at most four <see cref="FieldSource.Owner"/>
    /// rows per claimed game, and reading every row to keep them means dragging the connect screen
    /// — routinely thousands of characters, and 9,376 at the longest in this catalogue — across the
    /// wire once per game per page load, to discard it.
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
    /// <b>How long a value has been what it is cannot be read off the row.</b>
    /// <see cref="GameField.FirstSeenAt"/> means "when this (game, field, source) was first seen" and
    /// survives a change deliberately, so the age a provenance chip shows is the age of the row.
    /// §5.7's "the name has been stable for one grace period" is a different question and the change
    /// feed is the only thing that answers it. Matched case-insensitively on the field name, as MSSP
    /// variables are everywhere else: <c>NAME</c> and <c>name</c> are one field.
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
