using Dapper;

using Npgsql;

namespace MUI.Import;

/// <summary>The <c>import_provenance</c> table (spec §7.6).</summary>
public sealed class NpgsqlImportProvenanceStore(NpgsqlDataSource source) : IImportProvenanceStore
{
    private readonly NpgsqlDataSource _source = source ?? throw new ArgumentNullException(nameof(source));

    public async Task<bool> ExistsAsync(
        Guid gameId,
        string sourceName,
        ImportSubjectKind subject,
        string? subjectKey,
        DateTimeOffset? subjectAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _source.OpenConnectionAsync(cancellationToken);

        // COALESCE on both sides so the comparison matches the unique index exactly. Comparing NULLs
        // with = would answer "unknown" and the row would be written again on every re-run.
        var found = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1 FROM import_provenance
                 WHERE game_id = @gameId
                   AND source_name = @sourceName
                   AND subject_kind = @subject
                   AND COALESCE(subject_key, '') = COALESCE(@subjectKey, '')
                   AND COALESCE(subject_at, 'epoch') = COALESCE(@subjectAt, 'epoch'))
            """,
            new
            {
                gameId,
                sourceName,
                subject = ToDb(subject),
                subjectKey,
                subjectAt = subjectAt?.ToUniversalTime(),
            },
            cancellationToken: cancellationToken));

        return found;
    }

    public async Task RecordAsync(ImportProvenance provenance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        await using var connection = await _source.OpenConnectionAsync(cancellationToken);

        // ON CONFLICT DO NOTHING rather than an upsert: the sidecar records that a site told us a
        // thing, and the first time it did is the interesting date. A re-import must not restamp it.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO import_provenance
                (game_id, subject_kind, subject_key, subject_at,
                 source_name, source_key, source_uri, tier, imported_at)
            VALUES (@gameId, @subject, @subjectKey, @subjectAt,
                    @sourceName, @sourceKey, @sourceUri, @tier, @importedAt)
            ON CONFLICT DO NOTHING
            """,
            new
            {
                gameId = provenance.GameId,
                subject = ToDb(provenance.Subject),
                subjectKey = provenance.SubjectKey,
                subjectAt = provenance.SubjectAt?.ToUniversalTime(),
                sourceName = provenance.SourceName,
                sourceKey = provenance.SourceKey,
                sourceUri = provenance.SourceUri?.AbsoluteUri,
                tier = ToDb(provenance.Tier),
                importedAt = provenance.ImportedAt.ToUniversalTime(),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<SourceContribution>> ContributionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT source_name AS SourceName, tier AS Tier, COUNT(*) AS ValueCount,
                   MIN(imported_at) AS FirstImportedAt, MAX(imported_at) AS LastImportedAt
              FROM import_provenance
             GROUP BY source_name, tier
             ORDER BY source_name
            """,
            cancellationToken: cancellationToken));

        return rows
            .Select(row => new SourceContribution(
                row.SourceName, ToTier(row.Tier), row.ValueCount, row.FirstImportedAt, row.LastImportedAt))
            .ToList();
    }

    internal static string ToDb(ImportSubjectKind subject) => subject switch
    {
        ImportSubjectKind.Field => "field",
        ImportSubjectKind.Endpoint => "endpoint",
        ImportSubjectKind.Presence => "presence",
        ImportSubjectKind.Availability => "availability",
        _ => throw new ArgumentOutOfRangeException(nameof(subject)),
    };

    /// <summary>
    /// The tier's spelling in the schema is <see cref="MUI.Catalog.FieldSource"/>'s, deliberately —
    /// <c>imported_measured</c> and <c>imported_asserted</c> are the same two words everywhere in the
    /// database, so a query joining the sidecar to a field row reads as one vocabulary.
    /// </summary>
    internal static string ToDb(ImportTier tier) =>
        MUI.Catalog.Persistence.SqlEnums.ToDb(ImportTierMap.SourceFor(tier));

    internal static ImportTier ToTier(string value) => value switch
    {
        "imported_measured" => ImportTier.Measured,
        "imported_asserted" => ImportTier.Asserted,
        _ => throw new InvalidOperationException($"Unreadable import tier '{value}'."),
    };

    private sealed class Row
    {
        public string SourceName { get; init; } = string.Empty;

        public string Tier { get; init; } = string.Empty;

        public int ValueCount { get; init; }

        public DateTimeOffset FirstImportedAt { get; init; }

        public DateTimeOffset LastImportedAt { get; init; }
    }
}
