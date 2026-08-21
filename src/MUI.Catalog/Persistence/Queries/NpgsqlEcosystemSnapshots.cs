using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// The dated record behind §9's adoption curves.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard is computed live and has always been point-in-time, so "adoption curve" named
/// something with one point on it. This keeps the components of each share once a day, and
/// <see cref="ProtocolAdoption"/> divides them — the same record the live dashboard is made of, so a
/// point on a curve and the figure on the dashboard cannot mean different things.
/// </para>
/// <para>
/// <b>Idempotent per day.</b> A maintenance pass that runs twice, or a replica that takes the lease
/// mid-afternoon and runs it again, must not put two points on one day: the write is an upsert keyed
/// on the date, and the later pass wins because it saw more of the day.
/// </para>
/// </remarks>
public sealed class NpgsqlEcosystemSnapshots(NpgsqlDataSource source) : IEcosystemSnapshots
{
    public async Task<int> RecordAsync(
        DateTimeOffset asOf,
        IReadOnlyList<ProtocolAdoption> protocols,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(protocols);

        if (protocols.Count == 0)
        {
            return 0;
        }

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var written = 0;

        foreach (var protocol in protocols)
        {
            written += await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO ecosystem_snapshot (
                    at, metric, key, offered, declined, handshakes, declared, mssp_reports)
                VALUES (
                    @at, 'protocol', @key, @offered, @declined, @handshakes, @declared, @msspReports)
                ON CONFLICT (at, metric, key) DO UPDATE SET
                    offered      = EXCLUDED.offered,
                    declined     = EXCLUDED.declined,
                    handshakes   = EXCLUDED.handshakes,
                    declared     = EXCLUDED.declared,
                    mssp_reports = EXCLUDED.mssp_reports
                """,
                new
                {
                    at = asOf.UtcDateTime.Date,
                    key = protocol.Protocol,
                    offered = protocol.Offered,
                    declined = protocol.Declined,
                    handshakes = protocol.Handshakes,
                    declared = protocol.Declared,
                    msspReports = protocol.MsspReports,
                },
                cancellationToken: cancellationToken));
        }

        return written;
    }

    /// <summary>One protocol's history, oldest first.</summary>
    public async Task<IReadOnlyList<AdoptionPoint>> CurveAsync(
        string protocol,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT at AS At, key AS Key, offered AS Offered, declined AS Declined,
                   handshakes AS Handshakes, declared AS Declared, mssp_reports AS MsspReports
              FROM ecosystem_snapshot
             WHERE metric = 'protocol' AND key = @protocol AND at >= @from AND at <= @to
             ORDER BY at
            """,
            new { protocol, from = from.UtcDateTime.Date, to = to.UtcDateTime.Date },
            cancellationToken: cancellationToken));

        return [.. rows.Select(r => new AdoptionPoint(
            new DateTimeOffset(r.At.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new ProtocolAdoption(
                r.Key, r.Offered, r.Declined, r.Handshakes, r.Declared, r.MsspReports)))];
    }

    /// <summary>
    /// The row as the column types are: <c>at</c> is a <c>date</c>, which Npgsql hands back as a
    /// <see cref="DateOnly"/>, and Dapper matches a record constructor by exact parameter type.
    /// </summary>
    private sealed record Row(
        DateOnly At,
        string Key,
        int? Offered,
        int Declined,
        int Handshakes,
        int Declared,
        int MsspReports);
}
