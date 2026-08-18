using Dapper;

using Microsoft.AspNetCore.Identity;

using Npgsql;

namespace MUI.Web.Accounts;

/// <summary>
/// ASP.NET Core Identity's user and passkey stores, over the same Dapper and plain SQL as everything
/// else here.
/// </summary>
/// <remarks>
/// EF Core is Identity's default store; this codebase keeps SQL visible and migrations
/// hand-numbered, so these interfaces are implemented directly over Dapper instead. Only the
/// interfaces this app uses are implemented — no <c>IUserPasswordStore</c>, no email, no lockout, no
/// two-factor — because passkeys are the only, primary factor (§8.2); adding one would make
/// <see cref="UserManager{TUser}"/> start offering flows this site has no pages for.
/// </remarks>
public sealed class DapperUserStore(NpgsqlDataSource source, TimeProvider time)
    : IUserStore<MuiUser>, IUserPasskeyStore<MuiUser>
{
    private const string UserColumns = """
        id AS Id, display_name AS DisplayName, normalised_name AS NormalisedName,
        security_stamp AS SecurityStamp, concurrency_stamp AS ConcurrencyStamp,
        created_at AS CreatedAt, last_signed_in_at AS LastSignedInAt
        """;

    private const string PasskeyColumns = """
        credential_id AS CredentialId, public_key AS PublicKey, sign_count AS SignCount,
        is_backed_up AS IsBackedUp, is_backup_eligible AS IsBackupEligible,
        is_user_verified AS IsUserVerified, transports AS Transports,
        attestation_object AS AttestationObject, client_data_json AS ClientDataJson,
        name AS Name, created_at AS CreatedAt
        """;

    public Task<string> GetUserIdAsync(MuiUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return Task.FromResult(user.Id.ToString());
    }

    public Task<string?> GetUserNameAsync(MuiUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return Task.FromResult<string?>(user.DisplayName);
    }

    public Task SetUserNameAsync(MuiUser user, string? userName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        user.DisplayName = userName ?? string.Empty;

        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(MuiUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return Task.FromResult<string?>(user.NormalisedName);
    }

    public Task SetNormalizedUserNameAsync(
        MuiUser user,
        string? normalizedName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        user.NormalisedName = normalizedName ?? string.Empty;

        return Task.CompletedTask;
    }

    public async Task<IdentityResult> CreateAsync(MuiUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        user.CreatedAt = user.CreatedAt == default ? time.GetUtcNow() : user.CreatedAt;

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO app_user (id, display_name, normalised_name, security_stamp,
                                      concurrency_stamp, created_at, last_signed_in_at)
                VALUES (@Id, @DisplayName, @NormalisedName, @SecurityStamp,
                        @ConcurrencyStamp, @CreatedAt, @LastSignedInAt)
                """,
                user,
                cancellationToken: cancellationToken));
        }
        catch (PostgresException error) when (error.SqlState == "23505")
        {
            // The unique index on normalised name — a validation failure, not an error.
            return IdentityResult.Failed(new IdentityError
            {
                Code = "DuplicateUserName",
                Description = $"The name '{user.DisplayName}' is taken.",
            });
        }

        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(MuiUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // Guarded on the concurrency stamp read plus a new one written in the same statement, per
        // Identity's optimistic-concurrency contract.
        var previous = user.ConcurrencyStamp;
        user.ConcurrencyStamp = Guid.NewGuid().ToString();

        var rows = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE app_user
               SET display_name = @DisplayName,
                   normalised_name = @NormalisedName,
                   security_stamp = @SecurityStamp,
                   concurrency_stamp = @ConcurrencyStamp,
                   last_signed_in_at = @LastSignedInAt
             WHERE id = @Id AND concurrency_stamp = @Previous
            """,
            new
            {
                user.Id,
                user.DisplayName,
                user.NormalisedName,
                user.SecurityStamp,
                user.ConcurrencyStamp,
                user.LastSignedInAt,
                Previous = previous,
            },
            cancellationToken: cancellationToken));

        return rows == 1
            ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityError
            {
                Code = "ConcurrencyFailure",
                Description = "This account was changed somewhere else. Reload and try again.",
            });
    }

    public async Task<IdentityResult> DeleteAsync(MuiUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // Passkeys cascade; claims don't — the FK refuses the delete while any exist, since a claim
        // marks a game owned and dropping the account behind it would orphan `game.is_claimed`.
        // Revoke first.
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM app_user WHERE id = @Id",
            new { user.Id },
            cancellationToken: cancellationToken));

        return IdentityResult.Success;
    }

    public async Task<MuiUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(userId, out var id))
        {
            return null;
        }

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<MuiUser>(new CommandDefinition(
            $"SELECT {UserColumns} FROM app_user WHERE id = @id",
            new { id },
            cancellationToken: cancellationToken));
    }

    public async Task<MuiUser?> FindByNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<MuiUser>(new CommandDefinition(
            $"SELECT {UserColumns} FROM app_user WHERE normalised_name = @name",
            new { name = normalizedUserName },
            cancellationToken: cancellationToken));
    }

    public async Task AddOrUpdatePasskeyAsync(
        MuiUser user,
        UserPasskeyInfo passkey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(passkey);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        // Upsert: Identity calls this both to register a credential and to write back the signature
        // counter after every sign-in.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO user_passkey (
                credential_id, user_id, public_key, sign_count, is_backed_up, is_backup_eligible,
                is_user_verified, transports, attestation_object, client_data_json, name,
                created_at, last_used_at)
            VALUES (
                @CredentialId, @UserId, @PublicKey, @SignCount, @IsBackedUp, @IsBackupEligible,
                @IsUserVerified, @Transports, @AttestationObject, @ClientDataJson, @Name,
                @CreatedAt, @LastUsedAt)
            ON CONFLICT (credential_id) DO UPDATE
               SET sign_count = EXCLUDED.sign_count,
                   is_backed_up = EXCLUDED.is_backed_up,
                   is_backup_eligible = EXCLUDED.is_backup_eligible,
                   is_user_verified = EXCLUDED.is_user_verified,
                   name = EXCLUDED.name,
                   last_used_at = EXCLUDED.last_used_at
            """,
            new
            {
                passkey.CredentialId,
                UserId = user.Id,
                passkey.PublicKey,
                SignCount = (long)passkey.SignCount,
                passkey.IsBackedUp,
                passkey.IsBackupEligible,
                passkey.IsUserVerified,
                Transports = passkey.Transports,
                passkey.AttestationObject,
                passkey.ClientDataJson,
                Name = Trim(passkey.Name),
                passkey.CreatedAt,
                LastUsedAt = time.GetUtcNow(),
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IList<UserPasskeyInfo>> GetPasskeysAsync(
        MuiUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<PasskeyRow>(new CommandDefinition(
            $"SELECT {PasskeyColumns} FROM user_passkey WHERE user_id = @id ORDER BY created_at",
            new { id = user.Id },
            cancellationToken: cancellationToken));

        return [.. rows.Select(r => r.ToInfo())];
    }

    public async Task<MuiUser?> FindByPasskeyIdAsync(
        byte[] credentialId,
        CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<MuiUser>(new CommandDefinition(
            """
            SELECT u.id AS Id, u.display_name AS DisplayName, u.normalised_name AS NormalisedName,
                   u.security_stamp AS SecurityStamp, u.concurrency_stamp AS ConcurrencyStamp,
                   u.created_at AS CreatedAt, u.last_signed_in_at AS LastSignedInAt
              FROM app_user u
              JOIN user_passkey p ON p.user_id = u.id
             WHERE p.credential_id = @credentialId
            """,
            new { credentialId },
            cancellationToken: cancellationToken));
    }

    public async Task<UserPasskeyInfo?> FindPasskeyAsync(
        MuiUser user,
        byte[] credentialId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<PasskeyRow>(new CommandDefinition(
            $"SELECT {PasskeyColumns} FROM user_passkey "
            + "WHERE user_id = @id AND credential_id = @credentialId",
            new { id = user.Id, credentialId },
            cancellationToken: cancellationToken));

        return row?.ToInfo();
    }

    public async Task RemovePasskeyAsync(
        MuiUser user,
        byte[] credentialId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM user_passkey WHERE user_id = @id AND credential_id = @credentialId",
            new { id = user.Id, credentialId },
            cancellationToken: cancellationToken));
    }

    public void Dispose()
    {
        // The data source is owned by the container and outlives every store built over it.
    }

    /// <summary>Bounded at the schema's own limit, so a long name is trimmed rather than refused.</summary>
    private static string? Trim(string? name) =>
        name is null ? null : name.Length <= 64 ? name : name[..64];

    private sealed class PasskeyRow
    {
        public byte[] CredentialId { get; init; } = [];

        public byte[] PublicKey { get; init; } = [];

        public long SignCount { get; init; }

        public bool IsBackedUp { get; init; }

        public bool IsBackupEligible { get; init; }

        public bool IsUserVerified { get; init; }

        public string[]? Transports { get; init; }

        public byte[]? AttestationObject { get; init; }

        public byte[]? ClientDataJson { get; init; }

        public string? Name { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public UserPasskeyInfo ToInfo() => new(
            CredentialId,
            PublicKey,
            CreatedAt,
            (uint)SignCount,
            Transports ?? [],
            IsUserVerified,
            IsBackupEligible,
            IsBackedUp,
            AttestationObject ?? [],
            ClientDataJson ?? [])
        {
            Name = Name,
        };
    }
}
