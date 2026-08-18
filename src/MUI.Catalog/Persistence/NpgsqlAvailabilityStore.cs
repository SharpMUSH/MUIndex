using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// The <c>availability_interval</c> table (spec §5.3, §7.6).
/// </summary>
/// <remarks>
/// Intervals rather than samples, and at most one open per game — which the partial unique index
/// enforces, so a caller that forgot to close the first cannot leave two running.
/// </remarks>
public sealed class NpgsqlAvailabilityStore(NpgsqlDataSource source) : IAvailabilityStore, IReachableHistory
{
    private const string Columns = """
        game_id AS GameId, state AS State, from_at AS FromAt, to_at AS ToAt, cause AS Cause, detail AS Detail
        """;

    public async Task<AvailabilityInterval?> OpenIntervalAsync(
        Guid gameId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM availability_interval WHERE game_id = @gameId AND to_at IS NULL",
            new { gameId },
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public Task OpenAsync(AvailabilityInterval interval, CancellationToken cancellationToken = default) =>
        OpenAsync(interval, IntervalOrigin.FirstParty, cancellationToken);

    /// <summary>
    /// Opens an interval, stating who measured it. §7.6's importers use this arm; everything on the
    /// probe path uses the interface's, which is first-party by definition.
    /// </summary>
    public async Task OpenAsync(
        AvailabilityInterval interval,
        IntervalOrigin origin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interval);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO availability_interval (game_id, state, from_at, to_at, cause, origin, detail)
            VALUES (@gameId, @state, @fromAt, @toAt, @cause, @origin, @detail)
            """,
            new
            {
                gameId = interval.GameId,
                state = SqlEnums.ToDb(interval.State),
                fromAt = interval.FromAt.ToUniversalTime(),
                toAt = interval.ToAt?.ToUniversalTime(),
                cause = SqlEnums.ToDb(interval.Cause),
                origin = SqlEnums.ToDb(origin),
                detail = interval.Detail,
            },
            cancellationToken: cancellationToken));
    }

    public async Task CloseAsync(Guid gameId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // Closing leaves the row in the history rather than removing it. Nothing is ever deleted.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE availability_interval SET to_at = @at WHERE game_id = @gameId AND to_at IS NULL",
            new { gameId, at = at.ToUniversalTime() },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<AvailabilityInterval>> ForGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM availability_interval WHERE game_id = @gameId ORDER BY from_at",
            new { gameId },
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToRecord()).ToList();
    }

    public Task<TimeSpan> CumulativeReachableAsync(
        Guid gameId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        CumulativeAsync(gameId, now, IntervalOrigin.FirstParty, cancellationToken);

    private async Task<TimeSpan> CumulativeAsync(
        Guid gameId,
        DateTimeOffset now,
        IntervalOrigin origin,
        CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // Summed here rather than by reading every interval into memory: a game watched for a decade
        // has few intervals, but §7.5's sweep asks this of every game in the catalogue at once. The
        // open interval is counted to `now`, and a clamp keeps a clock skew from crediting a game
        // with negative time.
        var seconds = await connection.ExecuteScalarAsync<double>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(GREATEST(EXTRACT(EPOCH FROM (LEAST(COALESCE(to_at, @now), @now) - from_at)), 0)), 0)
              FROM availability_interval
             WHERE game_id = @gameId AND state = 'reachable' AND origin = @origin
            """,
            new { gameId, now = now.ToUniversalTime(), origin = SqlEnums.ToDb(origin) },
            cancellationToken: cancellationToken));

        return TimeSpan.FromSeconds(seconds);
    }

    private sealed class Row
    {
        public Guid GameId { get; init; }

        public string State { get; init; } = string.Empty;

        public DateTimeOffset FromAt { get; init; }

        public DateTimeOffset? ToAt { get; init; }

        public string Cause { get; init; } = string.Empty;

        public string? Detail { get; init; }

        public AvailabilityInterval ToRecord() => new()
        {
            GameId = GameId,
            State = SqlEnums.ToAvailabilityState(State),
            FromAt = FromAt,
            ToAt = ToAt,
            Cause = SqlEnums.ToFailureCause(Cause),
            Detail = Detail,
        };
    }
}
