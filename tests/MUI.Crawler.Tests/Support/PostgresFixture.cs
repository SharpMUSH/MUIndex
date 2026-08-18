using Dapper;

using MUI.Catalog.Persistence;

using Npgsql;

using Testcontainers.PostgreSql;

using TUnit.Core.Exceptions;

namespace MUI.Crawler.Tests.Support;

/// <summary>
/// One PostgreSQL 17 container for the whole suite, and a fresh database per test.
/// </summary>
/// <remarks>
/// A real database rather than a fake, since half of what this pipeline must obey is written in
/// <c>CHECK</c> constraints, not asserted in code. Where no container runtime is available the tests
/// <b>skip</b>, loudly and by name — a green suite that never touched Postgres would be worse than an
/// honestly skipped one. Set <c>MUI_TEST_POSTGRES</c> to use a database you already have.
/// This is a deliberate copy of <c>MUI.Catalog.Tests</c>' fixture (referencing that project would run
/// its storage suite twice); keep the two in step until there's a shared testing project.
/// </remarks>
public static class PostgresFixture
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static NpgsqlDataSource? _admin;

    private static string? _template;

    private static PostgreSqlContainer? _container;

    private static string? _unavailable;

    private static int _sequence;

    /// <summary>
    /// A fresh, empty database on the shared server — no schema at all.
    /// </summary>
    /// <remarks>
    /// What a deployment's first seconds look like — hosted services start beside the migration run
    /// rather than after it, and have to survive this state.
    /// </remarks>
    public static async Task<TestDatabase> FreshDatabaseAsync()
    {
        var admin = await AdminAsync();
        var name = $"mui_crawler_{Interlocked.Increment(ref _sequence)}_{Guid.NewGuid():N}"[..40];

        await using (var connection = await admin.OpenConnectionAsync())
        {
            await connection.ExecuteAsync($"CREATE DATABASE \"{name}\"");
        }

        var builder = new NpgsqlConnectionStringBuilder(_template) { Database = name };

        return new TestDatabase(name, builder.ConnectionString, admin);
    }

    /// <summary>A fresh database with the whole schema already applied.</summary>
    public static async Task<TestDatabase> MigratedAsync()
    {
        var database = await FreshDatabaseAsync();

        await new MigrationRunner(database.DataSource).ApplyAsync();

        return database;
    }

    private static async Task<NpgsqlDataSource> AdminAsync()
    {
        if (_admin is not null)
        {
            return _admin;
        }

        await Gate.WaitAsync();

        try
        {
            if (_admin is not null)
            {
                return _admin;
            }

            if (_unavailable is not null)
            {
                throw Unavailable(_unavailable);
            }

            var external = Environment.GetEnvironmentVariable("MUI_TEST_POSTGRES");

            if (!string.IsNullOrWhiteSpace(external))
            {
                _template = external;
                return _admin = NpgsqlDataSource.Create(external);
            }

            try
            {
                _container = new PostgreSqlBuilder("postgres:17-alpine")
                    .WithCleanUp(true)

                    // A database per test, run in parallel, means one live connection pool per test
                    // against one server — Postgres's default 100-client ceiling is not enough for
                    // this suite. Keep in step with MUI.Catalog.Tests' copy of this fixture.
                    .WithCommand("-c", "max_connections=500")
                    .Build();

                await _container.StartAsync();
                _template = _container.GetConnectionString();

                return _admin = NpgsqlDataSource.Create(_template);
            }
            catch (Exception error)
            {
                // Deliberately broad: Testcontainers reports several unrelated failure modes as
                // different exception types, and the honest answer to all of them is the same one.
                _unavailable =
                    "No PostgreSQL was reachable, so the crawler's storage tests did not run. Start a "
                    + "container runtime (Testcontainers reads DOCKER_HOST) or set MUI_TEST_POSTGRES "
                    + $"to a connection string. The runtime said: {error.Message}";

                throw Unavailable(_unavailable);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Skipping is the honest answer on a laptop with no container runtime and the wrong one in CI,
    /// where a silently skipped storage suite is a green build that tested no storage.
    /// </summary>
    private static Exception Unavailable(string reason) =>
        string.Equals(
            Environment.GetEnvironmentVariable("MUI_REQUIRE_POSTGRES"), "true", StringComparison.OrdinalIgnoreCase)
            ? new InvalidOperationException($"MUI_REQUIRE_POSTGRES is set. {reason}")
            : new SkipTestException(reason);
}

/// <summary>A throwaway database, dropped when the test that made it is done.</summary>
public sealed class TestDatabase(string name, string connectionString, NpgsqlDataSource admin)
    : IAsyncDisposable
{
    public NpgsqlDataSource DataSource { get; } = NpgsqlDataSource.Create(connectionString);

    /// <summary>
    /// A second, independent pool against the same database — a second replica, for the
    /// advisory-lock tests.
    /// </summary>
    /// <remarks>
    /// Built from the original connection string, not <c>NpgsqlDataSource.ConnectionString</c> (which
    /// redacts the password) — a pool from that can't authenticate, and the failure looks like a
    /// broken lock rather than a broken fixture.
    /// </remarks>
    public NpgsqlDataSource SecondPool() => NpgsqlDataSource.Create(connectionString);

    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync();

        await using var connection = await admin.OpenConnectionAsync();
        await connection.ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }
}
