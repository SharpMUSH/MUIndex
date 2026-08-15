using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

using Npgsql;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// The ownership schema's own guarantees (spec §8), asserted where they are enforced.
/// </summary>
/// <remarks>
/// These are constraints rather than code, so a test that exercised a C# path would prove nothing
/// about them. Each one exists because a handler above it could otherwise get it wrong quietly.
/// </remarks>
public class OwnershipSchemaTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    /// <summary>
    /// Deleting an account takes its passkeys and refuses while it still owns a claim.
    /// </summary>
    /// <remarks>
    /// The asymmetry is the point. A passkey is a way in and means nothing once the account is gone;
    /// a claim is what tells the world a game is owned, and dropping the account behind one would
    /// leave <c>game.is_claimed</c> true with nobody attached.
    /// </remarks>
    [Test]
    public async Task AnAccountTakesItsPasskeysAndIsHeldByItsClaims()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await AccountAsync(db, "owner");

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO user_passkey (credential_id, user_id, public_key, created_at)
            VALUES (@credential, @user, @key, @now)
            """,
            new { credential = new byte[] { 1, 2, 3 }, user, key = new byte[] { 4, 5, 6 }, now = Now });

        await connection.ExecuteAsync(
            """
            INSERT INTO game_claim (id, game_id, user_id, token, issued_at, expires_at)
            VALUES (@id, @game, @user, 'muidx-aaaaaaaaaaaaaaaaaaaa', @now, @later)
            """,
            new { id = Guid.CreateVersion7(), game, user, now = Now, later = Now.AddDays(30) });

        await Assert.That(async () => await connection.ExecuteAsync(
                "DELETE FROM app_user WHERE id = @user", new { user }))
            .Throws<PostgresException>();

        await connection.ExecuteAsync("DELETE FROM game_claim WHERE user_id = @user", new { user });
        await connection.ExecuteAsync("DELETE FROM app_user WHERE id = @user", new { user });

        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM user_passkey WHERE user_id = @user", new { user }))
            .IsEqualTo(0L);
    }

    /// <summary>
    /// One account cannot hold two live pending claims on one game.
    /// </summary>
    /// <remarks>
    /// A second token would invalidate the one the operator has just finished pasting into
    /// <c>mush.cnf</c>. The service returns the existing claim; the partial index is what makes that a
    /// guarantee rather than a courtesy.
    /// </remarks>
    [Test]
    public async Task TheSchemaRefusesASecondPendingClaimOnOneGameByOneAccount()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await AccountAsync(db, "owner");

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await InsertClaimAsync(connection, game, user, "muidx-aaaaaaaaaaaaaaaaaaaa");

        await Assert.That(async () =>
                await InsertClaimAsync(connection, game, user, "muidx-bbbbbbbbbbbbbbbbbbbb"))
            .Throws<PostgresException>();
    }

    /// <summary>
    /// The index is partial, so a verified claim does not block the account asking again later.
    /// </summary>
    /// <remarks>
    /// A game changes hands, the new owner revokes, the old one wants back in. If the index covered
    /// every row rather than only live pending ones, that account would be locked out of a game it
    /// can still prove control of — by an index, silently.
    /// </remarks>
    [Test]
    public async Task AVerifiedOrRevokedClaimDoesNotBlockANewOne()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await AccountAsync(db, "owner");

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await InsertClaimAsync(connection, game, user, "muidx-aaaaaaaaaaaaaaaaaaaa");
        await connection.ExecuteAsync(
            "UPDATE game_claim SET claimed_at = @now, verified_via = 'mssp' WHERE user_id = @user",
            new { now = Now, user });

        await InsertClaimAsync(connection, game, user, "muidx-bbbbbbbbbbbbbbbbbbbb");
        await connection.ExecuteAsync(
            "UPDATE game_claim SET revoked_at = @now WHERE token = 'muidx-bbbbbbbbbbbbbbbbbbbb'",
            new { now = Now });

        await InsertClaimAsync(connection, game, user, "muidx-cccccccccccccccccccc");

        await Assert.That(await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM game_claim WHERE user_id = @user", new { user }))
            .IsEqualTo(3L);
    }

    /// <summary>An audit entry must name something that actually happens (§8.5).</summary>
    [Test]
    public async Task TheAuditLogHasAClosedVocabulary()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await AccountAsync(db, "owner");
        var claim = Guid.CreateVersion7();

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO game_claim (id, game_id, user_id, token, issued_at, expires_at)
            VALUES (@claim, @game, @user, 'muidx-aaaaaaaaaaaaaaaaaaaa', @now, @later)
            """,
            new { claim, game, user, now = Now, later = Now.AddDays(30) });

        await Assert.That(async () => await connection.ExecuteAsync(
                "INSERT INTO claim_event (claim_id, at, kind) VALUES (@claim, @now, 'unclaimed')",
                new { claim, now = Now }))
            .Throws<PostgresException>();

        // And every kind the code can produce is accepted, which is the half a vocabulary test
        // usually forgets — a CHECK that refuses a real value fails in production, not in a test.
        foreach (var kind in Enum.GetValues<ClaimEventKind>())
        {
            await connection.ExecuteAsync(
                "INSERT INTO claim_event (claim_id, at, kind) VALUES (@claim, @now, @kind)",
                new { claim, now = Now, kind = SqlEnums.ToDb(kind) });
        }
    }

    /// <summary>
    /// Both directions of <c>intent</c> (spec §8.4): the schema refuses what we cannot write, and
    /// accepts everything we can.
    /// </summary>
    /// <remarks>
    /// The second half matters more than it looks. A <c>CHECK</c> that refuses a value the code
    /// really produces does not fail here — it fails the first time somebody tries to take over a
    /// game, in production, at the end of a flow they have already published a token for.
    /// </remarks>
    [Test]
    public async Task TheSchemaAndTheCodeAgreeAboutWhatAClaimCanMean()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await AccountAsync(db, "owner");

        await using var connection = await db.DataSource.OpenConnectionAsync();

        // One account per intent, because a partial unique index allows an account only one pending
        // claim per game — which is §8.1's own rule and not something to work around here.
        foreach (var intent in Enum.GetValues<ClaimIntent>())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO game_claim (id, game_id, user_id, token, intent, issued_at, expires_at)
                VALUES (@id, @game, @user, @token, @intent, @now, @later)
                """,
                new
                {
                    id = Guid.CreateVersion7(),
                    game,
                    user = await AccountAsync(db, $"claimant-{intent}"),
                    token = "muidx-" + intent.ToString().ToLowerInvariant().PadRight(20, 'a'),
                    intent = SqlEnums.ToDb(intent),
                    now = Now,
                    later = Now.AddDays(30),
                });
        }

        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO game_claim (id, game_id, user_id, token, intent, issued_at, expires_at)
                VALUES (@id, @game, @user, 'muidx-bbbbbbbbbbbbbbbbbbbb', 'seize', @now, @later)
                """,
                new { id = Guid.CreateVersion7(), game, user, now = Now, later = Now.AddDays(30) }))
            .Throws<PostgresException>();
    }

    private static Task InsertClaimAsync(NpgsqlConnection connection, Guid game, Guid user, string token) =>
        connection.ExecuteAsync(
            """
            INSERT INTO game_claim (id, game_id, user_id, token, issued_at, expires_at)
            VALUES (@id, @game, @user, @token, @now, @later)
            """,
            new { id = Guid.CreateVersion7(), game, user, token, now = Now, later = Now.AddDays(30) });

    private static async Task<Guid> AccountAsync(TestDatabase db, string name)
    {
        var id = Guid.CreateVersion7();

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO app_user (id, display_name, normalised_name, security_stamp,
                                  concurrency_stamp, created_at)
            VALUES (@id, @name, @normalised, @stamp, @stamp, @now)
            """,
            new
            {
                id,
                name,
                normalised = name.ToUpperInvariant(),
                stamp = Guid.NewGuid().ToString(),
                now = Now,
            });

        return id;
    }
}
