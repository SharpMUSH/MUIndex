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
/// <para>
/// A real database rather than a fake, because half of what this pipeline must obey is written in
/// <c>CHECK</c> constraints — the three presence states, the availability vocabulary, "at most one
/// open interval per game", "a crawl target's host is canonical" — and a cycle asserted only against
/// in-memory dictionaries has not been tested against the thing that enforces them.
/// </para>
/// <para>
/// Where no container runtime is available the tests <b>skip</b>, loudly and by name. A green suite
/// that never touched Postgres would be worse than an honestly skipped one. Set
/// <c>MUI_TEST_POSTGRES</c> to use a database you already have, and Testcontainers reads
/// <c>DOCKER_HOST</c>, so a rootless Podman socket works as well as Docker.
/// </para>
/// <para>
/// <b>This is a copy of <c>MUI.Catalog.Tests</c>' fixture, and the duplication is deliberate rather
/// than accidental.</b> Referencing that project would pull its source-generated tests into this
/// assembly and run the storage suite twice. The right fix is a small shared testing project; until
/// there is one, the two must be kept in step.
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

    /// <summary>A fresh database with the whole schema already applied.</summary>
    public static async Task<TestDatabase> MigratedAsync()
    {
        var admin = await AdminAsync();
        var name = $"mui_crawler_{Interlocked.Increment(ref _sequence)}_{Guid.NewGuid():N}"[..40];

        await using (var connection = await admin.OpenConnectionAsync())
        {
            await connection.ExecuteAsync($"CREATE DATABASE \"{name}\"");
        }

        var builder = new NpgsqlConnectionStringBuilder(_template) { Database = name };
        var database = new TestDatabase(name, builder.ConnectionString, admin);

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
                    // test against one server. Postgres allows a hundred clients by default and this
                    // suite reached that ceiling the moment the opt-out, on-demand-probe and
                    // submission tests were in it together — as "sorry, too many clients already",
                    // which fails whichever tests happened to be starting and so reports itself as a
                    // flake in half a dozen unrelated places. MUI.Catalog.Tests' copy of this fixture
                    // hit it first and lifted the ceiling there; this is the same lift, and the two
                    // fixtures are meant to be kept in step.
                    .WithCommand("-c", "max_connections=500")
                    .Build();

                await _container.StartAsync();
                _template = _container.GetConnectionString();

                return _admin = NpgsqlDataSource.Create(_template);
            }
            catch (Exception error)
            {
                // Deliberately broad: Testcontainers reports "no container runtime", a socket refusal
                // and an image pull failure as three unrelated exception types, and the honest answer
                // to all three is the same one.
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
    /// Built from the original connection string rather than from
    /// <c>NpgsqlDataSource.ConnectionString</c>, which redacts the password: a pool built from that
    /// cannot authenticate, and the failure looks like a broken lock rather than a broken fixture.
    /// </remarks>
    public NpgsqlDataSource SecondPool() => NpgsqlDataSource.Create(connectionString);

    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync();

        await using var connection = await admin.OpenConnectionAsync();
        await connection.ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }
}
