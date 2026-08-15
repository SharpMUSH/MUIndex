using Dapper;

using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// The <c>game_submission</c> table (migration 0010).
/// </summary>
/// <remarks>
/// <para>
/// The rate limit reads this and nothing else, which is why the bound is a table rather than a
/// counter: several web replicas share one database and would not share an in-memory count, and a
/// burst of submissions is a thing somebody will want to look at afterwards. A number that only went
/// up could not be looked at.
/// </para>
/// <para>
/// <b>There is no game id here and there must never be one.</b> A submission — including one we
/// refused under §7.2 — is a thing somebody did to us and a decision of ours about it. Attaching
/// either to a game would put our own security policy into that game's public record, which is the
/// same class of lie as recording a scope refusal as downtime.
/// </para>
/// </remarks>
public sealed class NpgsqlSubmissionLog(NpgsqlDataSource dataSource) : ISubmissionLog
{
    public async Task RecordAsync(SubmissionRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await dataSource.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game_submission (
                id, host, port, submitted_at, outcome, crawl_target_id, source)
            VALUES (@id, @host, @port, @submittedAt, @outcome, @crawlTargetId, @source)
            """,
            new
            {
                id = record.Id,
                host = record.Host,
                port = record.Port,
                submittedAt = record.SubmittedAt.ToUniversalTime(),
                outcome = ToDb(record.Outcome),
                crawlTargetId = record.CrawlTargetId,
                source = record.Source,
            },
            cancellationToken: ct));
    }

    public async Task<int> CountSinceAsync(string source, DateTimeOffset since, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*)::int
              FROM game_submission
             WHERE source = @source AND submitted_at >= @since
            """,
            new { source, since = since.ToUniversalTime() },
            cancellationToken: ct));
    }

    /// <summary>
    /// The vocabulary the table's own CHECK carries, spelled here once.
    /// </summary>
    /// <remarks>
    /// <see cref="SubmissionOutcome.TooMany"/> throws rather than mapping, and that is the point: a
    /// source already at its bound is never recorded, or the window would slide forward for as long
    /// as somebody kept knocking. A caller that reaches here with it has a bug, and the table would
    /// refuse the row anyway.
    /// </remarks>
    private static string ToDb(SubmissionOutcome outcome) => outcome switch
    {
        SubmissionOutcome.Accepted => "accepted",
        SubmissionOutcome.AlreadyListed => "already_listed",
        SubmissionOutcome.AlreadyQueued => "already_queued",
        SubmissionOutcome.Malformed => "malformed",
        SubmissionOutcome.RefusedNotRoutable => "refused_not_routable",
        SubmissionOutcome.Unresolvable => "unresolvable",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a recordable outcome."),
    };
}
