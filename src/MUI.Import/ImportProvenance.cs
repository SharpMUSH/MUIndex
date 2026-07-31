namespace MUI.Import;

/// <summary>What an imported value was about.</summary>
public enum ImportSubjectKind
{
    Field,
    Endpoint,
    Presence,
    Availability,
}

/// <summary>
/// Which site said this, about what, and when we read it (spec §7.6).
/// </summary>
/// <remarks>
/// <para>
/// The sidecar exists because §7.6 requires every imported value to carry its originating site and
/// import date, and nothing already stored can say that: a <c>GameField</c> carries a
/// <c>FieldSource</c>, a <c>PresenceSample</c> a <c>FieldSource</c>, an <c>AvailabilityInterval</c> a
/// tier-valued <c>origin</c>, and none of the three names a site or a date. This serves the
/// provenance chip and the about page's attribution list.
/// </para>
/// <para>
/// <b>It is not on the archive-grace path.</b> §7.5's half weight is computed from
/// <c>availability_interval.origin</c> by <c>ArchivePolicy.GraceFor</c> and nowhere else; a second
/// calculator reading these rows would count the same history twice.
/// </para>
/// </remarks>
/// <param name="SubjectKey">
/// The field name, or <c>host:port</c> for an endpoint. Null for a dated history row, whose identity
/// is <paramref name="SubjectAt"/> instead — exactly one of the two is set, and the schema's
/// <c>import_provenance_has_one_subject</c> CHECK is what keeps the idempotence lookup total.
/// </param>
/// <param name="SubjectAt">The instant a history row is about. Null for a field or an endpoint.</param>
public sealed record ImportProvenance(
    Guid GameId,
    ImportSubjectKind Subject,
    string? SubjectKey,
    DateTimeOffset? SubjectAt,
    string SourceName,
    string SourceKey,
    Uri? SourceUri,
    ImportTier Tier,
    DateTimeOffset ImportedAt)
{
    public static ImportProvenance ForField(
        Guid gameId,
        string field,
        ImportedGame record,
        ImportTier tier,
        DateTimeOffset importedAt) =>
        new(gameId, ImportSubjectKind.Field, field, null,
            record.SourceName, record.SourceKey, record.SourceUri, tier, importedAt);

    public static ImportProvenance ForEndpoint(
        Guid gameId,
        ImportedEndpoint endpoint,
        ImportedGame record,
        ImportTier tier,
        DateTimeOffset importedAt) =>
        new(gameId, ImportSubjectKind.Endpoint, $"{endpoint.Host}:{endpoint.Port}", null,
            record.SourceName, record.SourceKey, record.SourceUri, tier, importedAt);

    public static ImportProvenance ForHistory(
        Guid gameId,
        ImportSubjectKind subject,
        DateTimeOffset at,
        ImportedGame record,
        DateTimeOffset importedAt) =>
        new(gameId, subject, null, at,
            record.SourceName, record.SourceKey, record.SourceUri, ImportTier.Measured, importedAt);
}

/// <summary>The sidecar's store.</summary>
public interface IImportProvenanceStore
{
    /// <summary>
    /// Whether this site has already given us this subject for this game. The idempotence question:
    /// re-running a backfill must change nothing, and this is what every writer asks first.
    /// </summary>
    Task<bool> ExistsAsync(
        Guid gameId,
        string sourceName,
        ImportSubjectKind subject,
        string? subjectKey,
        DateTimeOffset? subjectAt,
        CancellationToken cancellationToken = default);

    Task RecordAsync(ImportProvenance provenance, CancellationToken cancellationToken = default);

    /// <summary>What each site actually contributed, for the about page (spec §7.6).</summary>
    Task<IReadOnlyList<SourceContribution>> ContributionsAsync(CancellationToken cancellationToken = default);
}

/// <summary>One site's contribution, counted from what actually landed.</summary>
public sealed record SourceContribution(
    string SourceName,
    ImportTier Tier,
    int Values,
    DateTimeOffset FirstImportedAt,
    DateTimeOffset LastImportedAt);
