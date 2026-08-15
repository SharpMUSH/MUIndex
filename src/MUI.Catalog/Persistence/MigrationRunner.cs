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
/// Idempotent by construction: it runs on every process start, in every replica, for ever, and a
/// second run must apply nothing. Deliberately not a migration framework — plain SQL files and a
/// ledger table are legible to anyone with <c>psql</c>, which is the property that matters when
/// something has gone wrong in production at four in the morning.
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
    public async Task<IReadOnlyList<string>> ApplyAsync(CancellationToken cancellationToken = default)
    {
        // An empty set is never "nothing to do": this assembly is built with the migrations embedded
        // in it, so no scripts means the binary was assembled without them, and applying nothing
        // would leave a site running against a database with no schema and no complaint. Refusing to
        // start is the smaller failure.
        if (Scripts.Count == 0)
        {
            throw new InvalidOperationException(
                "This build of MUI.Catalog carries no embedded migrations, so it cannot say what "
                + "schema it expects. It was assembled without the migrations/ directory.");
        }

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

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
