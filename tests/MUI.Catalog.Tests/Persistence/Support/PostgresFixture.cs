using Dapper;

using MUI.Catalog.Persistence;

using Npgsql;

using Testcontainers.PostgreSql;

using TUnit.Core.Exceptions;

namespace MUI.Catalog.Tests.Persistence.Support;

/// <summary>
/// One PostgreSQL 17 container for the whole suite, and a fresh database per test.
/// </summary>
/// <remarks>
/// <para>
/// A real database rather than a fake, because half of this design is written in <c>CHECK</c>
/// constraints — the three presence states, the availability vocabulary, "at most one open interval
/// per game" — and a writer asserted only against an in-memory dictionary has not been tested against
/// the thing that enforces them.
/// </para>
/// <para>
/// Where no container runtime is available the tests <b>skip</b>, loudly and by name. A green suite
/// that never touched Postgres would be worse than an honestly skipped one: it would report that the
/// schema works when nothing had run it. Set <c>MUI_TEST_POSTGRES</c> to a connection string to use a
/// database you already have, and Testcontainers reads <c>DOCKER_HOST</c>, so a rootless Podman
/// socket works as well as Docker.
/// </para>
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

        return new TestDatabase(name, NpgsqlDataSource.Create(builder.ConnectionString), admin);
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

                    // A database per test, and tests in parallel, means one live connection pool per
                    // test against one server. Postgres allows a hundred clients by default and the
                    // suite reached that ceiling on the run that took it past a hundred and thirty
                    // tests — as "sorry, too many clients already", which fails whichever tests
                    // happened to be starting and so reports itself as a flake in twenty unrelated
                    // places. The ceiling is a property of this fixture's shape and not of anything
                    // under test, so it is lifted here rather than paid for by whoever adds the next
                    // storage test.
                    .WithCommand("-c", "max_connections=500")
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
                    "No PostgreSQL was reachable, so the persistence tests did not run. Start a "
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
    /// Skipping is the honest answer on a laptop with no container runtime. It is the wrong answer in
    /// CI, where a silently skipped storage suite is a green build that tested no storage — so the
    /// Linux leg sets <c>MUI_REQUIRE_POSTGRES</c> and turns the skip into a failure.
    /// </summary>
    private static Exception Unavailable(string reason) =>
        string.Equals(
            Environment.GetEnvironmentVariable("MUI_REQUIRE_POSTGRES"), "true", StringComparison.OrdinalIgnoreCase)
            ? new InvalidOperationException($"MUI_REQUIRE_POSTGRES is set. {reason}")
            : new SkipTestException(reason);
}

/// <summary>A throwaway database, dropped when the test that made it is done.</summary>
public sealed class TestDatabase(string name, NpgsqlDataSource dataSource, NpgsqlDataSource admin)
    : IAsyncDisposable
{
    public NpgsqlDataSource DataSource { get; } = dataSource;

    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync();

        await using var connection = await admin.OpenConnectionAsync();
        await connection.ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }
}
