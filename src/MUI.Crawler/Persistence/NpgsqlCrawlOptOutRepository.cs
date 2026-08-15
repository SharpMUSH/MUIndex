using Dapper;

using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// The <c>crawl_opt_out</c> table (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no delete here and there must never be one.</b> An opt-out taken back is withdrawn and
/// keeps its row: "they asked us to stop, and later asked us back" is a thing the record has to be
/// able to say, and a deletion is the one edit nobody can review afterwards.
/// </para>
/// <para>
/// Hosts are canonicalised on the way in and on every lookup with <see cref="CanonicalHost.Normalize"/>,
/// the same rule the crawl registry obeys — an opt-out filed under a spelling the crawl loop never
/// looks up is an opt-out that stops nothing, and the table's own CHECK refuses the other spellings
/// rather than trusting this class.
/// </para>
/// </remarks>
public sealed class NpgsqlCrawlOptOutRepository(NpgsqlDataSource source) : ICrawlOptOutRepository
{
    private const string Columns = """
        host AS Host, port AS Port, source AS Source, recorded_at AS RecordedAt,
        last_confirmed_at AS LastConfirmedAt, detail AS Detail, withdrawn_at AS WithdrawnAt
        """;

    /// <summary>
    /// The standing opt-out covering this address, or null.
    /// </summary>
    /// <remarks>
    /// <c>port IS NULL</c> is "every port on this host", so the predicate matches both shapes.
    /// Ordered by when they first asked, because where two routes stand for one address the honest
    /// thing to report is the first time somebody told us.
    /// </remarks>
    public async Task<CrawlOptOut?> StandingAsync(string host, int port, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var row = await connection.QueryFirstOrDefaultAsync<Row>(new CommandDefinition(
            $"""
            SELECT {Columns}
              FROM crawl_opt_out
             WHERE host = @host
               AND (port IS NULL OR port = @port)
               AND withdrawn_at IS NULL
             ORDER BY recorded_at, port NULLS FIRST
             LIMIT 1
            """,
            new { host = CanonicalHost.Normalize(host), port },
            cancellationToken: ct));

        return row?.ToRecord();
    }

    /// <summary>
    /// Records an opt-out, or confirms one already held, and returns the row as it now stands.
    /// </summary>
    /// <remarks>
    /// <b><c>recorded_at</c> is not in the update list, and that is the point.</b> When they asked and
    /// when we last heard it are two facts; a confirmation may move the second only. Clearing
    /// <c>withdrawn_at</c> is the same event read forwards: a record that came back is somebody asking
    /// again. One statement, so two workers meeting the same TXT record cannot both insert.
    /// <para>
    /// <b><c>xmax = 0</c> is how the statement says which arm it took</b> — the system column is zero
    /// on a freshly inserted tuple and holds the updating transaction on the conflict arm. The obvious
    /// alternative, comparing the returned <c>recorded_at</c> against the clock the caller passed, is
    /// wrong: <c>timestamptz</c> keeps microseconds and <c>DateTimeOffset</c> counts 100ns ticks, so
    /// the value that comes back is not the value that went in and every write would read as a
    /// confirmation.
    /// </para>
    /// </remarks>
    public async Task<OptOutRecording> RecordAsync(CrawlOptOut optOut, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(optOut);

        await using var connection = await source.OpenConnectionAsync(ct);

        var row = await connection.QuerySingleAsync<Row>(new CommandDefinition(
            $"""
            INSERT INTO crawl_opt_out (
                id, host, port, source, recorded_at, last_confirmed_at, detail, withdrawn_at)
            VALUES (@id, @host, @port, @source, @recordedAt, @lastConfirmedAt, @detail, NULL)
            ON CONFLICT (host, port, source) DO UPDATE
               SET last_confirmed_at = GREATEST(crawl_opt_out.last_confirmed_at, EXCLUDED.last_confirmed_at),
                   detail = EXCLUDED.detail,
                   withdrawn_at = NULL
            RETURNING {Columns}, (xmax = 0) AS IsFirstAsk
            """,
            new
            {
                id = Guid.CreateVersion7(),
                host = CanonicalHost.Normalize(optOut.Host),
                port = optOut.Port,
                source = SqlName(optOut.Source),
                recordedAt = optOut.RecordedAt.ToUniversalTime(),
                lastConfirmedAt = optOut.LastConfirmedAt.ToUniversalTime(),
                detail = optOut.Detail,
            },
            cancellationToken: ct));

        return new OptOutRecording(row.ToRecord(), row.IsFirstAsk);
    }

    public async Task WithdrawAsync(
        string host,
        int? port,
        OptOutSource route,
        DateTimeOffset at,
        CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE crawl_opt_out
               SET withdrawn_at = @at
             WHERE host = @host
               AND port IS NOT DISTINCT FROM @port
               AND source = @source
               AND withdrawn_at IS NULL
            """,
            new
            {
                host = CanonicalHost.Normalize(host),
                port,
                source = SqlName(route),
                at = at.ToUniversalTime(),
            },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CrawlOptOut>> AllAsync(CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM crawl_opt_out ORDER BY recorded_at, host, port NULLS FIRST",
            cancellationToken: ct));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    /// <summary>
    /// The wire spelling of a route, which the table's own CHECK also enumerates.
    /// </summary>
    /// <remarks>
    /// Written out rather than <c>ToString()</c>-ed: the schema is meant to be legible in <c>psql</c>,
    /// and a column whose values are whatever a C# enum happened to be named is a column that changes
    /// under a rename nobody thought was a migration.
    /// </remarks>
    private static string SqlName(OptOutSource source) => source switch
    {
        OptOutSource.Mssp => "mssp",
        OptOutSource.DnsTxt => "dns_txt",
        OptOutSource.Request => "request",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown opt-out route."),
    };

    private static OptOutSource FromSqlName(string source) => source switch
    {
        "mssp" => OptOutSource.Mssp,
        "dns_txt" => OptOutSource.DnsTxt,
        "request" => OptOutSource.Request,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown opt-out route."),
    };

    private sealed class Row
    {
        public string Host { get; init; } = string.Empty;

        public int? Port { get; init; }

        public string Source { get; init; } = string.Empty;

        public DateTimeOffset RecordedAt { get; init; }

        public DateTimeOffset LastConfirmedAt { get; init; }

        public string Detail { get; init; } = string.Empty;

        public DateTimeOffset? WithdrawnAt { get; init; }

        /// <summary>Only ever set by the write path; a read leaves it false and nothing reads it.</summary>
        public bool IsFirstAsk { get; init; }

        public CrawlOptOut ToRecord() => new()
        {
            Host = Host,
            Port = Port,
            Source = FromSqlName(Source),
            RecordedAt = RecordedAt,
            LastConfirmedAt = LastConfirmedAt,
            Detail = Detail,
            WithdrawnAt = WithdrawnAt,
        };
    }
}
