using Dapper;

using MUI.Catalog.Persistence;
using MUI.Catalog.Tests.Persistence.Support;

using Npgsql;

namespace MUI.Catalog.Tests.Persistence;

/// <summary>
/// Several owners, a game changing hands, and the log that says which happened (spec §8.4, §8.5).
/// </summary>
/// <remarks>
/// The whole of this file turns on one thing: a co-founder joining and a new operator taking over
/// publish the identical line in the identical config file, so the difference cannot be measured and
/// must be declared. These assert that it is — and that neither outcome can be reached by accident.
/// </remarks>
public class OwnershipPostgresTests
{
    private static readonly DateTimeOffset Now = Seed.Now;

    /// <summary>§8.5 — a game may have several owners, each having verified a token of their own.</summary>
    [Test]
    public async Task TwoAccountsMayBothOwnOneGame()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var service = Service(db);
        var one = await UserAsync(db, "one");
        var two = await UserAsync(db, "two");

        var first = await service.IssueAsync(game, one);
        await service.OfferBeaconAsync(game, first.Token, ClaimChannel.Mssp);

        var second = await service.IssueAsync(game, two);
        var verdict = await service.OfferBeaconAsync(game, second.Token, ClaimChannel.ConnectScreen);

        // Joining is the default, so the first owner is untouched.
        await Assert.That(verdict).IsEqualTo(ClaimVerdict.Verified);
        await Assert.That((await service.OwnersAsync(game)).Count).IsEqualTo(2);
        await Assert.That(await IsClaimedAsync(db, game)).IsTrue();
    }

    /// <summary>
    /// §8.4 — a counter-claim is how a game changes hands, and it says so before it is published.
    /// </summary>
    [Test]
    public async Task ACounterClaimTakesTheGameOverAndRevokesTheOthers()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var service = Service(db);
        var departing = await UserAsync(db, "departing");
        var arriving = await UserAsync(db, "arriving");

        var old = await service.IssueAsync(game, departing);
        await service.OfferBeaconAsync(game, old.Token, ClaimChannel.Mssp);

        var takeover = await service.IssueAsync(game, arriving, ClaimIntent.Assume);
        var verdict = await service.OfferBeaconAsync(game, takeover.Token, ClaimChannel.Mssp);

        await Assert.That(verdict).IsEqualTo(ClaimVerdict.Assumed);

        var owners = await service.OwnersAsync(game);

        await Assert.That(owners.Count).IsEqualTo(1);
        await Assert.That(owners.Single().UserId).IsEqualTo(arriving);

        // The game never stops being claimed on the way through, so the listing badge does not
        // flicker off and on while a handover completes.
        await Assert.That(await IsClaimedAsync(db, game)).IsTrue();
    }

    /// <summary>
    /// The displaced owner's own log says what happened to them, and when.
    /// </summary>
    /// <remarks>
    /// The event goes on the losing claim rather than the winning one, because that is whose record
    /// changed. An owner who finds their claim gone must be able to read why from their own history
    /// rather than by inferring it from somebody else's.
    /// </remarks>
    [Test]
    public async Task TheDisplacedOwnerCanReadWhatHappenedInTheirOwnHistory()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var service = Service(db);
        var departing = await UserAsync(db, "departing");
        var arriving = await UserAsync(db, "arriving");

        var old = await service.IssueAsync(game, departing);
        await service.OfferBeaconAsync(game, old.Token, ClaimChannel.Mssp);

        var takeover = await service.IssueAsync(game, arriving, ClaimIntent.Assume);
        await service.OfferBeaconAsync(game, takeover.Token, ClaimChannel.Mssp);

        var history = await service.HistoryAsync(old.Id);

        await Assert.That(history.Select(e => e.Kind)).Contains(ClaimEventKind.CounterClaimed);

        var revoked = (await new NpgsqlClaimStore(db.DataSource).ForGameAsync(game))
            .Single(c => c.Id == old.Id);

        await Assert.That(revoked.RevokedAt).IsNotNull();
        await Assert.That(revoked.RevokedReason).Contains("counter-claim");
    }

    /// <summary>
    /// A takeover that displaces nobody is an ordinary first claim, and is not reported as a seizure.
    /// </summary>
    [Test]
    public async Task AssumingAnUnclaimedGameIsJustAClaim()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var service = Service(db);
        var user = await UserAsync(db, "first");

        var claim = await service.IssueAsync(game, user, ClaimIntent.Assume);
        var verdict = await service.OfferBeaconAsync(game, claim.Token, ClaimChannel.Mssp);

        await Assert.That(verdict).IsEqualTo(ClaimVerdict.Verified);
        await Assert.That((await service.OwnersAsync(game)).Count).IsEqualTo(1);
    }

    /// <summary>
    /// Intent is recorded when the token is issued, so it cannot be changed after it is published.
    /// </summary>
    /// <remarks>
    /// The claimant declares it on the page that explains both, and the row carries it from that
    /// moment. A design that read intent at verification time would let an account publish a token
    /// as a co-owner and settle it as a takeover.
    /// </remarks>
    [Test]
    public async Task IntentIsStoredWithTheTokenAndSurvivesARoundTrip()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var service = Service(db);
        var user = await UserAsync(db, "owner");

        var claim = await service.IssueAsync(game, user, ClaimIntent.Assume);
        var stored = (await new NpgsqlClaimStore(db.DataSource).ForGameAsync(game)).Single();

        await Assert.That(stored.Intent).IsEqualTo(ClaimIntent.Assume);
        await Assert.That(claim.Intent).IsEqualTo(ClaimIntent.Assume);
    }

    /// <summary>The schema refuses an intent nothing maps, as it does every other vocabulary.</summary>
    [Test]
    public async Task TheSchemaRefusesAnIntentWeDoNotKnow()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await UserAsync(db, "owner");

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await Assert.That(async () => await connection.ExecuteAsync(
                """
                INSERT INTO game_claim (id, game_id, user_id, token, intent, issued_at, expires_at)
                VALUES (@id, @game, @user, 'muidx-aaaaaaaaaaaaaaaaaaaa', 'seize', @now, @later)
                """,
                new { id = Guid.CreateVersion7(), game, user, now = Now, later = Now.AddDays(30) }))
            .Throws<PostgresException>();
    }

    /// <summary>A claim that existed before intent did was a first claim, and reads as one.</summary>
    /// <remarks>
    /// The column's DEFAULT is the backfill: none of those claims displaced anybody, so recording
    /// them as joins states what happened rather than inventing an intent nobody expressed.
    /// </remarks>
    [Test]
    public async Task AClaimWrittenWithoutAnIntentIsAJoin()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var user = await UserAsync(db, "owner");

        await using var connection = await db.DataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            INSERT INTO game_claim (id, game_id, user_id, token, issued_at, expires_at)
            VALUES (@id, @game, @user, 'muidx-aaaaaaaaaaaaaaaaaaaa', @now, @later)
            """,
            new { id = Guid.CreateVersion7(), game, user, now = Now, later = Now.AddDays(30) });

        var stored = (await new NpgsqlClaimStore(db.DataSource).ForGameAsync(game)).Single();

        await Assert.That(stored.Intent).IsEqualTo(ClaimIntent.Join);
    }

    /// <summary>An owner may give up a game, and the others keep theirs (§8.5).</summary>
    [Test]
    public async Task AnOwnerMayResignWithoutUnclaimingTheGameForTheRest()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var service = Service(db);
        var leaving = await UserAsync(db, "leaving");
        var staying = await UserAsync(db, "staying");

        var theirs = await service.IssueAsync(game, leaving);
        await service.OfferBeaconAsync(game, theirs.Token, ClaimChannel.Mssp);
        var others = await service.IssueAsync(game, staying);
        await service.OfferBeaconAsync(game, others.Token, ClaimChannel.Mssp);

        await Assert.That(await service.ResignAsync(theirs.Id, leaving)).IsEqualTo(ResignOutcome.Resigned);

        await Assert.That((await service.OwnersAsync(game)).Single().UserId).IsEqualTo(staying);
        await Assert.That(await IsClaimedAsync(db, game)).IsTrue();
    }

    /// <summary>
    /// A claim id is not a credential, so resigning is scoped to the account that holds the claim.
    /// </summary>
    /// <remarks>
    /// <c>RevokeAsync</c> takes a claim id and nothing else — right for the service, wrong for a
    /// caller reached from a form. Anybody who learned an id could otherwise unclaim somebody
    /// else's game, and ids travel: URLs, logs, this test.
    /// </remarks>
    [Test]
    public async Task NobodyCanResignSomebodyElsesClaim()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var service = Service(db);
        var owner = await UserAsync(db, "owner");
        var stranger = await UserAsync(db, "stranger");

        var theirs = await service.IssueAsync(game, owner);
        await service.OfferBeaconAsync(game, theirs.Token, ClaimChannel.Mssp);

        await Assert.That(await service.ResignAsync(theirs.Id, stranger)).IsEqualTo(ResignOutcome.NotYours);
        await Assert.That((await service.OwnersAsync(game)).Count).IsEqualTo(1);
    }

    /// <summary>The last owner leaving unclaims the game, and the badge goes with it.</summary>
    [Test]
    public async Task TheLastOwnerLeavingUnclaimsTheGame()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var service = Service(db);
        var owner = await UserAsync(db, "owner");

        var theirs = await service.IssueAsync(game, owner);
        await service.OfferBeaconAsync(game, theirs.Token, ClaimChannel.Mssp);

        await service.ResignAsync(theirs.Id, owner);

        await Assert.That(await service.OwnersAsync(game)).IsEmpty();
        await Assert.That(await IsClaimedAsync(db, game)).IsFalse();

        // Nothing is deleted: the claim is still there, revoked, with its reason and its history.
        var stored = (await new NpgsqlClaimStore(db.DataSource).ForGameAsync(game)).Single();
        await Assert.That(stored.RevokedAt).IsNotNull();
        await Assert.That((await service.HistoryAsync(theirs.Id)).Select(e => e.Kind))
            .Contains(ClaimEventKind.Revoked);
    }

    /// <summary>
    /// The audit log is the whole story of a claim, in order (§8.5).
    /// </summary>
    [Test]
    public async Task TheHistoryReadsAsWhatActuallyHappened()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var service = Service(db);
        var owner = await UserAsync(db, "owner");

        var claim = await service.IssueAsync(game, owner);
        await service.OfferBeaconAsync(game, claim.Token, ClaimChannel.Mssp);
        await service.OfferBeaconAsync(game, claim.Token, ClaimChannel.Mssp);
        await service.ResignAsync(claim.Id, owner);

        var kinds = (await service.HistoryAsync(claim.Id)).Select(e => e.Kind).ToList();

        await Assert.That(kinds[0]).IsEqualTo(ClaimEventKind.Issued);
        await Assert.That(kinds).Contains(ClaimEventKind.Verified);
        await Assert.That(kinds).Contains(ClaimEventKind.BeaconSeen);
        await Assert.That(kinds[^1]).IsEqualTo(ClaimEventKind.Revoked);
    }

    /// <summary>
    /// A revoked claim's token stops working, so a takeover cannot be undone by the old beacon.
    /// </summary>
    /// <remarks>
    /// The displaced operator's token is very likely still sitting in the config file — that is the
    /// normal state of a handover — and every probe reads it. It must settle nothing: presence
    /// establishes and absence never revokes (§8.4), but a revoked claim is not a claim.
    /// </remarks>
    [Test]
    public async Task ADisplacedOwnersTokenStillOnTheServerSettlesNothing()
    {
        await using var db = await PostgresFixture.MigratedAsync();
        var game = await Seed.GameAsync(db);
        var service = Service(db);
        var departing = await UserAsync(db, "departing");
        var arriving = await UserAsync(db, "arriving");

        var old = await service.IssueAsync(game, departing);
        await service.OfferBeaconAsync(game, old.Token, ClaimChannel.Mssp);

        var takeover = await service.IssueAsync(game, arriving, ClaimIntent.Assume);
        await service.OfferBeaconAsync(game, takeover.Token, ClaimChannel.Mssp);

        // The next probe still reads the old token off the connect screen.
        var verdict = await service.OfferBeaconAsync(game, old.Token, ClaimChannel.ConnectScreen);

        await Assert.That(verdict).IsEqualTo(ClaimVerdict.Stale);
        await Assert.That((await service.OwnersAsync(game)).Single().UserId).IsEqualTo(arriving);
    }

    private static ClaimService Service(TestDatabase db) =>
        new(
            new NpgsqlClaimStore(db.DataSource),
            new NpgsqlGameStore(db.DataSource),
            TimeProvider.System);

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
}
