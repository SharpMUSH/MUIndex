using Dapper;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// The submissions §7.8's rubric did not publish, and the operator's one lever over them.
/// </summary>
/// <remarks>
/// The rubric stops where measurement stops — a probe can't tell a real game from a host that merely
/// prints a banner, so some submissions go unpublished — and this is what stands behind that line: a
/// person can look. It is a read and a write and no judgement of its own; nothing here scores
/// anything, since a rule spelled once in <c>MuLikeness</c> and again in SQL would be two rules within
/// a release. Deliberately not a web surface — there's no staff role on <c>app_user</c>.
/// </remarks>
public sealed class NpgsqlSubmissionQueue(NpgsqlDataSource source)
{
    /// <summary>
    /// Every submitted game that has never been published, newest first.
    /// </summary>
    /// <remarks>
    /// Archived games are left out because §7.5 already says what they are — off the listing, still
    /// probed, restored by one successful probe — and a decision about visibility is not the
    /// decision they are waiting on. One that answers again is unarchived and reappears here.
    /// </remarks>
    public async Task<IReadOnlyList<PendingSubmission>> AwaitingAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT g.id AS GameId, g.slug AS Slug, g.name AS Name,
                   g.submitted_at AS SubmittedAt, g.last_reachable_at AS LastReachableAt,
                   e.host AS Host, e.port AS Port
              FROM game g
              LEFT JOIN LATERAL (
                   SELECT host, port FROM game_endpoint
                    WHERE game_id = g.id AND state <> 'gone'
                    ORDER BY last_seen_at DESC
                    LIMIT 1
              ) e ON true
             WHERE g.submitted_at IS NOT NULL
               AND g.corroborated_at IS NULL
               AND g.state <> 'archived'
             ORDER BY g.submitted_at DESC
            """,
            cancellationToken: cancellationToken));

        return [.. rows.Select(row => new PendingSubmission(
            row.GameId,
            row.Slug,
            row.Name,
            row.Host is null ? "(no address)" : $"{row.Host}:{row.Port}",
            new DateTimeOffset(DateTime.SpecifyKind(row.SubmittedAt, DateTimeKind.Utc)),
            row.LastReachableAt is { } reachable
                ? new DateTimeOffset(DateTime.SpecifyKind(reachable, DateTimeKind.Utc))
                : null))];
    }

    /// <summary>
    /// Publishes one by hand, recorded as <c>staff</c>. False when there was nothing waiting.
    /// </summary>
    /// <remarks>
    /// <c>staff</c> rather than a signal name: no probe measured this game to be a game, a person
    /// decided it was, and recording it as though the rubric had made the call would be exactly rule
    /// 5's forbidden move. The same <c>WHERE corroborated_at IS NULL</c> that makes the crawler's
    /// write once-only makes this once-only too.
    /// </remarks>
    public async Task<bool> ReleaseAsync(
        string slug,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE game
               SET corroborated_at = @at, corroborated_by = ARRAY['staff']
             WHERE slug = @slug AND submitted_at IS NOT NULL AND corroborated_at IS NULL
            """,
            new { slug, at = at.ToUniversalTime() },
            cancellationToken: cancellationToken));

        return affected == 1;
    }

    private sealed class Row
    {
        public Guid GameId { get; init; }

        public string Slug { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public DateTime SubmittedAt { get; init; }

        public DateTime? LastReachableAt { get; init; }

        public string? Host { get; init; }

        public int Port { get; init; }
    }
}

/// <summary>A submission that answered, was listed, and has not been published (spec §7.8).</summary>
public sealed record PendingSubmission(
    Guid GameId,
    string Slug,
    string Name,
    string Address,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? LastReachableAt);
