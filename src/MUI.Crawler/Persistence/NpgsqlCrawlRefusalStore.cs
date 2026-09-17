using Dapper;

using MUI.Crawl;
using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// One address we are declining to dial, as the register holds it.
/// </summary>
/// <param name="Host">The address, canonicalised the way the crawl registry canonicalises it.</param>
/// <param name="Port">The port. Unlike an opt-out, a refusal is always about one socket.</param>
/// <param name="Reason">Which of the two decisions this was.</param>
/// <param name="Detail">What the guard or the opt-out register said, in its own words.</param>
/// <param name="FirstRefusedAt">When we first declined this address.</param>
/// <param name="LastRefusedAt">When we last declined it.</param>
/// <param name="Times">How many cycles have met it.</param>
public sealed record CrawlRefusal(
    string Host,
    int Port,
    DialRefusal Reason,
    string Detail,
    DateTimeOffset FirstRefusedAt,
    DateTimeOffset LastRefusedAt,
    int Times);

/// <summary>
/// Where the addresses we decline to dial are written down (issue #185).
/// </summary>
/// <remarks>
/// <b>Standing refusals, not a log.</b> <see cref="ClearAsync"/> is called on every dial that
/// happens, so a row means "we are not dialling this, now" rather than "this was once refused". A
/// table that only grew would be a queue nobody reads, which is the defect <c>icon_attempt</c>'s
/// ancestor shipped.
/// </remarks>
public interface ICrawlRefusalStore
{
    /// <summary>Records a refusal, or counts another meeting with one already held.</summary>
    Task RecordAsync(
        string host,
        int port,
        Guid? gameId,
        DialRefusal reason,
        string detail,
        DateTimeOffset at,
        CancellationToken ct);

    /// <summary>Forgets any refusal held for this address, because it no longer stands.</summary>
    Task ClearAsync(string host, int port, CancellationToken ct);

    /// <summary>Every standing refusal, longest-standing first.</summary>
    Task<IReadOnlyList<CrawlRefusal>> StandingAsync(int limit, CancellationToken ct);
}

/// <summary>
/// The <c>crawl_refusal</c> table.
/// </summary>
/// <remarks>
/// Hosts are canonicalised on the way in and on every lookup with
/// <see cref="CanonicalHost.Normalize"/>, the rule the crawl registry obeys — a refusal filed under
/// a spelling the crawl loop never looks up would never be cleared.
/// </remarks>
public sealed class NpgsqlCrawlRefusalStore(NpgsqlDataSource source) : ICrawlRefusalStore
{
    public async Task RecordAsync(
        string host,
        int port,
        Guid? gameId,
        DialRefusal reason,
        string detail,
        DateTimeOffset at,
        CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        // first_refused_at is not in the update list, for the same reason crawl_opt_out leaves
        // recorded_at alone: when we first declined and when we last did are two different facts,
        // and meeting a standing refusal again may only move the second.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO crawl_refusal (
                host, port, game_id, reason, detail, first_refused_at, last_refused_at, times)
            VALUES (@host, @port, @gameId, @reason, @detail, @at, @at, 1)
            ON CONFLICT (host, port) DO UPDATE
               SET game_id = EXCLUDED.game_id,
                   reason = EXCLUDED.reason,
                   detail = EXCLUDED.detail,
                   last_refused_at = EXCLUDED.last_refused_at,
                   times = crawl_refusal.times + 1
            """,
            new
            {
                host = CanonicalHost.Normalize(host),
                port,
                gameId,
                reason = ToDb(reason),
                detail,
                at = at.ToUniversalTime(),
            },
            cancellationToken: ct));
    }

    public async Task ClearAsync(string host, int port, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM crawl_refusal WHERE host = @host AND port = @port",
            new { host = CanonicalHost.Normalize(host), port },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CrawlRefusal>> StandingAsync(int limit, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await using var connection = await source.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT host AS Host, port AS Port, reason AS Reason, detail AS Detail,
                   first_refused_at AS FirstRefusedAt, last_refused_at AS LastRefusedAt, times AS Times
              FROM crawl_refusal
             ORDER BY first_refused_at, host, port
             LIMIT @limit
            """,
            new { limit },
            cancellationToken: ct));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    /// <summary>
    /// The two words the table's own vocabulary allows.
    /// </summary>
    /// <remarks>
    /// <c>None</c> throws rather than storing anything: a refusal that was not a refusal is a defect
    /// at the call site, and writing a row for it would put an address in the operator's "not
    /// dialling" list that we are in fact dialling.
    /// </remarks>
    private static string ToDb(DialRefusal reason) => reason switch
    {
        DialRefusal.OutOfScope => "out_of_scope",
        DialRefusal.OptedOut => "opted_out",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Not a refusal."),
    };

    private static DialRefusal ToRefusal(string value) => value switch
    {
        "out_of_scope" => DialRefusal.OutOfScope,
        "opted_out" => DialRefusal.OptedOut,
        _ => throw new InvalidOperationException($"Unread crawl_refusal reason: {value}."),
    };

    /// <summary>
    /// The row as Dapper materialises it.
    /// </summary>
    /// <remarks>
    /// Settable properties rather than a positional record, the shape every other store here uses:
    /// Dapper matches a record's constructor by parameter type, and Npgsql hands it a
    /// <see cref="DateTime"/> for a <c>timestamptz</c>, so a positional
    /// <see cref="DateTimeOffset"/> parameter fails to materialise at run time with nothing to see
    /// at compile time.
    /// </remarks>
    private sealed class Row
    {
        public string Host { get; init; } = string.Empty;

        public int Port { get; init; }

        public string Reason { get; init; } = string.Empty;

        public string Detail { get; init; } = string.Empty;

        public DateTimeOffset FirstRefusedAt { get; init; }

        public DateTimeOffset LastRefusedAt { get; init; }

        public int Times { get; init; }

        public CrawlRefusal ToRecord() => new(
            Host, Port, ToRefusal(Reason), Detail, FirstRefusedAt, LastRefusedAt, Times);
    }
}
