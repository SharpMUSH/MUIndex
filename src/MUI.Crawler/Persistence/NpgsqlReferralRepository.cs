using Dapper;

using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// The <c>referral_edge</c> table (spec §7.1, §7.2).
/// </summary>
/// <remarks>
/// <b>Provenance only. Nothing here schedules anything.</b> An edge disappearing sets
/// <c>present = false</c> and the referred game carries on being probed on its own account, for ever.
/// The graph is kept for two reasons: <c>REFERRAL</c> is attacker-controllable, so recording who
/// referred whom lets a poisoned source's whole subtree be traced and pruned; and who points at whom
/// is worth rendering.
/// </remarks>
public sealed class NpgsqlReferralRepository(NpgsqlDataSource source) : IReferralRepository
{
    private const string Columns = """
        from_game_id AS FromGameId, to_host AS ToHost, to_port AS ToPort,
        first_seen_at AS FirstSeenAt, last_seen_at AS LastSeenAt, present AS Present
        """;

    public async Task<IReadOnlyList<ReferralEdge>> FromGameAsync(Guid gameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM referral_edge WHERE from_game_id = @gameId ORDER BY to_host, to_port",
            new { gameId },
            cancellationToken: ct));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    /// <summary>
    /// Records the edge, keeping the earliest <c>first_seen_at</c> it has ever had.
    /// </summary>
    /// <remarks>
    /// An edge is never deleted and its first sighting never moves: "how long has this game pointed
    /// there" is a fact about the graph, and an upsert that reset it would answer "since the last time
    /// we looked" instead.
    /// </remarks>
    public async Task UpsertAsync(ReferralEdge edge, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(edge);

        await using var connection = await source.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO referral_edge (from_game_id, to_host, to_port, first_seen_at, last_seen_at, present)
            VALUES (@fromGameId, @toHost, @toPort, @firstSeenAt, @lastSeenAt, @present)
            ON CONFLICT (from_game_id, to_host, to_port) DO UPDATE
               SET first_seen_at = LEAST(referral_edge.first_seen_at, EXCLUDED.first_seen_at),
                   last_seen_at = GREATEST(referral_edge.last_seen_at, EXCLUDED.last_seen_at),
                   present = EXCLUDED.present
            """,
            new
            {
                fromGameId = edge.FromGameId,
                toHost = CanonicalHost.Normalize(edge.ToHost),
                toPort = edge.ToPort,
                firstSeenAt = edge.FirstSeenAt.ToUniversalTime(),
                lastSeenAt = edge.LastSeenAt.ToUniversalTime(),
                present = edge.Present,
            },
            cancellationToken: ct));
    }

    /// <summary>
    /// Marks every edge from this game that is not in <paramref name="stillPresent"/> absent.
    /// </summary>
    /// <remarks>
    /// <b>The dates are left alone</b>: <c>last_seen_at</c> means "last seen", not "last looked". A
    /// game that dropped a referral last week did not drop it again today.
    /// </remarks>
    public async Task MarkAbsentAsync(
        Guid fromGameId,
        IReadOnlyCollection<(string Host, int Port)> stillPresent,
        DateTimeOffset at,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stillPresent);

        _ = at;

        await using var connection = await source.OpenConnectionAsync(ct);

        var hosts = stillPresent.Select(p => CanonicalHost.Normalize(p.Host)).ToArray();
        var ports = stillPresent.Select(p => p.Port).ToArray();

        // Paired arrays rather than a VALUES list, so the whole set is one parameterised statement
        // however long a REFERRAL list is.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE referral_edge
               SET present = false
             WHERE from_game_id = @fromGameId
               AND present
               AND (to_host, to_port) <> ALL (
                       SELECT h, p FROM unnest(@hosts::text[], @ports::int[]) AS s(h, p))
            """,
            new { fromGameId, hosts, ports },
            cancellationToken: ct));
    }

    /// <summary>
    /// Everything reachable from this source through the graph (spec §7.2's subtree prune).
    /// </summary>
    /// <remarks>
    /// A recursive CTE, and <c>UNION</c> rather than <c>UNION ALL</c> so a cycle terminates — a
    /// referral graph is a stranger's data structure and two games pointing at each other is not even
    /// unusual. This is the one query §4.13 says recursion is needed for, and it is why the graph
    /// living in Postgres is a decision rather than an oversight.
    /// </remarks>
    public async Task<IReadOnlyList<ReferralEdge>> SubtreeOfAsync(Guid rootGameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"""
            WITH RECURSIVE reached (game_id) AS (
                SELECT @rootGameId
                UNION
                SELECT t.game_id
                  FROM reached r
                  JOIN referral_edge e ON e.from_game_id = r.game_id
                  JOIN crawl_target t ON t.host = e.to_host AND t.port = e.to_port
                 WHERE t.game_id IS NOT NULL)
            SELECT {Columns}
              FROM referral_edge
             WHERE from_game_id IN (SELECT game_id FROM reached)
             ORDER BY from_game_id, to_host, to_port
            """,
            new { rootGameId },
            cancellationToken: ct));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    private sealed class Row
    {
        public Guid FromGameId { get; init; }

        public string ToHost { get; init; } = string.Empty;

        public int ToPort { get; init; }

        public DateTimeOffset FirstSeenAt { get; init; }

        public DateTimeOffset LastSeenAt { get; init; }

        public bool Present { get; init; }

        public ReferralEdge ToRecord() =>
            new(FromGameId, ToHost, ToPort, FirstSeenAt, LastSeenAt, Present);
    }
}
