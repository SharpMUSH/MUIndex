using Dapper;

using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// The <c>crawl_target</c> table (spec §7.1, §7.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Monotonic by construction.</b> There is no delete here, no retire, and no method that can stop a
/// target being probed — the same absence <see cref="ICrawlTargetRepository"/> holds in place by
/// reflection and <c>0006_crawl_registry.sql</c> holds in place by having no column for it. A game
/// dark for two years is still probed weekly, for ever, including after archiving, because a returning
/// game re-listing itself with no human involved is the thing every incumbent failed at (§3, §7.4).
/// </para>
/// <para>
/// Hosts are canonicalised on the way in and on every lookup, with
/// <see cref="CanonicalHost.Normalize"/> — the same rule the in-memory registry obeys. The unique
/// index on <c>(host, port)</c> is only a guarantee that one machine is one target if one machine has
/// one spelling, and the table's own CHECK refuses anything else rather than trusting this class.
/// </para>
/// </remarks>
public sealed class NpgsqlCrawlTargetRepository(NpgsqlDataSource source) : ICrawlTargetRepository
{
    private const string Columns = """
        id AS Id, game_id AS GameId, host AS Host, port AS Port, use_tls AS UseTls,
        next_probe_at AS NextProbeAt, consecutive_failures AS ConsecutiveFailures,
        crawl_delay AS CrawlDelay, first_seen_at AS FirstSeenAt, last_probed_at AS LastProbedAt,
        discovered_from_game_id AS DiscoveredFromGameId, depth AS Depth,
        is_operator_seed AS IsOperatorSeed
        """;

    public async Task<CrawlTarget?> ByAddressAsync(string host, int port, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM crawl_target WHERE host = @host AND port = @port",
            new { host = CanonicalHost.Normalize(host), port },
            cancellationToken: ct));

        return row?.ToRecord();
    }

    /// <summary>
    /// Adds a target, or returns the existing one's id.
    /// </summary>
    /// <remarks>
    /// <b>A repeat sighting may lower the recorded depth and may change nothing else.</b> Not the
    /// schedule, not the failure count, not the operator-seed exemption — a second referral naming an
    /// address we already crawl must not drag it forward, which is exactly the coupling §7.1 removes
    /// by giving every target its own <c>next_probe_at</c>. One statement, so two workers racing the
    /// same referral cannot both insert.
    /// </remarks>
    public async Task<Guid> AddAsync(CrawlTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);

        await using var connection = await source.OpenConnectionAsync(ct);

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO crawl_target (
                id, game_id, host, port, use_tls, next_probe_at, consecutive_failures, crawl_delay,
                first_seen_at, last_probed_at, discovered_from_game_id, depth, is_operator_seed)
            VALUES (
                @id, @gameId, @host, @port, @useTls, @nextProbeAt, @consecutiveFailures, @crawlDelay,
                @firstSeenAt, @lastProbedAt, @discoveredFromGameId, @depth, @isOperatorSeed)
            ON CONFLICT (host, port) DO UPDATE
               SET depth = LEAST(crawl_target.depth, EXCLUDED.depth)
            RETURNING id
            """,
            new
            {
                id = target.Id,
                gameId = target.GameId,
                host = CanonicalHost.Normalize(target.Host),
                port = target.Port,
                useTls = target.UseTls,
                nextProbeAt = target.NextProbeAt.ToUniversalTime(),
                consecutiveFailures = target.ConsecutiveFailures,
                crawlDelay = target.CrawlDelay,
                firstSeenAt = target.FirstSeenAt.ToUniversalTime(),
                lastProbedAt = target.LastProbedAt?.ToUniversalTime(),
                discoveredFromGameId = target.DiscoveredFromGameId,
                depth = target.Depth,
                isOperatorSeed = target.IsOperatorSeed,
            },
            cancellationToken: ct));
    }

    /// <summary>
    /// The targets that are due, oldest first, then shallowest.
    /// </summary>
    /// <remarks>
    /// Depth breaks the tie so that a batch which cannot hold everything spends itself nearer the
    /// seeds — a referral four hops out is by construction less likely to be a game than one hop out,
    /// and waiting a cycle costs it nothing because it keeps its own schedule.
    /// </remarks>
    public async Task<IReadOnlyList<CrawlTarget>> DueAsync(DateTimeOffset now, int limit, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"""
            SELECT {Columns}
              FROM crawl_target
             WHERE next_probe_at <= @now
             ORDER BY next_probe_at, depth, first_seen_at
             LIMIT @limit
            """,
            new { now = now.ToUniversalTime(), limit },
            cancellationToken: ct));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    public async Task RecordAttemptAsync(
        Guid id,
        DateTimeOffset at,
        bool succeeded,
        TimeSpan? crawlDelay,
        DateTimeOffset nextProbeAt,
        CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // COALESCE on crawl_delay, not assignment: a server that stated CRAWL DELAY once and then
        // stopped publishing MSSP has not withdrawn the request, and forgetting it would mean
        // knocking more often at a server that asked us not to.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE crawl_target
               SET last_probed_at = @at,
                   consecutive_failures = CASE WHEN @succeeded THEN 0 ELSE consecutive_failures + 1 END,
                   crawl_delay = COALESCE(@crawlDelay, crawl_delay),
                   next_probe_at = @nextProbeAt
             WHERE id = @id
            """,
            new
            {
                id,
                at = at.ToUniversalTime(),
                succeeded,
                crawlDelay,
                nextProbeAt = nextProbeAt.ToUniversalTime(),
            },
            cancellationToken: ct));
    }

    /// <summary>
    /// Attaches the game this address turned out to be.
    /// </summary>
    /// <remarks>
    /// <c>WHERE game_id IS NULL</c> so a later probe cannot silently re-point an address at a
    /// different game. That is a merge, it is §7.3's business, and it is logged and reversible when it
    /// happens — not a side effect of a crawl cycle.
    /// </remarks>
    public async Task AttachGameAsync(Guid id, Guid gameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE crawl_target SET game_id = @gameId WHERE id = @id AND game_id IS NULL",
            new { id, gameId },
            cancellationToken: ct));
    }

    private sealed class Row
    {
        public Guid Id { get; init; }

        public Guid? GameId { get; init; }

        public string Host { get; init; } = string.Empty;

        public int Port { get; init; }

        public bool UseTls { get; init; }

        public DateTimeOffset NextProbeAt { get; init; }

        public int ConsecutiveFailures { get; init; }

        public TimeSpan? CrawlDelay { get; init; }

        public DateTimeOffset FirstSeenAt { get; init; }

        public DateTimeOffset? LastProbedAt { get; init; }

        public Guid? DiscoveredFromGameId { get; init; }

        public int Depth { get; init; }

        public bool IsOperatorSeed { get; init; }

        public CrawlTarget ToRecord() => new()
        {
            Id = Id,
            GameId = GameId,
            Host = Host,
            Port = Port,
            UseTls = UseTls,
            NextProbeAt = NextProbeAt,
            ConsecutiveFailures = ConsecutiveFailures,
            CrawlDelay = CrawlDelay,
            FirstSeenAt = FirstSeenAt,
            LastProbedAt = LastProbedAt,
            DiscoveredFromGameId = DiscoveredFromGameId,
            Depth = Depth,
            IsOperatorSeed = IsOperatorSeed,
        };
    }
}
