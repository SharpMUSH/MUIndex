using System.Reflection;

using Dapper;

using Microsoft.Extensions.Logging;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// Applies the numbered <c>.sql</c> files from <c>migrations/</c>, in lexical order, each inside its
/// own transaction, recording each in the <c>mui_migration</c> ledger.
/// </summary>
/// <remarks>
/// Idempotent by construction: runs on every process start, in every replica, and a second run
/// applies nothing. Deliberately plain SQL files and a ledger table rather than a migration
/// framework, so it stays legible with just <c>psql</c>.
/// </remarks>
public sealed class MigrationRunner(NpgsqlDataSource source, ILogger? logger = null)
{
    private const string ResourcePrefix = "MUI.Catalog.Migrations.";

    private const string LedgerDdl = """
        CREATE TABLE IF NOT EXISTS mui_migration (
            name       text PRIMARY KEY,
            applied_at timestamptz NOT NULL DEFAULT now()
        )
        """;

    /// <summary>Every embedded migration, in the order it will be applied.</summary>
    public static IReadOnlyList<Migration> Scripts { get; } = LoadScripts();

    /// <summary>Applies whatever has not been applied yet, and returns the names of what it ran.</summary>
    /// <summary>
    /// The advisory-lock key the migration run holds. <c>MUI_MIGR</c> in ASCII.
    /// </summary>
    /// <remarks>
    /// Declared here rather than beside the background services' keys in <c>AdvisoryLease</c>, because
    /// that type lives in <c>MUI.Crawler</c> and the dependency runs the other way -- the catalogue
    /// does not know the crawler exists. Advisory locks share one namespace per database, so the
    /// number is reserved there in a comment even though it is defined here.
    /// </remarks>
    public const long MigrationKey = 0x4D55495F4D49_4752L;

    /// <summary>How long to wait for the migration lock before giving up and letting the restart retry.</summary>
    private static readonly TimeSpan LockWait = TimeSpan.FromMinutes(5);

    /// <summary>How often to re-ask while waiting.</summary>
    private static readonly TimeSpan LockPoll = TimeSpan.FromSeconds(2);

    public async Task<IReadOnlyList<string>> ApplyAsync(CancellationToken cancellationToken = default)
    {
        // An empty set means this binary was assembled without migrations/, not that there's
        // nothing to do — applying nothing would leave the site running against a schema-less
        // database with no complaint. Refusing to start is the smaller failure.
        if (Scripts.Count == 0)
        {
            throw new InvalidOperationException(
                "This build of MUI.Catalog carries no embedded migrations, so it cannot say what "
                + "schema it expects. It was assembled without the migrations/ directory.");
        }

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // Every replica runs this on every start (spec §4.11), so without a lock they run it *at the
        // same time* -- and the ledger read below is what each one decides from. Two replicas both
        // seeing a migration missing will both apply it.
        //
        // Found in production on 2026-08-27 deploying migration 0037: one replica applied it, the
        // other was mid-flight in the same script, and its `CREATE TABLE IF NOT EXISTS <partition>`
        // silently matched the partition the winner had just created and attached elsewhere. Its new
        // parent therefore had no partitions at all and the copy died on "no partition of relation
        // presence_rollup_hour found for row", taking the process down with an unhandled exception.
        // Transactional DDL meant nothing was lost and the replica came back clean on restart, which
        // is the only reason that was an incident and not a disaster.
        //
        // Session-level rather than transaction-level: the run spans one transaction per script, so a
        // lock scoped to a transaction would be released between them and let a second replica in
        // exactly where the damage is.
        await AcquireAsync(connection, cancellationToken);

        try
        {
            return await ApplyHeldAsync(connection, cancellationToken);
        }
        finally
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_unlock(@key)", new { key = MigrationKey }));
        }
    }

    /// <summary>
    /// Takes the migration lock, saying so if it has to wait, and giving up rather than hanging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deliberately not a bare <c>pg_advisory_lock</c>, which waits for ever.</b> Postgres frees a
    /// session lock when the backend goes away, so a replica that crashes mid-migration cannot leak it
    /// permanently — checked both ways: terminating the backend releases it, and so does the
    /// <c>DISCARD ALL</c> Npgsql sends when a pooled connection is returned. What is *not* bounded is
    /// a backend that is wedged rather than dead. TCP keepalive defaults are measured in hours, and
    /// for all of that time an unbounded waiter would sit in startup holding no lock, doing nothing,
    /// and logging nothing, on every replica at once. That is a worse outage than the race this lock
    /// exists to close, and it is silent, which is the part that makes it worse.
    /// </para>
    /// <para>
    /// So: poll, say out loud that we are waiting and why, and fail with something an operator can act
    /// on rather than hanging. Failing is safe — the process exits before serving, and
    /// <c>restart: unless-stopped</c> brings it back to try again, by which time the winner has
    /// normally finished. A replica that cannot get the lock must never carry on and serve: what it
    /// would be serving is a database whose schema another replica is halfway through changing.
    /// </para>
    /// <para>
    /// The ceiling is generous because a legitimate migration can be slow — 0037 rewrote a table — and
    /// a loser that gives up too early turns one slow migration into a restart loop across every
    /// replica.
    /// </para>
    /// </remarks>
    private async Task AcquireAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var waited = TimeSpan.Zero;
        var announced = false;

        while (true)
        {
            var taken = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT pg_try_advisory_lock(@key)",
                new { key = MigrationKey },
                cancellationToken: cancellationToken));

            if (taken)
            {
                if (announced)
                {
                    logger?.LogInformation(
                        "Took the migration lock after waiting {Seconds:0}s", waited.TotalSeconds);
                }

                return;
            }

            if (!announced)
            {
                logger?.LogInformation(
                    "Another replica is applying migrations; waiting for the schema lock");
                announced = true;
            }

            if (waited >= LockWait)
            {
                throw new TimeoutException(
                    $"Waited {LockWait.TotalMinutes:0} minutes for the migration lock and did not get "
                    + "it. Another replica is applying migrations and has not finished, or a backend "
                    + "holding the lock is wedged rather than dead — `SELECT pid, query FROM pg_locks "
                    + "JOIN pg_stat_activity USING (pid) WHERE locktype = 'advisory'` says which. This "
                    + "process is exiting rather than serving a database whose schema is mid-change; "
                    + "it will try again when it restarts.");
            }

            await Task.Delay(LockPoll, cancellationToken);
            waited += LockPoll;
        }
    }

    /// <summary>Applies what the ledger says is missing, with the migration lock already held.</summary>
    /// <remarks>
    /// <b>The ledger is read here, inside the lock, and never before it.</b> A replica that waited for
    /// the lock waited precisely because another was applying migrations, so anything it read before
    /// waiting describes a database that no longer exists. Reading after is what turns the loser of
    /// the race into a no-op instead of a second application.
    /// </remarks>
    private async Task<IReadOnlyList<string>> ApplyHeldAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(LedgerDdl, cancellationToken: cancellationToken));

        var already = (await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT name FROM mui_migration", cancellationToken: cancellationToken)))
            .ToHashSet(StringComparer.Ordinal);

        var applied = new List<string>();

        foreach (var (name, sql) in Scripts)
        {
            if (already.Contains(name))
            {
                continue;
            }

            // DDL is transactional in PostgreSQL, so a migration that fails halfway leaves nothing
            // behind, and the ledger entry is written by the same transaction as the schema change.
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await connection.ExecuteAsync(new CommandDefinition(
                sql, transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO mui_migration (name) VALUES (@name)",
                new { name },
                transaction,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            logger?.LogInformation("Applied migration {Migration}", name);
            applied.Add(name);
        }

        return applied;
    }

    private static IReadOnlyList<Migration> LoadScripts()
    {
        var assembly = typeof(MigrationRunner).Assembly;

        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new Migration(name[ResourcePrefix.Length..], Read(assembly, name)))
            .ToList();
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded migration '{resource}' is missing.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>One migration file: the name recorded in the ledger, and its SQL.</summary>
    public sealed record Migration(string Name, string Sql);
}
