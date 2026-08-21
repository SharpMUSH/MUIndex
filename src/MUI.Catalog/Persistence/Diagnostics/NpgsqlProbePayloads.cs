using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>Which exchange a stored shape came from.</summary>
public enum ProbePayloadKind
{
    /// <summary>The pre-login <c>WHO</c> or <c>DOING</c> response.</summary>
    Who,

    /// <summary>The MSSP report, as text.</summary>
    Mssp,

    /// <summary>The connect screen.</summary>
    Banner,
}

/// <summary>One probe exchange, as shape (spec §11).</summary>
/// <param name="Shape">
/// <see cref="MUI.Crawl.PayloadRedaction"/>'s output. Never the payload — see the type's own remarks
/// for why the two cannot both be had.
/// </param>
public sealed record ProbePayload(
    Guid? GameId,
    string Host,
    int Port,
    DateTimeOffset At,
    ProbePayloadKind Kind,
    string Shape);

/// <summary>The short-lived record parser improvements are replayed against (spec §11).</summary>
public interface IProbePayloads
{
    Task<int> RecordAsync(IReadOnlyList<ProbePayload> payloads, CancellationToken cancellationToken = default);

    /// <summary>One game's recent shapes, newest first — the replay window.</summary>
    Task<IReadOnlyList<ProbePayload>> ForGameAsync(
        Guid gameId,
        DateTimeOffset since,
        CancellationToken cancellationToken = default);

    /// <summary>Drops everything older than the cutoff, and returns how many rows went.</summary>
    Task<int> SweepAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IProbePayloads"/>
public sealed class NpgsqlProbePayloads(NpgsqlDataSource source) : IProbePayloads
{
    public async Task<int> RecordAsync(
        IReadOnlyList<ProbePayload> payloads,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloads);

        if (payloads.Count == 0)
        {
            return 0;
        }

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var written = 0;

        foreach (var payload in payloads)
        {
            // DO NOTHING rather than DO UPDATE: two rows for one (address, instant, kind) means the
            // same probe written twice, and the second copy has nothing to add.
            written += await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO probe_payload (game_id, host, port, at, kind, shape)
                VALUES (@gameId, @host, @port, @at, @kind, @shape)
                ON CONFLICT (host, port, at, kind) DO NOTHING
                """,
                new
                {
                    gameId = payload.GameId,
                    host = HostName.Normalize(payload.Host),
                    payload.Port,
                    at = payload.At.ToUniversalTime(),
                    kind = SqlEnums.ToDb(payload.Kind),
                    payload.Shape,
                },
                cancellationToken: cancellationToken));
        }

        return written;
    }

    public async Task<IReadOnlyList<ProbePayload>> ForGameAsync(
        Guid gameId,
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT game_id AS GameId, host AS Host, port AS Port, at AS At,
                   kind AS Kind, shape AS Shape
              FROM probe_payload
             WHERE game_id = @gameId AND at >= @since
             ORDER BY at DESC
            """,
            new { gameId, since = since.ToUniversalTime() },
            cancellationToken: cancellationToken));

        return [.. rows.Select(r => new ProbePayload(
            r.GameId,
            r.Host,
            r.Port,
            new DateTimeOffset(DateTime.SpecifyKind(r.At, DateTimeKind.Utc)),
            SqlEnums.ToProbePayloadKind(r.Kind),
            r.Shape))];
    }

    /// <summary>
    /// Drops everything older than the cutoff.
    /// </summary>
    /// <remarks>
    /// A plain delete rather than a partition drop, unlike presence retention. The table is small by
    /// construction — a short TTL over a bounded crawl rate — and partitioning it would be machinery
    /// standing guard over a fortnight of rows.
    /// </remarks>
    public async Task<int> SweepAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM probe_payload WHERE at < @before",
            new { before = before.ToUniversalTime() },
            cancellationToken: cancellationToken));
    }

    private sealed record Row(
        Guid? GameId,
        string Host,
        int Port,
        DateTime At,
        string Kind,
        string Shape);
}
