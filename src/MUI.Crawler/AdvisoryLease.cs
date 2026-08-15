using Dapper;

using Npgsql;

namespace MUI.Crawler;

/// <summary>
/// The one-worker-per-database gate (spec §12).
/// </summary>
/// <remarks>
/// <para>
/// The crawler is an in-process <c>BackgroundService</c> in the web deployable, so N web replicas
/// would otherwise be N crawlers: N times the traffic at every game in the catalogue, N presence rows
/// racing for one <c>(game_id, at)</c> key, and a politeness contract broken N ways. A PostgreSQL
/// <b>session-level advisory lock</b> makes the worker conditionally active instead — every replica
/// asks, one gets it, and the rest sit in a retry loop doing nothing.
/// </para>
/// <para>
/// <b>One mechanism, a key per worker.</b> Presence maintenance (§5.2) has the same problem and a
/// worse failure — two passes racing are two <c>DROP TABLE</c>s for one partition — and takes its own
/// key rather than sharing the crawl's, so neither worker's cycle length can delay the other's.
/// </para>
/// <para>
/// <b>Session-level, which is why the lease owns a connection and holds it open.</b> The lock lives
/// for as long as the session that took it, so returning the connection to the pool would return the
/// lock with it. Disposal closes the connection, which is also what makes the failure mode right: a
/// replica that is killed, loses the network, or has its process die releases the lock when the
/// backend notices the socket has gone, with no lease table to clean up and no expiry to tune.
/// </para>
/// <para>
/// <b>A lease also has to be re-checked, not merely taken.</b> A connection that dropped and was not
/// noticed is a replica that believes it is the crawler and holds nothing — and the moment a second
/// replica takes the lock, both are crawling. <see cref="IsHeldAsync"/> is the cheap question the
/// loop asks every cycle.
/// </para>
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
    /// Advisory locks share one namespace per database, so the number matters only in that nothing
    /// else in this database may pick it. It is a literal rather than a hash of a string precisely
    /// because a hash invites someone to change the string. Spelled <c>MUI_CRAW</c> in ASCII, which is
    /// a courtesy to whoever finds it in <c>pg_locks</c> at four in the morning.
    /// </remarks>
    public const long CrawlKey = 0x4D55495F4352_4157L;

    /// <summary>The presence rollup, partition and retention pass's key (§5.2). <c>MUI_ROLL</c>.</summary>
    public const long PresenceMaintenanceKey = 0x4D55495F524F_4C4CL;

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
    /// Answers false rather than throwing when the connection has gone, because "we no longer hold it"
    /// is exactly what a dead connection means and the caller's response to both is the same: stop
    /// crawling and ask again.
    /// </remarks>
    public async Task<bool> IsHeldAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // The lock is ours only if this session is the one holding it. Asking pg_locks rather
            // than re-taking it: pg_try_advisory_lock is re-entrant within a session and would answer
            // true while stacking a second hold we would then have to unwind.
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
    /// <para>
    /// <b>Closing the connection is not enough, and assuming it was is a deadlock that would only
    /// appear in production.</b> Npgsql pools: disposing an <c>NpgsqlConnection</c> returns the
    /// connector to the pool rather than closing the socket, so the backend session — and with it a
    /// session-level advisory lock — outlives this object. Measured, by
    /// <c>ReleasingTheLeaseLetsTheNextReplicaTakeIt</c>, which failed exactly that way before this
    /// call existed: a replica that stood down left the crawl stopped for the whole deployment until
    /// its process exited.
    /// </para>
    /// <para>
    /// The session-scoped lock is still what makes the <em>crash</em> case right — a killed process
    /// releases everything when the backend notices the socket has gone, with no lease table to clean
    /// up and no expiry to tune. This call handles the orderly case, which is the common one.
    /// </para>
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
