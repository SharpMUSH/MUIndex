using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

/// <summary>
/// The <c>game_claim</c> and <c>claim_event</c> tables (spec §8).
/// </summary>
/// <remarks>
/// Nothing here decides whether a claim is legitimate. It stores what the layer above concluded, and
/// the two guarantees it does enforce are the ones a handler cannot be trusted with: a claim always
/// carries the account that asked for it (the column is <c>NOT NULL</c>), and one account holds at
/// most one pending claim per game (a partial unique index). Both are §8.1's ordering made
/// unavoidable rather than remembered.
/// </remarks>
public sealed class NpgsqlClaimStore(NpgsqlDataSource source) : IClaimStore
{
    private const string Columns = """
        id AS Id, game_id AS GameId, user_id AS UserId, token AS Token,
        issued_at AS IssuedAt, expires_at AS ExpiresAt, claimed_at AS ClaimedAt,
        beacon_last_seen_at AS BeaconLastSeenAt, verified_via AS VerifiedVia,
        revoked_at AS RevokedAt, revoked_reason AS RevokedReason, last_checked_at AS LastCheckedAt
        """;

    public async Task<GameClaim?> FindAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM game_claim WHERE id = @claimId",
            new { claimId },
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public async Task<IReadOnlyList<GameClaim>> ForGameAsync(
        Guid gameId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM game_claim WHERE game_id = @gameId ORDER BY issued_at DESC",
            new { gameId },
            cancellationToken: cancellationToken));

        return [.. rows.Select(r => r.ToRecord())];
    }

    public async Task<IReadOnlyList<GameClaim>> ForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"SELECT {Columns} FROM game_claim WHERE user_id = @userId ORDER BY issued_at DESC",
            new { userId },
            cancellationToken: cancellationToken));

        return [.. rows.Select(r => r.ToRecord())];
    }

    /// <summary>
    /// The live pending claim on <paramref name="gameId"/> holding <paramref name="token"/>.
    /// </summary>
    /// <remarks>
    /// Expiry is applied in SQL rather than by the caller. A token that outlived its window is not a
    /// match, and leaving that to a downstream <c>if</c> would make an expired token complete a claim
    /// on any path that forgot it.
    /// </remarks>
    public async Task<GameClaim?> FindPendingByTokenAsync(
        Guid gameId,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"""
            SELECT {Columns}
              FROM game_claim
             WHERE game_id = @gameId
               AND token = @token
               AND claimed_at IS NULL
               AND revoked_at IS NULL
               AND expires_at > now()
            """,
            new { gameId, token },
            cancellationToken: cancellationToken));

        return row?.ToRecord();
    }

    public async Task InsertAsync(GameClaim claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game_claim (
                id, game_id, user_id, token, issued_at, expires_at,
                claimed_at, beacon_last_seen_at, verified_via,
                revoked_at, revoked_reason, last_checked_at)
            VALUES (
                @Id, @GameId, @UserId, @Token, @IssuedAt, @ExpiresAt,
                @ClaimedAt, @BeaconLastSeenAt, @VerifiedVia,
                @RevokedAt, @RevokedReason, @LastCheckedAt)
            """,
            Parameters(claim),
            cancellationToken: cancellationToken));
    }

    public async Task UpdateAsync(GameClaim claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE game_claim
               SET expires_at = @ExpiresAt,
                   claimed_at = @ClaimedAt,
                   beacon_last_seen_at = @BeaconLastSeenAt,
                   verified_via = @VerifiedVia,
                   revoked_at = @RevokedAt,
                   revoked_reason = @RevokedReason,
                   last_checked_at = @LastCheckedAt
             WHERE id = @Id
            """,
            Parameters(claim),
            cancellationToken: cancellationToken));
    }

    public async Task RecordEventAsync(ClaimEvent claimEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimEvent);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO claim_event (claim_id, at, kind, detail)
            VALUES (@ClaimId, @At, @Kind, @Detail)
            """,
            new
            {
                claimEvent.ClaimId,
                claimEvent.At,
                Kind = SqlEnums.ToDb(claimEvent.Kind),
                claimEvent.Detail,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ClaimEvent>> EventsAsync(
        Guid claimId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<EventRow>(new CommandDefinition(
            "SELECT claim_id AS ClaimId, at AS At, kind AS Kind, detail AS Detail "
            + "FROM claim_event WHERE claim_id = @claimId ORDER BY at, id",
            new { claimId },
            cancellationToken: cancellationToken));

        return [.. rows.Select(r => new ClaimEvent(
            r.ClaimId, r.At, SqlEnums.ToClaimEventKind(r.Kind), r.Detail))];
    }

    private static object Parameters(GameClaim claim) => new
    {
        claim.Id,
        claim.GameId,
        claim.UserId,
        claim.Token,
        claim.IssuedAt,
        claim.ExpiresAt,
        claim.ClaimedAt,
        claim.BeaconLastSeenAt,
        VerifiedVia = claim.VerifiedVia is { } via ? SqlEnums.ToDb(via) : null,
        claim.RevokedAt,
        claim.RevokedReason,
        claim.LastCheckedAt,
    };

    // A class with settable properties rather than a positional record, matching every other store
    // here. Dapper materialises a positional record by finding a constructor whose parameter types
    // match the reader's, and Npgsql hands back `DateTime` for `timestamptz` — so the record wants a
    // `DateTime` constructor and refuses to use a `DateTimeOffset` one. Properties are set
    // individually, with the conversion applied per column.
    private sealed class Row
    {
        public Guid Id { get; init; }

        public Guid GameId { get; init; }

        public Guid UserId { get; init; }

        public string Token { get; init; } = string.Empty;

        public DateTimeOffset IssuedAt { get; init; }

        public DateTimeOffset ExpiresAt { get; init; }

        public DateTimeOffset? ClaimedAt { get; init; }

        public DateTimeOffset? BeaconLastSeenAt { get; init; }

        public string? VerifiedVia { get; init; }

        public DateTimeOffset? RevokedAt { get; init; }

        public string? RevokedReason { get; init; }

        public DateTimeOffset? LastCheckedAt { get; init; }

        public GameClaim ToRecord() => new()
        {
            Id = Id,
            GameId = GameId,
            UserId = UserId,
            Token = Token,
            IssuedAt = IssuedAt,
            ExpiresAt = ExpiresAt,
            ClaimedAt = ClaimedAt,
            BeaconLastSeenAt = BeaconLastSeenAt,
            VerifiedVia = VerifiedVia is null ? null : SqlEnums.ToClaimChannel(VerifiedVia),
            RevokedAt = RevokedAt,
            RevokedReason = RevokedReason,
            LastCheckedAt = LastCheckedAt,
        };
    }

    private sealed class EventRow
    {
        public Guid ClaimId { get; init; }

        public DateTimeOffset At { get; init; }

        public string Kind { get; init; } = string.Empty;

        public string? Detail { get; init; }
    }
}
