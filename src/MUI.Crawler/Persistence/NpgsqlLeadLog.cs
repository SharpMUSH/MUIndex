using Dapper;

using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// The <c>crawl_lead</c> table (migration 0041).
/// </summary>
/// <remarks>
/// Our note about our own crawl. <b>Nothing here may reach a game</b>: the evidence URL says which
/// page we happened to read first, which is not a fact about where a game came from.
/// </remarks>
public sealed class NpgsqlLeadLog(NpgsqlDataSource dataSource) : ILeadLog
{
    /// <summary>
    /// One key for the whole table, since there is one routine and one bound — unlike the form, where
    /// the lock is per source so unrelated submitters never wait on each other.
    /// </summary>
    private const string LockKey = "crawl_lead";

    /// <summary>Takes one of the window's slots, or none — counted and inserted under one lock.</summary>
    public async Task<bool> TryBeginAsync(
        Guid id,
        LeadEvidence evidence,
        DateTimeOffset now,
        int perWindow,
        DateTimeOffset since,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))",
            new { key = LockKey },
            transaction,
            cancellationToken: ct));

        var taken = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM crawl_lead WHERE found_at >= @since",
            new { since = since.ToUniversalTime() },
            transaction,
            cancellationToken: ct));

        if (taken >= perWindow)
        {
            // No row: recording a refusal would slide the window forward for as long as the routine
            // kept trying.
            await transaction.RollbackAsync(ct);
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO crawl_lead (id, host, port, evidence_url, post_url, channel, found_at, outcome, crawl_target_id)
            VALUES (@id, NULL, NULL, @evidenceUrl, @postUrl, @channel, @now, 'pending', NULL)
            """,
            new
            {
                id,
                evidenceUrl = evidence.EvidenceUrl.AbsoluteUri,
                postUrl = evidence.PostUrl?.AbsoluteUri,
                channel = evidence.Channel,
                now = now.ToUniversalTime(),
            },
            transaction,
            cancellationToken: ct));

        await transaction.CommitAsync(ct);

        return true;
    }

    public async Task CompleteAsync(
        Guid id,
        SubmittedAddress? address,
        SubmissionOutcome outcome,
        Guid? crawlTargetId,
        CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE crawl_lead
               SET host = @host, port = @port, outcome = @outcome, crawl_target_id = @crawlTargetId
             WHERE id = @id
            """,
            new
            {
                id,
                host = address?.Host,
                port = address?.Port,
                outcome = NpgsqlSubmissionLog.ToDb(outcome),
                crawlTargetId,
            },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<LeadRecord>> RecentAsync(int limit, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT id AS Id, host AS Host, port AS Port, evidence_url AS EvidenceUrl, post_url AS PostUrl,
                   channel AS Channel, found_at AS FoundAt, outcome AS Outcome,
                   crawl_target_id AS CrawlTargetId
              FROM crawl_lead
             ORDER BY found_at DESC, id DESC
             LIMIT @limit
            """,
            new { limit = Math.Max(1, limit) },
            cancellationToken: ct));

        return [.. rows.Select(r => r.ToRecord())];
    }

    /// <summary>
    /// The outcome a stored spelling names, or null for <c>pending</c> — a reservation whose process
    /// died before it could say what happened.
    /// </summary>
    private static SubmissionOutcome? FromDb(string value) => value switch
    {
        "accepted" => SubmissionOutcome.Accepted,
        "already_listed" => SubmissionOutcome.AlreadyListed,
        "already_queued" => SubmissionOutcome.AlreadyQueued,
        "malformed" => SubmissionOutcome.Malformed,
        "refused_not_routable" => SubmissionOutcome.RefusedNotRoutable,
        "unresolvable" => SubmissionOutcome.Unresolvable,
        "refused_opt_out" => SubmissionOutcome.RefusedOptOut,
        _ => null,
    };

    private sealed class Row
    {
        public Guid Id { get; init; }

        public string? Host { get; init; }

        public int? Port { get; init; }

        public string EvidenceUrl { get; init; } = "";

        public string? PostUrl { get; init; }

        public string Channel { get; init; } = "";

        public DateTimeOffset FoundAt { get; init; }

        public string Outcome { get; init; } = "";

        public Guid? CrawlTargetId { get; init; }

        public LeadRecord ToRecord() => new(
            Id,
            Host,
            Port,
            new LeadEvidence(
                new Uri(EvidenceUrl, UriKind.Absolute),
                PostUrl is null ? null : new Uri(PostUrl, UriKind.Absolute),
                Channel),
            FoundAt,
            FromDb(Outcome),
            CrawlTargetId);
    }
}
