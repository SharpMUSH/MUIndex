using Dapper;

using MUI.Catalog.Persistence;

using Npgsql;

using Testcontainers.PostgreSql;

using TUnit.Core.Exceptions;

namespace MUI.Web.Tests.Support;

/// <summary>
/// One PostgreSQL 17 container for the whole suite, and a fresh database per test.
/// </summary>
/// <remarks>
/// A real database is what proves <c>GET /health</c> reports a real connection failure correctly — a
/// mocked <c>NpgsqlDataSource</c> can be told to throw, but not to reproduce the timeout behaviour.
/// Where no container runtime is available the tests skip loudly by name; set
/// <c>MUI_TEST_POSTGRES</c> to use a database you already have (Testcontainers reads
/// <c>DOCKER_HOST</c>, so a rootless Podman socket works). This is a deliberate copy of
/// <c>MUI.Catalog.Tests</c>' and <c>MUI.Crawler.Tests</c>' fixture — referencing either project would
/// pull its source-generated tests into this assembly and run their suites again here.
/// </remarks>
public static class PostgresFixture
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static NpgsqlDataSource? _admin;

    private static string? _template;

    private static PostgreSqlContainer? _container;

    private static string? _unavailable;

    private static int _sequence;

    /// <summary>A fresh, empty database on the shared server.</summary>
    public static async Task<TestDatabase> FreshDatabaseAsync()
    {
        var admin = await AdminAsync();
        var name = $"mui_test_{Interlocked.Increment(ref _sequence)}_{Guid.NewGuid():N}"[..40];

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

        await using (var dataSource = NpgsqlDataSource.Create(database.ConnectionString))
        {
            await new MigrationRunner(dataSource).ApplyAsync();
        }

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
                    .Build();

                await _container.StartAsync();
                _template = _container.GetConnectionString();

                return _admin = NpgsqlDataSource.Create(_template);
            }
            catch (Exception error)
            {
                // Deliberately broad: Testcontainers reports "no container runtime", a socket
                // refusal and an image pull failure as three unrelated exception types, and the
                // honest answer to all three is the same one.
                _unavailable =
                    "No PostgreSQL was reachable, so the health-check tests did not run. Start a "
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
    /// Skipping is honest on a laptop with no container runtime, but wrong in CI, where a silently
    /// skipped storage suite is a green build that tested no storage — so CI's Linux leg sets
    /// <c>MUI_REQUIRE_POSTGRES</c> and turns the skip into a failure.
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
    public string ConnectionString { get; } = connectionString;

    public async ValueTask DisposeAsync()
    {
        await using var connection = await admin.OpenConnectionAsync();
        await connection.ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }
}
