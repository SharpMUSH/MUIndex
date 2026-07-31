using Dapper;

using MUI.Catalog.Persistence;

using Npgsql;

using Testcontainers.PostgreSql;

using TUnit.Core.Exceptions;

namespace MUI.Import.Tests.Support;

/// <summary>
/// One PostgreSQL 17 container for this suite, and a fresh migrated database per test.
/// </summary>
/// <remarks>
/// A real database rather than a fake, because the half-weight rule finally rests on a column with a
/// default and a CHECK — <c>availability_interval.origin</c> — and the in-memory writer beside it is
/// a second spelling of the same fact with nothing holding the two together. Where no container
/// runtime is available the tests skip, loudly and by name; <c>MUI_REQUIRE_POSTGRES</c> turns that
/// skip into a failure, which is what CI's Linux leg sets. Testcontainers reads <c>DOCKER_HOST</c>,
/// so a rootless Podman socket works as well as Docker.
/// </remarks>
public static class PostgresFixture
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static NpgsqlDataSource? _admin;

    private static string? _template;

    private static PostgreSqlContainer? _container;

    private static string? _unavailable;

    private static int _sequence;

    /// <summary>A fresh database with the whole schema applied.</summary>
    public static async Task<TestDatabase> MigratedAsync()
    {
        var admin = await AdminAsync();
        var name = $"mui_import_{Interlocked.Increment(ref _sequence)}_{Guid.NewGuid():N}"[..40];

        await using (var connection = await admin.OpenConnectionAsync())
        {
            await connection.ExecuteAsync($"CREATE DATABASE \"{name}\"");
        }

        var builder = new NpgsqlConnectionStringBuilder(_template) { Database = name };
        var database = new TestDatabase(NpgsqlDataSource.Create(builder.ConnectionString));

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
                throw Skip(_unavailable);
            }

            if (Environment.GetEnvironmentVariable("MUI_TEST_POSTGRES") is { Length: > 0 } existing)
            {
                _template = existing;
                _admin = NpgsqlDataSource.Create(existing);

                return _admin;
            }

            try
            {
                _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
                await _container.StartAsync();

                _template = _container.GetConnectionString();
                _admin = NpgsqlDataSource.Create(_template);

                return _admin;
            }
            catch (Exception exception)
            {
                _unavailable = exception.Message;

                throw Skip(_unavailable);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static Exception Skip(string reason)
    {
        // A silently skipped storage test is a green build that tested no storage, so CI demands one.
        if (Environment.GetEnvironmentVariable("MUI_REQUIRE_POSTGRES") is "1" or "true" or "True")
        {
            return new InvalidOperationException(
                $"MUI_REQUIRE_POSTGRES is set and no PostgreSQL was available: {reason}");
        }

        return new SkipTestException($"No container runtime for PostgreSQL: {reason}");
    }

    public sealed class TestDatabase(NpgsqlDataSource dataSource) : IAsyncDisposable
    {
        public NpgsqlDataSource DataSource { get; } = dataSource;

        public async ValueTask DisposeAsync() => await DataSource.DisposeAsync();
    }
}
