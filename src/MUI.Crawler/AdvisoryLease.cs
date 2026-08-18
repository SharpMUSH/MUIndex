using Dapper;

using Npgsql;

namespace MUI.Crawler;

/// <summary>
/// The one-worker-per-database gate (spec §12).
/// </summary>
/// <remarks>
/// The crawler is an in-process <c>BackgroundService</c> in the web deployable, so N replicas would
/// otherwise be N crawlers racing on the same targets. A PostgreSQL session-level advisory lock makes
/// one replica active; the rest retry. Presence maintenance (§5.2) takes its own key rather than
/// sharing the crawl's, so neither worker's cycle length can delay the other's. The lock lives as long
/// as the session, so the lease owns its connection and holds it open rather than returning it to the
/// pool; a killed or disconnected replica releases the lock when the backend notices the socket is
/// gone. <see cref="IsHeldAsync"/> lets the loop re-check each cycle in case that happened unnoticed.
/// </remarks>
public sealed class AdvisoryLease : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly long _key;

    private int _released;

    private AdvisoryLease(NpgsqlConnection connection, long key)
    {
        _connection = connection;
        _key = key;
    }

    /// <summary>
    /// The crawl loop's key, chosen once and never derived from anything that could change.
    /// </summary>
    /// <remarks>
    /// Advisory locks share one namespace per database, so nothing else here may reuse this number. A
    /// literal, not a hash of a string, so it can't drift if the string changes. Spells <c>MUI_CRAW</c>
    /// in ASCII.
    /// </remarks>
    public const long CrawlKey = 0x4D55495F4352_4157L;

    /// <summary>The presence rollup, partition and retention pass's key (§5.2). <c>MUI_ROLL</c>.</summary>
    public const long PresenceMaintenanceKey = 0x4D55495F524F_4C4CL;

    /// <summary>
    /// The Intermud-3 pass's key. <c>MUI_IMUD</c>.
    /// </summary>
    /// <remarks>
    /// Its own key rather than the crawl lease's, so a long crawl cycle cannot delay an I3 pass and a
    /// deployment running with the crawler off still keeps its I3 bindings current.
    /// </remarks>
    public const long I3Key = 0x4D55495F494D_5544L;

    /// <summary>
    /// Takes the lock if it is free, or returns null. Never waits: a replica that cannot have the
    /// lock has nothing to wait for, and <c>pg_advisory_lock</c> would block a hosted service's
    /// startup indefinitely.
    /// </summary>
    public static async Task<AdvisoryLease?> TryAcquireAsync(
        NpgsqlDataSource source,
        long key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var connection = await source.OpenConnectionAsync(cancellationToken);

        try
        {
            var taken = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT pg_try_advisory_lock(@key)",
                new { key },
                cancellationToken: cancellationToken));

            if (taken)
            {
                return new AdvisoryLease(connection, key);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();

        return null;
    }

    /// <summary>
    /// Whether this lease still holds the lock, asked of the database rather than of our own memory.
    /// </summary>
    /// <remarks>
    /// Answers false rather than throwing when the connection has gone, since that's exactly what a
    /// dead connection means and the caller's response to both is the same: stop crawling and ask again.
    /// </remarks>
    public async Task<bool> IsHeldAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Asks pg_locks rather than re-taking it: pg_try_advisory_lock is re-entrant within a
            // session and would answer true while stacking a second hold we'd then have to unwind.
            return await _connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (
                    SELECT 1 FROM pg_locks
                     WHERE locktype = 'advisory'
                       AND granted
                       AND pid = pg_backend_pid()
                       -- objsubid 1 is the single-bigint form of the key, which is the one
                       -- pg_try_advisory_lock(@key) took; 2 would be the two-int overload.
                       AND objsubid = 1
                       AND ((classid::bigint << 32) | objid::bigint) = @key)
                """,
                new { key = _key },
                cancellationToken: cancellationToken));
        }
        catch (Exception error) when (error is NpgsqlException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Releases the lock, explicitly, and then lets the connection go.
    /// </summary>
    /// <remarks>
    /// Disposing the connection alone is not enough: Npgsql pools connections, so disposing an
    /// <c>NpgsqlConnection</c> returns the connector to the pool rather than closing the socket, and
    /// the backend session — with the advisory lock still held — outlives this object. An explicit
    /// unlock is required for the orderly shutdown case; the crash case is still handled by the
    /// session dying when the backend notices the socket has gone.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        try
        {
            await _connection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_unlock(@key)", new { key = _key }));
        }
        catch (Exception error) when (error is NpgsqlException or InvalidOperationException)
        {
            // The connection has already gone, which released the lock more thoroughly than this
            // would have.
        }

        await _connection.DisposeAsync();
    }
}
