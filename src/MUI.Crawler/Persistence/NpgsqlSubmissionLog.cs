using Dapper;

using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// The <c>game_submission</c> table (migration 0010).
/// </summary>
/// <remarks>
/// The rate limit reads and writes this table rather than an in-memory counter, since several web
/// replicas share one database and a burst of submissions is worth being able to look at afterwards.
/// <b>There is no game id here and there must never be one.</b> A submission — including one refused
/// under §7.2 — is a decision of ours about something somebody did to us; attaching it to a game
/// would put our own security policy into that game's public record.
/// </remarks>
public sealed class NpgsqlSubmissionLog(NpgsqlDataSource dataSource) : ISubmissionLog
{
    /// <summary>
    /// Takes one of this source's slots, or none.
    /// </summary>
    /// <remarks>
    /// A transaction and an advisory lock, because counting then inserting is not a bound: under
    /// <c>READ COMMITTED</c>, two concurrent requests can both read a count that neither has written
    /// to yet and both pass. <c>pg_advisory_xact_lock</c> keyed on the source serialises the count and
    /// insert against every other caller, in this process and every replica, and releases when the
    /// transaction ends. Per source rather than global, so unrelated submitters never wait on each other.
    /// </remarks>
    public async Task<bool> TryBeginAsync(
        Guid id,
        string source,
        DateTimeOffset now,
        int perSource,
        DateTimeOffset since,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@source, 0))",
            new { source },
            transaction,
            cancellationToken: ct));

        var taken = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*)::int
              FROM game_submission
             WHERE source = @source AND submitted_at >= @since
            """,
            new { source, since = since.ToUniversalTime() },
            transaction,
            cancellationToken: ct));

        if (taken >= perSource)
        {
            // No row at all: recording a refusal would slide the window forward for as long as
            // somebody kept knocking.
            await transaction.RollbackAsync(ct);
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game_submission (id, host, port, submitted_at, outcome, crawl_target_id, source)
            VALUES (@id, NULL, NULL, @now, 'pending', NULL, @source)
            """,
            new { id, now = now.ToUniversalTime(), source },
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
            UPDATE game_submission
               SET host = @host, port = @port, outcome = @outcome, crawl_target_id = @crawlTargetId
             WHERE id = @id
            """,
            new
            {
                id,
                host = address?.Host,
                port = address?.Port,
                outcome = ToDb(outcome),
                crawlTargetId,
            },
            cancellationToken: ct));
    }

    /// <summary>
    /// The vocabulary the table's own CHECK carries, spelled here once.
    /// </summary>
    /// <remarks>
    /// <see cref="SubmissionOutcome.TooMany"/> throws rather than mapping: a source already at its
    /// bound never reserved a row, so there's nothing to complete, and a caller that reaches here
    /// with it has a bug.
    /// </remarks>
    private static string ToDb(SubmissionOutcome outcome) => outcome switch
    {
        SubmissionOutcome.Accepted => "accepted",
        SubmissionOutcome.AlreadyListed => "already_listed",
        SubmissionOutcome.AlreadyQueued => "already_queued",
        SubmissionOutcome.Malformed => "malformed",
        SubmissionOutcome.RefusedNotRoutable => "refused_not_routable",
        SubmissionOutcome.Unresolvable => "unresolvable",
        SubmissionOutcome.RefusedOptOut => "refused_opt_out",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a recordable outcome."),
    };
}

/// <summary>
/// §11's rotating salt, in the one place every replica can agree about (migration 0010).
/// </summary>
/// <remarks>
/// The epoch is computed from the clock rather than looked up, so no replica has to ask which salt is
/// current — the first to need a new epoch inserts it and everybody else takes what's there.
/// <c>ON CONFLICT DO NOTHING</c> followed by a read, not <c>RETURNING</c>, since two replicas crossing
/// an epoch boundary together must end up with the same bytes, and only the loser of that race would
/// see nothing returned. Cached per epoch: crossing a boundary replaces the cache rather than
/// accumulating.
/// </remarks>
public sealed class NpgsqlSubmissionSalt(
    NpgsqlDataSource dataSource,
    SubmissionOptions options,
    TimeProvider time) : ISubmissionSalt
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private long _epoch = -1;
    private byte[]? _salt;

    /// <summary>How long a salt lives, in the units the table's primary key counts.</summary>
    public long EpochOf(DateTimeOffset at) =>
        (long)((at - DateTimeOffset.UnixEpoch).Ticks / options.SaltEpoch.Ticks);

    public async Task<byte[]> CurrentAsync(CancellationToken ct)
    {
        var epoch = EpochOf(time.GetUtcNow());

        if (_epoch == epoch && _salt is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(ct);

        try
        {
            if (_epoch == epoch && _salt is { } raced)
            {
                return raced;
            }

            await using var connection = await dataSource.OpenConnectionAsync(ct);

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO submission_salt (epoch, salt, created_at)
                VALUES (@epoch, @salt, @now)
                ON CONFLICT (epoch) DO NOTHING
                """,
                new
                {
                    epoch,
                    salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
                    now = time.GetUtcNow().ToUniversalTime(),
                },
                cancellationToken: ct));

            var stored = await connection.ExecuteScalarAsync<byte[]>(new CommandDefinition(
                "SELECT salt FROM submission_salt WHERE epoch = @epoch",
                new { epoch },
                cancellationToken: ct));

            _salt = stored;
            _epoch = epoch;

            return stored!;
        }
        finally
        {
            _gate.Release();
        }
    }
}
