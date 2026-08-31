using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

using Npgsql;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Spec §8 against a real database: who a verified claim belongs to, and what a beacon means.
/// </summary>
/// <remarks>
/// The properties here are the ones a handler cannot be trusted with, so several are asserted against
/// the schema rather than against the service — a constraint that fires is a guarantee, and a branch
/// in C# is an intention.
/// </remarks>
public class ClaimPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    /// <summary>
    /// The whole point of §8.1: the claim binds to the account that asked, not to the token holder.
    /// </summary>
    /// <remarks>
    /// The token is published where every anonymous connection reads it, so holding it must confer
    /// nothing: Mallory reads Alice's token off the connect screen and can do nothing with it.
    /// </remarks>
    [Test]
    public async Task AVerifiedClaimBelongsToTheAccountThatMintedTheTokenNotWhoeverHoldsIt()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var alice = await UserAsync(db, "alice");
        var mallory = await UserAsync(db, "mallory");
        var service = Service(db);

        var alices = await service.IssueAsync(game, alice);

        // Mallory has her own pending claim on the same game, and has read Alice's token.
        var mallorys = await service.IssueAsync(game, mallory);
        await Assert.That(mallorys.Token).IsNotEqualTo(alices.Token);

        var verdict = await service.OfferBeaconAsync(game, alices.Token, ClaimChannel.Mssp);

        await Assert.That(verdict).IsEqualTo(ClaimVerdict.Verified);

        var store = new NpgsqlClaimStore(db.DataSource);
        var settled = (await store.ForGameAsync(game)).Single(c => c.Id == alices.Id);

        await Assert.That(settled.UserId).IsEqualTo(alice);
        await Assert.That(settled.VerifiedVia).IsEqualTo(ClaimChannel.Mssp);

        var hers = (await store.ForGameAsync(game)).Single(c => c.Id == mallorys.Id);
        await Assert.That(hers.IsVerified).IsFalse();
    }

    /// <summary>§8.4 — presence establishes, absence never revokes.</summary>
    /// <remarks>
    /// Absence-revokes would hand revocation to any transient failure — a restart, an MSSP hiccup, a
    /// compression bug eating a subnegotiation — indistinguishable from an owner actually walking away.
    /// </remarks>
    [Test]
    public async Task AProbeThatSeesNoBeaconLeavesAVerifiedClaimAlone()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await UserAsync(db, "owner");
        var clock = new MovableClock(Now);
        var service = Service(db, clock);

        var claim = await service.IssueAsync(game, user);
        await service.OfferBeaconAsync(game, claim.Token, ClaimChannel.ConnectScreen);

        // Two probes later: one saw nothing at all, one saw a token we never issued.
        clock.Advance(TimeSpan.FromDays(1));
        await service.OfferBeaconAsync(game, null, ClaimChannel.Mssp);
        await service.OfferBeaconAsync(game, "muidx-aaaaaaaaaaaaaaaaaaaa", ClaimChannel.Mssp);

        var settled = (await new NpgsqlClaimStore(db.DataSource).ForGameAsync(game)).Single();

        await Assert.That(settled.IsVerified).IsTrue();
        await Assert.That(settled.BeaconLastSeenAt).IsEqualTo(Now);
        await Assert.That(await IsClaimedAsync(db, game)).IsTrue();
    }

    /// <summary>Seeing it again refreshes the second timestamp and touches nothing else.</summary>
    [Test]
    public async Task SeeingTheBeaconAgainMovesOnlyWhenWeLastSawIt()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await UserAsync(db, "owner");
        var clock = new MovableClock(Now);
        var service = Service(db, clock);

        var claim = await service.IssueAsync(game, user);
        await service.OfferBeaconAsync(game, claim.Token, ClaimChannel.Mssp);

        clock.Advance(TimeSpan.FromDays(3));
        var verdict = await service.OfferBeaconAsync(game, claim.Token, ClaimChannel.Mssp);

        await Assert.That(verdict).IsEqualTo(ClaimVerdict.StillSeen);

        var settled = (await new NpgsqlClaimStore(db.DataSource).ForGameAsync(game)).Single();

        await Assert.That(settled.ClaimedAt).IsEqualTo(Now);
        await Assert.That(settled.BeaconLastSeenAt).IsEqualTo(Now.AddDays(3));
    }

    /// <summary>
    /// A second attempt returns the token already published rather than minting a rival.
    /// </summary>
    /// <remarks>
    /// Replacing it would invalidate what the operator just pasted into <c>mush.cnf</c>. The partial
    /// unique index makes it a schema guarantee rather than a service courtesy.
    /// </remarks>
    [Test]
    public async Task AskingTwiceReturnsTheTokenAlreadyPublished()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await UserAsync(db, "owner");
        var service = Service(db);

        var first = await service.IssueAsync(game, user);
        var second = await service.IssueAsync(game, user);

        await Assert.That(second.Id).IsEqualTo(first.Id);
        await Assert.That(second.Token).IsEqualTo(first.Token);

        var all = await new NpgsqlClaimStore(db.DataSource).ForGameAsync(game);
        await Assert.That(all.Count).IsEqualTo(1);
    }

    /// <summary>An expired token proves nothing, and expiry is applied by the query, not the caller.</summary>
    [Test]
    public async Task AnExpiredTokenDoesNotCompleteAClaim()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await UserAsync(db, "owner");
        var clock = new MovableClock(Now);
        var service = Service(db, clock);

        var claim = await service.IssueAsync(game, user);

        clock.Advance(ClaimToken.PendingLifetime + TimeSpan.FromDays(1));
        await ExpireAsync(db, claim.Id, clock.GetUtcNow());

        var verdict = await service.OfferBeaconAsync(game, claim.Token, ClaimChannel.Mssp);

        await Assert.That(verdict).IsEqualTo(ClaimVerdict.Stale);
        await Assert.That(await IsClaimedAsync(db, game)).IsFalse();
    }

    /// <summary>§8.5 — several owners, and one leaving does not unclaim the game for the rest.</summary>
    [Test]
    public async Task RevokingOneOfTwoOwnersLeavesTheGameClaimed()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var one = await UserAsync(db, "one");
        var two = await UserAsync(db, "two");
        var service = Service(db);

        var first = await service.IssueAsync(game, one);
        await service.OfferBeaconAsync(game, first.Token, ClaimChannel.Mssp);

        var secondClaim = await service.IssueAsync(game, two);
        await service.OfferBeaconAsync(game, secondClaim.Token, ClaimChannel.ConnectScreen);

        await service.RevokeAsync(first.Id, "handed the game over");

        await Assert.That(await IsClaimedAsync(db, game)).IsTrue();

        await service.RevokeAsync(secondClaim.Id, "closed the game");

        await Assert.That(await IsClaimedAsync(db, game)).IsFalse();
    }

    /// <summary>The audit log is append-only and records what actually happened (§8.5).</summary>
    [Test]
    public async Task EveryStepOfAClaimIsInTheAuditLog()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await UserAsync(db, "owner");
        var service = Service(db);
        var store = new NpgsqlClaimStore(db.DataSource);

        var claim = await service.IssueAsync(game, user);
        await service.OfferBeaconAsync(game, claim.Token, ClaimChannel.Mssp);
        await service.RevokeAsync(claim.Id, "moved house");

        var kinds = (await store.EventsAsync(claim.Id)).Select(e => e.Kind).ToList();

        await Assert.That(kinds).Contains(ClaimEventKind.Issued);
        await Assert.That(kinds).Contains(ClaimEventKind.Verified);
        await Assert.That(kinds).Contains(ClaimEventKind.Revoked);
    }

    /// <summary>
    /// The database refuses a claim with no account, whatever a handler above it believes.
    /// </summary>
    /// <remarks>
    /// §8.1's ordering — sign in, then claim — is a <c>NOT NULL</c> here rather than a rule someone
    /// remembers, so a token that verified without anybody having asked has nowhere to go.
    /// </remarks>
    [Test]
    public async Task TheSchemaRefusesAClaimWithNoAccount()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO game_claim (id, game_id, user_id, token, issued_at, expires_at)
                VALUES (@id, @game, NULL, 'muidx-aaaaaaaaaaaaaaaaaaaa', @now, @later)
                """,
                new { id = Guid.CreateVersion7(), game, now = Now, later = Now.AddDays(30) }))
            .Throws<PostgresException>();
    }

    /// <summary>
    /// One token cannot exist on two claims, or one game's published token would complete another's.
    /// </summary>
    [Test]
    public async Task TheSchemaRefusesTheSameTokenTwice()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var first = await Seed.GameAsync(db, slug: "one", name: "One");
        var second = await Seed.GameAsync(db, slug: "two", name: "Two");
        var user = await UserAsync(db, "owner");
        var store = new NpgsqlClaimStore(db.DataSource);

        await store.InsertAsync(Claim(first, user, "muidx-aaaaaaaaaaaaaaaaaaaa"));

        await Assert.That(async () => await store.InsertAsync(
                Claim(second, user, "muidx-aaaaaaaaaaaaaaaaaaaa")))
            .Throws<PostgresException>();
    }

    /// <summary>A verified row must say which channel it was read from — half a record is not one.</summary>
    [Test]
    public async Task TheSchemaRefusesAVerifiedClaimWithNoChannel()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await UserAsync(db, "owner");

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO game_claim (id, game_id, user_id, token, issued_at, expires_at, claimed_at)
                VALUES (@id, @game, @user, 'muidx-aaaaaaaaaaaaaaaaaaaa', @now, @later, @now)
                """,
                new
                {
                    id = Guid.CreateVersion7(),
                    game,
                    user,
                    now = Now,
                    later = Now.AddDays(30),
                }))
            .Throws<PostgresException>();
    }

    /// <summary>
    /// One clock decides whether a token is still pending, and it is the injected one.
    /// </summary>
    /// <remarks>
    /// <see cref="GameClaim.IsPending"/> asks the <see cref="TimeProvider"/> and the store asked the
    /// database, so the same claim could be pending in C# and expired in SQL. Two consequences, both
    /// real: a fixture dated far enough in the past verifies nothing however carefully it sets its
    /// clock, and every test in this file that dates a claim near the wall clock is on a fuse that
    /// burns down thirty days after somebody wrote the literal. Time comes from the service.
    /// </remarks>
    [Test]
    public async Task WhetherATokenIsStillPendingIsDecidedByTheInjectedClock()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await UserAsync(db, "owner");

        // Long enough ago that the database's own now() considers the token expired the moment it
        // is minted, which is exactly what a fixed fixture date becomes as it ages.
        var clock = new MovableClock(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service = Service(db, clock);

        var claim = await service.IssueAsync(game, user);

        await Assert.That(await service.OfferBeaconAsync(game, claim.Token, ClaimChannel.Mssp))
            .IsEqualTo(ClaimVerdict.Verified);
    }

    /// <summary>§8.3's third channel: a TXT record, port-qualified, read off the operator's own zone.</summary>
    [Test]
    public async Task ADnsTxtBeaconVerifiesAClaimAndTheChannelSurvivesTheRoundTrip()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await UserAsync(db, "owner");
        var service = Service(db);

        var claim = await service.IssueAsync(game, user);

        var verdict = await service.OfferBeaconAsync(game, claim.Token, ClaimChannel.DnsTxt);

        await Assert.That(verdict).IsEqualTo(ClaimVerdict.Verified);
        await Assert.That(await IsClaimedAsync(db, game)).IsTrue();

        var settled = (await new NpgsqlClaimStore(db.DataSource).ForGameAsync(game)).Single();

        // Through the schema and back, not just through the service: the channel vocabulary is a
        // CHECK constraint, so a member the migration does not know about fails on write.
        await Assert.That(settled.VerifiedVia).IsEqualTo(ClaimChannel.DnsTxt);
    }

    /// <summary>
    /// §8.3, §8.4 — DNS may join a game's owners and may never displace them.
    /// </summary>
    /// <remarks>
    /// The two other channels are published by the listener being claimed, so proving one proves
    /// control of that game. A TXT record is published by whoever controls the domain, and on shared
    /// MU* hosting that is the host's operator rather than the game's. Joining on that evidence is a
    /// listing gaining an owner; a takeover on it is somebody who proved control of the actual server
    /// losing theirs, so the destructive half of the flow stays on the wire.
    /// </remarks>
    [Test]
    public async Task ADnsTxtBeaconCannotCompleteATakeover()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var owner = await UserAsync(db, "owner");
        var newcomer = await UserAsync(db, "newcomer");
        var service = Service(db);

        var theirs = await service.IssueAsync(game, owner);
        await service.OfferBeaconAsync(game, theirs.Token, ClaimChannel.Mssp);

        var takeover = await service.IssueAsync(game, newcomer, ClaimIntent.Assume);

        await Assert.That(await service.OfferBeaconAsync(game, takeover.Token, ClaimChannel.DnsTxt))
            .IsEqualTo(ClaimVerdict.NothingToDo);

        var store = new NpgsqlClaimStore(db.DataSource);

        await Assert.That((await store.ForGameAsync(game)).Single(c => c.Id == takeover.Id).IsVerified)
            .IsFalse();
        await Assert.That((await store.ForGameAsync(game)).Single(c => c.Id == theirs.Id).IsVerified)
            .IsTrue();

        // The same pending claim still completes over a channel that proves control of the listener,
        // so the rule bounds the evidence rather than the intent.
        await Assert.That(await service.OfferBeaconAsync(game, takeover.Token, ClaimChannel.Mssp))
            .IsEqualTo(ClaimVerdict.Assumed);
    }

    /// <summary>
    /// A DNS-verified claim's beacon still refreshes, and a verified takeover's still does too.
    /// </summary>
    /// <remarks>
    /// The Join-only rule guards the pending-to-verified transition and nothing else. Refusing to
    /// move <c>beacon_last_seen_at</c> for an already-verified Assume claim would make the sweep
    /// report a live record as unseen — §8.4's two timestamps exist to keep those apart.
    /// </remarks>
    [Test]
    public async Task DnsStillRefreshesTheBeaconOfAClaimItCouldNotHaveVerified()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var owner = await UserAsync(db, "owner");
        var newcomer = await UserAsync(db, "newcomer");
        var clock = new MovableClock(Now);
        var service = Service(db, clock);

        await service.OfferBeaconAsync(game, (await service.IssueAsync(game, owner)).Token, ClaimChannel.Mssp);

        var takeover = await service.IssueAsync(game, newcomer, ClaimIntent.Assume);
        await service.OfferBeaconAsync(game, takeover.Token, ClaimChannel.Mssp);

        clock.Advance(TimeSpan.FromHours(1));

        await Assert.That(await service.OfferBeaconAsync(game, takeover.Token, ClaimChannel.DnsTxt))
            .IsEqualTo(ClaimVerdict.StillSeen);

        var settled = (await new NpgsqlClaimStore(db.DataSource).ForGameAsync(game))
            .Single(c => c.Id == takeover.Id);

        await Assert.That(settled.BeaconLastSeenAt).IsEqualTo(Now.AddHours(1));
    }

    private static GameClaim Claim(Guid game, Guid user, string token) => new()
    {
        Id = Guid.CreateVersion7(),
        GameId = game,
        UserId = user,
        Token = token,
        IssuedAt = Now,
        ExpiresAt = Now.AddDays(30),
    };

    private static ClaimService Service(TestDatabase db, TimeProvider? time = null) =>
        new(
            new NpgsqlClaimStore(db.DataSource),
            new NpgsqlGameStore(db.DataSource),
            time ?? new MovableClock(Now));

    private static async Task<Guid> UserAsync(TestDatabase db, string name)
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

    private static async Task<bool> IsClaimedAsync(TestDatabase db, Guid game)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();

        return await connection.ExecuteScalarAsync<bool>(
            "SELECT is_claimed FROM game WHERE id = @game", new { game });
    }

    /// <summary>
    /// Expires a claim as of the test's own clock rather than the database's.
    /// </summary>
    /// <remarks>
    /// This used to write <c>now() - interval '1 day'</c>, which is the database's wall clock, while
    /// the query it is setting up compares <c>expires_at</c> against the injected clock. The two
    /// agreed only for as long as real time stayed behind the seeded clock plus the lifetime under
    /// test — a window that closed on its own. <see cref="Seed.Now"/> is 2026-07-30 12:00Z, the test
    /// advances 31 days to 2026-08-30 12:00Z, and once the real calendar reached 2026-08-31 12:00Z
    /// the row written here stopped being in the past as far as the query was concerned, so an
    /// expired token started reading as <c>Verified</c>. Nothing changed in the code; the date did.
    /// <para>
    /// The clock is passed in for the same reason it exists: so "later" is a fact rather than a
    /// sleep, and so the test says the same thing on every day it is run.
    /// </para>
    /// </remarks>
    private static async Task ExpireAsync(TestDatabase db, Guid claim, DateTimeOffset asOf)
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            "UPDATE game_claim SET expires_at = @expiresAt WHERE id = @claim",
            new { claim, expiresAt = asOf - TimeSpan.FromDays(1) });
    }

    /// <summary>A clock a test can push forward, so "later" is a fact rather than a sleep.</summary>
    private sealed class MovableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
