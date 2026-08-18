using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// <see cref="IUnitOfWork"/> over one <see cref="NpgsqlConnection"/> and one
/// <see cref="NpgsqlTransaction"/> -- what actually lets <see cref="NpgsqlMergeLog"/> and
/// <see cref="NpgsqlDuplicateReviewRepository"/> share a commit.
/// </summary>
/// <remarks>
/// Every other repository in this namespace opens its own connection per call
/// (<c>source.OpenConnectionAsync</c>), which is correct when each call is its own atomic write. This
/// type exists for the one caller, <see cref="ReviewMergeService"/>, that needs two calls across two
/// different repositories to be one atomic write instead.
/// </remarks>
public sealed class NpgsqlUnitOfWork(NpgsqlConnection connection, NpgsqlTransaction transaction) : IUnitOfWork
{
    /// <summary>The connection a joining repository call must run its command against.</summary>
    public NpgsqlConnection Connection { get; } = connection;

    /// <summary>The transaction a joining repository call must attach its command to.</summary>
    public NpgsqlTransaction Transaction { get; } = transaction;

    public async Task CommitAsync(CancellationToken ct) => await Transaction.CommitAsync(ct);

    /// <summary>
    /// An uncommitted transaction rolls back on disposal -- Npgsql's own default behaviour -- so a
    /// caller that throws before calling <see cref="CommitAsync"/> gets a rollback for free.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }
}

/// <summary><see cref="IUnitOfWorkFactory"/> over a shared <see cref="NpgsqlDataSource"/>.</summary>
public sealed class NpgsqlUnitOfWorkFactory(NpgsqlDataSource source) : IUnitOfWorkFactory
{
    public async Task<IUnitOfWork> BeginAsync(CancellationToken ct)
    {
        var connection = await source.OpenConnectionAsync(ct);

        try
        {
            var transaction = await connection.BeginTransactionAsync(ct);
            return new NpgsqlUnitOfWork(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
